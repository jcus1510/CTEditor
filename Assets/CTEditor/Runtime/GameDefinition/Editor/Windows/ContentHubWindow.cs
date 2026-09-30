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

        // El orden importa: las habilidades referencian tipos y estados, así que van al final.
        /// <summary>Carpeta del pack de 1ª generación dentro del proyecto.</summary>
        public static string Gen1PackFolder => Path.Combine(Application.dataPath, "GameContent", "Packs", "Gen1");

        /// <summary>Carpeta del pack de 1ª y 2ª generación (251 especies, Pokédex y entrenadores).</summary>
        public static string Gen12PackFolder => Path.Combine(Application.dataPath, "GameContent", "Packs", "Gen1-2");

        /// <summary>Carpeta del pack de la 1ª a la 4ª generación (493 especies, MT, tutor, huevo, habilidades ocultas).</summary>
        public static string Gen14PackFolder => Path.Combine(Application.dataPath, "GameContent", "Packs", "Gen1-4");

        /// <summary>El pack que se usa: el más completo que haya (1ª-4ª, luego 1ª-2ª, luego 1ª).</summary>
        public static string PackFolder => Directory.Exists(Gen14PackFolder) ? Gen14PackFolder
                                         : Directory.Exists(Gen12PackFolder) ? Gen12PackFolder : Gen1PackFolder;

        /// <summary>Nombre del pack para botones y mensajes.</summary>
        public static string PackName => Directory.Exists(Gen14PackFolder) ? "la 1ª a la 4ª generación"
                                       : Directory.Exists(Gen12PackFolder) ? "la 1ª y 2ª generación" : "la 1ª generación";

        /// <summary>
        /// PACK 1ª GENERACIÓN: primero asegura la base clásica (tipos, estados, curvas, Forcejeo...), que es
        /// lo que el pack referencia; luego abre la ventana de Excel sobre el pack, ya analizado y en modo
        /// "solo crear lo que falta", para que el autor vea lo que entra y pulse Aplicar.
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

            CreateClassicBase(new List<string>());
            ItemEditorWindow.CreateClassicSet();          // las piedras que usan las evoluciones del pack
            AbilityEditorWindow.CreateClassicSet(out _); // las habilidades con efecto real, antes que las del pack
            AssetDatabase.SaveAssets();
            RecountIssues();
            CsvWindow.OpenFolder(PackFolder, mode,
                "PACK DE " + PackName.ToUpperInvariant() + ": especies con sus datos de Pokédex (número, altura, peso, descripción), evoluciones (nivel, piedra, " +
                "intercambio, amistad y hora), movimientos (por nivel, MT, tutor y huevo), habilidades (1ª, 2ª y oculta), grupos huevo y entrenadores " +
                "(líderes, Alto Mando y de ruta) con su nivel de IA. Ya se creó la base clásica que necesita (tipos, estados, climas, efectos de lado, " +
                "curvas, objetos, niveles de IA, grupos huevo, Forcejeo).\n\n" +
                (mode == ImportMode.CreateOnly
                    ? "Modo \"solo crear las que faltan\": no se toca nada que ya exista con el mismo id. Revisa el análisis "
                    : "Modo \"actualizar también\": lo que ya existe recibe los datos del pack (el id y las referencias se mantienen). Revisa los cambios ") +
                "y pulsa Aplicar. Lo que se ha aproximado o aún no tiene efecto está explicado en INFORME.txt del pack.");
        }

        // Crea la base clásica que falte (no toca lo existente). Devuelve el informe en 'report'.
        private static void CreateClassicBase(List<string> report)
        {
            report.Add($"Tipos: {TypeChartTools.CreateClassicSet()} nuevos (tabla completada)");
            report.Add($"Estados: {ClassicStatusPresets.CreateClassicSet()} nuevos");
            report.Add($"Climas: {WeatherEditorWindow.CreateClassicSet()} nuevos");
            report.Add($"Objetos: {ItemEditorWindow.CreateClassicSet()} nuevos");
            report.Add($"Trampas de campo: {HazardEditorWindow.CreateClassicSet()} nuevas");
            report.Add($"Efectos de lado: {SideConditionEditorWindow.CreateClassicSet()} nuevos");
            report.Add($"Movimientos-plantilla (incluye Forcejeo): {ClassicMovePresets.CreateAll(out _)} nuevos");
            report.Add($"Naturalezas: {NatureEditorWindow.CreateClassicSet()} nuevas");
            report.Add($"Curvas: {GrowthCurveEditorWindow.CreateClassicSet()} nuevas");
            report.Add($"Reglas: {RulesetEditorWindow.CreateClassicSet()} nuevas");
            report.Add($"Menús: {MenuEditorWindow.CreateClassicSet()} nuevos");
            report.Add($"Controles y caja de texto: {ControlsEditorWindow.CreateClassicSet()} nuevos");
            report.Add($"Niveles de IA: {AiLevelEditorWindow.CreateClassicSet()} nuevos");
            report.Add($"Grupos huevo: {EggGroupEditorWindow.CreateClassicSet()} nuevos");
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
            string msg = $"Entrenadores: {t} · Equipos prearmados: {e} · Zonas salvajes: {z} nuevos";
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
