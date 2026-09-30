using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CTEditor.Adventure.Domain.Interface;
using CTEditor.GameDefinition.Editor;
using CTEditor.GameDefinition.Infrastructure.Acl;
using CTEditor.GameDefinition.Infrastructure.Catalog;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.Bootstrap.Editor
{
    /// <summary>
    /// MAPA DE MENÚS (CTEditor → Interfaz → Mapa de menús): todos los menús como cajas y, con flechas, QUÉ
    /// OPCIÓN ABRE QUÉ (como las cadenas evolutivas, pero de menús).
    ///
    ///   • Cada caja es un menú: el de la ESCENA (🎨, con tus gráficos) si lo hay; si no, su ficha (📋) o el clásico.
    ///   • A la derecha, las PANTALLAS del juego (Equipo, Mochila, Opciones...) y «Cerrar».
    ///   • Para ENLAZAR: pulsa el ● de una opción y luego la caja a la que debe llevar. Clic derecho en el ●:
    ///     acciones de pantalla o quitar el enlace.
    ///   • Arrastra las cajas por su título; arrastra el fondo para moverte.
    /// </summary>
    public sealed class MenuFlowWindow : EditorWindow
    {
        private const float NodeW = 250f, HeaderH = 26f, RowH = 20f;

        private enum Source { Scene, Data, Classic, Screen }

        private sealed class Node
        {
            public string Id;
            public string Title;
            public Source Source;
            public MenuScreen Scene;
            public MenuData Data;
            public MenuDefinition Classic;
            public MenuActionKind ScreenKind;           // para las cajas de pantalla
            public List<MenuOption> Options = new List<MenuOption>();
            public Rect Rect;
        }

        private readonly List<Node> _nodes = new List<Node>();
        private Vector2 _pan = new Vector2(20, 60);
        private Node _dragNode;
        private bool _panning;
        private (Node node, int option)? _linkFrom;
        private string _newId = "";
        private string _rootId = ClassicInterface.PauseMenu;

        // CAPAS: para no amontonar líneas. Capa 1 = el menú que abre el botón Menú; capa 2 = los que abre ese...
        private readonly Dictionary<string, int> _depth = new Dictionary<string, int>();
        private int _layer = -1;            // -1 = todas
        private bool _showClose;            // las flechas a «Cerrar» (muchas y poco útiles): ocultas por defecto
        private Node _selected;             // si hay uno elegido, solo SUS enlaces se ven fuertes

        private int DepthOf(Node n) => n.Source == Source.Screen ? int.MaxValue : _depth.TryGetValue(n.Id, out var d) ? d : 99;

        /// <summary>¿Se dibuja esta caja con la capa elegida? (la capa y la siguiente, más las pantallas)</summary>
        private bool Visible(Node n)
        {
            if (_layer < 0 || n.Source == Source.Screen) return true;
            int d = DepthOf(n);
            return d == _layer || d == _layer + 1 || (d == 99 && _layer == MaxLayer + 1);
        }

        private int MaxLayer => _depth.Count > 0 ? _depth.Values.Max() : 0;

        [MenuItem(CTEditor.GameDefinition.Editor.EditorMenus.Interface + "Mapa de menús", false, CTEditor.GameDefinition.Editor.EditorMenus.InterfaceOrder + 3)]
        public static void Open()
        {
            var w = GetWindow<MenuFlowWindow>();
            w.titleContent = new GUIContent("🔗 Mapa de menús");
            w.minSize = new Vector2(900, 500);
            w.Reload();
            w.Show();
        }

        private void OnEnable() { wantsMouseMove = true; Reload(); }
        private void OnFocus() => Reload();
        private void OnHierarchyChange() { if (!EditorApplication.isPlaying) Reload(); }

        // ---------------- Datos ----------------

        private void Reload()
        {
            _nodes.Clear();
            var settings = ContentAssets.LoadAll<InterfaceSettingsData>().FirstOrDefault(s => s.Id == "ajustes");
            _rootId = settings != null && !string.IsNullOrWhiteSpace(settings.PauseMenuId) ? settings.PauseMenuId : ClassicInterface.PauseMenu;

            var scene = MenuScreen.AllInScene().Where(s => !s.IsList && !string.IsNullOrWhiteSpace(s.ScreenId)).ToList();
            var data = ContentAssets.LoadAll<MenuData>().Where(d => !string.IsNullOrWhiteSpace(d.Id)).ToList();
            var ids = scene.Select(s => s.ScreenId).Concat(data.Select(d => d.Id)).Concat(ClassicInterface.Menus().Select(m => m.Id)).Distinct().ToList();

            foreach (var id in ids)
            {
                var n = new Node { Id = id };
                var sc = scene.FirstOrDefault(s => s.ScreenId == id);
                var d = data.FirstOrDefault(x => x.Id == id);
                if (sc != null) { n.Source = Source.Scene; n.Scene = sc; n.Data = d; n.Options = sc.StaticItems().Select(i => i.ToOption()).ToList(); n.Title = sc.name; }
                else if (d != null) { n.Source = Source.Data; n.Data = d; var def = InterfaceMapper.ToDomain(d); n.Options = def.Options.ToList(); n.Title = d.DisplayName; }
                else { n.Source = Source.Classic; n.Classic = ClassicInterface.Find(id); n.Options = n.Classic.Options.ToList(); n.Title = n.Classic.DisplayName; }
                _nodes.Add(n);
            }
            foreach (var (kind, title) in ScreenNodes)
                _nodes.Add(new Node { Id = "#" + kind, Title = title, Source = Source.Screen, ScreenKind = kind });
            Layout();
            // Las cajas son objetos NUEVOS tras recargar: la elegida (y la que se arrastra o enlaza) se buscan por id,
            // y la capa se ajusta si ahora hay menos.
            _selected = _selected == null ? null : _nodes.FirstOrDefault(n => n.Id == _selected.Id);
            _dragNode = null;
            if (_linkFrom.HasValue)
            {
                var from = _nodes.FirstOrDefault(n => n.Id == _linkFrom.Value.node.Id);
                _linkFrom = from != null ? (from, _linkFrom.Value.option) : ((Node, int)?)null;
            }
            if (_layer > MaxLayer + 1) _layer = -1;
            Repaint();
        }

        private static readonly (MenuActionKind kind, string title)[] ScreenNodes =
        {
            (MenuActionKind.OpenParty, "Pantalla: Equipo"), (MenuActionKind.OpenBag, "Pantalla: Mochila"),
            (MenuActionKind.OpenSummary, "Pantalla: Resumen"), (MenuActionKind.OpenTrainerCard, "Pantalla: Ficha"),
            (MenuActionKind.Options, "Pantalla: Opciones"), (MenuActionKind.Save, "Guardar (próximamente)"),
            (MenuActionKind.OpenPokedex, "Pokédex (próximamente)"), (MenuActionKind.Close, "✖ Cerrar el menú"),
        };

        // Posición: la guardada, o por niveles desde el menú de pausa (como un árbol).
        private void Layout()
        {
            var depth = new Dictionary<string, int>();
            var queue = new Queue<string>();
            if (_nodes.Any(n => n.Id == _rootId)) { depth[_rootId] = 0; queue.Enqueue(_rootId); }
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                var node = _nodes.First(n => n.Id == id);
                foreach (var o in node.Options.Where(o => o.Action == MenuActionKind.OpenMenu && _nodes.Any(n => n.Id == o.Target)))
                    if (!depth.ContainsKey(o.Target)) { depth[o.Target] = depth[id] + 1; queue.Enqueue(o.Target); }
            }
            _depth.Clear();
            foreach (var kv in depth) _depth[kv.Key] = kv.Value;
            int maxDepth = depth.Count > 0 ? depth.Values.Max() : 0;
            var yByColumn = new Dictionary<int, float>();
            foreach (var n in _nodes)
            {
                int col = n.Source == Source.Screen ? maxDepth + 2 : depth.TryGetValue(n.Id, out var dd) ? dd : maxDepth + 1;
                float h = HeaderH + Math.Max(1, n.Options.Count) * RowH + 8f;
                if (!yByColumn.ContainsKey(col)) yByColumn[col] = 0f;
                var def = new Vector2(col * (NodeW + 90f), yByColumn[col]);
                yByColumn[col] += h + 24f;
                var pos = LoadPos(n.Id, def);
                n.Rect = new Rect(pos.x, pos.y, NodeW, n.Source == Source.Screen ? HeaderH + 4f : h);
            }
        }

        private static string PosKey(string id) => "CTEditor.MenuFlow." + Application.dataPath.GetHashCode() + "." + id;
        private static Vector2 LoadPos(string id, Vector2 fallback)
        {
            string s = EditorPrefs.GetString(PosKey(id), "");
            var parts = s.Split(';');
            return parts.Length == 2 && float.TryParse(parts[0], out var x) && float.TryParse(parts[1], out var y) ? new Vector2(x, y) : fallback;
        }
        private static void SavePos(Node n) => EditorPrefs.SetString(PosKey(n.Id), n.Rect.x + ";" + n.Rect.y);

        // ---------------- Dibujo ----------------

        private void OnGUI()
        {
            DrawToolbar();
            var area = new Rect(0, 52, position.width, position.height - 52);
            EditorGUI.DrawRect(area, new Color(0.16f, 0.17f, 0.2f));
            GUI.BeginGroup(area);
            DrawEdges();
            foreach (var n in _nodes) if (Visible(n)) DrawNode(n);
            if (_linkFrom.HasValue) DrawLinking();
            HandleEvents();
            GUI.EndGroup();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("🔗 Mapa de menús", EditorStyles.boldLabel, GUILayout.Width(140));
            if (GUILayout.Button("↻ Refrescar", GUILayout.Width(90))) Reload();
            if (GUILayout.Button("Ordenar", GUILayout.Width(70)))
            {
                foreach (var n in _nodes) EditorPrefs.DeleteKey(PosKey(n.Id));
                _pan = new Vector2(20, 60); Reload();
            }
            GUILayout.Space(16);
            GUILayout.Label("Menú nuevo:", GUILayout.Width(80));
            _newId = EditorGUILayout.TextField(_newId, GUILayout.Width(140));
            GUI.enabled = !string.IsNullOrWhiteSpace(_newId) && _nodes.All(n => n.Id != _newId.Trim());
            if (GUILayout.Button("+ Crear", GUILayout.Width(70))) CreateMenu(_newId.Trim());
            GUI.enabled = true;
            GUILayout.Space(16);
            var layers = new List<string> { "Todas las capas" };
            for (int i = 0; i <= MaxLayer; i++) layers.Add(i == 0 ? "Capa 1 (se abre con Menú)" : $"Capa {i + 1}");
            layers.Add("Menús sueltos (nadie los abre)");
            int sel = EditorGUILayout.Popup(_layer + 1, layers.ToArray(), GUILayout.Width(210));
            if (sel - 1 != _layer) { _layer = sel - 1; Repaint(); }
            _showClose = GUILayout.Toggle(_showClose, "Flechas a «Cerrar»", GUILayout.Width(130));
            if (_selected != null && GUILayout.Button("Ver todas las flechas", GUILayout.Width(140))) _selected = null;
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(_linkFrom.HasValue
                    ? "Ahora haz clic en la CAJA a la que debe llevar esa opción (Esc o clic en el fondo = cancelar)."
                    : "Pulsa el ● de una opción y luego la caja de destino. Clic derecho en el ● = más acciones. 🎨 escena · 📋 ficha · ◌ clásico. Arrastra el fondo para moverte.",
                EditorStyles.miniLabel);
        }

        private Rect Screen(Node n) => new Rect(n.Rect.position + _pan, n.Rect.size);

        private void DrawNode(Node n)
        {
            var r = Screen(n);
            Color accent = n.Source == Source.Screen ? (n.ScreenKind == MenuActionKind.Close ? EditorTheme.Bad : EditorTheme.Ok)
                         : n.Id == _rootId ? EditorTheme.Warn : EditorTheme.Interface;
            if (_selected == n) EditorGUI.DrawRect(new Rect(r.x - 5, r.y - 5, r.width + 10, r.height + 10), Color.white);
            EditorGUI.DrawRect(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), accent);
            EditorGUI.DrawRect(r, new Color(0.23f, 0.24f, 0.28f));
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, HeaderH), EditorTheme.WithAlpha(accent, 0.55f));

            string badge = n.Source == Source.Scene ? "🎨 " : n.Source == Source.Data ? "📋 " : n.Source == Source.Classic ? "◌ " : "";
            string root = n.Id == _rootId ? "  (se abre con Menú)" : "";
            GUI.Label(new Rect(r.x + 6, r.y + 4, r.width - 70, 18), badge + (n.Source == Source.Screen ? n.Title : n.Id + root), EditorStyles.whiteLabel);
            if (n.Source != Source.Screen && GUI.Button(new Rect(r.xMax - 60, r.y + 4, 56, 18), "Abrir", EditorStyles.miniButton)) OpenNode(n);

            for (int i = 0; i < n.Options.Count; i++)
            {
                var o = n.Options[i];
                var row = new Rect(r.x + 6, r.y + HeaderH + i * RowH, r.width - 30, RowH);
                string what = o.Action == MenuActionKind.ScreenAction ? $"  · {ScreenActions.NameOf(o.Target)}" : "";
                string flag = string.IsNullOrWhiteSpace(o.RequiredFlag) ? "" : $"  [{o.RequiredFlag}]";
                GUI.Label(row, TextTokens.Replace(o.Label, new Dictionary<string, string> { ["jugador"] = "JUGADOR" }) + what + flag, EditorStyles.whiteMiniLabel);
                var port = PortRect(n, i);
                bool linked = TargetOf(o) != null;
                EditorGUI.DrawRect(port, linked ? EditorTheme.Ok : o.Action == MenuActionKind.OpenMenu ? EditorTheme.Bad : new Color(0.6f, 0.6f, 0.65f));
            }
        }

        private Rect PortRect(Node n, int option)
        {
            var r = Screen(n);
            return new Rect(r.xMax - 18, r.y + HeaderH + option * RowH + 5, 11, 11);
        }

        private Node TargetOf(MenuOption o)
        {
            switch (o.Action)
            {
                case MenuActionKind.OpenMenu: return _nodes.FirstOrDefault(n => n.Source != Source.Screen && n.Id == o.Target);
                case MenuActionKind.ScreenAction: return null;
                default: return _nodes.FirstOrDefault(n => n.Source == Source.Screen && n.ScreenKind == o.Action);
            }
        }

        private void DrawEdges()
        {
            foreach (var n in _nodes.Where(x => x.Source != Source.Screen && Visible(x)))
                for (int i = 0; i < n.Options.Count; i++)
                {
                    if (_layer >= 0 && DepthOf(n) != _layer && !(DepthOf(n) == 99 && _layer == MaxLayer + 1)) continue; // solo salen flechas de la capa elegida
                    var target = TargetOf(n.Options[i]);
                    if (target == null || !Visible(target)) continue;
                    if (!_showClose && target.Source == Source.Screen && target.ScreenKind == MenuActionKind.Close) continue;
                    var from = PortRect(n, i).center;
                    var t = Screen(target);
                    var to = new Vector2(t.x, t.y + HeaderH / 2f);
                    Color c = target.Source == Source.Screen ? (target.ScreenKind == MenuActionKind.Close ? new Color(0.9f, 0.4f, 0.35f, 0.35f) : new Color(0.4f, 0.8f, 0.45f, 0.8f))
                                                             : new Color(0.65f, 0.6f, 1f, 0.9f);
                    // Con una caja elegida, sus enlaces (de ida y de vuelta) fuertes y el resto casi transparente.
                    if (_selected != null && n != _selected && target != _selected) c.a = 0.12f;
                    Curve(from, to, c);
                }
        }

        private static void Curve(Vector2 a, Vector2 b, Color c)
        {
            var pts = new Vector3[24];
            float dx = Mathf.Max(60f, Mathf.Abs(b.x - a.x) * 0.5f);
            var p1 = new Vector2(a.x + dx, a.y); var p2 = new Vector2(b.x - dx, b.y);
            for (int i = 0; i < pts.Length; i++)
            {
                float t = i / (pts.Length - 1f), u = 1 - t;
                var p = u * u * u * a.x + 3 * u * u * t * p1.x + 3 * u * t * t * p2.x + t * t * t * b.x;
                var q = u * u * u * a.y + 3 * u * u * t * p1.y + 3 * u * t * t * p2.y + t * t * t * b.y;
                pts[i] = new Vector3(p, q, 0);
            }
            Handles.color = c;
            Handles.DrawAAPolyLine(3f, pts);
            // Punta de flecha.
            Handles.DrawAAPolyLine(3f, new Vector3(b.x - 8, b.y - 5, 0), new Vector3(b.x, b.y, 0), new Vector3(b.x - 8, b.y + 5, 0));
        }

        private void DrawLinking()
        {
            var (node, option) = _linkFrom.Value;
            Curve(PortRect(node, option).center, Event.current.mousePosition, new Color(1f, 0.85f, 0.3f, 1f));
            Repaint();
        }

        // ---------------- Ratón ----------------

        private void HandleEvents()
        {
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape && _linkFrom.HasValue) { _linkFrom = null; e.Use(); return; }

            if (e.type == EventType.MouseDown)
            {
                // ¿Un puerto?
                foreach (var n in _nodes.Where(x => x.Source != Source.Screen && Visible(x)))
                    for (int i = 0; i < n.Options.Count; i++)
                        if (PortRect(n, i).Contains(e.mousePosition))
                        {
                            if (EditorApplication.isPlaying)
                                ShowNotification(new GUIContent("Sal de Play para enlazar (lo que cambies jugando se pierde)."));
                            else if (e.button == 1) PortMenu(n, i);
                            else _linkFrom = (n, i);
                            e.Use();
                            return;
                        }

                var hit = _nodes.LastOrDefault(n => Visible(n) && Screen(n).Contains(e.mousePosition));
                if (_linkFrom.HasValue)
                {
                    if (hit != null) Link(_linkFrom.Value.node, _linkFrom.Value.option, hit);
                    _linkFrom = null;
                    e.Use();
                    return;
                }
                if (hit != null && e.mousePosition.y - Screen(hit).y <= HeaderH) { _dragNode = hit; _selected = hit; e.Use(); return; }
                if (hit == null) { _panning = true; e.Use(); }
            }
            else if (e.type == EventType.MouseDrag)
            {
                if (_dragNode != null) { _dragNode.Rect.position += e.delta; e.Use(); Repaint(); }
                else if (_panning) { _pan += e.delta; e.Use(); Repaint(); }
            }
            else if (e.type == EventType.MouseUp)
            {
                if (_dragNode != null) { SavePos(_dragNode); _dragNode = null; e.Use(); }
                _panning = false;
            }
            else if (e.type == EventType.MouseMove && _linkFrom.HasValue) Repaint();
        }

        private void PortMenu(Node n, int i)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Cerrar el menú"), false, () => SetAction(n, i, MenuActionKind.Close, ""));
            foreach (var (kind, title) in ScreenNodes.Where(s => s.kind != MenuActionKind.Close))
            {
                var k = kind;
                menu.AddItem(new GUIContent("Abrir pantalla/" + title.Replace("Pantalla: ", "")), false, () => SetAction(n, i, k, ""));
            }
            foreach (var a in ScreenActions.Known)
            {
                string id = a.id;
                menu.AddItem(new GUIContent($"Acción de pantalla/{a.screen}/{a.name}"), false, () => SetAction(n, i, MenuActionKind.ScreenAction, id));
            }
            menu.ShowAsContext();
        }

        private void Link(Node from, int option, Node to)
        {
            if (to.Source == Source.Screen) SetAction(from, option, to.ScreenKind, "");
            else if (to == from) EditorUtility.DisplayDialog("Mapa de menús", "Un menú no puede abrirse a sí mismo.", "Vale");
            else SetAction(from, option, MenuActionKind.OpenMenu, to.Id);
        }

        // ---------------- Guardar el enlace (en la escena o en la ficha) ----------------

        private void SetAction(Node n, int option, MenuActionKind action, string target)
        {
            if (n.Source == Source.Scene)
            {
                var item = n.Scene.StaticItems()[option];
                Undo.RecordObject(item, "Enlazar opción");
                item.Action = action;
                item.Target = target;
                EditorUtility.SetDirty(item);
                EditorSceneManager.MarkSceneDirty(item.gameObject.scene);
            }
            else
            {
                var data = n.Data;
                if (data == null) // clásico: se crea su ficha para poder cambiarlo
                {
                    if (!EditorUtility.DisplayDialog("Mapa de menús", $"«{n.Id}» es el menú clásico. Para cambiarlo se creará su ficha (editable). ¿Seguir?", "Crear y enlazar", "Cancelar")) return;
                    var def = n.Classic;
                    ContentAssets.CreateIfMissing<MenuData>(ContentFolders.Menus, def.Id, def.DisplayName, so => MenuEditorWindow.Fill(so, def));
                    AssetDatabase.SaveAssets();
                    data = ContentAssets.FindById<MenuData>(n.Id);
                    if (data == null) return;
                }
                ContentAssets.Edit(data, so =>
                {
                    var e = so.FindProperty("options").GetArrayElementAtIndex(option);
                    e.FindPropertyRelative("action").enumValueIndex = (int)action;
                    e.FindPropertyRelative("target").stringValue = target ?? "";
                });
                AssetDatabase.SaveAssets();
            }
            Reload();
        }

        private void CreateMenu(string id)
        {
            var def = new MenuDefinition(id, id) { Anchor = MenuAnchor.Center }.Add(new MenuOption("salir", "SALIR", MenuActionKind.Close));
            ContentAssets.CreateIfMissing<MenuData>(ContentFolders.Menus, id, id, so => MenuEditorWindow.Fill(so, def));
            AssetDatabase.SaveAssets();
            _newId = "";
            Reload();
        }

        private static void OpenNode(Node n)
        {
            if (n.Scene != null) { Selection.activeGameObject = n.Scene.gameObject; EditorGUIUtility.PingObject(n.Scene.gameObject); return; }
            if (n.Data != null) { MenuEditorWindow.OpenOn(n.Data); return; }
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Crear su ficha (para editarlo)"), false, () =>
            {
                ContentAssets.CreateIfMissing<MenuData>(ContentFolders.Menus, n.Classic.Id, n.Classic.DisplayName, so => MenuEditorWindow.Fill(so, n.Classic));
                AssetDatabase.SaveAssets();
                var d = ContentAssets.FindById<MenuData>(n.Classic.Id);
                if (d != null) MenuEditorWindow.OpenOn(d);
            });
            menu.AddItem(new GUIContent("Crearlo en la escena (con gráficos)"), false, () => SceneMenuBuilder.CreateMenu(n.Classic));
            menu.ShowAsContext();
        }
    }
}
