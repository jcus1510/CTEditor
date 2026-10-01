using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTEditor.SharedKernel.Editing;

namespace CTEditor.Content
{
    /// <summary>What to do with the uses of something being deleted.</summary>
    public enum DeleteMode
    {
        /// <summary>Every use points to another one instead (Pikachu → Raichu).</summary>
        Replace,
        /// <summary>The uses are taken out (the move leaves the learnsets; a team member leaves the team).</summary>
        RemoveUses,
        /// <summary>The uses stay, pointing to nothing (the Problems window lists them).</summary>
        KeepUses,
    }

    /// <summary>Something deleted, kept to bring it back.</summary>
    public sealed class TrashEntry
    {
        public string Category { get; }
        public ContentRecord Record { get; }
        public DateTime When { get; }
        public TrashEntry(string category, ContentRecord record, DateTime when) { Category = category; Record = record; When = when; }
        public string Id => Record[ContentSchemas.Find(Category)?.IdColumn ?? "id"];
    }

    /// <summary>
    /// CASO DE USO «editar el contenido»: create, change, duplicate, delete and rename species, moves, items... SAFELY:
    /// deleting says who uses it (replace, take out or leave the uses), renaming an id changes it everywhere, deleted things
    /// go to a trash and everything can be undone (Ctrl+Z). Uses outside the sheets (maps) come from <see cref="ExternalUses"/>.
    /// </summary>
    public sealed class ContentSession
    {
        public ContentDatabase Db { get; }
        public CommandHistory History { get; } = new CommandHistory(300);
        public List<TrashEntry> Trash { get; } = new List<TrashEntry>();

        /// <summary>Uses outside the sheets (species in the encounters of the maps...).</summary>
        public Func<IEnumerable<ContentRef>> ExternalUses { get; set; }
        /// <summary>Changes those outside uses: (category, old id, new id or null to take them out).</summary>
        public Action<string, string, string> ExternalRewrite { get; set; }

        /// <summary>A sheet changed (its category).</summary>
        public event Action<string> Changed;
        public event Action<string, string> Message;

        private ReferenceIndex _index;

        public ContentSession(ContentDatabase db)
        {
            Db = db ?? throw new ArgumentNullException(nameof(db));
            LoadTrash();
        }

        public ReferenceIndex Index => _index ??= ReferenceIndex.Build(Db, ExternalUses?.Invoke());

        /// <summary>The references changed outside (a map): rebuilt next time.</summary>
        public void InvalidateIndex() => _index = null;

        public IReadOnlyList<ContentRef> UsesOf(string category, string id) => Index.UsesOf(category, id);

        public ContentRecord Get(string category, string id) => Db.Table(category).Find(id);

        // ── Changes (all undoable) ─────────────────────────────────────────────────────────────

        /// <summary>Runs a change over some sheets and records it as one undo step (the sheets before and after).</summary>
        private void Change(string label, IEnumerable<string> categories, Action change)
        {
            var cats = categories.Distinct().ToList();
            var before = cats.Select(c => Db.Table(c).Clone()).ToList();
            change();
            var after = cats.Select(c => Db.Table(c).Clone()).ToList();
            History.Record(new TablesCommand(this, label, before, after));
            foreach (var c in cats) Touched(c);
        }

        private void Touched(string category)
        {
            Db.MarkDirty(category);
            _index = null;
            Changed?.Invoke(category);
        }

        private sealed class TablesCommand : IEditCommand
        {
            private readonly ContentSession _s;
            private readonly List<ContentTable> _before, _after;
            public string Label { get; }

            public TablesCommand(ContentSession s, string label, List<ContentTable> before, List<ContentTable> after)
            {
                _s = s; Label = label; _before = before; _after = after;
            }

            public void Do() => Put(_after);
            public void Undo() => Put(_before);

            private void Put(List<ContentTable> tables)
            {
                foreach (var t in tables)
                {
                    _s.Db.Put(t.Clone());
                    _s.Touched(t.Schema.Key);
                }
            }
        }

        private sealed class ValueCommand : IEditCommand
        {
            private readonly ContentSession _s;
            private readonly string _cat, _column, _before, _after;
            private string _id;
            public string Label { get; }

