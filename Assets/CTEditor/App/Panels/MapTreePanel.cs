using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using CTEditor.Art.Domain;
using CTEditor.Editing;
using CTEditor.World.Domain;

namespace CTEditor.App
{
    /// <summary>
    /// Panel MAPAS: el árbol de mapas del juego, como en RPG Maker (un mapa puede contener otros). Clic abre, doble clic
    /// cambia el nombre, clic derecho: nuevo mapa dentro, mover, borrar...
    /// </summary>
    public sealed class MapTreePanel : VisualElement
    {
        private readonly AppShell _shell;
        private MapEditorSession S => _shell.Maps;
        private readonly ScrollView _list;

        public MapTreePanel(AppShell shell)
        {
            _shell = shell;
            style.flexGrow = 1;
            var bar = Ui.Row(6).Pad(8, 6);
            bar.style.flexShrink = 0;
            bar.With(Ui.Button("Nuevo mapa…", () => NewMapDialog(_shell, S?.Map?.Id ?? ""), Ui.ButtonKind.Primary,
                    "Crea un mapa (dentro del mapa abierto; en el diálogo puedes elegir la raíz)"),
                Ui.Spacer());
            Add(bar);
            Add(Ui.Separator());
            _list = Ui.Scroll().Grow();
            Add(_list);
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                if (S == null) return;
                S.TreeChanged += Refresh;
                S.MapOpened += Refresh;
                S.DirtyChanged += Refresh;
                S.PlayerStartChanged += Refresh;
                Refresh();
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (S == null) return;
                S.TreeChanged -= Refresh;
                S.MapOpened -= Refresh;
                S.DirtyChanged -= Refresh;
                S.PlayerStartChanged -= Refresh;
            });
        }

        private void Refresh()
        {
            _list.Clear();
            if (S == null) return;
            var walk = S.Tree.Walk(onlyExpanded: true).ToList();
            if (walk.Count == 0)
            {
                _list.Add(Ui.Hint("Aún no hay mapas. Pulsa «Nuevo mapa…».").Margin(10, 8, 10, 8));
                return;
            }
            foreach (var (entry, depth) in walk) _list.Add(Row(entry, depth));
        }

        private VisualElement Row(MapEntry e, int depth)
        {
            bool current = S.Map?.Id == e.Id;
            bool hasChildren = S.Tree.ChildrenOf(e.Id).Any();
            var row = Ui.Row(4).Pad(6, 3);
            row.style.paddingLeft = 6 + depth * 16;
            if (current) row.style.backgroundColor = Ui.C("seleccion");

            var toggle = Ui.Button(hasChildren ? (e.Expanded ? "-" : "+") : " ", () => { if (hasChildren) S.SetExpanded(e.Id, !e.Expanded); },
                Ui.ButtonKind.Flat, hasChildren ? (e.Expanded ? "Plegar" : "Desplegar") : "");
            toggle.style.width = 20;
            toggle.style.paddingLeft = 0; toggle.style.paddingRight = 0;
            toggle.style.height = Ui.FontSize + 6;
            row.Add(toggle);

            string suffix = (S.PlayerStart.map == e.Id ? "  (inicio)" : "") + (S.IsMapDirty(e.Id) ? "  *" : "");
            var label = Ui.Text(e.Name + suffix, bold: current);
            if (current) label.Colored("acento");
            label.pickingMode = PickingMode.Ignore;
            row.Add(label.Grow());

            row.RegisterCallback<PointerEnterEvent>(_ => { if (!current) row.style.backgroundColor = Ui.C("panel_alt"); });
            row.RegisterCallback<PointerLeaveEvent>(_ => { if (!current) row.style.backgroundColor = new Color(0, 0, 0, 0); });
            row.RegisterCallback<PointerUpEvent>(ev =>
            {
                if (ev.target == toggle) return;
                if (ev.button == 1) { Menu(e, ev.position); return; }
                if (ev.button != 0) return;
                if (!current) S.OpenMap(e.Id);
            });
            row.RegisterCallback<ClickEvent>(ev => { if (ev.clickCount == 2 && ev.target != toggle) Rename(e); });
            return row;
        }

        private void Menu(MapEntry e, Vector2 at)
        {
            var items = new List<MenuItem>
            {
                new MenuItem("Abrir", () => S.OpenMap(e.Id)),
                new MenuItem("Nuevo mapa dentro…", () => NewMapDialog(_shell, e.Id)),
                new MenuItem("Cambiar el nombre…", () => Rename(e)),
                MenuItem.Separator,
                new MenuItem("Mover a la raíz", () => S.MoveMap(e.Id, ""), enabled: e.ParentId.Length > 0),
            };
            foreach (var other in S.Tree.Walk().Select(w => w.entry).Where(o => o.Id != e.Id && o.Id != e.ParentId).Take(20))
            {
                var target = other.Id;
                items.Add(new MenuItem("Mover dentro de: " + other.Name, () => S.MoveMap(e.Id, target)));
            }
            items.Add(MenuItem.Separator);
            items.Add(new MenuItem("Borrar…", () => _shell.Confirm("Borrar mapa",
                $"¿Borrar «{e.Name}»? Se borra su archivo; los mapas que contiene pasan a su padre. No se puede deshacer.",
                "Borrar", () => S.DeleteMap(e.Id), danger: true)));
            _shell.ShowMenu(at, items);
        }

        private void Rename(MapEntry e) =>
            _shell.Prompt("Cambiar el nombre del mapa", "Nombre", e.Name, "Cambiar", name => S.RenameMap(e.Id, name));

        /// <summary>
        /// Diálogo «Nuevo mapa»: nombre, tipo (tramo exterior del mundo o interior), categoría (pueblo, ruta...), tamaño
        /// (20 × 15 como RPG Maker XP), tileset y, para exteriores, junto a qué tramo y por qué lado se coloca.
        /// </summary>
        public static void NewMapDialog(AppShell shell, string parentId, string nextTo = null)
        {
            var s = shell.Maps;
            if (s == null) return;
            var d = shell.ShowDialog("Nuevo mapa");
            string name = "Mapa nuevo";
            int w = MapDefinition.DefaultWidth, h = MapDefinition.DefaultHeight;
            var tilesets = s.AvailableTilesets();
            string tileset = s.Map?.TilesetId is string cur && tilesets.Contains(cur) ? cur : tilesets.FirstOrDefault() ?? "";
            string parent = parentId ?? "";
            var kind = MapKind.Exterior;
            var category = SectionCategory.Route;
            string anchor = nextTo ?? (s.Map != null && s.Map.InWorld ? s.Map.Id : null);
            FacingDirection? side = anchor != null ? FacingDirection.Right : (FacingDirection?)null;

            var body = Ui.Column(10);
            void Fill()
            {
                body.Clear();
                body.Add(Ui.TextBox("Nombre", name, v => name = v));

                var kinds = Ui.Row(6);
                kinds.With(Ui.Text("Tipo", bold: true),
                    Ui.Chip("Exterior (tramo del mundo)", kind == MapKind.Exterior, () => { kind = MapKind.Exterior; Fill(); }),
                    Ui.Chip("Interior (casa, cueva, edificio)", kind == MapKind.Interior, () => { kind = MapKind.Interior; Fill(); }));
                body.Add(kinds);

                var cats = Ui.Row(4);
                cats.style.flexWrap = Wrap.Wrap;
                cats.Add(Ui.Text("Categoría", bold: true).Margin(0, 0, 6, 0));
                for (int i = 0; i < WorldPanel.CategoryNames.Length; i++)
                {
                    var c = (SectionCategory)i;
                    cats.Add(Ui.Chip(WorldPanel.CategoryNames[i], category == c, () => { category = c; Fill(); }).Margin(0, 0, 4, 4));
                }
                body.Add(cats);

                var size = Ui.Row(10);
                size.With(Ui.NumberBox("Ancho", w, 1, MapDefinition.MaxSize, v => w = v), Ui.NumberBox("Alto", h, 1, MapDefinition.MaxSize, v => h = v));
                body.With(Ui.Text("Tamaño en tiles", bold: true), size,
                    Ui.Hint("20 × 15 es el tamaño de RPG Maker XP. Se puede cambiar luego en Propiedades."));

                if (kind == MapKind.Exterior)
                {
                    var where = Ui.Row(4);
                    where.style.flexWrap = Wrap.Wrap;
                    var anchorMap = s.Find(anchor);
                    if (anchorMap != null && anchorMap.InWorld)
                    {
                        where.Add(Ui.Text($"Junto a «{anchorMap.Name}»:", bold: true).Margin(0, 0, 6, 0));
                        foreach (var (label, dir) in new[] { ("arriba", FacingDirection.Up), ("abajo", FacingDirection.Down), ("a la izquierda", FacingDirection.Left), ("a la derecha", FacingDirection.Right) })
                        {
                            var dd = dir;
                            where.Add(Ui.Chip(label, side == dd, () => { side = dd; Fill(); }).Margin(0, 0, 4, 4));
                        }
                        where.Add(Ui.Chip("en un hueco libre", side == null, () => { side = null; Fill(); }).Margin(0, 0, 4, 4));
                    }
                    else where.Add(Ui.Hint("Se coloca en un hueco libre del mundo; luego lo puedes arrastrar en el panel Mundo."));
                    body.With(Ui.Text("Dónde en el mundo", bold: true), where);
                }
                else body.Add(Ui.Hint("Los interiores no van en el mundo: se entra por puertas (fase de eventos)."));

                var tsRow = Ui.Row(6);
                tsRow.style.flexWrap = Wrap.Wrap;
                if (tilesets.Count == 0) tsRow.Add(Ui.Hint("No hay tilesets cortados: el mapa se crea sin tileset. Corta uno en Recursos."));
                foreach (var t in tilesets)
                {
                    var id = t;
                    tsRow.Add(Ui.Chip(s.TilesetFor(t)?.Name ?? t, tileset == t, () => { tileset = id; Fill(); }, t).Margin(0, 0, 6, 6));
                }
                body.With(Ui.Text("Tileset", bold: true), tsRow);

                var parentRow = Ui.Row(6);
                parentRow.style.flexWrap = Wrap.Wrap;
                parentRow.Add(Ui.Chip("En la raíz del árbol", parent.Length == 0, () => { parent = ""; Fill(); }).Margin(0, 0, 6, 6));
                if (!string.IsNullOrEmpty(parentId) && s.Tree.Find(parentId) is MapEntry p)
                    parentRow.Add(Ui.Chip("Dentro de «" + p.Name + "»", parent == parentId, () => { parent = parentId; Fill(); }).Margin(0, 0, 6, 6));
                body.With(Ui.Text("En el árbol de mapas", bold: true), parentRow);
            }
            Fill();
            var scroll = Ui.Scroll();
            scroll.style.maxHeight = 560;
            scroll.Add(body);
            d.Body.Add(scroll);
            d.Buttons.With(Ui.Button("Cancelar", () => shell.CloseDialog(d)), Ui.Button("Crear", () =>
            {
                shell.CloseDialog(d);
                s.CreateMap(name, parent, w, h, tileset, kind, category, kind == MapKind.Exterior && side.HasValue ? anchor : null,
                    side ?? FacingDirection.Right);
                shell.Success($"Mapa «{name}» creado.");
            }, Ui.ButtonKind.Primary));
        }
    }

    /// <summary>Panel CAPAS: las capas del mapa (arriba la de encima): ver, bloquear, opacidad, orden, nombre.</summary>
    public sealed class LayersPanel : VisualElement
    {
        private readonly AppShell _shell;
        private MapEditorSession S => _shell.Maps;
        private readonly ScrollView _list;
        private readonly VisualElement _bar;

        public LayersPanel(AppShell shell)
        {
            _shell = shell;
            style.flexGrow = 1;
            _bar = Ui.Row(4).Pad(8, 6);
            _bar.style.flexShrink = 0;
            _bar.style.flexWrap = Wrap.Wrap;
            Add(_bar);
            Add(Ui.Separator());
            _list = Ui.Scroll().Grow();
            Add(_list);
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                if (S == null) return;
                S.MapOpened += Refresh;
                S.StructureChanged += Refresh;
                S.SelectionChanged += Refresh;
                Refresh();
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (S == null) return;
                S.MapOpened -= Refresh;
                S.StructureChanged -= Refresh;
                S.SelectionChanged -= Refresh;
            });
        }

        private void Refresh()
        {
            _bar.Clear();
            _list.Clear();
            var m = S?.Map;
            if (m == null) { _list.Add(Ui.Hint("Abre un mapa para ver sus capas.").Margin(10, 8, 10, 8)); return; }
            int active = S.ActiveLayer;
            _bar.With(Ui.Button("Añadir", () => S.AddLayer(), Ui.ButtonKind.Primary, "Nueva capa encima de la activa"),
                Ui.Button("Subir", () => S.MoveLayer(active, +1), Ui.ButtonKind.Normal, "Más arriba (se dibuja encima)"),
                Ui.Button("Bajar", () => S.MoveLayer(active, -1)),
                Ui.Button("Quitar", () => _shell.Confirm("Quitar capa", $"¿Quitar la capa «{m.Layers[active].Name}»? Se puede deshacer con Ctrl+Z.",
                    "Quitar", () => S.RemoveLayer(active), danger: true), Ui.ButtonKind.Flat));
            for (int i = m.Layers.Count - 1; i >= 0; i--)
            {
                int index = i;
                var l = m.Layers[i];
                var row = Ui.Row(6).Pad(8, 4);
                if (i == active) row.style.backgroundColor = Ui.C("seleccion");
                string[] roles = { "", " · suelo", " · detalles", " · encima" };
                var name = Ui.Text(l.Name + roles[(int)l.Role], bold: i == active).Grow();
                name.pickingMode = PickingMode.Ignore;
                var visible = Ui.Chip("Ver", l.Visible, () => S.SetLayerView(index, visible: !l.Visible), "Mostrar u ocultar (solo en el editor)");
                var locked = Ui.Chip("Bloq.", l.Locked, () => S.SetLayerView(index, locked: !l.Locked), "Bloquear: las herramientas no la cambian");
                var opacity = new Slider(0f, 1f) { value = l.Opacity, tooltip = "Opacidad en el editor" };
                opacity.style.width = 70;
                opacity.RegisterValueChangedCallback(e => S.SetLayerView(index, opacity: e.newValue));
                row.With(visible, locked, name, opacity);
                row.RegisterCallback<PointerUpEvent>(e =>
                {
                    if (e.button == 0 && (e.target == row || e.target == name)) S.SetActiveLayer(index);
                });
                row.RegisterCallback<ClickEvent>(e =>
                {
                    if (e.clickCount == 2 && (e.target == row || e.target == name))
                        _shell.Prompt("Nombre de la capa", "Nombre", l.Name, "Cambiar", n => S.RenameLayer(index, n));
                });
                // Right click: what the layer is for (automatic layers send each tile to the layer of its piece).
                row.RegisterCallback<PointerUpEvent>(e =>
                {
                    if (e.button != 1) return;
                    _shell.ShowMenu(e.position, new List<MenuItem>
                    {
                        new MenuItem("Capa de suelo", () => S.SetLayerRole(index, LayerRole.Ground), isChecked: l.Role == LayerRole.Ground),
                        new MenuItem("Capa de detalles", () => S.SetLayerRole(index, LayerRole.Detail), isChecked: l.Role == LayerRole.Detail),
                        new MenuItem("Capa de encima", () => S.SetLayerRole(index, LayerRole.Above), isChecked: l.Role == LayerRole.Above),
                        new MenuItem("Solo a mano", () => S.SetLayerRole(index, LayerRole.Custom), isChecked: l.Role == LayerRole.Custom),
                    });
                });
                _list.Add(row);
            }
            _list.Add(Ui.Hint("Arriba, la que se dibuja encima. Doble clic: cambiar el nombre. Clic derecho: para qué es (suelo, detalles, encima) — con capas automáticas cada tile va solo a la suya. Los tiles con prioridad se ven por encima del jugador sea cual sea su capa.")
                .Margin(10, 8, 10, 8));
        }
    }
}
