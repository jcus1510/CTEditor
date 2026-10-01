using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CTEditor.Content
{
    /// <summary>A use of a piece of content: who (category, id, column) points to what (category, id).</summary>
    public sealed class ContentRef
    {
        public string Category { get; }
        public string Id { get; }
        public string OwnerCategory { get; }
        public string OwnerId { get; }
        public string Column { get; }
        /// <summary>The reference can be taken out on its own (a move from a learnset); otherwise only replaced.</summary>
        public bool Removable { get; }
        /// <summary>Text for lists («Entrenador Misty · equipo»).</summary>
        public string Where { get; }

        public ContentRef(string category, string id, string ownerCategory, string ownerId, string column, bool removable, string where = null)
        {
            Category = category; Id = id; OwnerCategory = ownerCategory; OwnerId = ownerId; Column = column; Removable = removable;
            Where = where ?? $"{ownerCategory} «{ownerId}» · {column}";
        }

        /// <summary>Uses outside the data sheets (maps, events...) are not edited from here.</summary>
        public bool External => OwnerCategory != null && OwnerCategory.StartsWith("@");
    }

    /// <summary>
    /// How each column format holds ids, to FIND them and to CHANGE them (rename or take out) without touching the rest
    /// of the text. Every format reads and writes the same text the packs and Excel use.
    /// </summary>
    public static class RefFormat
    {
        private static readonly Regex Token = new Regex(@"[A-Za-z0-9_]+", RegexOptions.Compiled);

        private static string[] Split(string v, char sep) => (v ?? "").Split(sep);

        /// <summary>The ids in a cell, with their category. «exists» says if an id is real (for free text formats).</summary>
        public static IEnumerable<(string category, string id, bool removable)> Extract(ColumnSpec col, string value, Func<string, string, bool> exists)
        {
            if (string.IsNullOrWhiteSpace(value) || !col.IsReference) yield break;
            string first = col.Targets[0];
            switch (col.Kind)
            {
                case ColumnKind.Ref:
                    yield return (first, value.Trim(), true);
                    break;
                case ColumnKind.RefList:
                    foreach (var e in value.Split('|', ',')) if (e.Trim().Length > 0) yield return (first, e.Trim(), true);
                    break;
                case ColumnKind.SlashRefs:
                    foreach (var e in value.Split('/', ',')) if (e.Trim().Length > 0) yield return (first, e.Trim(), true);
                    break;
                case ColumnKind.LevelRefs:
                    foreach (var e in Split(value, '|'))
                    {
                        int i = e.IndexOf(':');
                        var id = (i >= 0 ? e.Substring(i + 1) : e).Trim();
                        if (id.Length > 0) yield return (first, id, true);
                    }
                    break;
                case ColumnKind.RefAmounts:
                    foreach (var e in Split(value, '|'))
                    {
                        int i = e.IndexOf(':');
                        var id = (i >= 0 ? e.Substring(0, i) : e).Trim();
                        if (id.Length > 0) yield return (first, id, true);
                    }
                    break;
                case ColumnKind.Evolutions:
                    foreach (var e in Split(value, '|'))
                    {
                        int at = e.IndexOf('@');
                        var target = (at >= 0 ? e.Substring(0, at) : e).Trim();
                        if (target.Length > 0) yield return (first, target, true);
                        if (at < 0) continue;
                        foreach (Match t in Token.Matches(e.Substring(at + 1)))
                            foreach (var cat in col.Targets.Skip(1))
                                if (exists(cat, t.Value)) { yield return (cat, t.Value, true); break; }
                    }
                    break;
                case ColumnKind.Team:
                    foreach (var m in TeamFormat.Parse(value))
                        foreach (var (slot, id) in TeamFormat.Ids(m))
                            if (slot < col.Targets.Count) yield return (col.Targets[slot], id, true);
                    break;
                case ColumnKind.Script:
                    foreach (Match t in Token.Matches(value))
                        foreach (var cat in col.Targets)
                            if (exists(cat, t.Value)) { yield return (cat, t.Value, false); break; }
                    break;
            }
        }

        /// <summary>
        /// The cell with every use of (category, oldId) changed to newId, or taken out when newId is null. «changed» says
        /// whether something was touched; «leftovers» counts uses that could not be taken out (free text: replace only).
        /// </summary>
        public static string Rewrite(ColumnSpec col, string value, string category, string oldId, string newId, out bool changed, out int leftovers)
        {
            changed = false;
            leftovers = 0;
            if (string.IsNullOrEmpty(value) || !col.PointsTo(category)) return value;
            bool Same(string a) => string.Equals(a.Trim(), oldId, StringComparison.OrdinalIgnoreCase);
            bool first = col.Targets[0] == category;
            bool ch = false;
            string result = value;
            switch (col.Kind)
            {
                case ColumnKind.Ref:
                    if (Same(value)) { result = newId ?? ""; ch = true; }
                    break;
                case ColumnKind.RefList:
                {
                    char sep0 = value.Contains('|') || !value.Contains(',') ? '|' : ',';
                    var parts0 = value.Split(sep0).ToList();
                    var output0 = new List<string>();
                    foreach (var p in parts0)
                    {
                        if (!Same(p)) { output0.Add(p); continue; }
                        ch = true;
                        if (newId != null && !output0.Any(o => string.Equals(o.Trim(), newId, StringComparison.OrdinalIgnoreCase))) output0.Add(newId);
                    }
                    result = string.Join(sep0.ToString(), output0);
                    break;
                }
                case ColumnKind.SlashRefs:
                {
                    // Move slots «a,b/c/d»: each slot may have alternatives separated with commas.
                    var slots = value.Split('/').Select(slot =>
                    {
                        var alts = slot.Split(',').ToList();
                        if (!alts.Any(Same)) return slot;
                        ch = true;
                        var kept = new List<string>();
                        foreach (var a in alts)
                        {
                            if (!Same(a)) { kept.Add(a); continue; }
                            if (newId != null && !kept.Any(k => string.Equals(k.Trim(), newId, StringComparison.OrdinalIgnoreCase))) kept.Add(newId);
                        }
                        return string.Join(",", kept);
                    }).Where(x => x.Trim().Length > 0);
                    result = string.Join("/", slots);
                    break;
                }
                case ColumnKind.LevelRefs:
                case ColumnKind.RefAmounts:
                {
                    var output = new List<string>();
                    foreach (var e in value.Split('|'))
                    {
                        int i = e.IndexOf(':');
                        bool idAfter = col.Kind == ColumnKind.LevelRefs;
                        string id = i < 0 ? e : idAfter ? e.Substring(i + 1) : e.Substring(0, i);
                        if (!Same(id)) { output.Add(e); continue; }
                        ch = true;
                        if (newId == null) continue;
                        output.Add(i < 0 ? newId : idAfter ? e.Substring(0, i + 1) + newId : newId + e.Substring(i));
                    }
                    result = string.Join("|", output);
                    break;
                }
                case ColumnKind.Evolutions:
                {
                    var output = new List<string>();
                    foreach (var e in value.Split('|'))
                    {
                        int at = e.IndexOf('@');
                        string target = at >= 0 ? e.Substring(0, at) : e, cond = at >= 0 ? e.Substring(at) : "";
                        bool hit = false;
                        if (first && Same(target)) { hit = true; target = newId; }
                        if (!first && cond.Length > 0)
                        {
                            var c2 = Token.Replace(cond, m => Same(m.Value) ? (newId ?? "\0") : m.Value);
                            if (c2 != cond) { hit = true; cond = c2; }
                        }
                        if (!hit) { output.Add(e); continue; }
                        ch = true;
                        if (newId == null) continue; // an evolution that needs what was removed goes too
                        output.Add(target + cond);
                    }
                    result = string.Join("|", output);
                    break;
                }
                case ColumnKind.Team:
                {
                    int slot = col.Targets.ToList().IndexOf(category);
                    var parts = value.Split('|');
                    var output = new List<string>();
                    foreach (var part in parts)
                    {
                        var m = TeamFormat.ParseMember(part);
                        if (m.Raw != null || !TeamFormat.Ids(m).Any(x => x.slot == slot && Same(x.id))) { output.Add(part); continue; }
                        ch = true;
                        string lead = part.Length - part.TrimStart().Length > 0 ? " " : "", trail = part.Length - part.TrimEnd().Length > 0 ? " " : "";
                        switch (slot)
                        {
                            case 0: if (newId == null) continue; m.Species = newId; break;
                            case 1:
                                int at = m.Moves.FindIndex(Same);
                                m.Moves.RemoveAll(x => Same(x));
                                if (newId != null) m.Moves.Insert(at, newId);
                                break;
                            case 2: m.Item = newId ?? ""; break;
                            case 3: m.Nature = newId ?? ""; break;
                            case 4: m.Ability = newId ?? ""; break;
                        }
                        output.Add(lead + TeamFormat.FormatMember(m) + trail);
                    }
                    result = string.Join("|", output);
                    break;
                }
                case ColumnKind.Script:
                {
                    int left = 0;
                    result = Token.Replace(value, m =>
                    {
                        if (!Same(m.Value)) return m.Value;
                        if (newId == null) { left++; return m.Value; } // free text: it can only be replaced
                        ch = true;
                        return newId;
                    });
                    leftovers = left;
                    break;
                }
            }
            changed = ch;
            return result;
        }
    }

    /// <summary>
    /// WHO USES WHAT: every reference between the sheets (a species in a trainer's team, a move in a learnset, a type in a
    /// move...), plus the ones outside them that the application adds (species in the encounters of the maps).
    /// </summary>
    public sealed class ReferenceIndex
    {
        private readonly Dictionary<(string, string), List<ContentRef>> _uses = new Dictionary<(string, string), List<ContentRef>>();

        public static ReferenceIndex Build(ContentDatabase db, IEnumerable<ContentRef> external = null)
        {
            var index = new ReferenceIndex();
            bool Exists(string cat, string id) => db.Has(cat, id);
            foreach (var table in db.Tables)
            {
                var schema = table.Schema;
                var specs = table.Columns.Select(c => (name: c, spec: schema.Column(c))).Where(c => c.spec != null && c.spec.IsReference).ToList();
                foreach (var rec in table.Records)
                {
                    string owner = table.IdOf(rec);
                    string ownerName = table.NameOf(rec);
                    foreach (var (name, spec) in specs)
                        foreach (var (cat, id, removable) in RefFormat.Extract(spec, rec[name], Exists))
                            index.Add(new ContentRef(cat, id, schema.Key, owner, name, removable, $"{schema.Title}: {ownerName} · {spec.Label}"));
                    if (schema.HeaderTarget != null)
                        foreach (var c in table.Columns.Where(c => c != schema.IdColumn))
                            if (!string.IsNullOrWhiteSpace(rec[c]))
                                index.Add(new ContentRef(schema.HeaderTarget, c, schema.Key, owner, c, false, $"{schema.Title}: {ownerName} contra {c}"));
                }
            }
            if (external != null) foreach (var r in external) index.Add(r);
            return index;
        }

        private void Add(ContentRef r)
        {
            var key = (r.Category, r.Id.ToLowerInvariant());
            if (!_uses.TryGetValue(key, out var list)) _uses[key] = list = new List<ContentRef>();
            list.Add(r);
        }

        /// <summary>Everything that uses that piece of content (not counting itself).</summary>
        public IReadOnlyList<ContentRef> UsesOf(string category, string id) =>
            id != null && _uses.TryGetValue((category, id.ToLowerInvariant()), out var list)
                ? list.Where(r => !(r.OwnerCategory == category && string.Equals(r.OwnerId, id, StringComparison.OrdinalIgnoreCase))).ToList()
                : (IReadOnlyList<ContentRef>)new ContentRef[0];

        public IEnumerable<ContentRef> All => _uses.Values.SelectMany(v => v);
    }
}
