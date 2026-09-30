using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using CTEditor.Adventure.Domain.Interface;
using CTEditor.GameDefinition.Editor;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.Bootstrap.Editor
{
    /// <summary>Ids de menú conocidos, con su explicación (para los desplegables).</summary>
    internal static class KnownMenus
    {
        public static List<(string id, string what)> All()
        {
            var list = new List<(string, string)>
            {
                (ClassicInterface.PauseMenu, "Menú de pausa (botón Menú)"),
                (ClassicInterface.YesNo, "Preguntas Sí / No"),
                (ClassicInterface.PartyMember, "Equipo: al elegir un miembro"),
                (ClassicInterface.PartyItem, "Equipo: dar o quitar objeto"),
                (ClassicInterface.BagItem, "Mochila: al elegir un objeto"),
                ("opciones", "Opciones"),
                ("equipo_lista", "LISTA del equipo"),
                ("mochila_lista", "LISTA de la mochila"),
                ("olvidar", "LISTA: qué movimiento olvidar"),
                ("controles", "LISTA: controles del jugador"),
            };
            foreach (var d in ContentAssets.LoadAll<MenuData>())
                if (!string.IsNullOrWhiteSpace(d.Id) && list.All(x => x.Item1 != d.Id)) list.Add((d.Id, "Menú (ficha): " + d.DisplayName));
            foreach (var s in MenuScreen.AllInScene())
                if (!string.IsNullOrWhiteSpace(s.ScreenId) && list.All(x => x.Item1 != s.ScreenId)) list.Add((s.ScreenId, "Menú de la escena"));
            return list;
        }

        /// <summary>Desplegable «▾» que escribe un id en una propiedad de texto.</summary>
        public static void Picker(SerializedProperty p, IEnumerable<(string id, string what)> options)
        {
            if (!GUILayout.Button("▾", GUILayout.Width(26))) return;
            var menu = new GenericMenu();
            foreach (var (id, what) in options)
            {
                string value = id;
                menu.AddItem(new GUIContent($"{what}  ({id})"), p.stringValue == id, () =>
                {
                    p.serializedObject.Update();
                    p.stringValue = value;
                    p.serializedObject.ApplyModifiedProperties();
                });
            }
            menu.ShowAsContext();
        }
    }

    /// <summary>
    /// Inspector de un MENÚ DE ESCENA: explica qué es, deja elegir su id de una lista, enseña sus opciones
    /// en orden (▲▼ para reordenar, + para añadir), y una vista previa del cursor sin darle a Play.
    /// </summary>
    [CustomEditor(typeof(MenuScreen))]
    public sealed class MenuScreenInspector : UnityEditor.Editor
    {
        private int _preview;

        public override void OnInspectorGUI()
        {
            var screen = (MenuScreen)target;
            serializedObject.Update();
            EditorTheme.TitleBand("Menú de la escena",
                $"El juego usa ESTE menú en lugar del automático «{screen.ScreenId}». Su aspecto lo cambias en la escena como cualquier UI: " +
                "selecciona el marco, el fondo o una opción y arrastra tus imágenes, fuentes y colores.", EditorTheme.Interface, "🖼", navigation: false);

            var id = serializedObject.FindProperty("screenId");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(id, new GUIContent("Id del menú", "Qué menú del juego sustituye, o el id con el que lo abren tus opciones."));
            KnownMenus.Picker(id, KnownMenus.All());
            EditorGUILayout.EndHorizontal();

            // Resto de ajustes, en español.
            foreach (var name in new[] { "navigation", "columns", "wrap", "rememberCursor", "onCancel", "cancelOptionId", "cursor", "cursorOffset", "titleText", "helpText" })
            {
                var p = serializedObject.FindProperty(name);
                if (name == "columns" && screen.NavigationMode != MenuScreen.Navigation.Grid) continue;
                if (name == "cancelOptionId" && serializedObject.FindProperty("onCancel").enumValueIndex != (int)MenuScreen.CancelMode.ChooseOption) continue;
                if (p != null) Field(p, name);
            }
            EditorGUILayout.Space();
            EditorTheme.Section("Lista (equipo, mochila...)", EditorTheme.Interface);
            foreach (var name in new[] { "itemTemplate", "visibleRows", "moreAbove", "moreBelow" })
            {
                var p = serializedObject.FindProperty(name);
                if (p != null) Field(p, name);
            }
            serializedObject.ApplyModifiedProperties();

            if (screen.IsList)
            {
                EditorTheme.Tip("Es una LISTA: la «Opción de plantilla» se copia para cada elemento. Dale el aspecto que quieras a la plantilla.", EditorTheme.Interface, "📋");
                return;
            }
            DrawOptions(screen);
        }

        private static void Field(SerializedProperty p, string name)
            => EditorGUILayout.PropertyField(p, new GUIContent(Etiquetas.Field(name), p.tooltip), true);

        private void DrawOptions(MenuScreen screen)
        {
            EditorTheme.Section("Opciones (en orden)", EditorTheme.Interface);
            var items = screen.StaticItems();
            var layout = items.Count > 0 ? items[0].transform.parent.GetComponent<LayoutGroup>() : null;
            if (layout != null)
                EditorTheme.Tip("Las coloca un «" + layout.GetType().Name + "» (en orden). ¿Las quieres donde tú digas? Quita ese componente y muévelas a mano: " +
                                "con navegación «Automático» las flechas siguen funcionando.", EditorTheme.Interface, "📐");

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                EditorGUILayout.BeginHorizontal();
                GUI.enabled = i > 0;
                if (GUILayout.Button("▲", EditorStyles.miniButtonLeft, GUILayout.Width(24))) Move(item, -1);
                GUI.enabled = i < items.Count - 1;
                if (GUILayout.Button("▼", EditorStyles.miniButtonMid, GUILayout.Width(24))) Move(item, +1);
                GUI.enabled = true;
                if (GUILayout.Button(item.RawLabel, EditorStyles.miniButtonRight, GUILayout.Width(140))) Selection.activeGameObject = item.gameObject;
                GUILayout.Label(Describe(item), EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Añadir opción")) AddOption(items);
            if (GUILayout.Button("🔗 Mapa de menús")) MenuFlowWindow.Open();
            EditorGUILayout.EndHorizontal();

            // Vista previa del cursor sin Play.
            if (items.Count > 0)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Vista previa del cursor:", GUILayout.Width(150));
                if (GUILayout.Button("◀", GUILayout.Width(30))) Preview(screen, items, _preview - 1);
                GUILayout.Label($"{Mathf.Clamp(_preview, 0, items.Count - 1) + 1} / {items.Count}", GUILayout.Width(50));
                if (GUILayout.Button("▶", GUILayout.Width(30))) Preview(screen, items, _preview + 1);
                EditorGUILayout.EndHorizontal();
            }
        }

        private void Preview(MenuScreen screen, List<MenuItemView> items, int index)
        {
            _preview = (index % items.Count + items.Count) % items.Count;
            var objs = new List<Object> { screen };
            foreach (var i in items)
            {
                objs.Add(i); objs.Add(i.transform);
                if (i.Label != null) objs.Add(i.Label);
                var bg = i.GetComponent<Image>(); if (bg != null) objs.Add(bg);
                foreach (var t in i.GetComponentsInChildren<Transform>(true)) objs.Add(t.gameObject); // marcadores
            }
            Undo.RecordObjects(objs.ToArray(), "Vista previa del menú");
            screen.PreviewSelect(_preview);
            SceneView.RepaintAll();
        }

        internal static string Describe(MenuItemView item)
        {
            string a = Etiquetas.Enum(item.Action.ToString());
            if (item.Action == MenuActionKind.OpenMenu) return $"{a} → {item.Target}";
            if (item.Action == MenuActionKind.ScreenAction) return $"{a}: {ScreenActions.NameOf(item.Target)}";
            return a;
        }

        private static void Move(MenuItemView item, int delta)
        {
            Undo.RegisterFullObjectHierarchyUndo(item.transform.parent.gameObject, "Reordenar opción");
            item.transform.SetSiblingIndex(Mathf.Max(0, item.transform.GetSiblingIndex() + delta));
        }

        private static void AddOption(List<MenuItemView> items)
        {
            if (items.Count == 0) { EditorUtility.DisplayDialog("Añadir opción", "Crea el menú desde GameObject → CTEditor UI para tener una opción que copiar.", "Vale"); return; }
            var last = items[items.Count - 1];
            // El cursor nunca debe copiarse con la opción: si estuviera dentro, se saca antes.
            var screenCursor = last.GetComponentInParent<MenuScreen>(true)?.Cursor;
            if (screenCursor != null && screenCursor.IsChildOf(last.transform))
                Undo.SetTransformParent(screenCursor, last.GetComponentInParent<MenuScreen>(true).transform, "Sacar el cursor");
            var copy = Object.Instantiate(last.gameObject, last.transform.parent);
            Undo.RegisterCreatedObjectUndo(copy, "Añadir opción");
            var item = copy.GetComponent<MenuItemView>();
            string id = "opcion_" + (items.Count + 1);
            copy.name = "Opción · " + id;
            item.OptionId = id;
            item.Action = MenuActionKind.Close;
            item.Target = "";
            if (item.Label != null) item.Label.text = "NUEVA OPCIÓN";
            Selection.activeGameObject = copy;
        }
    }

    /// <summary>Inspector de una OPCIÓN: qué hace (con desplegables de menús y acciones) y cómo se ve en cada estado.</summary>
    [CustomEditor(typeof(MenuItemView))]
    public sealed class MenuItemViewInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var item = (MenuItemView)target;
            serializedObject.Update();
            EditorTheme.Section("Qué hace", EditorTheme.Interface);
            Field("optionId");

            var action = serializedObject.FindProperty("action");
            var kinds = (MenuActionKind[])System.Enum.GetValues(typeof(MenuActionKind));
            int current = System.Array.IndexOf(kinds, (MenuActionKind)action.enumValueIndex);
            int chosen = EditorGUILayout.Popup(new GUIContent("Qué hace"), Mathf.Max(0, current),
                kinds.Select(k => new GUIContent(ClassicInterface.ActionName(k))).ToArray());
            action.enumValueIndex = (int)kinds[chosen];

            var t = serializedObject.FindProperty("target");
            var kind = kinds[chosen];
            if (kind == MenuActionKind.OpenMenu || kind == MenuActionKind.ScreenAction)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(t, new GUIContent(kind == MenuActionKind.OpenMenu ? "Menú que abre (id)" : "Acción (id)"));
                KnownMenus.Picker(t, kind == MenuActionKind.OpenMenu ? KnownMenus.All() : ScreenActions.Known.Select(k => (k.id, $"{k.screen}: {k.name}")));
                EditorGUILayout.EndHorizontal();
            }
            Field("requiredFlag"); Field("hiddenByFlag"); Field("help");

            EditorTheme.Section("Cómo se ve", EditorTheme.Interface);
            EditorTheme.Paragraph("Arrastra tus imágenes (sprites) para cada estado. El texto se escribe en el propio texto de la escena.");
            foreach (var n in new[] { "label", "background", "normalSprite", "selectedSprite", "disabledSprite", "tintBackground",
                                      "backgroundNormal", "backgroundSelected", "backgroundDisabled", "tintText", "textNormal", "textSelected",
                                      "textDisabled", "selectedMarker", "selectedScale" })
                Field(n);
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Ver como:", GUILayout.Width(70));
            if (GUILayout.Button("Normal")) Preview(item, MenuItemView.State.Normal);
            if (GUILayout.Button("Elegida")) Preview(item, MenuItemView.State.Selected);
            if (GUILayout.Button("Desactivada")) Preview(item, MenuItemView.State.Disabled);
            EditorGUILayout.EndHorizontal();
        }

        private void Field(string name)
        {
            var p = serializedObject.FindProperty(name);
            if (p != null) EditorGUILayout.PropertyField(p, new GUIContent(Etiquetas.Field(name), p.tooltip), true);
        }

        private static void Preview(MenuItemView item, MenuItemView.State s)
        {
            var objs = new List<Object> { item, item.transform };
            if (item.Label != null) objs.Add(item.Label);
            var bg = item.GetComponent<Image>(); if (bg != null) objs.Add(bg);
            foreach (var t in item.GetComponentsInChildren<Transform>(true)) objs.Add(t.gameObject); // marcadores
            Undo.RecordObjects(objs.ToArray(), "Vista previa de la opción");
            item.SetState(s);
            SceneView.RepaintAll();
        }
    }

    [CustomEditor(typeof(DialogueBoxView))]
    public sealed class DialogueBoxViewInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorTheme.TitleBand("Caja de texto de la escena",
                "El juego escribe aquí TODOS sus mensajes (diálogos, avisos, Sí/No). Cambia el fondo, el marco y la fuente como en cualquier UI. " +
                "Cuántas líneas caben se ajusta en Interfaz → Controles y caja de texto.", EditorTheme.Interface, "💬", navigation: false);
            serializedObject.Update();
            foreach (var n in new[] { "text", "moreIndicator" })
            {
                var p = serializedObject.FindProperty(n);
                if (p != null) EditorGUILayout.PropertyField(p, new GUIContent(Etiquetas.Field(n), p.tooltip));
            }
            serializedObject.ApplyModifiedProperties();
        }
    }
}
