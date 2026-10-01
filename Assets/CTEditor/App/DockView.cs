using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using CTEditor.Workspace;

namespace CTEditor.App
{
    /// <summary>
    /// Dibuja la distribución de paneles (DockLayout) en la ventana y la deja cambiar con el ratón:
    ///   - arrastrar un SEPARADOR cambia el tamaño de las dos partes (como el Inspector y el Hierarchy de Unity);
    ///   - arrastrar una PESTAÑA la lleva a otro grupo (en medio) o a un lado de él (divide el espacio);
    ///   - la ✕ de la pestaña cierra el panel; el «+» del grupo abre uno cerrado.
    /// El contenido de cada panel se crea una vez y se conserva al reordenar (no pierde lo que tenía).
    /// </summary>
    public sealed class DockView : VisualElement
    {
        private const float SplitterSize = 5f;
        private const float DragThreshold = 6f;

        private readonly AppShell _shell;
        private readonly DockLayout _layout;
        private readonly Dictionary<string, VisualElement> _contents = new Dictionary<string, VisualElement>();
        private readonly Dictionary<DockTabs, VisualElement> _groupViews = new Dictionary<DockTabs, VisualElement>();

        // Tab drag state.
        private string _dragPanel;
        private Vector2 _dragStart;
        private bool _dragging;
        private VisualElement _ghost, _dropMarker;
        private DockTabs _dropTarget;
        private DockSide _dropSide;

        public DockView(AppShell shell, DockLayout layout)
        {
            _shell = shell;
            _layout = layout;
            style.flexGrow = 1;
            style.overflow = Overflow.Hidden;
            Refresh();
        }

        /// <summary>Rebuilds the tree (after docking, closing or opening); panel contents are reused.</summary>
        public void Refresh()
        {
            Clear();
            _groupViews.Clear();
            var root = Build(_layout.Root);
            root.style.flexGrow = 1;
            Add(root);
        }

        private void Changed()
        {
            _shell.SaveWorkspaceSoon();
            Refresh();
        }

        private VisualElement Build(DockNode node)
        {
            if (node is DockTabs tabs) return BuildGroup(tabs);
            var split = (DockSplit)node;
            bool horizontal = split.Direction == SplitDirection.Horizontal;
            var box = new VisualElement();
            box.style.flexDirection = horizontal ? FlexDirection.Row : FlexDirection.Column;
            var childViews = new List<VisualElement>();
            for (int i = 0; i < split.Children.Count; i++)
            {
                if (i > 0) box.Add(Splitter(split, i - 1, box, childViews, horizontal));
                var child = Build(split.Children[i]);
                SetShare(child, split.Children[i].Size);
                childViews.Add(child);
                box.Add(child);
            }
            return box;
        }

        private static void SetShare(VisualElement e, float size)
        {
            e.style.flexGrow = size;
            e.style.flexShrink = 1;
            e.style.flexBasis = 0;
            e.style.minWidth = 60;
            e.style.minHeight = 40;
        }

        // ── Splitters ────────────────────────────────────────────────────────────────────────────

