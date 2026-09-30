using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using CTEditor.Adventure.Domain.Interface;
using CTEditor.GameDefinition.Editor;
using CTEditor.GameDefinition.Infrastructure.Acl;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.Bootstrap.Editor
{
    /// <summary>
    /// CREA MENÚS EDITABLES EN LA ESCENA (clic derecho en la Jerarquía → CTEditor UI, o menú GameObject → CTEditor UI).
    /// Cada uno sale ya montado y funcionando, con un aspecto sencillo que tú cambias a tu gusto con las
    /// herramientas normales de Unity: arrastra tus imágenes (fondos, marcos, botones), cambia fuentes,
    /// colores, tamaños y posiciones. El juego lo usa EN LUGAR del menú automático con el mismo id.
    ///
    /// Todo va en un lienzo propio, «Menús del juego (CTEditor)», por encima de la interfaz del juego.
    /// </summary>
    public static class SceneMenuBuilder
    {
        private const string Root = "GameObject/CTEditor UI/";
        public const string CanvasName = "Menús del juego (CTEditor)";

        private static Color Border => new Color(0.25f, 0.35f, 0.55f);
        private static Color BoxColor => new Color(0.97f, 0.97f, 0.99f);
        private static Color Ink => new Color(0.2f, 0.2f, 0.25f);

        [InitializeOnLoadMethod]
        private static void Register()
        {
            SceneMenuHooks.CreateInScene = def => Select(CreateMenu(def));
            SceneMenuHooks.ExistsInScene = id => MenuScreen.AllInScene().Any(m => m.ScreenId == id);
            SceneMenuHooks.OpenFlow = MenuFlowWindow.Open;
            EditorCatalog.Register(new EditorEntry
            {
                Category = EditorCategory.Interface, Order = 3, Title = "Mapa de menús", Icon = "🔗",
                Description = "Todos los menús como un árbol: qué opción abre qué. Enlaza arrastrando, como las cadenas evolutivas.",
                Color = EditorTheme.Interface, Open = MenuFlowWindow.Open, Keywords = "enlazar flujo grafo navegación",
            });
            EditorCatalog.Register(new EditorEntry
            {
                Category = EditorCategory.Interface, Order = 4, Title = "Pantallas", Icon = "🖼",
                Description = "Menús y caja de texto EN LA ESCENA con tus gráficos (fondos, botones, fuentes). Crea el set clásico y edítalo en la escena.",
                Color = EditorTheme.Interface, Open = () => { if (EditorUtility.DisplayDialog("Pantallas en la escena",
                    "Se creará en la escena abierta el set clásico (pausa, Sí/No, equipo, mochila, listas y caja de texto) para que le pongas tus gráficos.\n\n" +
                    "También puedes crearlos uno a uno: clic derecho en la Jerarquía → CTEditor UI.", "Crear el set", "Cancelar")) CreateClassicSet(); },
                Keywords = "diseño layout gráficos sprites fondos",
            });
        }

        // ---------------- Menús del GameObject ----------------

        [MenuItem(Root + "Set clásico completo (todo)", false, 0)] private static void MenuAll() => CreateClassicSet();
        [MenuItem(Root + "Menú de pausa", false, 20)] private static void MenuPause() => Select(CreateMenu(Def(ClassicInterface.PauseMenu)));
        [MenuItem(Root + "Sí o No", false, 21)] private static void MenuYesNo() => Select(CreateMenu(Def(ClassicInterface.YesNo)));
        [MenuItem(Root + "Equipo: miembro elegido", false, 22)] private static void MenuMember() => Select(CreateMenu(Def(ClassicInterface.PartyMember)));
        [MenuItem(Root + "Equipo: objeto", false, 23)] private static void MenuPartyItem() => Select(CreateMenu(Def(ClassicInterface.PartyItem)));
        [MenuItem(Root + "Mochila: objeto elegido", false, 24)] private static void MenuBagItem() => Select(CreateMenu(Def(ClassicInterface.BagItem)));
        [MenuItem(Root + "Menú propio (vacío)", false, 25)] private static void MenuEmpty()
            => Select(CreateMenu(new MenuDefinition("mi_menu", "Mi menú") { Anchor = MenuAnchor.Center }
                .Add(new MenuOption("opcion_1", "OPCIÓN 1", MenuActionKind.Close)).Add(new MenuOption("salir", "SALIR", MenuActionKind.Close))));
        [MenuItem(Root + "Lista: equipo", false, 40)] private static void ListParty() => Select(CreateList("equipo_lista", "EQUIPO"));
        [MenuItem(Root + "Lista: mochila", false, 41)] private static void ListBag() => Select(CreateList("mochila_lista", "MOCHILA"));
        [MenuItem(Root + "Lista: olvidar movimiento", false, 42)] private static void ListForget() => Select(CreateList("olvidar", "¿QUÉ MOVIMIENTO OLVIDA?", 4));
        [MenuItem(Root + "Caja de texto", false, 60)] private static void MenuTextBox() => Select(CreateTextBox());

        /// <summary>La ficha del autor con ese id, o la clásica.</summary>
        private static MenuDefinition Def(string id)
        {
            var data = ContentAssets.FindById<MenuData>(id);
            return data != null ? InterfaceMapper.ToDomain(data) : ClassicInterface.Find(id);
        }

        public static void CreateClassicSet()
        {
            int n = 0;
            foreach (var id in new[] { ClassicInterface.PauseMenu, ClassicInterface.YesNo, ClassicInterface.PartyMember, ClassicInterface.PartyItem, ClassicInterface.BagItem })
                if (!Exists(id)) { CreateMenu(Def(id), quiet: true); n++; }
            if (!Exists("equipo_lista")) { CreateList("equipo_lista", "EQUIPO", quiet: true); n++; }
            if (!Exists("mochila_lista")) { CreateList("mochila_lista", "MOCHILA", quiet: true); n++; }
            if (!Exists("olvidar")) { CreateList("olvidar", "¿QUÉ MOVIMIENTO OLVIDA?", 4, quiet: true); n++; }
            if (DialogueBoxView.FindInScene() == null) { CreateTextBox(quiet: true); n++; }
            EditorUtility.DisplayDialog("Set clásico en la escena",
                (n == 0 ? "Ya estaba todo en la escena." : $"Creadas {n} pantallas en «{CanvasName}».") +
                "\n\nAhora selecciona cualquiera y cámbiale el aspecto: arrastra tus imágenes al fondo o a las opciones, cambia fuentes, tamaños y posiciones. " +
                "Cada menú se oculta solo al pulsar Play y aparece cuando el juego lo abre.", "Vale");
        }

        private static bool Exists(string id) => MenuScreen.AllInScene().Any(m => m.ScreenId == id);

        private static void Select(GameObject go) { if (go != null) { Selection.activeGameObject = go; EditorGUIUtility.PingObject(go); } }

        // ---------------- Lienzo ----------------

        public static RectTransform EnsureCanvas()
        {
            var existing = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(c => c.name == CanvasName);
            if (existing != null) return (RectTransform)existing.transform;
            var go = new GameObject(CanvasName, typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 510; // por encima de la interfaz del juego y del bloqueador de clics
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            Undo.RegisterCreatedObjectUndo(go, "Crear lienzo de menús");
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem));
                var module = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
                if (module != null) es.AddComponent(module); else es.AddComponent<StandaloneInputModule>();
                Undo.RegisterCreatedObjectUndo(es, "Crear EventSystem");
            }
            return (RectTransform)go.transform;
        }

        // ---------------- Piezas ----------------

        private static Sprite RoundedSprite => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

        private static GameObject Node(string name, Transform parent, params Type[] components)
        {
            var go = new GameObject(name, new[] { typeof(RectTransform) }.Concat(components).ToArray());
            go.transform.SetParent(parent, false);
            return go;
        }

        private static Image Framed(GameObject root)
        {
            // Marco (el propio objeto) + fondo (hijo). Cambia sus imágenes por las tuyas.
            var frame = root.GetComponent<Image>();
            if (frame == null) frame = root.AddComponent<Image>();
            frame.sprite = RoundedSprite; frame.type = Image.Type.Sliced; frame.color = Border;
            var fill = Node("Fondo", root.transform, typeof(Image)).GetComponent<Image>();
            fill.sprite = RoundedSprite; fill.type = Image.Type.Sliced; fill.color = BoxColor;
            var rt = fill.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(4, 4); rt.offsetMax = new Vector2(-4, -4);
            fill.raycastTarget = false;
            return fill;
        }

        private static TextMeshProUGUI Label(Transform parent, string name, string text, float size, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var t = Node(name, parent).AddComponent<TextMeshProUGUI>();
            t.text = TextSafety.Clean(text, t.font);
            t.fontSize = size; t.color = Ink; t.alignment = align;
            t.raycastTarget = false;
            return t;
        }

        private static void Stretch(RectTransform rt, float left, float top, float right, float bottom)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom); rt.offsetMax = new Vector2(-right, -top);
        }

        private static void Set(SerializedObject so, string field, object value)
        {
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogWarning($"[CTEditor] Campo '{field}' no encontrado."); return; }
            switch (value)
            {
                case string s: p.stringValue = s; break;
                case int i: p.intValue = i; break;
                case bool b: p.boolValue = b; break;
                case Vector2 v: p.vector2Value = v; break;
                case Color c: p.colorValue = c; break;
                case Enum e: p.enumValueIndex = Convert.ToInt32(e); break;
                case UnityEngine.Object o: p.objectReferenceValue = o; break;
                case null: p.objectReferenceValue = null; break;
            }
        }

        // Una opción: fondo transparente (se tiñe al elegirla) + texto con hueco a la izquierda para el cursor.
        private static MenuItemView CreateItem(Transform parent, MenuOption o, float fontSize)
        {
            var go = Node("Opción · " + o.Id, parent, typeof(Image));
            var bg = go.GetComponent<Image>();
            bg.sprite = RoundedSprite; bg.type = Image.Type.Sliced; bg.color = new Color(1, 1, 1, 0);
            var label = Label(go.transform, "Texto", o.Label, fontSize);
            Stretch(label.rectTransform, 34, 0, 8, 0);
            var item = go.AddComponent<MenuItemView>();
            var so = new SerializedObject(item);
            Set(so, "optionId", o.Id); Set(so, "action", o.Action); Set(so, "target", o.Target);
            Set(so, "requiredFlag", o.RequiredFlag); Set(so, "hiddenByFlag", o.HiddenByFlag); Set(so, "help", o.Help);
            Set(so, "label", label); Set(so, "background", bg);
            // Fondo transparente que se ilumina al elegirla (cámbialo por tus imágenes cuando quieras).
            Set(so, "backgroundNormal", new Color(1, 1, 1, 0f));
            Set(so, "backgroundSelected", new Color(1f, 0.85f, 0.4f, 0.35f));
            Set(so, "backgroundDisabled", new Color(1, 1, 1, 0f));
            so.ApplyModifiedPropertiesWithoutUndo();
            return item;
        }

        private static RectTransform CreateCursor(Transform parent)
        {
            var t = Label(parent, "Cursor", "►", 26, TextAlignmentOptions.Center);
            t.color = new Color(0.85f, 0.35f, 0.25f);
            t.rectTransform.sizeDelta = new Vector2(28, 36);
            return t.rectTransform;
        }

        // ---------------- Menú con opciones fijas ----------------

        public static GameObject CreateMenu(MenuDefinition def, bool quiet = false)
        {
            if (def == null) return null;
            if (!quiet && Exists(def.Id) && !EditorUtility.DisplayDialog("Ya existe",
                    $"La escena ya tiene un menú «{def.Id}». ¿Crear otro? (el juego usará el primero que encuentre)", "Crear otro", "Cancelar"))
                return null;

            var canvas = EnsureCanvas();
            int cols = Math.Max(1, def.Columns);
            int rows = Math.Max(1, (def.Options.Count + cols - 1) / cols);
            float colW = cols == 1 ? 280f : 230f, rowH = 52f, titleH = string.IsNullOrWhiteSpace(def.Title) ? 0f : 48f;
            float w = cols * colW + 36f, h = rows * rowH + titleH + 36f;

            var root = Node("Menú · " + def.Id, canvas, typeof(Image));
            Undo.RegisterCreatedObjectUndo(root, "Crear menú");
            Framed(root);
            UiKit.Anchor((RectTransform)root.transform, def.Anchor, w, h, 16f);

            TextMeshProUGUI title = null;
            if (titleH > 0)
            {
                title = Label(root.transform, "Título", def.Title, 28);
                title.fontStyle = FontStyles.Bold;
                var trt = title.rectTransform;
                trt.anchorMin = new Vector2(0, 1); trt.anchorMax = new Vector2(1, 1); trt.pivot = new Vector2(0.5f, 1);
                trt.anchoredPosition = new Vector2(0, -12); trt.sizeDelta = new Vector2(-36, titleH);
            }

            // Las opciones las coloca una rejilla. ¿Las quieres a mano? Quita el «Grid Layout Group» y muévelas.
            var list = Node("Opciones", root.transform, typeof(GridLayoutGroup));
            Stretch((RectTransform)list.transform, 16, 16 + titleH, 16, 16);
            var grid = list.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(colW - 8f, rowH - 6f);
            grid.spacing = new Vector2(8f, 6f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = cols;
            foreach (var o in def.Options) CreateItem(list.transform, o, 26);

            var cursor = CreateCursor(root.transform);
            var screen = root.AddComponent<MenuScreen>();
            var so = new SerializedObject(screen);
            Set(so, "screenId", def.Id);
            Set(so, "navigation", MenuScreen.Navigation.Automatic);
            Set(so, "columns", cols);
            Set(so, "wrap", def.Wrap);
            Set(so, "rememberCursor", def.RememberCursor);
            Set(so, "onCancel", def.CancelCloses ? MenuScreen.CancelMode.Close : string.IsNullOrEmpty(def.CancelOptionId) ? MenuScreen.CancelMode.Nothing : MenuScreen.CancelMode.ChooseOption);
            Set(so, "cancelOptionId", def.CancelOptionId ?? "");
            Set(so, "cursor", cursor);
            Set(so, "cursorOffset", new Vector2(32, 0));
            if (title != null) Set(so, "titleText", title);
            so.ApplyModifiedPropertiesWithoutUndo();
            // En el editor el cursor espera junto a la primera opción (en el juego se mueve solo).
            cursor.anchorMin = cursor.anchorMax = new Vector2(0, 1);
            cursor.pivot = new Vector2(0.5f, 0.5f);
            cursor.anchoredPosition = new Vector2(34, -(16 + titleH + rowH / 2f));
            return root;
        }

        // ---------------- Lista (equipo, mochila...) ----------------

        public static GameObject CreateList(string id, string titleText, int rows = 6, bool quiet = false)
        {
            if (!quiet && Exists(id) && !EditorUtility.DisplayDialog("Ya existe", $"La escena ya tiene «{id}». ¿Crear otra?", "Crear otra", "Cancelar")) return null;
            var canvas = EnsureCanvas();
            float rowH = 56f, w = 760f, h = rows * rowH + 48f + 60f + 30f;

            var root = Node("Lista · " + id, canvas, typeof(Image));
            Undo.RegisterCreatedObjectUndo(root, "Crear lista");
            Framed(root);
            UiKit.Anchor((RectTransform)root.transform, MenuAnchor.Center, w, h, 16f);

            var title = Label(root.transform, "Título", titleText, 28);
            title.fontStyle = FontStyles.Bold;
            var trt = title.rectTransform;
            trt.anchorMin = new Vector2(0, 1); trt.anchorMax = new Vector2(1, 1); trt.pivot = new Vector2(0.5f, 1);
            trt.anchoredPosition = new Vector2(0, -12); trt.sizeDelta = new Vector2(-40, 44);

            var list = Node("Filas", root.transform, typeof(VerticalLayoutGroup));
            Stretch((RectTransform)list.transform, 18, 60, 18, 70);
            var v = list.GetComponent<VerticalLayoutGroup>();
            v.spacing = 4; v.childControlHeight = false; v.childControlWidth = true; v.childForceExpandHeight = false; v.childForceExpandWidth = true;
            var template = CreateItem(list.transform, new MenuOption("fila", "Elemento", MenuActionKind.ScreenAction, "fila"), 26);
            template.name = "Fila (plantilla: se copia para cada elemento)";
            ((RectTransform)template.transform).sizeDelta = new Vector2(0, rowH - 4);

            var up = Label(root.transform, "Hay más arriba", "▲", 22, TextAlignmentOptions.Center);
            up.rectTransform.anchorMin = up.rectTransform.anchorMax = new Vector2(1, 1);
            up.rectTransform.anchoredPosition = new Vector2(-30, -30); up.rectTransform.sizeDelta = new Vector2(30, 30);
            var down = Label(root.transform, "Hay más abajo", "▼", 22, TextAlignmentOptions.Center);
            down.rectTransform.anchorMin = down.rectTransform.anchorMax = new Vector2(1, 0);
            down.rectTransform.anchoredPosition = new Vector2(-30, 78); down.rectTransform.sizeDelta = new Vector2(30, 30);

            var help = Label(root.transform, "Ayuda", "", 20);
            var hrt = help.rectTransform;
            hrt.anchorMin = new Vector2(0, 0); hrt.anchorMax = new Vector2(1, 0); hrt.pivot = new Vector2(0.5f, 0);
            hrt.anchoredPosition = new Vector2(0, 14); hrt.sizeDelta = new Vector2(-40, 48);

            var cursor = CreateCursor(root.transform);
            var screen = root.AddComponent<MenuScreen>();
            var so = new SerializedObject(screen);
            Set(so, "screenId", id);
            Set(so, "navigation", MenuScreen.Navigation.Vertical);
            Set(so, "rememberCursor", true);
            Set(so, "onCancel", MenuScreen.CancelMode.Close);
            Set(so, "cursor", cursor);
            Set(so, "cursorOffset", new Vector2(32, 0));
            Set(so, "titleText", title);
            Set(so, "helpText", help);
            Set(so, "itemTemplate", template);
            Set(so, "visibleRows", rows);
            Set(so, "moreAbove", up.gameObject);
            Set(so, "moreBelow", down.gameObject);
            so.ApplyModifiedPropertiesWithoutUndo();
            return root;
        }

        // ---------------- Caja de texto ----------------

        public static GameObject CreateTextBox(bool quiet = false)
        {
            if (!quiet && DialogueBoxView.FindInScene() != null && !EditorUtility.DisplayDialog("Ya existe",
                    "La escena ya tiene una caja de texto. ¿Crear otra? (el juego usará la primera)", "Crear otra", "Cancelar"))
                return null;
            var canvas = EnsureCanvas();
            var root = Node("Caja de texto", canvas, typeof(Image));
            Undo.RegisterCreatedObjectUndo(root, "Crear caja de texto");
            Framed(root);
            UiKit.Anchor((RectTransform)root.transform, MenuAnchor.BottomFull, 0, 150f, 12f);
            var text = Label(root.transform, "Texto", "Aquí sale el texto del juego. ¡Hola, {jugador}!", 30, TextAlignmentOptions.TopLeft);
            Stretch(text.rectTransform, 26, 18, 60, 18);
            text.richText = true;
            var more = Label(root.transform, "Hay más (▼)", "▼", 26, TextAlignmentOptions.Center);
            more.color = new Color(0.85f, 0.35f, 0.25f);
            more.rectTransform.anchorMin = more.rectTransform.anchorMax = new Vector2(1, 0);
            more.rectTransform.anchoredPosition = new Vector2(-30, 28); more.rectTransform.sizeDelta = new Vector2(30, 30);
            var view = root.AddComponent<DialogueBoxView>();
            var so = new SerializedObject(view);
            Set(so, "text", text);
            Set(so, "moreIndicator", more.gameObject);
            so.ApplyModifiedPropertiesWithoutUndo();
            return root;
        }
    }
}
