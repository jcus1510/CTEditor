using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEngine;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;
using CTEditor.GameDefinition.Editor.Csv;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// CENTRO DE CONTENIDO (menú CTEditor → Centro de Contenido): la puerta de entrada para el autor.
    ///
    ///   - Un botón que crea TODO el contenido clásico (tipos + tabla, estados, naturalezas, curvas,
    ///     reglas, habilidades) en el orden correcto, para arrancar con una base jugable.
    ///   - Los editores por CATEGORÍAS (Criaturas, Combate, Objetos, Personajes, Mundo, Interfaz,
    ///     Herramientas, Pruebas) desde EditorCatalog, plegables y con buscador. Los que llegarán más
    ///     adelante (mapas, menús, tiendas...) ya tienen su sitio marcado como «Pronto».
    ///   - El resumen de la validación (errores y avisos) con acceso al validador.
    ///
    /// Nada de esto borra ni sobrescribe fichas tuyas: los presets solo crean lo que falta (por id).
    /// </summary>
    public sealed class ContentHubWindow : EditorWindow
    {
        private Vector2 _scroll;
        private int _errors = -1, _warnings = -1;

        [MenuItem(EditorMenus.HubPath, false, EditorMenus.HubOrder)]
        public static void Open()
        {
            var w = GetWindow<ContentHubWindow>();
            w.titleContent = new GUIContent("Centro de Contenido");
            w.minSize = new Vector2(460, 520);
            w.RecountIssues();
            w.Show();
        }

        private void OnEnable() => RecountIssues();

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

            EditorTheme.TitleBand("CTEditor — Centro de Contenido",
                "Todo tu juego se edita desde aquí, sin programar. Cada color es una categoría.", EditorTheme.Tools, navigation: false);

            DrawFirstSteps();

            // ---------------- Compartir ----------------
            EditorTheme.Section("Excel y compartir", EditorTheme.Tools);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("⬆ Exportar a Excel", GUILayout.Height(24))) CsvWindow.OpenTab(0);
            if (GUILayout.Button("⬇ Importar CSV / paquete", GUILayout.Height(24))) CsvWindow.OpenTab(1);
            if (GUILayout.Button("📦 Importar " + PackName, GUILayout.Height(24))) ImportGen1Pack();
            EditorGUILayout.EndHorizontal();
            DrawPackPicker();
            EditorTheme.Paragraph("Para compartir tu configuración, exporta y manda la carpeta; quien la reciba usa «Importar».");

            // ---------------- Editores por categoría ----------------
            DrawCatalog();

            // ---------------- Validación ----------------
            if (_errors < 0) RecountIssues();
            var vColor = _errors > 0 ? EditorTheme.Bad : _warnings > 0 ? EditorTheme.Warn : EditorTheme.Ok;
            EditorTheme.Section("Validación", vColor);
            EditorTheme.Chip(_errors == 0 && _warnings == 0 ? "✔ Todo el contenido es válido."
                : $"{(_errors > 0 ? "✖" : "⚠")} {_errors} error(es) y {_warnings} aviso(s).", vColor);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Revisar de nuevo")) RecountIssues();
            if (GUILayout.Button("Abrir el validador")) ContentValidationWindow.Open();
            if (GUILayout.Button(new GUIContent("🔗 Reenlazar por id", "Arregla los equipos y zonas con referencias rotas (especies o movimientos borrados y vueltos a crear) usando su id.")))
            {
                EditorUtility.DisplayDialog("Reenlazar por id", ReferenceRelinker.RelinkAll().ToString(), "Vale");
                RecountIssues();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndScrollView();
        }

        // ---------------- Primeros pasos (lista con progreso) ----------------

        private void DrawFirstSteps()
        {
            var steps = new (string text, bool done, Action act, string button)[]
            {
                ("Tipos y tabla de tipos", Count<ElementTypeData>() > 0 && Count<TypeChartData>() > 0, TypeEditorWindow.Open, "Tipos"),
                ("Estados alterados", Count<StatusConditionData>() > 0, StatusEditorWindow.Open, "Estados"),
                ("Movimientos", Count<MoveData>() > 0, MoveEditorWindow.Open, "Movimientos"),
                ("Especies (con sus movimientos)", Count<SpeciesData>() > 0, SpeciesEditorWindow.Open, "Especies"),
                ("Objetos (pociones, bolas…)", Count<ItemData>() > 0, ItemEditorWindow.Open, "Objetos"),
                ("Reglas del juego", Count<RulesetData>() > 0, RulesetEditorWindow.Open, "Reglas"),
                ("Entrenadores, equipos y zonas (para probar combates)",
                    Count<TrainerData>() > 0 && Count<TeamPresetData>() > 0 && Count<EncounterZoneData>() > 0, TrainerEditorWindow.Open, "Entrenadores"),
            };
            int done = steps.Count(s => s.done);

            EditorTheme.Section("Primeros pasos", EditorTheme.Ok);
            EditorTheme.Progress(done / (float)steps.Length, $"{done} de {steps.Length} listos", done == steps.Length ? EditorTheme.Ok : EditorTheme.Warn);
            foreach (var st in steps)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField((st.done ? "✔ " : "○ ") + st.text, st.done ? EditorStyles.label : EditorStyles.boldLabel);
                if (GUILayout.Button(st.button, EditorStyles.miniButton, GUILayout.Width(90))) st.act();
                EditorGUILayout.EndHorizontal();
            }
            if (done < steps.Length)
            {
                if (GUILayout.Button("✨ Crear TODO el contenido clásico (no toca lo que ya tengas)", GUILayout.Height(30)))
                    CreateEverythingClassic();
            }
        }

        private static int Count<T>() where T : ScriptableObject => EditorCatalog.Count<T>();

        // ---------------- Catálogo de editores (por categorías, con buscador) ----------------

        private string _search = "";

        private void DrawCatalog()
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("🔎", GUILayout.Width(18));
            _search = EditorGUILayout.TextField(_search);
            if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22))) { _search = ""; GUI.FocusControl(null); }
            EditorGUILayout.EndHorizontal();
            if (string.IsNullOrEmpty(_search)) EditorTheme.Paragraph("Busca un editor por su nombre o por lo que hace (ej. «evolución», «ia», «pociones»). Pulsa una categoría para plegarla.");

            bool searching = !string.IsNullOrWhiteSpace(_search);
            foreach (var cat in EditorCatalog.Categories)
            {
                var entries = EditorCatalog.In(cat).Where(e => e.Matches(_search)).ToList();
                if (entries.Count == 0) continue;
                var info = EditorCatalog.Info(cat);
                string key = "CTEditor.Hub.Fold." + cat;
                bool open = searching || EditorPrefs.GetBool(key, true);
                int ready = entries.Count(e => !e.ComingSoon);

                var r = EditorGUILayout.GetControlRect(GUILayout.Height(24));
                EditorGUI.DrawRect(r, EditorTheme.WithAlpha(info.color, 0.22f));
                EditorGUI.DrawRect(new Rect(r.x, r.y, 4, r.height), info.color);
                if (GUI.Button(new Rect(r.x + 8, r.y, r.width - 8, r.height),
                        new GUIContent($"{(open ? "▾" : "▸")}  {info.icon}  {info.name}   ({ready})", info.description), EditorStyles.boldLabel) && !searching)
                    EditorPrefs.SetBool(key, !open);
                if (!open) continue;
                if (!searching) EditorTheme.Paragraph(info.description);
                foreach (var e in entries) CatalogRow(e);
                if (cat == EditorCategory.Characters && Count<SpeciesData>() > 0 &&
                    (Count<TrainerData>() == 0 || Count<TeamPresetData>() == 0 || Count<EncounterZoneData>() == 0))
                    if (GUILayout.Button("✨ Crear los ejemplos de combate (entrenadores, equipos y zonas)")) CreateBattleExamples(true);
            }
        }

        private static void CatalogRow(EditorEntry e)
        {
            var r = EditorGUILayout.BeginHorizontal();
            EditorGUI.DrawRect(new Rect(r.x + 10, r.y + 3, 3, 14), e.ComingSoon ? EditorTheme.WithAlpha(e.Color, 0.35f) : e.Color);
            GUILayout.Space(18);
            var old = GUI.color;
            if (e.ComingSoon) GUI.color = new Color(old.r, old.g, old.b, 0.55f);
            EditorGUILayout.LabelField(new GUIContent($"{e.Icon} {e.Title}", e.Description), EditorStyles.boldLabel, GUILayout.Width(190));
            if (e.Count != null)
            {
                int n = e.Count();
                EditorGUILayout.LabelField(n == 0 ? "— vacío" : n.ToString(), n == 0 ? EditorStyles.miniLabel : EditorStyles.boldLabel, GUILayout.Width(52));
            }
            else EditorGUILayout.LabelField("", GUILayout.Width(52));
            EditorGUILayout.LabelField(new GUIContent(e.Description, e.Description), EditorStyles.miniLabel); // al pasar el ratón se lee entero
            GUI.color = old;
            if (e.ComingSoon)
            {
                GUI.enabled = false;
                GUILayout.Button(new GUIContent("Pronto", "Este editor llegará en un próximo lote. Su sitio ya está reservado."), GUILayout.Width(60));
                GUI.enabled = true;
            }
            else if (GUILayout.Button("Abrir", GUILayout.Width(60))) e.Open();
            EditorGUILayout.EndHorizontal();
        }

        // ---------------- Packs (uno por generación: Gen1 … Gen6, o los que añadas) ----------------

        /// <summary>Carpeta donde viven los packs (Assets/GameContent/Packs).</summary>
        public static string PacksRoot => Path.Combine(Application.dataPath, "GameContent", "Packs");

        /// <summary>Los packs disponibles: cada carpeta de Packs/ que tenga especies.csv (ordenados: Gen1, Gen2...).</summary>
        public static List<string> AvailablePacks()
            => Directory.Exists(PacksRoot)
                ? Directory.GetDirectories(PacksRoot).Where(d => File.Exists(Path.Combine(d, "especies.csv")))
                    .OrderBy(d => Path.GetFileName(d).Length).ThenBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase).ToList()
                : new List<string>();

        private static string PackPrefKey => "CTEditor.PackElegido." + Application.dataPath.GetHashCode();

        /// <summary>El pack elegido (se recuerda por proyecto). Si no eligió ninguno, el más reciente (Gen6).</summary>
        public static string PackFolder
        {
            get
            {
                var all = AvailablePacks();
                string chosen = EditorPrefs.GetString(PackPrefKey, "");
                return all.FirstOrDefault(p => Path.GetFileName(p) == chosen) ?? all.LastOrDefault() ?? Path.Combine(PacksRoot, "Gen6");
            }
            set { EditorPrefs.SetString(PackPrefKey, Path.GetFileName(value ?? "")); PackTools.ForgetCache(); }
        }

        /// <summary>Nombre de un pack para mensajes: «la 3.ª generación» (o el nombre de su carpeta).</summary>
        public static string NameOf(string folder)
        {
            string n = Path.GetFileName((folder ?? "").TrimEnd('/', '\\'));
            var m = System.Text.RegularExpressions.Regex.Match(n, @"^Gen(\d+)$");
            return m.Success ? $"la {m.Groups[1].Value}.ª generación" : n;
        }

        /// <summary>Nombre corto: «pack 3.ª gen.».</summary>
        public static string ShortNameOf(string folder)
        {
            string n = Path.GetFileName((folder ?? "").TrimEnd('/', '\\'));
            var m = System.Text.RegularExpressions.Regex.Match(n, @"^Gen(\d+)$");
            return m.Success ? $"pack {m.Groups[1].Value}.ª gen." : "pack " + n;
        }

        /// <summary>Nombre del pack elegido para botones y mensajes.</summary>
        public static string PackName => NameOf(PackFolder);

        /// <summary>Desplegable para elegir el pack (Centro de Contenido).</summary>
        public static void DrawPackPicker()
        {
            var all = AvailablePacks();
            if (all.Count == 0) { EditorGUILayout.HelpBox("No hay packs en " + PacksRoot + ".", MessageType.Info); return; }
            int current = Mathf.Max(0, all.IndexOf(PackFolder));
            int picked = EditorGUILayout.Popup(new GUIContent("Pack", "Cada pack es una generación con sus datos originales: especies, " +
                "estadísticas, movimientos, tabla de tipos... (lee su INFORME.txt)."), current,
                all.Select(p => $"{Path.GetFileName(p)} · {NameOf(p)}").ToArray());
            if (picked != current) PackFolder = all[picked];
        }

        /// <summary>
        /// IMPORTAR EL PACK: primero asegura la base que el pack referencia (tipos, estados, climas, objetos,
        /// Forcejeo...) y luego abre la ventana de Excel sobre el pack, ya analizado, para que el autor vea lo
        /// que entra y pulse Aplicar.
        ///
        /// EL PACK MANDA: si la carpeta del pack trae la hoja de una categoría (estados.csv, objetos.csv...),
        /// esa categoría sale del pack y NO se crea desde las plantillas del código (cada generación trae sus
        /// propios datos). Las plantillas solo cubren lo que el pack no trae. En movimientos y habilidades se
        /// crean solo las plantillas cuyo id NO está en el pack (p. ej. Forcejeo), para que una plantilla nunca
        /// gane a los datos del pack en el modo «solo lo que falta».
        /// </summary>
        private void ImportGen1Pack()
        {
            if (!Directory.Exists(PackFolder))
            {
                EditorUtility.DisplayDialog("Pack de " + PackName,
                    "No encuentro la carpeta del pack:\n" + PackFolder + "\n\nCopia la carpeta GameContent del zip dentro de Assets.", "Vale");
                return;
            }
            // ¿Solo lo que falta o también actualizar lo que ya tienes? (0 = primer botón, 1 = Cancelar, 2 = tercero)
            int choice = EditorUtility.DisplayDialogComplex("Pack de " + PackName,
                "¿Qué quieres hacer con lo que YA tienes?\n\n" +
                "• Solo lo que falta: crea lo que no exista; lo tuyo no se toca (ideal si borraste algún Pokémon o movimiento).\n" +
                "• Actualizar también: además pone los datos del pack en lo que ya existe (verás los cambios fila a fila antes de Aplicar). " +
                "El id y las referencias se mantienen: NO hace falta borrar nada antes.",
                "Solo lo que falta", "Cancelar", "Actualizar también");
            if (choice == 1) return;
            var mode = choice == 2 ? ImportMode.CreateAndUpdate : ImportMode.CreateOnly;

            var pack = new PackContents(PackFolder);
            var report = new List<string>();
            try
            {
                CreateClassicBase(report, pack);
                // Habilidades: las del pack mandan. Un pack SIN habilidades (1.ª y 2.ª gen.: aún no existían) no recibe ninguna.
                if (pack.Has(PackTools.AbilitiesFile))
                    report.Add($"Habilidades-plantilla: {AbilityEditorWindow.CreateClassicSet(out _, pack.Ids(PackTools.AbilitiesFile))} nuevas");
                else report.Add("Habilidades: ninguna (en esta generación no existen)");
            }
            catch (Exception e)
            {
                // Antes de este arreglo, un fallo aquí dejaba fichas EN BLANCO (sin id). Ahora no se crea nada a medias:
                // se avisa y no se abre la importación, porque al pack le faltaría la base que referencia.
                Debug.LogException(e);
                AssetDatabase.SaveAssets();
                EditorUtility.DisplayDialog("Pack de " + PackName,
                    "No se pudo preparar la base que necesita el pack:\n\n" + e.Message +
                    "\n\nNo se ha importado nada del pack. El detalle está en la Consola.", "Vale");
                RecountIssues();
                return;
            }
            AssetDatabase.SaveAssets();
            RecountIssues();

            string fromPack = pack.Files.Count > 0 ? string.Join(", ", pack.Files.OrderBy(f => f)) : "(ninguna)";
            CsvWindow.OpenFolder(PackFolder, mode,
                "PACK DE " + PackName.ToUpperInvariant() + ". Hojas que trae el pack: " + fromPack + ".\n" +
                "Lo que el pack no trae se ha creado desde las plantillas clásicas:\n  • " + string.Join("\n  • ", report) + "\n\n" +
                (mode == ImportMode.CreateOnly
                    ? "Modo \"solo crear las que faltan\": no se toca nada que ya exista con el mismo id. Revisa el análisis "
                    : "Modo \"actualizar también\": lo que ya existe recibe los datos del pack (el id y las referencias se mantienen). Revisa los cambios ") +
                "y pulsa Aplicar. Lo que se ha aproximado o aún no tiene efecto está explicado en INFORME.txt del pack.");
        }

        /// <summary>Qué hojas trae la carpeta de un pack y qué ids hay en cada una (para no pisarlas con plantillas).</summary>
        private sealed class PackContents
        {
            private readonly string _folder;
            private readonly Dictionary<string, HashSet<string>> _ids = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<string> Files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public PackContents(string folder)
            {
                _folder = folder;
                if (folder == null || !Directory.Exists(folder)) return;
                foreach (var path in Directory.GetFiles(folder, "*.csv")) Files.Add(Path.GetFileName(path));
            }

            /// <summary>¿El pack trae esta hoja? (entonces esa categoría sale del pack, no de las plantillas)</summary>
            public bool Has(string file) => Files.Contains(file);

            /// <summary>Ids de una hoja del pack (vacío si no la trae o no se puede leer).</summary>
            public HashSet<string> Ids(string file)
            {
                if (_ids.TryGetValue(file, out var set)) return set;
                set = new HashSet<string>();
                if (Has(file))
                {
                    try
                    {
                        foreach (var row in CsvTable.Load(Path.Combine(_folder, file)).Rows)
                            if (row.TryGetValue("id", out var id) && !string.IsNullOrWhiteSpace(id)) set.Add(id.Trim());
                    }
                    catch (Exception e) { Debug.LogWarning($"No se pudo leer {file} del pack: {e.Message}"); }
                }
                _ids[file] = set;
                return set;
            }
        }

        // Crea la base clásica que falte (no toca lo existente). Devuelve el informe en 'report'.
        // Con 'pack': las categorías cuya hoja trae el pack NO se crean desde las plantillas (el pack manda).
        private static void CreateClassicBase(List<string> report, PackContents pack = null)
        {
            void Step(string file, string label, Func<int> create)
            {
                if (pack != null && pack.Has(file)) { report.Add($"{label}: del pack ({file})"); return; }
                report.Add($"{label}: {create()} nuevos");
            }

            // Tipos: la tabla va con ellos (la plantilla la completa); si el pack trae tipos.csv, trae también su tabla.
            Step("tipos.csv", "Tipos", TypeChartTools.CreateClassicSet);
            Step("estados.csv", "Estados", ClassicStatusPresets.CreateClassicSet);
            Step("climas.csv", "Climas", WeatherEditorWindow.CreateClassicSet);
            // Objetos: las plantillas (los que tienen EFECTO configurado) se crean siempre; el objetos.csv del pack solo trae
            // los DEMÁS objetos de su generación (clave, bayas, placas, Megapiedras...), así que no se pisan.
            report.Add($"Objetos con efecto: {ItemEditorWindow.CreateClassicSet()} nuevos" + (pack != null && pack.Has("objetos.csv") ? " (+ el resto, del pack)" : ""));
            Step("trampas.csv", "Trampas de campo", HazardEditorWindow.CreateClassicSet);
            Step("efectos_lado.csv", "Efectos de lado", SideConditionEditorWindow.CreateClassicSet);
            // Movimientos: si el pack trae los suyos, del código SOLO sale Forcejeo (lo necesita el motor): así un pack
            // de 1.ª gen. no recibe movimientos modernos. Sin pack, todas las plantillas.
            var skipMoves = pack != null && pack.Has(PackTools.MovesFile)
                ? new HashSet<string>(ClassicMovePresets.All.Select(p => p.Id).Where(id => id != "struggle"))
                : null;
            report.Add($"Movimientos-plantilla (Forcejeo{(skipMoves == null ? " y los clásicos" : "")}): {ClassicMovePresets.CreateAll(out _, skipMoves)} nuevos");
            Step("naturalezas.csv", "Naturalezas", NatureEditorWindow.CreateClassicSet);
            Step("curvas.csv", "Curvas", GrowthCurveEditorWindow.CreateClassicSet);
            Step("mecanicas.csv", "Mecánicas especiales", MechanicEditorWindow.CreateClassicSet);
            Step("reglas.csv", "Reglas", RulesetEditorWindow.CreateClassicSet);
            report.Add($"Menús: {MenuEditorWindow.CreateClassicSet()} nuevos");
            report.Add($"Controles y caja de texto: {ControlsEditorWindow.CreateClassicSet()} nuevos");
            Step("niveles_ia.csv", "Niveles de IA", AiLevelEditorWindow.CreateClassicSet);
            Step("grupos_huevo.csv", "Grupos huevo", EggGroupEditorWindow.CreateClassicSet);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private void CreateEverythingClassic()
        {
            var report = new List<string>();
            CreateClassicBase(report);
            report.Add($"Habilidades: {AbilityEditorWindow.CreateClassicSet(out var missing)} nuevas");
            if (ContentAssets.LoadAll<SpeciesData>().Count > 0) report.Add(CreateBattleExamples(false));
            else report.Add("Entrenadores, equipos y zonas: se crean cuando tengas especies (importa el pack y vuelve a pulsar)");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            RecountIssues();

            string extra = missing.Count > 0 ? "\n\nAviso: faltan tipos para algunas habilidades: " + string.Join(", ", missing) : "";
            EditorUtility.DisplayDialog("Contenido clásico",
                "Listo. Lo que ya existía (por id) no se tocó.\n\n• " + string.Join("\n• ", report) + extra +
                "\n\nSiguiente paso: crea tus especies y movimientos, o importa " + PackName + ".", "Genial");
        }

        /// <summary>Crea los entrenadores, equipos prearmados y zonas de ejemplo que falten (necesitan especies).</summary>
        private string CreateBattleExamples(bool notify)
        {
            int t = TrainerEditorWindow.CreateClassicSet();
            int e = TeamPresetEditorWindow.CreateClassicSet();
            int z = EncounterZoneEditorWindow.CreateClassicSet();
            AssetDatabase.SaveAssets();
            EditorCatalog.ClearCounts();
            string msg = $"Entrenadores: {t} · Equipos prearmados: {e} · Zonas salvajes: {z} nuevos o reparados";
            if (notify) ShowNotification(new GUIContent(msg));
            return msg;
        }

        private void RecountIssues()
        {
            _errors = 0; _warnings = 0;
            foreach (var issue in ContentValidator.Validate())
            {
                if (issue.Severity == IssueSeverity.Error) _errors++;
                else _warnings++;
            }
        }
    }
}