            public ValueCommand(ContentSession s, string cat, string id, string column, string before, string after, string label)
            {
                _s = s; _cat = cat; _id = id; _column = column; _before = before; _after = after; Label = label;
            }

            public void Do() => Set(_after);
            public void Undo() => Set(_before);

            private void Set(string v)
            {
                var r = _s.Db.Table(_cat).Find(_id);
                if (r == null) return;
                r[_column] = v;
                _s.Touched(_cat);
            }
        }

        /// <summary>Changes one cell (not the id: use Rename).</summary>
        public bool SetValue(string category, string id, string column, string value)
        {
            var t = Db.Table(category);
            var r = t.Find(id);
            if (r == null || column == t.Schema.IdColumn) return false;
            value ??= "";
            var before = r[column];
            if (before == value) return false;
            t.EnsureColumn(column);
            r[column] = value;
            var spec = t.Schema.ColumnOrText(column);
            History.Record(new ValueCommand(this, category, id, column, before, value, $"cambiar {spec.Label.ToLowerInvariant()} de «{t.NameOf(r)}»"));
            Touched(category);
            return true;
        }

        /// <summary>A new piece of content (empty, or a copy of a template) with a free id made from the name.</summary>
        public ContentRecord Create(string category, string name, ContentRecord template = null)
        {
            var t = Db.Table(category);
            var id = ContentIds.IsValid(name) && !t.Contains(name) ? name : t.NewId(name);
            ContentRecord rec = null;
            Change($"crear {t.Schema.Noun} «{name}»", new[] { category }, () =>
            {
                rec = template?.Clone() ?? new ContentRecord();
                rec[t.Schema.IdColumn] = id;
                if (t.Schema.NameColumn != t.Schema.IdColumn && (template == null || string.IsNullOrWhiteSpace(rec[t.Schema.NameColumn])))
                    rec[t.Schema.NameColumn] = string.IsNullOrWhiteSpace(name) ? id : name.Trim();
                t.Records.Add(rec);
            });
            return Get(category, id);
        }

        public ContentRecord Duplicate(string category, string id)
        {
            var t = Db.Table(category);
            var r = t.Find(id);
            if (r == null) return null;
            var copy = r.Clone();
            var newId = t.NewId(id + "_copia");
            Change($"duplicar «{t.NameOf(r)}»", new[] { category }, () =>
            {
                copy[t.Schema.IdColumn] = newId;
                if (t.Schema.NameColumn != t.Schema.IdColumn) copy[t.Schema.NameColumn] = t.NameOf(r) + " (copia)";
                t.Records.Insert(t.Records.IndexOf(r) + 1, copy);
            });
            return Get(category, newId);
        }

        /// <summary>
        /// Deletes safely: with uses, they are replaced by «replacement» (Replace), taken out (RemoveUses) or left
        /// (KeepUses). The deleted row goes to the trash. One undo step brings everything back.
        /// </summary>
        public bool Delete(string category, string id, DeleteMode mode = DeleteMode.RemoveUses, string replacement = null)
        {
            var t = Db.Table(category);
            var r = t.Find(id);
            if (r == null) return false;
            if (mode == DeleteMode.Replace && (string.IsNullOrEmpty(replacement) || !t.Contains(replacement) || string.Equals(replacement, id, StringComparison.OrdinalIgnoreCase)))
            {
                Message?.Invoke("Elige por cuál sustituirlo (otra ficha que exista).", "aviso");
                return false;
            }
            var uses = UsesOf(category, id);
            var cats = uses.Where(u => !u.External).Select(u => u.OwnerCategory).Append(category).ToList();
            int leftovers = 0;
            var removed = r.Clone();
            Change($"borrar {t.Schema.Noun} «{t.NameOf(r)}»", cats, () =>
            {
                if (mode != DeleteMode.KeepUses)
                    leftovers = RewriteUses(category, id, mode == DeleteMode.Replace ? replacement : null, uses);
                t.Records.Remove(t.Find(id));
            });
            if (mode != DeleteMode.KeepUses && uses.Any(u => u.External))
                ExternalRewrite?.Invoke(category, id, mode == DeleteMode.Replace ? replacement : null);
            Trash.Add(new TrashEntry(category, removed, DateTime.Now));
            SaveTrash();
            if (leftovers > 0)
                Message?.Invoke($"{leftovers} usos en efectos escritos a mano no se pudieron quitar: la ventana Problemas los señala.", "aviso");
            return true;
        }

