using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CTEditor.GameDefinition.Editor.Csv
{
    /// <summary>
    /// Ventana EXCEL / COMPARTIR (menú CTEditor → Herramientas → Excel y compartir), con tres pestañas:
    ///
    ///   ⬆ EXPORTAR  — guarda tu contenido en hojas CSV (todas o solo las categorías que elijas) para
    ///                 editarlo en Excel o mandárselo a otra persona.
    ///   ⬇ IMPORTAR  — trae CSV: una CARPETA entera (un paquete compartido) o ARCHIVOS SUELTOS. Detecta
    ///                 solo a qué categoría pertenece cada archivo, enseña qué cambiaría y, al aplicar,
    ///                 guarda antes una copia de seguridad.
    ///   ? AYUDA     — qué columnas tiene cada hoja y cómo se escribe cada celda.
    /// </summary>
    public sealed class CsvWindow : EditorWindow
    {
        private const string FolderPref = "CTEditor.Csv.Folder";
        private const int MaxRowsShown = 250;
        private static readonly string[] Tabs = { "⬆ Exportar", "⬇ Importar", "? Ayuda de columnas" };

        private int _tab;
        private string _folder;
        private ImportMode _mode = ImportMode.CreateAndUpdate;
        private string _intro;
        private List<FileAnalysis> _analyses;
        private ImportContext _context;
        private readonly Dictionary<string, bool> _open = new Dictionary<string, bool>();
        private bool _onlyProblems;
        private Vector2 _scroll;
        private string _lastReport;

        // Exportar: qué categorías (por archivo). Importar: archivos sueltos elegidos y su categoría.
        private readonly Dictionary<string, bool> _exportPick = new Dictionary<string, bool>();
        private readonly List<(string path, int schema)> _looseFiles = new List<(string, int)>();
        private bool _useLooseFiles;

        private static readonly string[] ModeLabels =
        {
            "Crear nuevas y actualizar existentes",
            "Solo crear las que faltan (no tocar lo existente)"
        };

        [MenuItem(EditorMenus.Tools + "Excel y compartir (CSV)", false, EditorMenus.ToolsOrder + 1)]
        public static void Open() => OpenTab(0);

        /// <summary>Abre la ventana en una pestaña (0 = exportar, 1 = importar, 2 = ayuda).</summary>
        public static void OpenTab(int tab)
        {
            var w = GetWindow<CsvWindow>();
            w.titleContent = new GUIContent("Excel y compartir");
            w.minSize = new Vector2(560, 520);
            w._tab = tab;
            w.Show();
        }

        /// <summary>Abre la pestaña de importar sobre una carpeta concreta (packs) y la analiza directamente.</summary>
        public static void OpenFolder(string folder, ImportMode mode, string intro)
        {
            OpenTab(1);
            var w = GetWindow<CsvWindow>();
            w._folder = folder;
            w._mode = mode;
            w._intro = intro;
            w._useLooseFiles = false;
            w.RunAnalysis();
        }

        private void OnEnable()
        {
            if (string.IsNullOrEmpty(_folder)) _folder = EditorPrefs.GetString(FolderPref, CsvImporter.DefaultFolder);
        }

        // Zoom de texto (Ctrl + rueda, o los botones A− / A+ de los editores).
        private void OnGUI()
        {
            EditorZoom.Begin(this);
            try { DrawWindow(); }
            finally { EditorZoom.End(); }
        }

        private void DrawWindow()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorTheme.TitleBand("Excel y compartir", "Edita tu juego en Excel y comparte configuraciones con otras personas.", EditorTheme.Tools);

            _tab = GUILayout.Toolbar(_tab, Tabs, GUILayout.Height(26));
            EditorGUILayout.Space();

            if (_tab == 0) DrawExport();
            else if (_tab == 1) DrawImport();
            else DrawHelp();

            if (!string.IsNullOrEmpty(_lastReport))
            {
                EditorGUILayout.Space();
                EditorTheme.Chip("Resultado", EditorTheme.Ok);
                EditorGUILayout.HelpBox(_lastReport, MessageType.None);
            }
            EditorGUILayout.EndScrollView();
        }

        // ---------------- Carpeta ----------------

        private void DrawFolder(string label)
        {
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            string edited = EditorGUILayout.TextField(_folder ?? "");
            if (edited != _folder) SetFolder(edited);
            if (GUILayout.Button("Elegir…", GUILayout.Width(70)))
            {
                string picked = EditorUtility.OpenFolderPanel("Carpeta de los CSV", Directory.Exists(_folder) ? _folder : "", "");
                if (!string.IsNullOrEmpty(picked)) SetFolder(picked);
            }
            GUI.enabled = Directory.Exists(_folder);
            if (GUILayout.Button("Abrir", GUILayout.Width(60))) EditorUtility.RevealInFinder(_folder);
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("Usar la carpeta por defecto (Excel/, junto a Assets)", EditorStyles.miniButton)) SetFolder(CsvImporter.DefaultFolder);
        }

        private void SetFolder(string folder)
        {
            _folder = folder;
            _analyses = null;
            _intro = null;
            EditorPrefs.SetString(FolderPref, folder);
        }

        // ---------------- ⬆ Exportar ----------------

        private void DrawExport()
        {
            EditorTheme.Guide("CsvExport", new[]
            {
                "Elige la carpeta donde se guardarán las hojas (por defecto, «Excel» junto a Assets).",
                "Marca qué categorías exportar: todas para editar tu juego en Excel, o solo algunas para compartir.",
                "Pulsa «Exportar». Se crea una hoja .csv por categoría y un LEEME.txt que explica cada columna.",
                "Para compartir, manda esa carpeta (o un .zip de ella). Quien la reciba usa la pestaña «Importar».",
            }, EditorTheme.Tools);

            DrawFolder("Carpeta de destino");
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Qué exportar", EditorStyles.boldLabel);
            var all = CsvSchemas.All();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Todas", EditorStyles.miniButton)) foreach (var s in all) _exportPick[s.FileName] = true;
            if (GUILayout.Button("Ninguna", EditorStyles.miniButton)) foreach (var s in all) _exportPick[s.FileName] = false;
            EditorGUILayout.EndHorizontal();
            foreach (var s in all)
            {
                if (!_exportPick.ContainsKey(s.FileName)) _exportPick[s.FileName] = true;
                _exportPick[s.FileName] = EditorGUILayout.ToggleLeft($"{s.Title}   ({s.FileName})", _exportPick[s.FileName]);
            }

            var chosen = all.Where(s => _exportPick[s.FileName]).Select(s => s.FileName).ToList();
            EditorGUILayout.Space();
            GUI.enabled = chosen.Count > 0;
            if (GUILayout.Button($"⬆ Exportar {chosen.Count} hoja(s)", GUILayout.Height(30)))
            {
                if (Directory.Exists(_folder) && Directory.GetFiles(_folder, "*.csv").Length > 0 &&
                    !EditorUtility.DisplayDialog("Exportar", "La carpeta ya tiene CSV: las hojas elegidas se sobrescribirán con el contenido actual del proyecto.\n¿Seguir?", "Sobrescribir", "Cancelar"))
                    return;
                try
                {
                    var files = CsvImporter.Export(_folder, chosen);
                    _lastReport = $"Exportadas {files.Count} hojas a {_folder} (más LEEME.txt con la ayuda de cada columna).";
                    _analyses = null;
                }
                catch (Exception e) { _lastReport = "No se pudo exportar: " + e.Message; }
            }
            GUI.enabled = true;
        }

        // ---------------- ⬇ Importar ----------------

        private void DrawImport()
        {
            if (_intro != null) EditorGUILayout.HelpBox(_intro, MessageType.Info);
            EditorTheme.Guide("CsvImport", new[]
            {
                "Elige DE DÓNDE: una carpeta entera (un paquete que te compartieron o tu carpeta de Excel) o archivos sueltos.",
                "Los archivos se reconocen solos (por su nombre o sus columnas). Si alguno no, elige su categoría a mano.",
                "Elige el modo: «actualizar» cambia lo existente; «solo crear» añade lo que falta sin tocar nada tuyo.",
                "Pulsa «Analizar»: verás filas nuevas, cambios (antes → después), errores y avisos. No se cambia nada todavía.",
                "Pulsa «Aplicar». Antes se guarda una copia de seguridad de todo en Excel/copias.",
            }, EditorTheme.Tools);

            int source = GUILayout.Toolbar(_useLooseFiles ? 1 : 0, new[] { "📁 Carpeta / paquete", "📄 Archivos sueltos" });
            if ((source == 1) != _useLooseFiles) { _useLooseFiles = source == 1; _analyses = null; }
            EditorGUILayout.Space();

            if (_useLooseFiles) DrawLooseFiles();
            else DrawFolder("Carpeta a importar");

            EditorGUILayout.Space();
            _mode = (ImportMode)EditorGUILayout.Popup("Modo", (int)_mode, ModeLabels);
            if (GUILayout.Button("🔍 Analizar (no cambia nada)", GUILayout.Height(28))) RunAnalysis();

            if (_analyses == null) return;
            if (_analyses.Count == 0)
            {
                EditorGUILayout.HelpBox("No hay ningún CSV reconocible. Nombres esperados: " +
                    string.Join(", ", CsvSchemas.All().Select(s => s.FileName)) + " (o archivos con esas columnas).", MessageType.Warning);
                return;
            }

            _onlyProblems = EditorGUILayout.ToggleLeft("Mostrar solo filas con errores o avisos", _onlyProblems);
            foreach (var a in _analyses) DrawFile(a);

            int toApply = _analyses.Sum(a => a.New + a.Changed);
            int errors = _analyses.Sum(a => a.WithErrors);
            EditorGUILayout.Space();
            if (errors > 0)
                EditorGUILayout.HelpBox($"{errors} fila(s) con errores NO se aplicarán. Corrígelas en Excel y vuelve a analizar, o aplica el resto.", MessageType.Warning);

            GUI.enabled = toApply > 0;
            var old = GUI.backgroundColor;
            if (toApply > 0) GUI.backgroundColor = EditorTheme.Ok;
            if (GUILayout.Button(toApply > 0 ? $"✔ Aplicar {toApply} ficha(s) nuevas o cambiadas" : "Nada que aplicar", GUILayout.Height(32)))
            {
                if (EditorUtility.DisplayDialog("Aplicar",
                        $"Se crearán {_analyses.Sum(a => a.New)} y se actualizarán {_analyses.Sum(a => a.Changed)} ficha(s).\n" +
                        "Antes se guarda una copia de seguridad de todo en la carpeta 'Excel/copias'.", "Aplicar", "Cancelar"))
                {
                    RunAnalysis(); // por si algo cambió desde el análisis
                    string backupRoot = Path.Combine(CsvImporter.DefaultFolder, "copias");
                    _lastReport = CsvImporter.Apply(_analyses, _context, backupRoot);
                    _analyses = null;
                }
            }
            GUI.backgroundColor = old;
            GUI.enabled = true;
        }

        private void DrawLooseFiles()
        {
            var all = CsvSchemas.All();
            var names = all.Select(s => $"{s.Title} ({s.FileName})").ToArray();
            EditorGUILayout.LabelField("Archivos a importar", EditorStyles.boldLabel);
            for (int i = 0; i < _looseFiles.Count; i++)
            {
                var (path, schema) = _looseFiles[i];
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(Path.GetFileName(path), GUILayout.Width(180));
                // Opción 0 = "sin categoría": el archivo no se importa hasta elegir una.
                var options = new[] { "⚠ (elige la categoría)" }.Concat(names).ToArray();
                int picked = EditorGUILayout.Popup(schema + 1, options) - 1;
                if (picked != schema) { _looseFiles[i] = (path, picked); _analyses = null; }
                if (GUILayout.Button("✕", GUILayout.Width(24))) { _looseFiles.RemoveAt(i); _analyses = null; break; }
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("+ Añadir un archivo .csv…"))
            {
                string path = EditorUtility.OpenFilePanel("Elige un CSV", Directory.Exists(_folder) ? _folder : "", "csv");
                if (!string.IsNullOrEmpty(path))
                {
                    var schema = CsvImporter.Detect(path);
                    _looseFiles.Add((path, schema == null ? -1 : all.FindIndex(s => s.FileName == schema.FileName)));
                    _analyses = null;
                }
            }
        }

        private void RunAnalysis()
        {
            _lastReport = null;
            try
            {
                if (_useLooseFiles)
                {
                    var all = CsvSchemas.All();
                    var sources = _looseFiles.Where(f => f.schema >= 0).Select(f => (all[f.schema], f.path)).ToList();
                    _analyses = CsvImporter.AnalyzeFiles(sources, _mode, out _context);
                }
                else
                {
                    if (!Directory.Exists(_folder)) { _analyses = new List<FileAnalysis>(); return; }
                    _analyses = CsvImporter.Analyze(_folder, _mode, out _context);
                }
            }
            catch (Exception e) { _analyses = null; _lastReport = "Error al analizar: " + e.Message; }
        }

        private void DrawFile(FileAnalysis a)
        {
            string key = a.Path;
            _open.TryGetValue(key, out bool open);
            var color = a.LoadError != null || a.WithErrors > 0 ? EditorTheme.Bad : a.Warnings > 0 ? EditorTheme.Warn : EditorTheme.Ok;
            string summary = a.LoadError != null ? "ERROR"
                : $"{a.New} nuevas · {a.Changed} cambios · {a.Unchanged} iguales" +
                  (a.Skipped > 0 ? $" · {a.Skipped} ya existen" : "") +
                  (a.WithErrors > 0 ? $" · {a.WithErrors} con ERRORES" : "") +
                  (a.Warnings > 0 ? $" · {a.Warnings} avisos" : "");
            EditorTheme.Chip($"{a.Schema.Title} ← {Path.GetFileName(a.Path)}   {summary}", color);
            open = EditorGUILayout.Foldout(open, "Ver detalle", true);
            _open[key] = open;

            if (a.LoadError != null) { EditorGUILayout.HelpBox(a.LoadError, MessageType.Error); return; }
            if (a.UnknownColumns.Count > 0)
                EditorGUILayout.HelpBox("Columnas que no se reconocen (se ignoran): " + string.Join(", ", a.UnknownColumns), MessageType.Warning);
            if (!open) return;

            EditorGUI.indentLevel++;
            int shown = 0;
            foreach (var r in a.Rows)
            {
                bool problem = r.Errors.Count > 0 || r.Warnings.Count > 0;
                if (_onlyProblems && !problem) continue;
                if (!problem && !r.HasChanges) continue;
                if (r.Skipped && !problem) continue;
                if (shown++ >= MaxRowsShown) { EditorGUILayout.LabelField("… y más filas (filtra por problemas para verlas)."); break; }

                string head = $"Línea {r.Line} · {(string.IsNullOrEmpty(r.Id) ? "(sin id)" : r.Id)} · {(r.IsNew ? "NUEVA" : "cambia")}";
                if (r.Errors.Count > 0) EditorGUILayout.HelpBox(head + "\n" + string.Join("\n", r.Errors), MessageType.Error);
                else
                {
                    EditorGUILayout.LabelField(head, EditorStyles.boldLabel);
                    if (!r.IsNew)
                        foreach (var (column, before, after) in r.Changes)
                            EditorGUILayout.LabelField($"   {column}: {Short(before)}  →  {Short(after)}", EditorStyles.miniLabel);
                }
                if (r.Warnings.Count > 0) EditorGUILayout.HelpBox(string.Join("\n", r.Warnings), MessageType.Warning);
            }
            if (shown == 0) EditorGUILayout.LabelField("Nada que mostrar.", EditorStyles.miniLabel);
            EditorGUI.indentLevel--;
        }

        private static string Short(string s)
        {
            if (string.IsNullOrEmpty(s)) return "(vacío)";
            return s.Length > 70 ? s.Substring(0, 67) + "…" : s;
        }

        // ---------------- ? Ayuda ----------------

        private void DrawHelp()
        {
            EditorGUILayout.HelpBox("Cada hoja tiene una fila por ficha, identificada por la columna «id». Listas con |, pares clave:valor, " +
                                    "decimales con coma o punto, sí/no con «si» y «no». Las columnas que falten no se tocan.", MessageType.Info);
            foreach (var schema in CsvSchemas.All())
            {
                EditorTheme.Section($"{schema.Title}  ({schema.FileName})", EditorTheme.Tools);
                foreach (var (header, help) in schema.ColumnHelp)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(header, EditorStyles.boldLabel, GUILayout.Width(170));
                    EditorGUILayout.LabelField(help, EditorStyles.wordWrappedLabel);
                    EditorGUILayout.EndHorizontal();
                }
            }
        }
    }
}
