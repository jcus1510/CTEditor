using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CTEditor.Content
{
    public enum ContentIssueLevel { Error = 0, Warning = 1, Tip = 2 }

    /// <summary>A problem in the data: which sheet, which row and column, and what is wrong.</summary>
    public sealed class ContentIssue
    {
        public ContentIssueLevel Level { get; }
        public string Category { get; }
        public string Id { get; }
        public string Column { get; }
        /// <summary>Row in the CSV as Excel numbers it (1 = the header).</summary>
        public int Row { get; }
        public string Text { get; }

        public ContentIssue(ContentIssueLevel level, string category, string id, string column, int row, string text)
        {
            Level = level; Category = category; Id = id; Column = column; Row = row; Text = text;
        }

        public override string ToString() => $"{Level}: {Text}";
    }

    /// <summary>
    /// Checks every sheet against its schema: ids (missing, repeated, with spaces), required cells, numbers, si/no,
    /// choices, and that every reference points to something that exists. Read only; nothing breaks the application.
    /// </summary>
    public static class ContentChecks
    {
        private static readonly string[] Yes = { "si", "sí", "true", "1", "x", "yes" };
        private static readonly string[] No = { "no", "false", "0", "" };

        public static bool TryNumber(string v, out double d) =>
            double.TryParse((v ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out d);

        public static bool IsYes(string v) => Yes.Contains((v ?? "").Trim().ToLowerInvariant());

        public static List<ContentIssue> Run(ContentDatabase db)
        {
            var list = new List<ContentIssue>(db.LoadIssues);
            foreach (var t in db.Tables.ToList()) list.AddRange(Cached(db, t));
            return list.OrderBy(i => i.Level).ThenBy(i => i.Category).ThenBy(i => i.Row).ToList();
        }

        private static readonly Dictionary<string, (int version, List<ContentIssue> issues)> Cache = new Dictionary<string, (int, List<ContentIssue>)>();

        /// <summary>The checks of one sheet, computed once per change of the data (lists and windows call it often).</summary>
        public static List<ContentIssue> Cached(ContentDatabase db, ContentTable t)
        {
            var key = t.Schema.Key + "@" + db.GetHashCode();
            if (Cache.TryGetValue(key, out var c) && c.version == db.Version) return c.issues;
            var list = Check(db, t).ToList();
            Cache[key] = (db.Version, list);
            return list;
        }

        /// <summary>All the sheets, cached per change.</summary>
        public static List<ContentIssue> RunCached(ContentDatabase db)
        {
            var list = new List<ContentIssue>(db.LoadIssues);
            foreach (var t in db.Tables.ToList()) list.AddRange(Cached(db, t));
            return list.OrderBy(i => i.Level).ThenBy(i => i.Category).ThenBy(i => i.Row).ToList();
        }

        private static readonly List<Func<ContentDatabase, ContentTable, IEnumerable<ContentIssue>>> Extra =
            new List<Func<ContentDatabase, ContentTable, IEnumerable<ContentIssue>>>();

        /// <summary>A module adds its own checks (the game rules: effects, evolutions, teams...).</summary>
        public static void Register(Func<ContentDatabase, ContentTable, IEnumerable<ContentIssue>> check)
        {
            if (!Extra.Contains(check)) Extra.Add(check);
            Cache.Clear();
        }

        public static IEnumerable<ContentIssue> Check(ContentDatabase db, ContentTable t)
        {
            foreach (var i in Basic(db, t)) yield return i;
            foreach (var i in LegalityIssues(db, t)) yield return i;
            foreach (var check in Extra)
            {
                List<ContentIssue> found;
                try { found = check(db, t)?.ToList() ?? new List<ContentIssue>(); }
                catch (Exception e) { found = new List<ContentIssue> { new ContentIssue(ContentIssueLevel.Warning, t.Schema.Key, null, null, 0, "Una comprobación falló: " + e.Message) }; }
                foreach (var i in found) yield return i;
            }
        }

        /// <summary>Warnings (never errors): an ability the species cannot have, a move it cannot learn (sets and teams).</summary>
        private static IEnumerable<ContentIssue> LegalityIssues(ContentDatabase db, ContentTable t)
        {
            var key = t.Schema.Key;
            if (key != ContentSchemas.Sets && key != ContentSchemas.Trainers && key != ContentSchemas.Teams) yield break;
            var species = db.Table(ContentSchemas.Species);
            if (species.Records.Count == 0) yield break;
            var moves = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> Learn(string sp) => moves.TryGetValue(sp, out var m) ? m : moves[sp] = Legality.MovesOf(species, sp);
            for (int i = 0; i < t.Records.Count; i++)
            {
                var r = t.Records[i];
                string id = t.IdOf(r), where = $"{t.Schema.File}, fila {i + 2}";
                var members = key == ContentSchemas.Sets
                    ? new[] { (sp: r["especie"].Trim(), abilities: r["habilidad"].Split(',', '|').Select(x => x.Trim()).Where(x => x.Length > 0).ToList(),
                        mv: r["movimientos"].Split('/', ',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList(), col: "habilidad", mcol: "movimientos") }
                    : TeamFormat.Parse(r["equipo"]).Where(m => m.Raw == null)
                        .Select(m => (sp: m.Species, abilities: m.Ability.Length > 0 ? new List<string> { m.Ability } : new List<string>(), mv: m.Moves, col: "equipo", mcol: "equipo")).ToArray();
                foreach (var m in members)
                {
                    var sp = species.Find(m.sp);
                    if (sp == null) continue;
                    var legal = new HashSet<string>(Legality.AbilitiesOf(sp), StringComparer.OrdinalIgnoreCase);
                    if (legal.Count > 0)
                        foreach (var a in m.abilities.Where(a => !legal.Contains(a)))
                            yield return new ContentIssue(ContentIssueLevel.Warning, key, id, m.col, i + 2, $"{where}: {species.NameOf(sp)} no tiene la habilidad «{a}» (habilidad ilegal).");
                    var learn = Learn(m.sp);
                    if (learn.Count > 0)
                        foreach (var mv in m.mv.Where(x => !learn.Contains(x)))
                            yield return new ContentIssue(ContentIssueLevel.Warning, key, id, m.mcol, i + 2, $"{where}: {species.NameOf(sp)} no aprende «{mv}».");
                }
            }
        }

        private static IEnumerable<ContentIssue> Basic(ContentDatabase db, ContentTable t)
        {
            var s = t.Schema;
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < t.Records.Count; i++)
            {
                var r = t.Records[i];
                int row = i + 2;
                string id = t.IdOf(r);
                string where = $"{s.File}, fila {row}";
                if (id.Length == 0) { yield return new ContentIssue(ContentIssueLevel.Error, s.Key, null, s.IdColumn, row, $"{where}: sin id."); continue; }
                if (seen.TryGetValue(id, out var firstRow))
                    yield return new ContentIssue(ContentIssueLevel.Error, s.Key, id, s.IdColumn, row, $"{where}: el id «{id}» ya está en la fila {firstRow}.");
                else seen[id] = row;
                if (s.HeaderTarget == null && !ContentIds.IsValid(id))
                    yield return new ContentIssue(ContentIssueLevel.Warning, s.Key, id, s.IdColumn, row, $"{where}: el id «{id}» tiene mayúsculas, espacios o tildes (mejor «{ContentIds.Normalize(id)}»).");

                foreach (var col in t.Columns)
                {
                    var spec = s.Column(col);
                    if (spec == null) continue;
                    var v = r[col];
                    string at = $"{where}, {spec.Label}";
                    if (string.IsNullOrWhiteSpace(v))
                    {
                        if (spec.Required && col != s.IdColumn) yield return new ContentIssue(ContentIssueLevel.Error, s.Key, id, col, row, $"{at}: está vacío y hace falta.");
                        continue;
                    }
                    switch (spec.Kind)
                    {
                        case ColumnKind.Int:
                        case ColumnKind.Number:
                            if (!TryNumber(v, out var d) || (spec.Kind == ColumnKind.Int && Math.Abs(d - Math.Round(d)) > 1e-9))
                                yield return new ContentIssue(ContentIssueLevel.Error, s.Key, id, col, row, $"{at}: «{v}» no es un número{(spec.Kind == ColumnKind.Int ? " entero" : "")}.");
                            else if ((spec.Min.HasValue && d < spec.Min) || (spec.Max.HasValue && d > spec.Max))
                                yield return new ContentIssue(ContentIssueLevel.Warning, s.Key, id, col, row,
                                    $"{at}: {v} está fuera de {spec.Min?.ToString(CultureInfo.InvariantCulture) ?? "…"}-{spec.Max?.ToString(CultureInfo.InvariantCulture) ?? "…"}.");
                            break;
                        case ColumnKind.Bool:
                            var b = v.Trim().ToLowerInvariant();
                            if (!Yes.Contains(b) && !No.Contains(b))
                                yield return new ContentIssue(ContentIssueLevel.Error, s.Key, id, col, row, $"{at}: «{v}» tiene que ser si o no.");
                            break;
                        case ColumnKind.Choice:
                            if (spec.Options.Count > 0 && !spec.Options.Contains(v.Trim().ToLowerInvariant()))
                                yield return new ContentIssue(ContentIssueLevel.Warning, s.Key, id, col, row, $"{at}: «{v}» no es {string.Join(", ", spec.Options)}.");
                            break;
                        case ColumnKind.LevelRefs:
                            foreach (var e in v.Split('|'))
                            {
                                int c = e.IndexOf(':');
                                if (c < 0 || !int.TryParse(e.Substring(0, c).Trim(), out _))
                                    yield return new ContentIssue(ContentIssueLevel.Error, s.Key, id, col, row, $"{at}: «{e}» tiene que ser nivel:movimiento.");
                            }
                            break;
                    }
                    if (spec.IsReference && spec.Kind != ColumnKind.Script)
                        foreach (var (cat, refId, _) in RefFormat.Extract(spec, v, db.Has))
                            if (!db.Has(cat, refId))
                            {
                                var target = ContentSchemas.Find(cat);
                                yield return new ContentIssue(ContentIssueLevel.Error, s.Key, id, col, row,
                                    $"{at}: «{refId}» no existe en {target?.Title.ToLowerInvariant() ?? cat}.");
                            }
                }
            }
            if (s.HeaderTarget != null)
            {
                foreach (var c in t.Columns.Where(c => c != s.IdColumn && !db.Has(s.HeaderTarget, c)))
                    yield return new ContentIssue(ContentIssueLevel.Warning, s.Key, null, c, 1, $"{s.File}: la columna «{c}» no es un tipo que exista.");
                for (int i = 0; i < t.Records.Count; i++)
                    foreach (var c in t.Columns.Where(c => c != s.IdColumn))
                    {
                        var v = t.Records[i][c];
                        if (!string.IsNullOrWhiteSpace(v) && !TryNumber(v, out _))
                            yield return new ContentIssue(ContentIssueLevel.Error, s.Key, t.IdOf(t.Records[i]), c, i + 2, $"{s.File}, fila {i + 2}, {c}: «{v}» no es un número.");
                    }
            }
        }
    }
}