        /// <summary>Changes an id and every use of it (sheets, the type chart's columns and the maps).</summary>
        public bool Rename(string category, string oldId, string newId)
        {
            var t = Db.Table(category);
            var r = t.Find(oldId);
            newId = (newId ?? "").Trim();
            if (r == null) return false;
            if (!ContentIds.IsValid(newId)) { Message?.Invoke($"«{newId}» no vale como id: minúsculas, números y _ (ej. {ContentIds.Normalize(newId)}).", "aviso"); return false; }
            if (t.Contains(newId) && !string.Equals(newId, oldId, StringComparison.OrdinalIgnoreCase)) { Message?.Invoke($"Ya hay {t.Schema.Un} {t.Schema.Noun} con el id «{newId}».", "aviso"); return false; }
            if (newId == oldId) return false;
            var uses = UsesOf(category, oldId);
            var cats = uses.Where(u => !u.External).Select(u => u.OwnerCategory).Append(category).ToList();
            Change($"renombrar «{oldId}» a «{newId}»", cats, () =>
            {
                RewriteUses(category, oldId, newId, uses);
                t.Find(oldId)[t.Schema.IdColumn] = newId;
            });
            if (uses.Any(u => u.External)) ExternalRewrite?.Invoke(category, oldId, newId);
            Message?.Invoke($"«{oldId}» ahora es «{newId}»" + (uses.Count > 0 ? $" ({uses.Count} usos cambiados)." : "."), "exito");
            return true;
        }

        /// <summary>Points every use to newId (or takes them out). Returns how many free-text uses could not be taken out.</summary>
        private int RewriteUses(string category, string oldId, string newId, IEnumerable<ContentRef> uses)
        {
            int left = 0;
            foreach (var group in uses.Where(u => !u.External).GroupBy(u => u.OwnerCategory))
            {
                var table = Db.Table(group.Key);
                var schema = table.Schema;
                // The type chart: the column named after the type.
                if (schema.HeaderTarget == category)
                {
                    int ci = table.Columns.FindIndex(c => string.Equals(c, oldId, StringComparison.OrdinalIgnoreCase));
                    if (ci >= 0)
                    {
                        if (newId == null) { foreach (var rec in table.Records) rec[table.Columns[ci]] = ""; table.Columns.RemoveAt(ci); }
                        else
                        {
                            foreach (var rec in table.Records) rec[newId] = rec[table.Columns[ci]];
                            table.Columns[ci] = newId;
                        }
                    }
                }
                foreach (var ownerId in group.Select(u => u.OwnerId).Distinct().ToList())
                {
                    var rec = table.Find(ownerId);
                    if (rec == null) continue;
                    foreach (var col in table.Columns)
                    {
                        var spec = schema.Column(col);
                        if (spec == null || !spec.PointsTo(category)) continue;
                        if (col == schema.IdColumn && newId == null && group.Key != category)
                        {
                            table.Records.Remove(rec); // a row of the type chart for a deleted type
                            break;
                        }
                        rec[col] = RefFormat.Rewrite(spec, rec[col], category, oldId, newId, out _, out var l);
                        left += l;
                    }
                }
            }
            return left;
        }

        // ── Changing generation (a pack) ────────────────────────────────────────────────────────

