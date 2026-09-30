using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CTEditor.GameDefinition.Infrastructure.Catalog;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// El "tema" visual de CTEditor: un COLOR por categoría (así cada editor se reconoce de un vistazo),
    /// bandas de título, chips, secciones y la GUÍA RÁPIDA plegable con pasos numerados.
    ///
    /// Todos los editores usan estas piezas: si mañana quieres otro estilo, se cambia aquí.
    /// </summary>
    public static class EditorTheme
    {
        // ---------------- Colores por categoría ----------------

        public static readonly Color Moves = new Color(0.95f, 0.55f, 0.30f);
        public static readonly Color Species = new Color(0.40f, 0.78f, 0.45f);
        public static readonly Color Status = new Color(0.62f, 0.40f, 0.85f);
        public static readonly Color Abilities = new Color(0.30f, 0.72f, 0.72f);
        public static readonly Color Types = new Color(0.90f, 0.35f, 0.40f);
        public static readonly Color Items = new Color(0.95f, 0.78f, 0.25f);
        public static readonly Color Weathers = new Color(0.40f, 0.62f, 0.95f);
        public static readonly Color Curves = new Color(0.55f, 0.85f, 0.95f);
        public static readonly Color Natures = new Color(0.95f, 0.55f, 0.75f);
        public static readonly Color Rules = new Color(0.60f, 0.62f, 0.68f);
        public static readonly Color Tools = new Color(0.45f, 0.55f, 0.85f);
        public static readonly Color Trainers = new Color(0.85f, 0.45f, 0.55f);
        public static readonly Color Teams = new Color(0.50f, 0.70f, 0.95f);
        public static readonly Color Zones = new Color(0.45f, 0.80f, 0.55f);
        public static readonly Color Hazards = new Color(0.75f, 0.60f, 0.40f);
        public static readonly Color SideConditions = new Color(0.45f, 0.72f, 0.92f);
        public static readonly Color Interface = new Color(0.62f, 0.55f, 0.92f);
        public static readonly Color Ok = new Color(0.35f, 0.75f, 0.40f);
        public static readonly Color Warn = new Color(0.95f, 0.70f, 0.20f);
        public static readonly Color Bad = new Color(0.90f, 0.35f, 0.30f);

        /// <summary>Color de una categoría de contenido (por su carpeta).</summary>
        public static Color ForCategory(string category)
        {
            switch (category)
            {
                case ContentFolders.Moves: return Moves;
                case ContentFolders.Species: return Species;
                case ContentFolders.Status: return Status;
                case ContentFolders.Abilities: return Abilities;
                case ContentFolders.Types: return Types;
                case ContentFolders.Items: return Items;
                case ContentFolders.Weathers: return Weathers;
                case ContentFolders.Curves: return Curves;
                case ContentFolders.Natures: return Natures;
                case ContentFolders.Rulesets: return Rules;
                case ContentFolders.Trainers: return Trainers;
                case ContentFolders.Teams: return Teams;
                case ContentFolders.Encounters: return Zones;
                case ContentFolders.Hazards: return Hazards;
                case ContentFolders.SideConditions: return SideConditions;
                case ContentFolders.Menus: return Interface;
                case ContentFolders.Interface: return Interface;
                default: return Tools;
            }
        }

        public static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        // ---------------- Estilos (se crean una vez) ----------------

        private static GUIStyle _wrapMini, _wrap, _title, _stepNumber, _pill;

        /// <summary>Texto pequeño que SE AJUSTA al ancho (nunca se corta).</summary>
        public static GUIStyle WrapMini => _wrapMini ?? (_wrapMini = new GUIStyle(EditorStyles.wordWrappedMiniLabel) { wordWrap = true, richText = true });
        /// <summary>Texto normal que se ajusta al ancho.</summary>
        public static GUIStyle Wrap => _wrap ?? (_wrap = new GUIStyle(EditorStyles.wordWrappedLabel) { wordWrap = true, richText = true });
        private static GUIStyle TitleStyle => _title ?? (_title = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 });
        private static GUIStyle StepNumber => _stepNumber ?? (_stepNumber = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter });
        private static GUIStyle Pill => _pill ?? (_pill = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter });

        /// <summary>Ancho útil aproximado de la zona actual (para medir cuánto ocupa un texto).</summary>
        public static float UsableWidth(float reserved = 0f) => Mathf.Max(120f, EditorGUIUtility.currentViewWidth - 40f - reserved);

        /// <summary>Alto que necesita un texto con un estilo y un ancho dados.</summary>
        public static float HeightOf(string text, GUIStyle style, float width)
            => string.IsNullOrEmpty(text) ? 0f : Mathf.Max(16f, style.CalcHeight(new GUIContent(text), width));

        // ---------------- Piezas ----------------

        /// <summary>
        /// Banda de título con el color de la categoría (arriba de cada editor). El subtítulo se AJUSTA
        /// al ancho de la ventana: por largo que sea, se lee entero (antes se cortaba en una línea).
        /// </summary>
        public static void TitleBand(string title, string subtitle, Color color, string icon = null, bool navigation = true)
        {
            float w = UsableWidth();
            float subH = HeightOf(subtitle, WrapMini, w - 16f);
            var r = EditorGUILayout.GetControlRect(GUILayout.Height(28f + subH + (subH > 0 ? 6f : 0f)));
            EditorGUI.DrawRect(r, WithAlpha(color, 0.28f));
            EditorGUI.DrawRect(new Rect(r.x, r.y, 6, r.height), color);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 2, r.width, 2), WithAlpha(color, 0.9f));

            // Navegación: la categoría (miga de pan), volver al Centro y saltar a cualquier otro editor.
            float navW = 0f;
            if (navigation)
            {
                navW = 124f;
                var entry = EditorCatalog.All.FirstOrDefault(e => e.Title == title);
                if (entry != null)
                {
                    var info = EditorCatalog.Info(entry.Category);
                    title = $"{info.name}  ›  {title}";
                }
                if (GUI.Button(new Rect(r.xMax - navW, r.y + 4, 34, 20), new GUIContent("🏠", "Volver al Centro de Contenido"), EditorStyles.miniButton))
                    ContentHubWindow.Open();
                if (GUI.Button(new Rect(r.xMax - navW + 38, r.y + 4, 82, 20), new GUIContent("Ir a… ▾", "Abrir otro editor"), EditorStyles.miniButton))
                    ShowEditorMenu();
            }
            EditorGUI.LabelField(new Rect(r.x + 14, r.y + 4, r.width - 14 - navW, 22), (icon != null ? icon + "  " : "") + title, TitleStyle);
            if (subH > 0)
                EditorGUI.LabelField(new Rect(r.x + 14, r.y + 26, r.width - 20, subH), subtitle, WrapMini);
            EditorGUILayout.Space(2);
        }

        /// <summary>Menú con todos los editores por categoría ("Criaturas/Especies"...).</summary>
        public static void ShowEditorMenu()
        {
            var menu = new GenericMenu();
            foreach (var e in EditorCatalog.All)
            {
                var info = EditorCatalog.Info(e.Category);
                var label = new GUIContent($"{info.icon} {info.name}/{e.Icon} {e.Title}{(e.ComingSoon ? "  (pronto)" : "")}");
                if (e.ComingSoon) menu.AddDisabledItem(label);
                else { var open = e.Open; menu.AddItem(label, false, () => open()); }
            }
            menu.ShowAsContext();
        }

        /// <summary>Etiqueta de color de ancho completo ("FÍSICO · Fuego"). Se ajusta si el texto es largo.</summary>
        public static void Chip(string text, Color color)
        {
            float h = Mathf.Max(20f, HeightOf(text, Wrap, UsableWidth(12f)) + 2f);
            var r = EditorGUILayout.GetControlRect(GUILayout.Height(h));
            EditorGUI.DrawRect(r, WithAlpha(color, 0.35f));
            EditorGUI.DrawRect(new Rect(r.x, r.y, 4, r.height), color);
            EditorGUI.LabelField(new Rect(r.x + 8, r.y + 1, r.width - 10, r.height), text, h > 22f ? Wrap : EditorStyles.boldLabel);
        }

        /// <summary>Varias pastillas pequeñas en fila ("Fuego", "Físico", "Contacto").</summary>
        public static void Pills(params (string text, Color color)[] pills)
        {
            var r = EditorGUILayout.GetControlRect(GUILayout.Height(18));
            float x = r.x;
            foreach (var (text, color) in pills)
            {
                if (string.IsNullOrEmpty(text)) continue;
                float w = Mathf.Max(40f, text.Length * 7f + 14f);
                if (x + w > r.xMax) break;
                var pr = new Rect(x, r.y + 1, w, 16);
                EditorGUI.DrawRect(pr, WithAlpha(color, 0.55f));
                EditorGUI.LabelField(pr, text, Pill);
                x += w + 4f;
            }
        }

        /// <summary>Título de sección con una línea de color debajo.</summary>
        public static void Section(string title, Color color)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            var r = EditorGUILayout.GetControlRect(GUILayout.Height(2));
            EditorGUI.DrawRect(r, WithAlpha(color, 0.8f));
        }

        /// <summary>Párrafo que se ajusta al ancho (para descripciones largas: nunca se cortan).</summary>
        public static void Paragraph(string text, bool small = true)
        {
            if (string.IsNullOrEmpty(text)) return;
            var style = small ? WrapMini : Wrap;
            float h = HeightOf(text, style, UsableWidth());
            EditorGUI.LabelField(EditorGUILayout.GetControlRect(GUILayout.Height(h)), text, style);
        }

        /// <summary>Consejo con icono y fondo suave (más amable que un HelpBox gris).</summary>
        public static void Tip(string text, Color color, string icon = "💡")
        {
            if (string.IsNullOrEmpty(text)) return;
            float h = HeightOf(text, WrapMini, UsableWidth(30f)) + 6f;
            var r = EditorGUILayout.GetControlRect(GUILayout.Height(Mathf.Max(22f, h)));
            EditorGUI.DrawRect(r, WithAlpha(color, 0.16f));
            EditorGUI.DrawRect(new Rect(r.x, r.y, 3, r.height), color);
            EditorGUI.LabelField(new Rect(r.x + 6, r.y + 2, 20, 18), icon);
            EditorGUI.LabelField(new Rect(r.x + 26, r.y + 3, r.width - 30, r.height - 4), text, WrapMini);
        }

        /// <summary>Abre una "tarjeta": caja con borde de color. Ciérrala con EndCard().</summary>
        public static void BeginCard(Color color, string title = null)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            if (title != null)
            {
                var r = EditorGUILayout.GetControlRect(GUILayout.Height(20));
                EditorGUI.DrawRect(r, WithAlpha(color, 0.30f));
                EditorGUI.DrawRect(new Rect(r.x, r.y, 4, r.height), color);
                EditorGUI.LabelField(new Rect(r.x + 8, r.y + 1, r.width - 8, 18), title, EditorStyles.boldLabel);
            }
        }

        public static void EndCard() => EditorGUILayout.EndVertical();

        /// <summary>Barra de progreso de color (Centro de Contenido: primeros pasos).</summary>
        public static void Progress(float fraction, string text, Color color)
        {
            var r = EditorGUILayout.GetControlRect(GUILayout.Height(18));
            EditorGUI.DrawRect(r, new Color(0.2f, 0.2f, 0.2f, 0.25f));
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width * Mathf.Clamp01(fraction), r.height), WithAlpha(color, 0.8f));
            EditorGUI.LabelField(new Rect(r.x + 6, r.y, r.width - 6, r.height), text, EditorStyles.miniLabel);
        }

        /// <summary>Barra horizontal de un valor (estadísticas base, porcentajes). max = el 100% de la barra.</summary>
        public static void Bar(string label, float value, float max, Color color, string valueText = null)
        {
            var r = EditorGUILayout.GetControlRect(GUILayout.Height(16));
            float lw = Mathf.Min(130f, r.width * 0.35f);
            EditorGUI.LabelField(new Rect(r.x, r.y, lw, 16), label, EditorStyles.miniLabel);
            var bar = new Rect(r.x + lw, r.y + 2, r.width - lw - 44f, 12);
            EditorGUI.DrawRect(bar, new Color(0.2f, 0.2f, 0.2f, 0.25f));
            EditorGUI.DrawRect(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(max <= 0 ? 0 : value / max), bar.height), WithAlpha(color, 0.85f));
            EditorGUI.LabelField(new Rect(bar.xMax + 4, r.y, 40, 16), valueText ?? value.ToString("0.#"), EditorStyles.miniBoldLabel);
        }

        // ---------------- Ayudas visibles bajo los campos ----------------

        private const string FieldHelpKey = "CTEditor.MostrarAyudas";

        /// <summary>
        /// ¿Mostrar la ayuda de cada campo DEBAJO de él (además de al pasar el ratón)? Útil cuando la
        /// ventana es estrecha y las etiquetas no caben. Se recuerda entre sesiones.
        /// </summary>
        public static bool ShowFieldHelp
        {
            get => EditorPrefs.GetBool(FieldHelpKey, false);
            set => EditorPrefs.SetBool(FieldHelpKey, value);
        }

        // ---------------- Guía rápida ----------------

        private static readonly Dictionary<string, bool> GuideOpen = new Dictionary<string, bool>();

        /// <summary>
        /// Guía rápida plegable con pasos numerados. Recuerda si estaba abierta (por ventana). La
        /// primera vez aparece ABIERTA para que el autor nuevo la vea.
        /// </summary>
        public static void Guide(string key, string[] steps, Color color)
        {
            if (steps == null || steps.Length == 0) return;
            string prefKey = "CTEditor.Guide." + key;
            if (!GuideOpen.TryGetValue(key, out bool open)) open = EditorPrefs.GetBool(prefKey, true);

            bool now = EditorGUILayout.Foldout(open, "📖 Guía rápida (cómo se usa)", true);
            if (now != open) EditorPrefs.SetBool(prefKey, now);
            GuideOpen[key] = now;
            if (!now) return;

            for (int i = 0; i < steps.Length; i++)
            {
                float h = Mathf.Max(22f, HeightOf(steps[i], Wrap, UsableWidth(34f)) + 2f);
                var r = EditorGUILayout.GetControlRect(GUILayout.Height(h));
                EditorGUI.DrawRect(new Rect(r.x, r.y + 2, 22, 18), WithAlpha(color, 0.85f));
                EditorGUI.LabelField(new Rect(r.x, r.y + 2, 22, 18), (i + 1).ToString(), StepNumber);
                EditorGUI.LabelField(new Rect(r.x + 28, r.y, r.width - 30, r.height), steps[i], Wrap);
            }
            EditorGUILayout.Space();
        }
    }
}
