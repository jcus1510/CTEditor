using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;

namespace CTEditor.GameDefinition.Editor.Csv
{
    /// <summary>Resultado del análisis de UN archivo: qué pasaría con cada fila.</summary>
    public sealed class FileAnalysis
    {
        public CsvSchema Schema;
        public string Path;
        public List<RowPlan> Rows = new List<RowPlan>();
        public string LoadError;
        public List<string> UnknownColumns = new List<string>();

        public int New => Rows.Count(r => r.IsNew && !r.Skipped && r.Errors.Count == 0);
        public int Changed => Rows.Count(r => !r.IsNew && !r.Skipped && r.Errors.Count == 0 && r.Changes.Count > 0);
        public int Unchanged => Rows.Count(r => !r.IsNew && !r.Skipped && r.Errors.Count == 0 && r.Changes.Count == 0);
        public int Skipped => Rows.Count(r => r.Skipped);
        public int WithErrors => Rows.Count(r => r.Errors.Count > 0);
        public int Warnings => Rows.Sum(r => r.Warnings.Count);
    }

    /// <summary>
    /// El motor de EXCEL: exporta todo a una carpeta de CSV y los vuelve a importar en dos pasos:
    ///   1) ANALIZAR (no toca nada): calcula filas nuevas, cambios, errores y avisos por archivo.
    ///   2) APLICAR: primero guarda una copia de seguridad (exporta lo actual), luego crea/actualiza.
    /// El orden de importación respeta las dependencias (tipos → estados → ... → movimientos → especies).
    /// </summary>
    public static class CsvImporter
    {
        /// <summary>Carpeta por defecto: "Excel" junto a Assets (fuera de Assets para que Unity no la importe).</summary>
        public static string DefaultFolder
            => Path.Combine(Directory.GetParent(UnityEngine.Application.dataPath).FullName, "Excel");

        // ---------------- Exportar ----------------

        /// <summary>Exporta todas las categorías (una hoja CSV por categoría) + LEEME.txt con la ayuda.</summary>
        public static List<string> ExportAll(string folder) => Export(folder, null);

        /// <summary>
        /// Exporta SOLO las categorías elegidas (por nombre de archivo; null = todas) + LEEME.txt. Sirve para
        /// compartir una parte de tu configuración (p. ej. solo movimientos y especies).
        /// </summary>
        public static List<string> Export(string folder, ICollection<string> onlyFiles)
        {
            Directory.CreateDirectory(folder);
            var written = new List<string>();
            foreach (var schema in CsvSchemas.All())
            {
                if (onlyFiles != null && !onlyFiles.Contains(schema.FileName)) continue;
                string path = Path.Combine(folder, schema.FileName);
                schema.Export().Save(path);
                written.Add(path);
            }
            File.WriteAllText(Path.Combine(folder, "LEEME.txt"), HelpText(), new UTF8Encoding(true));
            return written;
        }

