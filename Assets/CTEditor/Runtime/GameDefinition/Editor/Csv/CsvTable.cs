using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CTEditor.GameDefinition.Editor.Csv
{
    /// <summary>
    /// Lectura y escritura de CSV compatibles con EXCEL (también en español).
    ///
    ///   - Escribe con separador ';' y coma decimal, en UTF-8 con BOM: así Excel en español abre el
    ///     archivo ya separado en columnas y con las tildes bien.
    ///   - Lee con ';' o ',' (lo detecta en la cabecera) y acepta números con punto o con coma: así
    ///     también funciona si el archivo lo guardó un Excel en inglés.
    ///   - Respeta las comillas: una celda "a;b" o con saltos de línea se lee entera.
    ///
    /// No depende de Unity: es texto puro (así se puede testear).
    /// </summary>
    public sealed class CsvTable
    {
        public List<string> Headers { get; } = new List<string>();
        public List<Dictionary<string, string>> Rows { get; } = new List<Dictionary<string, string>>();

        /// <summary>Número de línea (1 = cabecera) de cada fila, para señalar errores al autor.</summary>
        public List<int> LineNumbers { get; } = new List<int>();

        public CsvTable() { }
        public CsvTable(IEnumerable<string> headers) => Headers.AddRange(headers);

        public void AddRow(Dictionary<string, string> row) { Rows.Add(row); LineNumbers.Add(Rows.Count + 1); }

        // ---------------- Escritura ----------------

        public string ToText(char separator = ';')
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(separator.ToString(), Headers.ConvertAll(h => Quote(h, separator))));
            foreach (var row in Rows)
            {
                var cells = new List<string>();
                foreach (var h in Headers) cells.Add(Quote(row.TryGetValue(h, out var v) ? v : "", separator));
                sb.AppendLine(string.Join(separator.ToString(), cells));
            }
            return sb.ToString();
        }

        public void Save(string path, char separator = ';')
            => File.WriteAllText(path, ToText(separator), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        private static string Quote(string v, char sep)
        {
            v = v ?? "";
            bool needs = v.IndexOf(sep) >= 0 || v.IndexOf('"') >= 0 || v.IndexOf('\n') >= 0 || v.IndexOf('\r') >= 0
                         || v.StartsWith(" ") || v.EndsWith(" ");
            return needs ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        }

        // ---------------- Lectura ----------------

        public static CsvTable Load(string path) => Parse(File.ReadAllText(path, Encoding.UTF8));

        public static CsvTable Parse(string text)
        {
            var table = new CsvTable();
            if (string.IsNullOrEmpty(text)) return table;
            if (text[0] == '﻿') text = text.Substring(1); // BOM

            // Línea "sep=;" que a veces añade Excel: se respeta y se salta.
            char sep = ';';
            int start = 0;
            if (text.StartsWith("sep=", StringComparison.OrdinalIgnoreCase) && text.Length > 4)
            {
                sep = text[4];
                start = text.IndexOf('\n') + 1;
            }
            else
            {
                int nl = text.IndexOf('\n');
                string first = nl < 0 ? text : text.Substring(0, nl);
                sep = first.IndexOf(';') >= 0 ? ';' : (first.IndexOf(',') >= 0 ? ',' : (first.IndexOf('\t') >= 0 ? '\t' : ';'));
            }

            var records = SplitRecords(text, start, sep);
            if (records.Count == 0) return table;

            foreach (var h in records[0].cells) table.Headers.Add(h.Trim());
            for (int r = 1; r < records.Count; r++)
            {
                var cells = records[r].cells;
                if (cells.TrueForAll(c => string.IsNullOrWhiteSpace(c))) continue; // fila vacía
                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int c = 0; c < table.Headers.Count; c++)
                    row[table.Headers[c]] = c < cells.Count ? cells[c].Trim() : "";
                table.Rows.Add(row);
                table.LineNumbers.Add(records[r].line);
            }
            return table;
        }

        // Separa registros respetando comillas (una celda entre comillas puede tener ';' o saltos de línea).
        private static List<(List<string> cells, int line)> SplitRecords(string text, int start, char sep)
        {
            var result = new List<(List<string>, int)>();
            var cells = new List<string>();
            var cell = new StringBuilder();
            bool inQuotes = false;
            // Si se saltó la línea "sep=", la cabecera está en la línea 2 (así los números coinciden con Excel).
            int line = start > 0 ? 2 : 1, recordLine = line;

            for (int i = start; i < text.Length; i++)
            {
                char ch = text[i];
                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else { if (ch == '\n') line++; cell.Append(ch); }
                    continue;
                }

                if (ch == '"') inQuotes = true;
                else if (ch == sep) { cells.Add(cell.ToString()); cell.Clear(); }
                else if (ch == '\r') { /* se ignora: el fin de línea lo marca \n */ }
                else if (ch == '\n')
                {
                    cells.Add(cell.ToString()); cell.Clear();
                    result.Add((cells, recordLine));
                    cells = new List<string>();
                    line++; recordLine = line;
                }
                else cell.Append(ch);
            }
            if (cell.Length > 0 || cells.Count > 0) { cells.Add(cell.ToString()); result.Add((cells, recordLine)); }
            return result;
        }

        // ---------------- Números (punto o coma decimal) ----------------

        public static bool TryNumber(string s, out float value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim().Replace(',', '.');
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        public static bool TryInt(string s, out int value)
        {
            value = 0;
            if (TryNumber(s, out var f) && Math.Abs(f - Math.Round(f)) < 1e-4) { value = (int)Math.Round(f); return true; }
            return false;
        }

        /// <summary>Escribe un número con coma decimal (lo que Excel en español entiende como número).</summary>
        public static string Number(float v)
            => Math.Abs(v - Math.Round(v)) < 1e-4 ? ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture)
                                                   : v.ToString("0.####", CultureInfo.InvariantCulture).Replace('.', ',');

        public static bool TryBool(string s, out bool value)
        {
            value = false;
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "si": case "sí": case "s": case "true": case "yes": case "y": case "1": case "x": value = true; return true;
                case "no": case "n": case "false": case "0": case "": value = false; return true;
                default: return false;
            }
        }
    }
}