        /// <summary>
        /// Moves the project to a pack, as ONE undo step: rows of the pack replace the ones with the same id (columns only
        /// the project has are kept), new ones are added, and the ones the pack does not have stay or go to the trash.
        /// Only the chosen categories (null = all the pack has).
        /// </summary>
        public int ApplyPack(ContentDatabase pack, bool trashMissing, IEnumerable<string> categories = null)
        {
            var keys = (categories ?? pack.Tables.Select(t => t.Schema.Key)).Where(k => pack.Tables.Any(t => t.Schema.Key == k)).ToList();
            int changed = 0;
            var trashed = new List<TrashEntry>();
            Change("cambiar al pack", keys, () =>
            {
                foreach (var key in keys)
                {
                    var from = pack.Table(key);
                    var mine = Db.Table(key);
                    var columns = from.Columns.ToList();
                    foreach (var c in mine.Columns) if (!columns.Contains(c)) columns.Add(c);
                    var result = new List<ContentRecord>();
                    foreach (var r in from.Records)
                    {
                        var id = from.IdOf(r);
                        var old = mine.Find(id);
                        var rec = old?.Clone() ?? new ContentRecord();
                        foreach (var c in from.Columns) rec[c] = r[c];
                        if (old == null || !old.SameAs(rec)) changed++;
                        result.Add(rec);
                    }
                    foreach (var r in mine.Records.Where(r => !from.Contains(mine.IdOf(r))))
                    {
                        if (trashMissing) { trashed.Add(new TrashEntry(key, r.Clone(), DateTime.Now)); changed++; }
                        else result.Add(r);
                    }
                    mine.Columns.Clear();
                    mine.Columns.AddRange(columns);
                    mine.Records.Clear();
                    mine.Records.AddRange(result);
                }
            });
            if (trashed.Count > 0) { Trash.AddRange(trashed); SaveTrash(); }
            return changed;
        }

        // ── Trash ──────────────────────────────────────────────────────────────────────────────

        private string TrashPath => Db.Folder == null ? null : Path.Combine(Db.Folder, "papelera.csv");

        /// <summary>Puts a deleted row back (with its id; if the id is taken now, with a new one).</summary>
        public ContentRecord Restore(TrashEntry entry)
        {
            if (entry == null || !Trash.Contains(entry)) return null;
            var t = Db.Table(entry.Category);
            var rec = entry.Record.Clone();
            var id = t.IdOf(rec);
            if (t.Contains(id)) rec[t.Schema.IdColumn] = id = t.NewId(id);
            Change($"recuperar «{t.NameOf(rec)}»", new[] { entry.Category }, () => t.Records.Add(rec));
            Trash.Remove(entry);
            SaveTrash();
            return Get(entry.Category, id);
        }

        public void EmptyTrash()
        {
            Trash.Clear();
            SaveTrash();
        }

        /// <summary>«datos/papelera.csv»: categoria; borrado; then the row as «columna=valor» lines.</summary>
        private void SaveTrash()
        {
            if (TrashPath == null) return;
            if (Trash.Count == 0) { if (File.Exists(TrashPath)) File.Delete(TrashPath); return; }
            var rows = new List<IReadOnlyList<string>> { new[] { "categoria", "borrado", "ficha" } };
            foreach (var e in Trash)
                rows.Add(new[] { e.Category, e.When.ToString("yyyy-MM-dd HH:mm"),
                    string.Join("\n", e.Record.Values.Where(kv => kv.Value.Length > 0).Select(kv => kv.Key + "=" + kv.Value.Replace("\n", " "))) });
            Directory.CreateDirectory(Db.Folder);
            File.WriteAllText(TrashPath, ContentCsv.Write(rows));
        }

        private void LoadTrash()
        {
            if (TrashPath == null || !File.Exists(TrashPath)) return;
            try
            {
                foreach (var row in ContentCsv.Read(File.ReadAllText(TrashPath)).Skip(1))
                {
                    if (row.Count < 3 || ContentSchemas.Find(row[0]) == null) continue;
                    var rec = new ContentRecord();
                    foreach (var line in row[2].Split('\n'))
                    {
                        int i = line.IndexOf('=');
                        if (i > 0) rec[line.Substring(0, i)] = line.Substring(i + 1);
                    }
                    DateTime.TryParse(row[1], out var when);
                    Trash.Add(new TrashEntry(row[0], rec, when));
                }
            }
            catch (Exception) { /* a broken trash file is ignored, never fatal */ }
        }

        public IReadOnlyList<string> Save()
        {
            var saved = Db.Save();
            History.MarkSaved();
            return saved;
        }

        public bool Undo() => History.Undo();
        public bool Redo() => History.Redo();
    }
}
