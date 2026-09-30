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
    /// Editor de MENÚS (CTEditor → Interfaz → Menús): pausa, combate, Sí/No, equipo, mochila... o los tuyos.
    /// Cada menú es una lista ORDENADA de opciones (con su acción), en 1 columna o en rejilla, y un sitio
    /// en la pantalla. La VISTA PREVIA es jugable: flechas, Confirmar y Cancelar como en el juego (con
    /// los botones o con el teclado), entrando en los submenús.
    /// </summary>
    public sealed class MenuEditorWindow : ContentEditorWindow<MenuData>
    {
        [MenuItem(EditorMenus.Interface + "Menús", false, EditorMenus.InterfaceOrder + 1)]
        public static void Open() => OpenWindow<MenuEditorWindow>("Menús");

        /// <summary>Abre el editor con esa ficha elegida (lo usa el mapa de menús).</summary>
        public static void OpenOn(MenuData d) => OpenWindow<MenuEditorWindow>("Menús").FocusOn(d);

        protected override string Category => ContentFolders.Menus;
        protected override string Noun => "menú";
        protected override string Title => "Menús";
        protected override string Intro =>
            "Qué opciones tiene cada menú, en qué ORDEN, en cuántas columnas y dónde aparece. Prueba la vista previa con las flechas: es como en el juego.";

        public override string[] GuideSteps => new[]
        {
            "Pulsa «Crear los menús clásicos»: pausa, combate (2×2), Sí/No, equipo y mochila. El juego los usa por su id (pausa, combate, si_no...).",
            "Cambia textos, orden (▲▼) y columnas. Cada opción tiene una ACCIÓN: abrir el equipo, la mochila, otro menú, cerrar... o una acción de la pantalla (resumen, mover, dar, usar...).",
            "Las MARCAS muestran u ocultan opciones: «POKéDEX» solo aparece si la partida tiene la marca tiene_pokedex. Pruébalo escribiendo marcas en la vista previa.",
            "Usa la vista previa: flechas para moverte, Confirmar para elegir (entra en los submenús) y Cancelar para volver.",
        };

        protected override Color? RowMark(MenuData d) => d.Columns > 1 ? EditorTheme.Interface : EditorTheme.WithAlpha(EditorTheme.Interface, 0.5f);
        protected override string RowTooltip(MenuData d) => $"{d.Options.Length} opción(es) · {(d.Columns > 1 ? d.Columns + " columnas" : "lista")}";

        // ---------------- Plantillas ----------------

        protected override IReadOnlyList<(string id, string name, string group)> Templates
            => ClassicInterface.Menus().Select(m => (m.Id, m.DisplayName, "Menús clásicos")).ToList();

        protected override void ApplyTemplate(SerializedObject so, string id)
        {
            var m = ClassicInterface.Find(id);
            if (m != null) Fill(so, m);
        }

        /// <summary>Copia un menú del dominio a la ficha.</summary>
        public static void Fill(SerializedObject so, MenuDefinition m)
        {
            so.FindProperty("displayName").stringValue = m.DisplayName;
            so.FindProperty("title").stringValue = m.Title;
            so.FindProperty("columns").intValue = m.Columns;
            so.FindProperty("wrap").boolValue = m.Wrap;
            so.FindProperty("rememberCursor").boolValue = m.RememberCursor;
            so.FindProperty("cancelCloses").boolValue = m.CancelCloses;
            so.FindProperty("cancelOptionId").stringValue = m.CancelOptionId;
            so.FindProperty("anchor").enumValueIndex = (int)m.Anchor;
            var arr = so.FindProperty("options");
            arr.arraySize = m.Options.Count;
            for (int i = 0; i < m.Options.Count; i++) WriteOption(arr.GetArrayElementAtIndex(i), m.Options[i]);
        }

        private static void WriteOption(SerializedProperty e, MenuOption o)
        {
            e.FindPropertyRelative("id").stringValue = o.Id;
            e.FindPropertyRelative("label").stringValue = o.Label;
            e.FindPropertyRelative("action").enumValueIndex = (int)o.Action;
            e.FindPropertyRelative("target").stringValue = o.Target;
            e.FindPropertyRelative("requiredFlag").stringValue = o.RequiredFlag;
            e.FindPropertyRelative("hiddenByFlag").stringValue = o.HiddenByFlag;
            e.FindPropertyRelative("help").stringValue = o.Help;
        }

        public static int CreateClassicSet()
        {
            int n = 0;
            foreach (var m in ClassicInterface.Menus())
            {
                var menu = m;
                if (ContentAssets.CreateIfMissing<MenuData>(ContentFolders.Menus, m.Id, m.DisplayName, so => Fill(so, menu))) n++;
            }
            AssetDatabase.SaveAssets();
            return n;
        }

        protected override void DrawBulkPresets()
        {
            if (GUILayout.Button($"Crear los {ClassicInterface.Menus().Count} menús clásicos")) FinishBulk(CreateClassicSet(), "menús");
            if (SceneMenuHooks.OpenFlow != null && GUILayout.Button("🔗 Mapa de menús (enlazarlos)")) SceneMenuHooks.OpenFlow();
        }

        // ---------------- Orden rápido y añadir opciones ----------------

        protected override void DrawPresets(MenuData d)
        {
            EditorTheme.Section("Opciones (en orden)", Accent);
            var opts = d.Options;
            for (int i = 0; i < opts.Length; i++)
            {
                var o = opts[i]; if (o == null) continue;
                int k = i;
                EditorGUILayout.BeginHorizontal();
                GUI.enabled = i > 0;
                if (GUILayout.Button("▲", EditorStyles.miniButtonLeft, GUILayout.Width(24))) EditSelected(so => so.FindProperty("options").MoveArrayElement(k, k - 1));
                GUI.enabled = i < opts.Length - 1;
                if (GUILayout.Button("▼", EditorStyles.miniButtonMid, GUILayout.Width(24))) EditSelected(so => so.FindProperty("options").MoveArrayElement(k, k + 1));
                GUI.enabled = true;
                if (GUILayout.Button("✕", EditorStyles.miniButtonRight, GUILayout.Width(24))) EditSelected(so => so.FindProperty("options").DeleteArrayElementAtIndex(k));
                EditorGUILayout.LabelField($"{i + 1}. {o.label}", EditorStyles.boldLabel, GUILayout.Width(150));
                EditorGUILayout.LabelField(DescribeAction(o) + (string.IsNullOrWhiteSpace(o.requiredFlag) ? "" : $"  · solo con «{o.requiredFlag}»"), EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("+ Añadir opción ▾")) ShowAddMenu();

            // Con gráficos propios: una copia EDITABLE en la escena (fondos, botones, fuentes, posiciones...).
            if (SceneMenuHooks.CreateInScene != null)
            {
                bool inScene = SceneMenuHooks.ExistsInScene != null && SceneMenuHooks.ExistsInScene(d.Id);
                EditorTheme.Tip(inScene
                    ? "Este menú ya está en la ESCENA: el juego usa esa versión (con sus gráficos). Edítala allí."
                    : "¿Quieres ponerle tus gráficos? Crea una copia en la escena y cambia fondos, botones, fuentes y posiciones con las herramientas de Unity.",
                    inScene ? EditorTheme.Ok : Accent, "🎨");
                if (!inScene && GUILayout.Button("🎨 Crear en la escena (para ponerle gráficos)")) SceneMenuHooks.CreateInScene(InterfaceMapper.ToDomain(d));
            }
            EditorGUILayout.Space();
        }

        private void ShowAddMenu()
        {
            var menu = new GenericMenu();
            void Add(string path, MenuOption o) => menu.AddItem(new GUIContent(path), false, () => AppendOption(o));
            Add("Abrir el equipo", new MenuOption("equipo", "POKéMON", MenuActionKind.OpenParty));
            Add("Abrir la mochila", new MenuOption("mochila", "MOCHILA", MenuActionKind.OpenBag));
            Add("Resumen del primero", new MenuOption("resumen", "RESUMEN", MenuActionKind.OpenSummary));
            Add("Ficha de entrenador", new MenuOption("ficha", "{jugador}", MenuActionKind.OpenTrainerCard));
            Add("Pokédex (con marca tiene_pokedex)", new MenuOption("pokedex", "POKéDEX", MenuActionKind.OpenPokedex, requiredFlag: "tiene_pokedex"));
            Add("Guardar", new MenuOption("guardar", "GUARDAR", MenuActionKind.Save));
            Add("Opciones", new MenuOption("opciones", "OPCIONES", MenuActionKind.Options));
            Add("Cerrar el menú", new MenuOption("salir", "SALIR", MenuActionKind.Close));
            menu.AddSeparator("");
            foreach (var m in AllMenus().Values.Where(m => Selected == null || m.Id != Selected.Id))
            {
                var target = m.Id;
                Add("Abrir otro menú/" + m.DisplayName + " (" + m.Id + ")", new MenuOption("abrir_" + target, m.DisplayName.ToUpperInvariant(), MenuActionKind.OpenMenu, target));
            }
            foreach (var a in ScreenActions.Known)
                Add($"Acción de pantalla/{a.screen}/{a.name} ({a.id})", new MenuOption(a.id, a.name.ToUpperInvariant(), MenuActionKind.ScreenAction, a.id));
            menu.ShowAsContext();
        }

        private void AppendOption(MenuOption o)
        {
            EditSelected(so =>
            {
                var arr = so.FindProperty("options");
                string id = o.Id; int n = 2;
                var used = Selected.Options.Where(x => x != null).Select(x => x.id).ToList();
                while (used.Contains(id)) id = o.Id + "_" + n++;
                arr.arraySize++;
                WriteOption(arr.GetArrayElementAtIndex(arr.arraySize - 1), new MenuOption(id, o.Label, o.Action, o.Target, o.RequiredFlag, o.HiddenByFlag, o.Help));
            });
            ResetPreview();
        }

        private static string DescribeAction(MenuOptionData o)
        {
            string name = ClassicInterface.ActionName(o.action);
            if (o.action == MenuActionKind.OpenMenu) return $"{name}: «{o.target}»";
            if (o.action == MenuActionKind.ScreenAction) return $"{name}: {ScreenActions.NameOf(o.target)}";
            return name;
        }

        // ---------------- Vista previa jugable ----------------

        private MenuData _previewFor;
        private readonly List<(MenuDefinition menu, MenuCursor cursor)> _stack = new List<(MenuDefinition, MenuCursor)>();
        private string _previewMessage = "";
        private string _previewFlags = "";
        private readonly Dictionary<string, int> _remembered = new Dictionary<string, int>();

        private void ResetPreview() { _previewFor = null; }

        private static Dictionary<string, MenuDefinition> AllMenus()
        {
            var dict = new Dictionary<string, MenuDefinition>();
            foreach (var m in ClassicInterface.Menus()) dict[m.Id] = m;                       // lo clásico, por si falta alguno
            foreach (var d in ContentAssets.LoadAll<MenuData>())
                if (!string.IsNullOrWhiteSpace(d.Id)) dict[d.Id] = InterfaceMapper.ToDomain(d);  // lo del autor manda
            return dict;
        }

        private bool HasFlag(string f) => _previewFlags.Split(' ', ',').Select(x => x.Trim()).Contains(f);

        private void Push(MenuDefinition m)
        {
            var visible = m.VisibleOptions(HasFlag);
            int start = m.RememberCursor && _remembered.TryGetValue(m.Id, out var r) ? r : 0;
            _stack.Add((m, new MenuCursor(visible.Count, m.Columns, m.Wrap, null, start)));
        }

        protected override void DrawPreview(MenuData d)
        {
            if (_previewFor != d || _stack.Count == 0)
            {
                _previewFor = d; _stack.Clear(); _previewMessage = "";
                Push(InterfaceMapper.ToDomain(d));
            }

            EditorTheme.Section("Vista previa (jugable)", Accent);
            EditorGUILayout.BeginHorizontal();
            string flags = EditorGUILayout.TextField(new GUIContent("Marcas de prueba", "Marcas de la partida, separadas por espacios (p. ej. tiene_pokedex)."), _previewFlags);
            if (flags != _previewFlags) { _previewFlags = flags; _stack.Clear(); Push(InterfaceMapper.ToDomain(d)); }
            if (GUILayout.Button("↺ Reiniciar", GUILayout.Width(90))) { _stack.Clear(); _remembered.Clear(); Push(InterfaceMapper.ToDomain(d)); _previewMessage = ""; }
            EditorGUILayout.EndHorizontal();

            var (menu, cursor) = _stack[_stack.Count - 1];
            var visible = menu.VisibleOptions(HasFlag);

            // "Pantalla" de 240×160 escalada (proporción de GBA).
            float width = Mathf.Min(EditorTheme.UsableWidth(), 480f);
            var screen = GUILayoutUtility.GetRect(width, width * 2f / 3f, GUILayout.ExpandWidth(false));
            EditorGUI.DrawRect(screen, new Color(0.18f, 0.32f, 0.22f));
            GUI.Label(new Rect(screen.x + 6, screen.y + 4, screen.width, 16),
                _stack.Count > 1 ? "Menú: " + string.Join(" › ", _stack.Select(s => s.menu.DisplayName)) : "Menú: " + menu.DisplayName, EditorStyles.whiteMiniLabel);

            DrawMenuBox(screen, menu, visible, cursor);

            // Ayuda de la opción y lo que pasaría.
            string help = visible.Count > 0 && cursor.Index < visible.Count ? visible[cursor.Index].Help : "";
            var helpRect = new Rect(screen.x + 4, screen.yMax - 22, screen.width - 8, 18);
            EditorGUI.DrawRect(helpRect, new Color(0, 0, 0, 0.45f));
            GUI.Label(helpRect, string.IsNullOrEmpty(_previewMessage) ? help : _previewMessage, EditorStyles.whiteMiniLabel);

            // Controles (botones + teclado cuando no hay un campo de texto activo).
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("◀", GUILayout.Width(32))) Nav(Direction.Left);
            if (GUILayout.Button("▲", GUILayout.Width(32))) Nav(Direction.Up);
            if (GUILayout.Button("▼", GUILayout.Width(32))) Nav(Direction.Down);
            if (GUILayout.Button("▶", GUILayout.Width(32))) Nav(Direction.Right);
            GUILayout.Space(12);
            if (GUILayout.Button("A · Confirmar", GUILayout.Width(110))) PreviewConfirm();
            if (GUILayout.Button("B · Cancelar", GUILayout.Width(110))) PreviewCancel();
            EditorGUILayout.EndHorizontal();
            EditorTheme.Paragraph("Teclado: flechas, Z/Intro = Confirmar, X/Esc = Cancelar (haz clic en un hueco vacío de la ventana si no responde).");
            HandlePreviewKeys();

            // Mapa: qué menús abre este.
            var links = d.Options.Where(o => o != null && o.action == MenuActionKind.OpenMenu).Select(o => $"«{o.label}» → {o.target}").ToList();
            if (links.Count > 0) EditorTheme.Tip("Abre: " + string.Join(" · ", links), Accent, "🔗");
            var usedBy = ContentAssets.LoadAll<MenuData>().Where(m => m != d && m.Options.Any(o => o != null && o.action == MenuActionKind.OpenMenu && o.target == d.Id)).Select(m => m.Id).ToList();
            if (usedBy.Count > 0) EditorTheme.Tip("Se abre desde: " + string.Join(", ", usedBy), Accent, "↩");
            if (ClassicInterface.Menus().Any(m => m.Id == d.Id)) EditorTheme.Tip($"El juego usa este menú por su id «{d.Id}».", EditorTheme.Ok, "🎮");
        }

        private void DrawMenuBox(Rect screen, MenuDefinition menu, List<MenuOption> visible, MenuCursor cursor)
        {
            int cols = Mathf.Max(1, menu.Columns);
            int rows = Mathf.Max(1, (visible.Count + cols - 1) / cols);
            float rowH = 18f, colW = cols == 1 ? 120f : Mathf.Min(120f, (screen.width - 30f) / cols);
            float titleH = string.IsNullOrWhiteSpace(menu.Title) ? 0f : rowH;
            float w = cols * colW + 14f, h = rows * rowH + titleH + 10f;
            if (menu.Anchor == MenuAnchor.BottomFull) w = screen.width - 8f;

            float x, y;
            switch (menu.Anchor)
            {
                case MenuAnchor.TopLeft: x = screen.x + 4; y = screen.y + 20; break;
                case MenuAnchor.BottomLeft: x = screen.x + 4; y = screen.yMax - h - 26; break;
                case MenuAnchor.BottomRight: x = screen.xMax - w - 4; y = screen.yMax - h - 26; break;
                case MenuAnchor.Center: x = screen.center.x - w / 2; y = screen.center.y - h / 2; break;
                case MenuAnchor.BottomFull: x = screen.x + 4; y = screen.yMax - h - 26; break;
                default: x = screen.xMax - w - 4; y = screen.y + 20; break;
            }
            var box = new Rect(x, y, w, h);
            EditorGUI.DrawRect(new Rect(box.x - 2, box.y - 2, box.width + 4, box.height + 4), new Color(0.25f, 0.35f, 0.55f));
            EditorGUI.DrawRect(box, new Color(0.97f, 0.97f, 0.99f));

            var style = new GUIStyle(EditorStyles.label) { fontSize = 11 };
            var bold = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11 };
            var vars = new Dictionary<string, string> { ["jugador"] = "ROJO", ["rival"] = "AZUL", ["dinero"] = "3000" };
            if (titleH > 0) GUI.Label(new Rect(box.x + 6, box.y + 4, box.width, rowH), TextTokens.Replace(menu.Title, vars), bold);
            if (visible.Count == 0) { GUI.Label(new Rect(box.x + 6, box.y + 4 + titleH, box.width, rowH), "(sin opciones visibles)", style); return; }

            for (int i = 0; i < visible.Count; i++)
            {
                int r = i / cols, c = i % cols;
                var cell = new Rect(box.x + 6 + c * colW, box.y + 5 + titleH + r * rowH, colW, rowH);
                bool sel = i == cursor.Index;
                var st = sel ? bold : style;
                st.normal.textColor = sel ? new Color(0.85f, 0.35f, 0.25f) : new Color(0.2f, 0.2f, 0.25f);
                GUI.Label(cell, (sel ? "▶ " : "   ") + TextTokens.Replace(visible[i].Label, vars), st);
                if (Event.current.type == EventType.MouseDown && cell.Contains(Event.current.mousePosition))
                {
                    if (cursor.Index == i) PreviewConfirm(); else cursor.Set(i);
                    Event.current.Use(); Repaint();
                }
            }
        }

        private void HandlePreviewKeys()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown || GUIUtility.keyboardControl != 0) return;
            switch (e.keyCode)
            {
                case KeyCode.UpArrow: Nav(Direction.Up); break;
                case KeyCode.DownArrow: Nav(Direction.Down); break;
                case KeyCode.LeftArrow: Nav(Direction.Left); break;
                case KeyCode.RightArrow: Nav(Direction.Right); break;
                case KeyCode.Z: case KeyCode.Return: case KeyCode.Space: PreviewConfirm(); break;
                case KeyCode.X: case KeyCode.Escape: case KeyCode.Backspace: PreviewCancel(); break;
                default: return;
            }
            e.Use();
        }

        private void Nav(Direction d)
        {
            if (_stack.Count == 0) return;
            _stack[_stack.Count - 1].cursor.Move(d);
            _previewMessage = "";
            Repaint();
        }

        private void PreviewConfirm()
        {
            if (_stack.Count == 0) return;
            var (menu, cursor) = _stack[_stack.Count - 1];
            var visible = menu.VisibleOptions(HasFlag);
            if (visible.Count == 0) return;
            var o = visible[cursor.Index];
            if (menu.RememberCursor) _remembered[menu.Id] = cursor.Index;
            switch (o.Action)
            {
                case MenuActionKind.OpenMenu:
                    if (AllMenus().TryGetValue(o.Target, out var sub)) { Push(sub); _previewMessage = ""; }
                    else _previewMessage = $"✖ El menú «{o.Target}» no existe.";
                    break;
                case MenuActionKind.Close:
                    _previewMessage = "→ Se cierra el menú.";
                    if (_stack.Count > 1) _stack.RemoveAt(_stack.Count - 1);
                    break;
                case MenuActionKind.ScreenAction:
                    _previewMessage = $"→ Acción de la pantalla: {ScreenActions.NameOf(o.Target)}.";
                    break;
                default:
                    _previewMessage = "→ " + ClassicInterface.ActionName(o.Action) + ".";
                    break;
            }
            Repaint();
        }

        private void PreviewCancel()
        {
            if (_stack.Count == 0) return;
            var (menu, cursor) = _stack[_stack.Count - 1];
            if (!menu.CancelCloses)
            {
                var visible = menu.VisibleOptions(HasFlag);
                int i = visible.FindIndex(o => o.Id == menu.CancelOptionId);
                if (i >= 0) { cursor.Set(i); PreviewConfirm(); }
                else _previewMessage = "→ Cancelar no hace nada en este menú.";
                Repaint();
                return;
            }
            if (menu.RememberCursor) _remembered[menu.Id] = cursor.Index;
            if (_stack.Count > 1) { _stack.RemoveAt(_stack.Count - 1); _previewMessage = "← Vuelves al menú anterior."; }
            else _previewMessage = "→ Se cierra el menú.";
            Repaint();
        }
    }
}
