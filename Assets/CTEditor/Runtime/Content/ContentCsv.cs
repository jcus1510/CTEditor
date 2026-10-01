using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CTEditor.Content
{
    /// <summary>
    /// The CSV of the project's data, as Excel writes it in Spanish: «;» between cells, double quotes around texts that
    /// have «;», quotes or line breaks, optional BOM. Written back the same way (with BOM, so Excel reads the accents).
    /// </summary>
    public static class ContentCsv
    {
        public static List<List<string>> Read(string text)
        {
            var rows = new List<List<string>>();
            if (string.IsNullOrEmpty(text)) return rows;
            var row = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;
            int i = text[0] == '﻿' ? 1 : 0;
            for (; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else if (c == '"') quoted = false;
                    else cell.Append(c);
                    continue;
                }
                switch (c)
                {
                    case '"': quoted = true; break;
                    case ';': row.Add(cell.ToString()); cell.Clear(); break;
                    case '\r': break;
                    case '\n': row.Add(cell.ToString()); cell.Clear(); rows.Add(row); row = new List<string>(); break;
                    default: cell.Append(c); break;
                }
            }
            if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
            // Lines that are completely empty (a trailing blank line) are not rows.
            rows.RemoveAll(r => r.All(string.IsNullOrWhiteSpace));
            return rows;
        }

        public static string Write(IEnumerable<IReadOnlyList<string>> rows)
        {
            var sb = new StringBuilder("﻿");
            foreach (var r in rows)
            {
                sb.Append(string.Join(";", r.Select(Quote)));
                sb.Append("\r\n");
            }
            return sb.ToString();
        }

        private static string Quote(string v)
        {
            v ??= "";
            return v.IndexOfAny(new[] { ';', '"', '\n', '\r' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        }
    }
}