        /// <summary>Instrucciones para el autor: cómo editar cada hoja y qué significa cada columna.</summary>
        public static string HelpText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("CTEditor — Datos del juego en Excel");
            sb.AppendLine("====================================");
            sb.AppendLine("Cada archivo .csv es una hoja: una FILA por ficha, identificada por su columna 'id'.");
            sb.AppendLine("  • Ábrelos con Excel (o LibreOffice / Google Sheets). Guarda como 'CSV UTF-8' o 'CSV (delimitado por punto y coma)'.");
            sb.AppendLine("  • Filas con un id NUEVO crean fichas; filas con un id existente las actualizan.");
            sb.AppendLine("  • Borrar una fila NO borra la ficha (para borrar usa el editor de su categoría).");
            sb.AppendLine("  • Puedes quitar columnas que no quieras tocar: solo se aplican las que estén.");
            sb.AppendLine("  • Antes de aplicar, Unity te enseña qué va a cambiar y guarda una copia de seguridad.");
            sb.AppendLine("  • Listas: separa con |   Pares: clave:valor   Decimales: coma o punto.");
            sb.AppendLine();
            foreach (var schema in CsvSchemas.All())
            {
                sb.AppendLine($"── {schema.FileName}  ({schema.Title})");
                foreach (var (header, help) in schema.ColumnHelp) sb.AppendLine($"   {header,-24} {help}");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        // ---------------- Analizar ----------------

        /// <summary>Lee los CSV conocidos que haya en la carpeta y calcula qué haría cada fila, SIN aplicar nada.</summary>
        public static List<FileAnalysis> Analyze(string folder, ImportMode mode, out ImportContext context)
        {
            var pairs = new List<(CsvSchema, string)>();
            foreach (var schema in CsvSchemas.All())
            {
                string path = Path.Combine(folder, schema.FileName);
                if (File.Exists(path)) pairs.Add((schema, path));
            }
            // CSV con otros nombres (compartidos por otra persona): se adivina su categoría por las cabeceras.
            if (Directory.Exists(folder))
                foreach (var path in Directory.GetFiles(folder, "*.csv"))
                {
                    if (pairs.Any(p => string.Equals(Path.GetFullPath(p.Item2), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))) continue;
                    var guess = Detect(path);
                    if (guess != null && !pairs.Any(p => p.Item1 == guess)) pairs.Add((guess, path));
                }
            return AnalyzeFiles(pairs, mode, out context);
        }

        /// <summary>
        /// ¿A qué categoría pertenece un CSV? Primero por el nombre del archivo (especies.csv...); si no,
        /// por sus cabeceras (la categoría con más columnas reconocidas). Null = no se reconoce.
        /// </summary>
        public static CsvSchema Detect(string path)
        {
            var all = CsvSchemas.All();
            string name = Path.GetFileName(path);
            var byName = all.FirstOrDefault(s => string.Equals(s.FileName, name, StringComparison.OrdinalIgnoreCase));
            if (byName != null) return byName;
            CsvTable table;
            try { table = CsvTable.Load(path); } catch (Exception) { return null; }
            return DetectByHeaders(table.Headers);
        }

        public static CsvSchema DetectByHeaders(IList<string> headers)
        {
            var all = CsvSchemas.All();
            if (headers.Count > 0 && headers[0].Contains("\\")) return all.FirstOrDefault(s => s is TypeChartCsvSchema);
            CsvSchema best = null; int bestScore = 1;
            foreach (var s in all)
            {
                var accepted = new HashSet<string>(s.AcceptedHeaders, StringComparer.OrdinalIgnoreCase);
                int score = headers.Count(h => accepted.Contains(h));
                if (score > bestScore) { best = s; bestScore = score; }
            }
            return best;
        }

        /// <summary>Analiza una lista concreta de archivos (con su categoría ya decidida), SIN aplicar nada.</summary>
        public static List<FileAnalysis> AnalyzeFiles(IList<(CsvSchema schema, string path)> sources, ImportMode mode, out ImportContext context)
        {
            context = new ImportContext();
            var files = new List<(FileAnalysis analysis, CsvTable table)>();

            foreach (var (schema, path) in sources.OrderBy(x => x.schema.Order))
            {
                if (!File.Exists(path)) continue;
                var a = new FileAnalysis { Schema = schema, Path = path };
                CsvTable table = null;
                try { table = CsvTable.Load(path); }
                catch (Exception e) { a.LoadError = e is IOException ? "No se pudo leer (¿está abierto en Excel?): " + e.Message : e.Message; }
                if (table != null)
                {
                    var known = new HashSet<string>(schema.ColumnHelp.Select(h => h.header), StringComparer.OrdinalIgnoreCase);
                    if (!(schema is TypeChartCsvSchema))
                    {
                        var accepted = new HashSet<string>(schema.AcceptedHeaders, StringComparer.OrdinalIgnoreCase);
                        a.UnknownColumns.AddRange(table.Headers.Where(h => h.Length > 0 && !accepted.Contains(h)));
                        if (!table.Headers.Any(h => string.Equals(h, "id", StringComparison.OrdinalIgnoreCase)))
                            a.LoadError = "Falta la columna 'id'.";
                    }
                }
                files.Add((a, a.LoadError == null ? table : null));
            }

            // Primero se registran TODOS los ids que existirán, para que una hoja pueda referenciar
            // fichas que crea otra hoja (o la misma) en esta importación.
            // Un fallo inesperado en un archivo se queda en ESE archivo (con el detalle en la Consola): los demás
            // se siguen analizando y se pueden aplicar.
            var failed = new HashSet<FileAnalysis>();
            foreach (var (a, table) in files)
            {
                if (table == null) continue;
                try { a.Schema.RegisterPlanned(table, context); }
                catch (Exception e) { Fail(a, e); failed.Add(a); }
            }
            foreach (var (a, table) in files)
            {
                if (table == null || failed.Contains(a)) continue;
                try { a.Rows = a.Schema.Plan(table, context, mode); }
                catch (Exception e) { Fail(a, e); }
            }

            return files.Select(f => f.analysis).ToList();
        }

        private static void Fail(FileAnalysis a, Exception e)
        {
            UnityEngine.Debug.LogError($"[Excel] Error al analizar {Path.GetFileName(a.Path)}: {e}");
            a.Rows = new List<RowPlan>();
            a.LoadError = $"Error interno al analizar este archivo ({e.GetType().Name}: {e.Message}). El detalle está en la Consola.";
        }

        // ---------------- Aplicar ----------------

        /// <summary>
        /// Aplica un análisis: copia de seguridad → crear/actualizar por orden → guardar.
        /// Las filas con errores se saltan (el resto sí se aplica). Devuelve un resumen legible.
        /// </summary>
        public static string Apply(List<FileAnalysis> analyses, ImportContext context, string backupRoot)
        {
            var report = new List<string>();
            if (!string.IsNullOrEmpty(backupRoot))
            {
                string backup = Path.Combine(backupRoot, "copia_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                ExportAll(backup);
                report.Add("Copia de seguridad: " + backup);
            }

            try
            {
                foreach (var a in analyses.OrderBy(x => x.Schema.Order))
                {
                    if (a.LoadError != null || a.Rows.Count == 0) continue;
                    EditorUtility.DisplayProgressBar("Importando desde Excel", a.Schema.Title, 0.5f);
                    int n;
                    try { n = a.Schema.Apply(a.Rows, context); }
                    catch (Exception e)
                    {
                        UnityEngine.Debug.LogError($"[Excel] Error al aplicar {a.Schema.Title}: {e}");
                        report.Add($"{a.Schema.Title}: ERROR al aplicar ({e.Message}). El detalle está en la Consola.");
                        continue;
                    }
                    report.Add($"{a.Schema.Title}: {n} aplicada(s)" + (a.WithErrors > 0 ? $", {a.WithErrors} con errores saltada(s)" : ""));
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            // Equipos y zonas que apuntaban a fichas borradas y vueltas a crear: se reenlazan por id.
            try
            {
                var relink = ReferenceRelinker.RelinkAll();
                if (relink.Relinked > 0) report.Add($"Referencias reenlazadas por id: {relink.Relinked}");
                if (relink.Unresolved.Count > 0) report.Add($"Referencias sin reenlazar (el id no existe): {relink.Unresolved.Count}. Usa «🔗 Reenlazar por id» para ver cuáles.");
            }
            catch (Exception e) { UnityEngine.Debug.LogError("[Excel] Error al reenlazar: " + e); }
            return string.Join("\n", report);
        }
    }
}
