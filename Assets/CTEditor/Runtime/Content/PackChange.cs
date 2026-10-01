using System;
using System.Collections.Generic;
using System.Linq;

namespace CTEditor.Content
{
    /// <summary>What would change in one category when moving the project to a pack.</summary>
    public sealed class CategoryDiff
    {
        public CategorySchema Schema { get; }
        public List<string> Added { get; } = new List<string>();
        /// <summary>Ids in both, with the columns whose value changes.</summary>
        public List<(string id, List<string> columns)> Changed { get; } = new List<(string, List<string>)>();
        /// <summary>Ids the project has and the pack does not (with how many uses they have).</summary>
        public List<(string id, int uses)> Missing { get; } = new List<(string, int)>();
        public int Same { get; set; }

        public CategoryDiff(CategorySchema schema) { Schema = schema; }
        public bool HasChanges => Added.Count > 0 || Changed.Count > 0 || Missing.Count > 0;
    }

    /// <summary>
    /// CHANGING GENERATION (or any pack): compares the project with a pack sheet by sheet, row by row, to show BEFORE
    /// what is added, what changes (and which columns) and what the pack does not have.
    /// </summary>
    public static class PackChange
    {
        public static List<CategoryDiff> Compare(ContentDatabase project, ContentDatabase pack, ReferenceIndex index = null)
        {
            var list = new List<CategoryDiff>();
            foreach (var packTable in pack.Tables)
            {
                var key = packTable.Schema.Key;
                var mine = project.Table(key);
                var diff = new CategoryDiff(packTable.Schema);
                foreach (var r in packTable.Records)
                {
                    var id = packTable.IdOf(r);
                    if (id.Length == 0) continue;
                    var old = mine.Find(id);
                    if (old == null) { diff.Added.Add(id); continue; }
                    var cols = packTable.Columns.Where(c => (old[c] ?? "").Trim() != (r[c] ?? "").Trim()).ToList();
                    if (cols.Count > 0) diff.Changed.Add((id, cols));
                    else diff.Same++;
                }
                foreach (var id in mine.Ids.Where(i => !packTable.Contains(i)))
                    diff.Missing.Add((id, index?.UsesOf(key, id).Count ?? 0));
                list.Add(diff);
            }
            return list;
        }
    }

}
