using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CTEditor.Adventure.Domain.Interface;
using CTEditor.GameDefinition.Infrastructure.Acl;
using CTEditor.GameDefinition.Infrastructure.Catalog;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// Editor de CONTROLES Y TEXTO (CTEditor → Interfaz → Controles y caja de texto):
    ///   • Qué teclas son cada botón del juego (Confirmar, Cancelar, Menú, flechas...). Para cambiar una,
    ///     pulsa «+ tecla» y aprieta la tecla que quieras: se captura sola.
    ///   • Cuánto hay que mantener una flecha para que se repita.
    ///   • La caja de texto: velocidad, líneas, colores; con una vista previa que teclea de verdad.
    /// Normalmente hay UNA sola ficha (id «ajustes»). Si no hay ninguna, el juego usa lo clásico.
    /// </summary>
    public sealed class ControlsEditorWindow : ContentEditorWindow<InterfaceSettingsData>
    {
        [MenuItem(EditorMenus.Interface + "Controles y caja de texto", false, EditorMenus.InterfaceOrder + 2)]
        public static void Open() => OpenWindow<ControlsEditorWindow>("Controles y texto");

        protected override string Category => ContentFolders.Interface;
        protected override string Noun => "ajuste";
        protected override string Title => "Controles y caja de texto";
        protected override string Intro =>
            "Las teclas de cada botón del juego (como en una consola: A, B, Start, cruceta) y cómo se ve y avanza el texto. Con una sola ficha basta.";

        public override string[] GuideSteps => new[]
        {
            "Pulsa «Crear los ajustes clásicos» (id «ajustes»): flechas o WASD, Z = Confirmar, X = Cancelar, Esc = Menú.",
            "Para cambiar una tecla, pulsa «+ tecla» en su botón y aprieta la tecla en el teclado. ✕ la quita. Un botón puede tener varias teclas.",
            "Ajusta la caja de texto (velocidad, líneas, colores) y mira la vista previa. Una línea en blanco en un texto fuerza una caja nueva.",
        };

        // ---------------- Plantillas ----------------

        private static readonly (string id, string name, System.Func<InputBindings> make)[] Presets =
        {
            ("ajustes", "Clásico de PC (flechas/WASD, Z, X, Esc)", ClassicInterface.Bindings),
            ("gba", "Como una GBA (flechas, Z = A, X = B, Intro = Start, Retroceso = Select)", () => new InputBindings()
                .Bind(GameButton.Up, "UpArrow").Bind(GameButton.Down, "DownArrow").Bind(GameButton.Left, "LeftArrow").Bind(GameButton.Right, "RightArrow")
                .Bind(GameButton.Confirm, "Z").Bind(GameButton.Cancel, "X").Bind(GameButton.Menu, "Return").Bind(GameButton.Select, "Backspace")
                .Bind(GameButton.L, "A").Bind(GameButton.R, "S").WithClassicPad()),
            ("wasd", "Una mano (WASD, J = Confirmar, K = Cancelar, Tab = Menú)", () => new InputBindings()
                .Bind(GameButton.Up, "W").Bind(GameButton.Down, "S").Bind(GameButton.Left, "A").Bind(GameButton.Right, "D")
                .Bind(GameButton.Confirm, "J", "Space").Bind(GameButton.Cancel, "K").Bind(GameButton.Menu, "Tab").Bind(GameButton.Select, "L")
                .Bind(GameButton.L, "Q").Bind(GameButton.R, "E").WithClassicPad()),
        };

        protected override IReadOnlyList<(string id, string name, string group)> Templates
            => Presets.Select(p => (p.id, p.name, "Controles")).ToList();

        protected override void ApplyTemplate(SerializedObject so, string id)
        {
            var p = Presets.FirstOrDefault(x => x.id == id);
            if (p.make != null) FillBindings(so, p.make());
        }

        public static void FillBindings(SerializedObject so, InputBindings b)
        {
            var arr = so.FindProperty("bindings");
            arr.arraySize = GameButtons.All.Length;
            for (int i = 0; i < GameButtons.All.Length; i++)
            {
                var e = arr.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("button").enumValueIndex = (int)GameButtons.All[i];
                var keys = e.FindPropertyRelative("keys");
                var list = b.KeysOf(GameButtons.All[i]);
                keys.arraySize = list.Count;
                for (int k = 0; k < list.Count; k++) keys.GetArrayElementAtIndex(k).stringValue = list[k];
            }
            so.FindProperty("repeatDelay").floatValue = b.RepeatDelay;
            so.FindProperty("repeatInterval").floatValue = b.RepeatInterval;
        }

        public static int CreateClassicSet()
        {
            bool created = ContentAssets.CreateIfMissing<InterfaceSettingsData>(ContentFolders.Interface, "ajustes", "Ajustes de interfaz",
                so => FillBindings(so, ClassicInterface.Bindings()));
            AssetDatabase.SaveAssets();
            return created ? 1 : 0;
        }

        protected override void DrawBulkPresets()
        {
            if (GUILayout.Button("Crear los ajustes clásicos")) FinishBulk(CreateClassicSet(), "ajustes");
            if (ContentAssets.LoadAll<InterfaceSettingsData>().Count > 1)
                EditorGUILayout.HelpBox("Hay varias fichas de ajustes: el juego usa la de id «ajustes» (o la primera).", MessageType.Warning);
        }

        // ---------------- Teclas (con captura) ----------------

        private GameButton? _capturing;

        protected override void DrawPresets(InterfaceSettingsData d)
        {
            var b = InterfaceMapper.ToDomain(d).Bindings;
            EditorTheme.Section("Botones del juego: TECLADO y MANDO", Accent);
            EditorTheme.Paragraph("Cada botón es un identificador (Confirmar, Cancelar...). El juego pregunta por el botón, nunca por la tecla: " +
                                  "así el jugador puede cambiarlas en Opciones → Controles. En gris, lo clásico que se usa si no pones nada.");
            foreach (var button in GameButtons.All)
            {
                var own = d.Bindings.Where(x => x != null && x.button == button).SelectMany(x => x.keys ?? new string[0]).ToList();
                EditorTheme.BeginCard(EditorTheme.Interface);
                EditorGUILayout.LabelField(GameButtons.NameOf(button), EditorStyles.boldLabel);
                DrawDeviceRow(button, false, own.Where(k => !PadControls.IsPad(k)).ToList(), b.Keyboard(button));
                DrawDeviceRow(button, true, own.Where(PadControls.IsPad).ToList(), b.Pad(button));
                EditorTheme.EndCard();
            }
            CaptureKey();

            var problems = b.Problems();
            if (problems.Count == 0) EditorTheme.Tip("Controles sin conflictos.", EditorTheme.Ok, "✔");
            foreach (var p in problems) EditorTheme.Tip(p, EditorTheme.Warn, "⚠");

            EditorGUILayout.BeginHorizontal();
            foreach (var p in Presets)
            {
                var make = p.make;
                if (GUILayout.Button(new GUIContent(p.name.Split('(')[0].Trim(), p.name), EditorStyles.miniButton)) EditSelected(so => FillBindings(so, make()));
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
        }

        private void DrawDeviceRow(GameButton button, bool pad, List<string> own, List<string> effective)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(pad ? "🎮 Mando" : "⌨ Teclado", GUILayout.Width(80));
            if (own.Count == 0)
                GUILayout.Label(effective.Count > 0 ? "(clásico: " + string.Join(", ", effective.Select(PadControls.Short)) + ")" : "(ninguno)", EditorStyles.miniLabel);
            foreach (var k in own)
            {
                string key = k; var btn = button;
                if (GUILayout.Button(new GUIContent(PadControls.Short(k) + "  ✕", "Quitar"), EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                    EditSelected(so => RemoveKey(so, btn, key));
            }
            GUILayout.FlexibleSpace();
            if (!pad)
            {
                bool capturing = _capturing == button;
                var old = GUI.backgroundColor;
                if (capturing) GUI.backgroundColor = EditorTheme.Warn;
                if (GUILayout.Button(capturing ? "Pulsa una tecla… (clic aquí = cancelar)" : "+ tecla", GUILayout.Width(capturing ? 240 : 80)))
                    _capturing = capturing ? (GameButton?)null : button;
                GUI.backgroundColor = old;
            }
            else if (GUILayout.Button("+ mando ▾", GUILayout.Width(80)))
            {
                var menu = new GenericMenu();
                foreach (var (id, name) in PadControls.All)
                {
                    string pid = id; var btn = button;
                    if (own.Contains(id)) menu.AddDisabledItem(new GUIContent(name));
                    else menu.AddItem(new GUIContent(name), false, () => EditSelected(so => AddKey(so, btn, pid)));
                }
                menu.ShowAsContext();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void CaptureKey()
        {
            if (_capturing == null) return;
            var e = Event.current;
            if (e.type != EventType.KeyDown || e.keyCode == KeyCode.None) return;
            var button = _capturing.Value;
            _capturing = null;
            e.Use();
            string key = e.keyCode.ToString();
            EditSelected(so => AddKey(so, button, key));
        }

        private static SerializedProperty EntryFor(SerializedObject so, GameButton button)
        {
            var arr = so.FindProperty("bindings");
            for (int i = 0; i < arr.arraySize; i++)
            {
                var e = arr.GetArrayElementAtIndex(i);
                if (e.FindPropertyRelative("button").enumValueIndex == (int)button) return e;
            }
            arr.arraySize++;
            var n = arr.GetArrayElementAtIndex(arr.arraySize - 1);
            n.FindPropertyRelative("button").enumValueIndex = (int)button;
            n.FindPropertyRelative("keys").arraySize = 0;
            return n;
        }

        private static void AddKey(SerializedObject so, GameButton button, string key)
        {
            var keys = EntryFor(so, button).FindPropertyRelative("keys");
            for (int i = 0; i < keys.arraySize; i++) if (keys.GetArrayElementAtIndex(i).stringValue == key) return;
            keys.arraySize++;
            keys.GetArrayElementAtIndex(keys.arraySize - 1).stringValue = key;
        }

        private static void RemoveKey(SerializedObject so, GameButton button, string key)
        {
            var keys = EntryFor(so, button).FindPropertyRelative("keys");
            for (int i = keys.arraySize - 1; i >= 0; i--)
                if (keys.GetArrayElementAtIndex(i).stringValue == key) keys.DeleteArrayElementAtIndex(i);
        }

        /// <summary>Nombre amigable de una tecla o botón ("UpArrow" → "↑", "Pad/South" → "A / Cruz").</summary>
        public static string KeyName(string key) => PadControls.Short(key);

        // ---------------- Vista previa de la caja de texto ----------------

        private string _sample = "¡Hola, {jugador}! Bienvenido al mundo de los monstruos. Este texto se reparte en cajas solas según las líneas y los caracteres por línea.\n\nUna línea en blanco empieza una caja nueva.";
        private double _started = -1;
        private int _page;

        protected override void DrawPreview(InterfaceSettingsData d)
        {
            var s = InterfaceMapper.ToDomain(d);
            EditorTheme.Section("Vista previa de la caja de texto", Accent);
            _sample = EditorGUILayout.TextArea(_sample, GUILayout.MinHeight(44));
            var text = TextTokens.Replace(_sample, new Dictionary<string, string> { ["jugador"] = "ROJO", ["rival"] = "AZUL", ["dinero"] = "3000" });
            var pages = TextPager.Paginate(text, s.CharsPerLine, s.LinesPerPage);
            if (pages.Count == 0) return;
            _page = Mathf.Clamp(_page, 0, pages.Count - 1);
            if (_started < 0) _started = EditorApplication.timeSinceStartup;

            var tw = new Typewriter(pages[_page], s.TextSpeed);
            tw.Tick((float)(EditorApplication.timeSinceStartup - _started));

            float width = Mathf.Min(EditorTheme.UsableWidth(), 520f);
            float lineH = Mathf.Clamp(d.FontSize * 0.6f, 12f, 30f);
            var box = GUILayoutUtility.GetRect(width, lineH * s.LinesPerPage + 20f, GUILayout.ExpandWidth(false));
            EditorGUI.DrawRect(new Rect(box.x - 3, box.y - 3, box.width + 6, box.height + 6), d.BorderColor);
            EditorGUI.DrawRect(box, d.BoxColor);
            var style = new GUIStyle(EditorStyles.label) { fontSize = Mathf.RoundToInt(lineH * 0.8f), richText = true, wordWrap = false };
            style.normal.textColor = d.TextColor;
            GUI.Label(new Rect(box.x + 10, box.y + 8, box.width - 30, box.height - 10), tw.Visible, style);
            if (tw.IsDone && _page < pages.Count - 1)
            {
                var more = new GUIStyle(style); more.normal.textColor = d.HighlightColor;
                GUI.Label(new Rect(box.xMax - 24, box.yMax - lineH - 4, 20, lineH), s.MoreSymbol, more);
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"Caja {_page + 1} de {pages.Count}", EditorStyles.miniLabel);
            if (GUILayout.Button(tw.IsDone ? (_page < pages.Count - 1 ? "A · Siguiente caja" : "A · Volver a empezar") : "A · Completar", GUILayout.Width(150)))
            {
                if (!tw.IsDone) _started = -1000; // completa
                else { _page = _page < pages.Count - 1 ? _page + 1 : 0; _started = EditorApplication.timeSinceStartup; }
            }
            EditorGUILayout.EndHorizontal();

            // Muestra de menú con los mismos colores.
            var menuRect = GUILayoutUtility.GetRect(160, 48, GUILayout.ExpandWidth(false));
            EditorGUI.DrawRect(new Rect(menuRect.x - 3, menuRect.y - 3, menuRect.width + 6, menuRect.height + 6), d.BorderColor);
            EditorGUI.DrawRect(menuRect, d.BoxColor);
            var sel = new GUIStyle(style) { fontSize = 12 }; sel.normal.textColor = d.HighlightColor;
            var off = new GUIStyle(style) { fontSize = 12 }; off.normal.textColor = d.DisabledColor;
            GUI.Label(new Rect(menuRect.x + 8, menuRect.y + 6, 150, 18), s.CursorSymbol + " SÍ", sel);
            GUI.Label(new Rect(menuRect.x + 8, menuRect.y + 24, 150, 18), "   NO (desactivado)", off);

            if (!tw.IsDone) Repaint(); // sigue tecleando
        }
    }
}
