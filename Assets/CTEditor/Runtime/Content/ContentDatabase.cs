using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CTEditor.Content
{
    /// <summary>One piece of content (a species, a move...): its cells by column name.</summary>
    public sealed class ContentRecord
    {
        private readonly Dictionary<string, string> _values;

        public ContentRecord(IDictionary<string, string> values = null)
        {
            _values = new Dictionary<string, string>(values ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        }

        public string this[string column]
        {
            get => _values.TryGetValue(column, out var v) ? v ?? "" : "";
            set => _values[column] = value ?? "";
        }

        public IReadOnlyDictionary<string, string> Values => _values;

        public ContentRecord Clone() => new ContentRecord(_values);

        public bool SameAs(ContentRecord o) =>
            o != null && _values.Keys.Union(o._values.Keys).All(k => this[k] == o[k]);
    }

    /// <summary>
    /// A CSV sheet of the project (all species, all moves...): its columns in their order (including the ones CTEditor
    /// does not know, which are kept) and its rows. The order of the rows is kept too.
    /// </summary>
    public sealed class ContentTable
    {
        public CategorySchema Schema { get; }
        public List<string> Columns { get; } = new List<string>();
        public List<ContentRecord> Records { get; } = new List<ContentRecord>();

        public ContentTable(CategorySchema schema)
        {
            Schema = schema ?? throw new ArgumentNullException(nameof(schema));
            Columns.AddRange(schema.Columns.Select(c => c.Name));
        }

        public string IdOf(ContentRecord r) => r[Schema.IdColumn].Trim();
        public string NameOf(ContentRecord r)
        {
            var n = r[Schema.NameColumn];
            return string.IsNullOrWhiteSpace(n) ? IdOf(r) : n;
        }

        public ContentRecord Find(string id) =>
            string.IsNullOrEmpty(id) ? null : Records.FirstOrDefault(r => string.Equals(IdOf(r), id, StringComparison.OrdinalIgnoreCase));

        public bool Contains(string id) => Find(id) != null;

        public IEnumerable<string> Ids => Records.Select(IdOf).Where(i => i.Length > 0);

        /// <summary>A free id from a name: «Bola Sombra» → «bola_sombra», «bola_sombra_2»...</summary>
        public string NewId(string from)
        {
            var baseId = ContentIds.Normalize(from);
            if (baseId.Length == 0) baseId = Schema.Noun.Replace(' ', '_');
            var id = baseId;
            for (int i = 2; Contains(id); i++) id = baseId + "_" + i;
            return id;
        }

        public void EnsureColumn(string column)
        {
            if (!Columns.Contains(column)) Columns.Add(column);
        }

        public static ContentTable Parse(CategorySchema schema, string text, List<ContentIssue> issues)
        {
            var table = new ContentTable(schema);
            var rows = ContentCsv.Read(text);
            if (rows.Count == 0) return table;
            table.Columns.Clear();
            var header = rows[0].Select(h => h.Trim()).ToList();
            for (int i = 0; i < header.Count; i++)
            {
                var h = header[i].Length == 0 ? $"columna_{i + 1}" : header[i];
                if (table.Columns.Contains(h))
                {
                    issues?.Add(new ContentIssue(ContentIssueLevel.Error, schema.Key, null, h, 1, $"{schema.File}: la columna «{h}» está repetida (se usa la primera)."));
                    h = h + "_" + (i + 1);
                }
                table.Columns.Add(h);
            }
            if (!table.Columns.Contains(schema.IdColumn))
                issues?.Add(new ContentIssue(ContentIssueLevel.Error, schema.Key, null, schema.IdColumn, 1, $"{schema.File}: falta la columna «{schema.IdColumn}»."));
            for (int r = 1; r < rows.Count; r++)
            {
                var row = rows[r];
                var rec = new ContentRecord();
                for (int c = 0; c < table.Columns.Count; c++) rec[table.Columns[c]] = c < row.Count ? row[c] : "";
                if (row.Count > table.Columns.Count && row.Skip(table.Columns.Count).Any(v => !string.IsNullOrWhiteSpace(v)))
                    issues?.Add(new ContentIssue(ContentIssueLevel.Warning, schema.Key, table.IdOf(rec), null, r + 1,
                        $"{schema.File}, fila {r + 1}: tiene más celdas que columnas (¿un «;» de más?). Las de más se pierden al guardar."));
                table.Records.Add(rec);
            }
            return table;
        }

        public string ToCsv()
        {
            var rows = new List<IReadOnlyList<string>> { Columns };
            rows.AddRange(Records.Select(r => (IReadOnlyList<string>)Columns.Select(c => r[c]).ToList()));
            return ContentCsv.Write(rows);
        }

        public ContentTable Clone()
        {
            var t = new ContentTable(Schema);
            t.Columns.Clear();
            t.Columns.AddRange(Columns);
            t.Records.AddRange(Records.Select(r => r.Clone()));
            return t;
        }
    }

    public static class ContentIds
    {
        /// <summary>«Bola Sombra!» → «bola_sombra» (no accents, no spaces, lower case).</summary>
        public static string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            var d = text.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder();
            foreach (var ch in d)
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
                if (char.IsLetterOrDigit(ch)) sb.Append(ch);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_');
            }
            return sb.ToString().Trim('_');
        }

        public static bool IsValid(string id) => !string.IsNullOrEmpty(id) && id == Normalize(id);
    }

    /// <summary>
    /// ALL the content of a project, read from its «datos/» folder (one CSV per category). Files that do not exist are
    /// empty categories; a file that cannot be read is reported and left untouched (it is not overwritten). Saves only
    /// what changed.
    /// </summary>
    public sealed class ContentDatabase
    {
        private readonly Dictionary<string, ContentTable> _tables = new Dictionary<string, ContentTable>();
        private readonly HashSet<string> _dirty = new HashSet<string>();
        private readonly HashSet<string> _broken = new HashSet<string>();

        public string Folder { get; }
        public List<ContentIssue> LoadIssues { get; } = new List<ContentIssue>();
        public event Action<string> Changed;

        public ContentDatabase(string folder = null) { Folder = folder; }

        public IEnumerable<ContentTable> Tables => _tables.Values;
        public bool IsDirty => _dirty.Count > 0;
        public IReadOnlyCollection<string> DirtyCategories => _dirty;

        public ContentTable Table(string category)
        {
            if (_tables.TryGetValue(category, out var t)) return t;
            var schema = ContentSchemas.Find(category) ?? throw new ArgumentException($"No existe la categoría «{category}».");
            return _tables[category] = new ContentTable(schema);
        }

        /// <summary>That id exists (a row, or one the game knows without a row, like the classic curves).</summary>
        public bool Has(string category, string id) =>
            (_tables.TryGetValue(category, out var t) && t.Contains(id))
            || (ContentSchemas.Find(category)?.BuiltIn.Contains(id, StringComparer.OrdinalIgnoreCase) ?? false)
            || ClassicContent.Has(category, id);

        /// <summary>The name of a piece of content (its row, or the classic one the engine has); null if it does not exist.</summary>
        public string NameOf(string category, string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            if (_tables.TryGetValue(category, out var t) && t.Find(id) is ContentRecord r) return t.NameOf(r);
            return ClassicContent.NameOf(category, id) ?? (ContentSchemas.Find(category)?.BuiltIn.Contains(id, StringComparer.OrdinalIgnoreCase) == true ? id : null);
        }

        public void Put(ContentTable table)
        {
            _tables[table.Schema.Key] = table;
            Version++;
        }

        /// <summary>Goes up with every change: caches (checks, lists) know when to recompute.</summary>
        public int Version { get; private set; }

        public static ContentDatabase Load(string folder)
        {
            var db = new ContentDatabase(folder);
            foreach (var schema in ContentSchemas.All)
            {
                var path = Path.Combine(folder, schema.File);
                if (!File.Exists(path)) continue;
                try { db._tables[schema.Key] = ContentTable.Parse(schema, File.ReadAllText(path), db.LoadIssues); }
                catch (Exception e)
                {
                    db._broken.Add(schema.Key);
                    db.LoadIssues.Add(new ContentIssue(ContentIssueLevel.Error, schema.Key, null, null, 0, $"{schema.File}: no se pudo leer ({e.Message}). No se tocará al guardar."));
                }
            }
            return db;
        }

        public void MarkDirty(string category)
        {
            Version++;
            _dirty.Add(category);
            Changed?.Invoke(category);
        }

        /// <summary>Writes the changed sheets (each through a temporary file, so a failure never leaves half a file).</summary>
        public IReadOnlyList<string> Save()
        {
            var saved = new List<string>();
            if (Folder == null) { _dirty.Clear(); return saved; }
            Directory.CreateDirectory(Folder);
            foreach (var key in _dirty.ToList())
            {
                if (_broken.Contains(key) || !_tables.TryGetValue(key, out var t)) continue;
                var path = Path.Combine(Folder, t.Schema.File);
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, t.ToCsv());
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                _dirty.Remove(key);
                saved.Add(t.Schema.File);
            }
            return saved;
        }
    }
}