        private VisualElement Splitter(DockSplit split, int index, VisualElement box, List<VisualElement> views, bool horizontal)
        {
            var s = new VisualElement();
            s.style.flexShrink = 0;
            if (horizontal) s.style.width = SplitterSize;
            else s.style.height = SplitterSize;
            var normal = Ui.C("fondo");
            var hot = Ui.C("acento");
            s.style.backgroundColor = normal;
            s.tooltip = "Arrastra para cambiar el tamaño";

            bool dragging = false;
            float startPos = 0, startA = 0, total = 0;
            s.RegisterCallback<PointerEnterEvent>(_ => s.style.backgroundColor = hot);
            s.RegisterCallback<PointerLeaveEvent>(_ => { if (!dragging) s.style.backgroundColor = normal; });
            s.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                dragging = true;
                startPos = horizontal ? e.position.x : e.position.y;
                startA = split.Children[index].Size;
                // Pixels that the split shares among its children (without the splitters).
                total = (horizontal ? box.layout.width : box.layout.height) - SplitterSize * (split.Children.Count - 1);
                s.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            s.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!dragging || !s.HasPointerCapture(e.pointerId) || total <= 1) return;
                float delta = ((horizontal ? e.position.x : e.position.y) - startPos) / total;
                _layout.Resize(split, index, startA + delta);
                SetShare(views[index], split.Children[index].Size);
                SetShare(views[index + 1], split.Children[index + 1].Size);
            });
            s.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!dragging) return;
                dragging = false;
                s.ReleasePointer(e.pointerId);
                s.style.backgroundColor = normal;
                _shell.SaveWorkspaceSoon();
            });
            // Double click: split evenly with the neighbour.
            s.RegisterCallback<ClickEvent>(e =>
            {
                if (e.clickCount != 2) return;
                float pair = split.Children[index].Size + split.Children[index + 1].Size;
                _layout.Resize(split, index, pair / 2f);
                SetShare(views[index], split.Children[index].Size);
                SetShare(views[index + 1], split.Children[index + 1].Size);
                _shell.SaveWorkspaceSoon();
            });
            return s;
        }

        // ── Tab groups ───────────────────────────────────────────────────────────────────────────

        private VisualElement BuildGroup(DockTabs tabs)
        {
            var group = Ui.Column().Bg("panel");
            group.style.overflow = Overflow.Hidden;
            _groupViews[tabs] = group;

            var header = Ui.Row(0).Bg("panel_alt");
            header.style.flexShrink = 0;
            header.style.height = Ui.FontSize + 14;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = Ui.C("borde");
            header.style.overflow = Overflow.Hidden;
            for (int i = 0; i < tabs.Panels.Count; i++) header.Add(Tab(tabs, i));
            header.Add(Ui.Spacer());
            var add = Ui.IconButton("mas", null, "Abrir una ventana aquí");
            add.clicked += () => ShowAddMenu(tabs, add);
            add.style.height = Ui.FontSize + 8;
            add.style.minHeight = Ui.FontSize + 8;
            add.style.width = 26;
            add.style.paddingLeft = 0; add.style.paddingRight = 0;
            add.style.marginRight = 4;
            header.Add(add);
            group.Add(header);

            var body = new VisualElement().Grow();
            body.style.overflow = Overflow.Hidden;
            var active = tabs.ActivePanel;
            if (active != null) body.Add(ContentFor(active));
            group.Add(body);
            return group;
        }

        private VisualElement Tab(DockTabs tabs, int index)
        {
            string id = tabs.Panels[index];
            bool active = index == tabs.Active;
            var tab = Ui.Row(6).Pad(10, 0);
            tab.style.height = Length.Percent(100);
            tab.style.backgroundColor = active ? Ui.C("panel") : new Color(0, 0, 0, 0);
            tab.style.borderTopWidth = 2;
            tab.style.borderTopColor = active ? Ui.C("acento") : new Color(0, 0, 0, 0);
            tab.style.borderRightWidth = 1;
            tab.style.borderRightColor = Ui.C("borde");
            var label = Ui.Text(PanelCatalog.LabelOf(id), 0.95f, dim: !active, bold: active);
            label.pickingMode = PickingMode.Ignore;
            tab.Add(label);
            var close = Ui.IconButton("cerrar", () => { _layout.Close(id); Changed(); }, "Cerrar la ventana", iconSize: Mathf.Round(Ui.FontSize * 0.8f));
            close.style.height = Ui.FontSize + 4;
            close.style.minHeight = Ui.FontSize + 4;
            close.style.width = Ui.FontSize + 4;
            close.style.paddingLeft = 0; close.style.paddingRight = 0;
            close.style.fontSize = Ui.FontSize - 2;
            close.style.opacity = active ? 0.8f : 0.4f;
            tab.Add(close);
            tab.tooltip = "Arrastra la pestaña para moverla a otro sitio";

            tab.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0 || e.target == close) return;
                _dragPanel = id;
                _dragStart = e.position;
                _dragging = false;
                tab.CapturePointer(e.pointerId);
            });
            tab.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (_dragPanel != id || !tab.HasPointerCapture(e.pointerId)) return;
                if (!_dragging && Vector2.Distance(e.position, _dragStart) > DragThreshold) BeginDrag(id);
                if (_dragging) UpdateDrag(e.position);
            });
            tab.RegisterCallback<PointerUpEvent>(e =>
            {
                if (_dragPanel != id) return;
                tab.ReleasePointer(e.pointerId);
                bool wasDragging = _dragging;
                var target = _dropTarget;
                var side = _dropSide;
                EndDrag();
                if (wasDragging)
                {
                    if (target != null) { _layout.Dock(id, target, side); Changed(); }
                }
                else if (!active)
                {
                    tabs.Active = tabs.Panels.IndexOf(id);
                    Changed();
                }
            });
            // Middle click closes, like browser tabs.
            tab.RegisterCallback<PointerUpEvent>(e => { if (e.button == 2) { _layout.Close(id); Changed(); } });
            return tab;
        }

        private void ShowAddMenu(DockTabs tabs, VisualElement anchor)
        {
            var closed = PanelCatalog.All.Where(p => !_layout.IsOpen(p.Id)).ToList();
            var items = new List<MenuItem>();
            foreach (var p in closed)
            {
                var id = p.Id;
                items.Add(new MenuItem(p.Label, () => { _layout.Dock(id, tabs, DockSide.Center); Changed(); }));
            }
            if (items.Count == 0) items.Add(new MenuItem("Todas las ventanas están abiertas", null, enabled: false));
            var r = anchor.worldBound;
            _shell.ShowMenu(new Vector2(r.x, r.yMax + 2), items);
        }

        private VisualElement ContentFor(string panel)
        {
            if (!_contents.TryGetValue(panel, out var content))
            {
                content = PanelRegistry.Create(panel, _shell);
                content.style.flexGrow = 1;
                _contents[panel] = content;
            }
            content.RemoveFromHierarchy();
            return content;
        }

        // ── Drag and drop of tabs ────────────────────────────────────────────────────────────────

        private void BeginDrag(string panel)
        {
            _dragging = true;
            _ghost = Ui.Row(6).Bg("acento").Pad(10, 4).Round(4);
            _ghost.pickingMode = PickingMode.Ignore;
            _ghost.style.position = Position.Absolute;
            _ghost.style.opacity = 0.9f;
            var l = Ui.Text(PanelCatalog.LabelOf(panel), bold: true);
            l.style.color = Color.white;
            _ghost.Add(l);
            _dropMarker = new VisualElement();
            _dropMarker.pickingMode = PickingMode.Ignore;
            _dropMarker.style.position = Position.Absolute;
            _dropMarker.style.backgroundColor = Ui.WithAlpha(Ui.C("acento"), 0.28f);
            _dropMarker.Border(2, "acento", 4);
            _dropMarker.Show(false);
            _shell.DragLayer.Add(_dropMarker);
            _shell.DragLayer.Add(_ghost);
        }

        private void UpdateDrag(Vector2 pos)
        {
            _ghost.style.left = pos.x + 12;
            _ghost.style.top = pos.y + 8;
            _dropTarget = null;
            foreach (var kv in _groupViews)
            {
                var r = kv.Value.worldBound;
                if (!r.Contains(pos)) continue;
                _dropTarget = kv.Key;
                float fx = (pos.x - r.x) / r.width, fy = (pos.y - r.y) / r.height;
                const float edge = 0.25f;
                // The nearest edge wins when the pointer is in a corner.
                float dl = fx, dr = 1 - fx, dt = fy, db = 1 - fy;
                float min = Mathf.Min(Mathf.Min(dl, dr), Mathf.Min(dt, db));
                if (min > edge) _dropSide = DockSide.Center;
                else if (min == dl) _dropSide = DockSide.Left;
                else if (min == dr) _dropSide = DockSide.Right;
                else if (min == dt) _dropSide = DockSide.Top;
                else _dropSide = DockSide.Bottom;

                Rect m = r;
                switch (_dropSide)
                {
                    case DockSide.Left: m.width = r.width / 2; break;
                    case DockSide.Right: m.x += r.width / 2; m.width = r.width / 2; break;
                    case DockSide.Top: m.height = r.height / 2; break;
                    case DockSide.Bottom: m.y += r.height / 2; m.height = r.height / 2; break;
                }
                _dropMarker.Absolute(m.x, m.y, m.width, m.height);
                _dropMarker.Show(true);
                return;
            }
            _dropMarker.Show(false);
        }

        private void EndDrag()
        {
            _ghost?.RemoveFromHierarchy();
            _dropMarker?.RemoveFromHierarchy();
            _ghost = _dropMarker = null;
            _dragging = false;
            _dragPanel = null;
            _dropTarget = null;
        }
    }
}
