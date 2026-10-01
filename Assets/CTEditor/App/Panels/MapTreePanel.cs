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
        private static string SideName(FacingDirection d) => d switch
        {
            FacingDirection.Up => "encima",
            FacingDirection.Down => "debajo",
            FacingDirection.Left => "a la izquierda",
            _ => "a la derecha",
        };

        public static void NewMapDialog(AppShell shell, string parentId, string nextTo = null)
        {
            var s = shell.Maps;
            if (s == null) return;
            var d = shell.ShowDialog("Nuevo mapa", 58);
            string name = "Mapa nuevo";
            int w = MapDefinition.DefaultWidth, h = MapDefinition.DefaultHeight;
            var tilesets = s.AvailableTilesets();
            string tileset = s.Map?.TilesetId is string cur && tilesets.Contains(cur) ? cur : tilesets.FirstOrDefault() ?? "";
            string parent = parentId ?? "";
            var kind = MapKind.Exterior;
            var category = SectionCategory.Route;
            string anchor = nextTo ?? (s.Map != null && s.Map.InWorld ? s.Map.Id : null);
            FacingDirection? side = anchor != null ? FacingDirection.Right : (FacingDirection?)null;

            // Two columns: the form on the left (a label column and its controls), a summary with the tileset on the right.
            var columns = Ui.Row(18);
            columns.style.alignItems = Align.FlexStart;
            var body = Ui.Column(12).Grow();
            var side_ = Ui.Column(8).NoShrink();
            side_.style.width = 230;
            columns.With(body, side_);

            VisualElement Field(string label, VisualElement content, string hint = null)
            {
                var row = Ui.Row(12);
                row.style.alignItems = Align.FlexStart;
                var l = Ui.Text(label, 0.95f, dim: true).NoShrink();
                l.style.width = 110;
                l.style.marginTop = 5;
                var right = Ui.Column(4).Grow();
                right.Add(content);
                if (hint != null) right.Add(Ui.Hint(hint));
                row.With(l, right);
                return row;
            }

            VisualElement Chips() => Ui.Row(0).Wrap();
            Button Pick(string text, bool on, System.Action a, string tip = null) => (Button)Ui.Chip(text, on, a, tip).Margin(0, 0, 4, 4);

            void Fill()
            {
                body.Clear();
                side_.Clear();
                var nameBox = Ui.TextBox(null, name, v => name = v);
                body.Add(Field("Nombre", nameBox));

                var kinds = Chips();
                kinds.With(Pick("Exterior", kind == MapKind.Exterior, () => { kind = MapKind.Exterior; Fill(); }, "Un tramo del mundo continuo: se pasa andando de uno a otro"),
                    Pick("Interior", kind == MapKind.Interior, () => { kind = MapKind.Interior; Fill(); }, "Casa, cueva, edificio: se entra por una puerta"));
                body.Add(Field("Tipo", kinds, kind == MapKind.Exterior ? "Tramo del mundo continuo (pueblo, ruta...)." : "Aparte del mundo: se entra por puertas."));

                var cats = Chips();
                for (int i = 0; i < WorldPanel.CategoryNames.Length; i++)
                {
                    var c = (SectionCategory)i;
                    cats.Add(Pick(WorldPanel.CategoryNames[i], category == c, () => { category = c; Fill(); }));
                }
                body.Add(Field("Categoría", cats));

                // Size: the usual ones in one click, or any.
                var sizes = Ui.Column(6);
                var presets = Chips();
                foreach (var (pw, ph, label) in new[] { (20, 15, "20 × 15 (pantalla de XP)"), (40, 30, "40 × 30"), (60, 40, "60 × 40 (ruta larga)"), (12, 10, "12 × 10 (casa)") })
                {
                    int ww = pw, hh = ph;
                    presets.Add(Pick(label, w == ww && h == hh, () => { w = ww; h = hh; Fill(); }));
                }
                var custom = Ui.Row(22).Wrap();
                custom.With(Ui.NumberBox("Ancho", w, 1, MapDefinition.MaxSize, v => w = v), Ui.NumberBox("Alto", h, 1, MapDefinition.MaxSize, v => h = v));
                sizes.With(presets, custom);
                body.Add(Field("Tamaño (tiles)", sizes, "Se puede cambiar luego en Propiedades."));

                if (kind == MapKind.Exterior)
                {
                    var anchorMap = s.Find(anchor);
                    if (anchorMap != null && anchorMap.InWorld)
                    {
                        // A small compass: the existing section in the middle, the new one goes to the chosen side.
                        var compass = Ui.Column(2);
                        float cell = Ui.ControlHeight + 4;
                        VisualElement Cell(FacingDirection? dir, string icon, string tip)
                        {
                            if (dir == null) { var e = new VisualElement(); e.style.width = cell; e.style.height = cell; return e; }
                            var dd = dir.Value;
                            var b = Ui.IconButton(icon, () => { side = dd; Fill(); }, tip, side == dd);
                            b.style.width = cell; b.style.height = cell; b.style.minHeight = cell;
                            if (side != dd) { b.style.backgroundColor = Ui.Mix(Ui.C("panel_alt"), Ui.C("texto"), 0.08f); b.Border(1, "borde", Ui.Radius); }
                            return b;
                        }
                        var center = Ui.Text(anchorMap.Name, 0.75f, bold: true);
                        center.style.width = cell * 1.6f; center.style.height = cell;
                        center.style.unityTextAlign = TextAnchor.MiddleCenter;
                        center.Bg("fondo").Border(1, "acento", Ui.Radius);
                        center.tooltip = anchorMap.Name;
                        var r1 = Ui.Row(2); r1.style.justifyContent = Justify.Center;
                        r1.With(Cell(FacingDirection.Up, "arriba", "Encima de «" + anchorMap.Name + "»"));
                        var r2 = Ui.Row(2); r2.style.justifyContent = Justify.Center;
                        r2.With(Cell(FacingDirection.Left, "anterior", "A la izquierda"), center, Cell(FacingDirection.Right, "siguiente", "A la derecha"));
                        var r3 = Ui.Row(2); r3.style.justifyContent = Justify.Center;
                        r3.With(Cell(FacingDirection.Down, "abajo", "Debajo"));
                        compass.With(r1, r2, r3);
                        compass.style.alignSelf = Align.FlexStart;
                        var where = Ui.Row(14);
                        where.style.alignItems = Align.Center;
                        where.With(compass, Pick("en un hueco libre", side == null, () => { side = null; Fill(); }, "Lejos de los demás; se puede arrastrar luego en la ventana Mundo"));
                        body.Add(Field("Junto a", where, $"El nuevo tramo se pega a «{anchorMap.Name}» por el lado elegido."));
                    }
                    else body.Add(Field("En el mundo", Ui.Hint("En un hueco libre; luego lo puedes arrastrar en la ventana Mundo.")));
                }

                var tsRow = Chips();
                if (tilesets.Count == 0) tsRow.Add(Ui.Hint("No hay tilesets cortados: el mapa se crea sin tileset. Corta uno en Recursos."));
                foreach (var t in tilesets)
                {
                    var id = t;
                    var chip = Pick(s.TilesetFor(t)?.Name ?? t, tileset == t, () => { tileset = id; Fill(); }, s.TilesetFor(t)?.Name ?? t);
                    chip.style.maxWidth = 220;
                    tsRow.Add(chip);
                }
                body.Add(Field("Tileset", tsRow));

                var parentRow = Chips();
                parentRow.Add(Pick("En la raíz", parent.Length == 0, () => { parent = ""; Fill(); }));
                if (!string.IsNullOrEmpty(parentId) && s.Tree.Find(parentId) is MapEntry p)
                    parentRow.Add(Pick("Dentro de «" + p.Name + "»", parent == parentId, () => { parent = parentId; Fill(); }));
                body.Add(Field("En el árbol", parentRow));

                // Summary card: the tileset and what will be created.
                var card = Ui.Card();
                var ts = s.TilesetFor(tileset);
                var thumbBox = new VisualElement().Bg("fondo").Round(4);
                thumbBox.style.height = 150;
                thumbBox.style.alignItems = Align.Center;
                thumbBox.style.justifyContent = Justify.Center;
                var tex = ts != null ? Textures.Thumbnail(System.IO.Path.Combine(shell.ProjectRoot, ts.ImagePath), 300) : null;
                if (tex != null)
                {
                    var img = new Image { image = tex, scaleMode = ScaleMode.ScaleToFit };
                    img.style.width = 200; img.style.height = 144;
                    thumbBox.Add(img);
                }
                else thumbBox.Add(Ui.Hint("Sin tileset"));
                string where2 = kind == MapKind.Interior ? "interior" : side.HasValue && s.Find(anchor) is MapDefinition am && am.InWorld
                    ? $"{SideName(side.Value)} de «{am.Name}»" : "en un hueco libre del mundo";
                card.With(thumbBox,
                    Ui.Text(string.IsNullOrWhiteSpace(name) ? "(sin nombre)" : name, bold: true).Margin(0, 8, 0, 0),
                    Ui.Text($"{WorldPanel.CategoryNames[(int)category]} · {w} × {h} tiles", 0.9f, dim: true),
                    Ui.Text(where2, 0.9f, dim: true),
                    Ui.Text(ts?.Name ?? "", 0.85f, dim: true));
                side_.Add(card);
            }
            Fill();
            var scroll = Ui.Scroll();
            scroll.style.maxHeight = 600;
            scroll.Add(columns);
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

    /// <summary>
    /// Ventana CAPAS: las capas del mapa (arriba la de encima). Cada fila: asa para arrastrarla a otra posición, ver,
    /// bloquear, nombre y opacidad (deslizador con %). Arriba: añadir, subir, bajar y quitar.
    /// </summary>
    public sealed class LayersPanel : VisualElement
    {
        private readonly AppShell _shell;
        private MapEditorSession S => _shell.Maps;
        private readonly ScrollView _list;
        private readonly VisualElement _bar;
        private readonly List<(VisualElement row, int index)> _rows = new List<(VisualElement, int)>();
        private bool _sliding;

        // Reordering by dragging the grip.
        private int _dragIndex = -1, _dropIndex = -1;

        public LayersPanel(AppShell shell)
        {
            _shell = shell;
            style.flexGrow = 1;
            _bar = Ui.Row(0).Pad(6, 4).Wrap();
            _bar.style.flexShrink = 0;
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
                S.LayerViewChanged += OnLayerView;
                Refresh();
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (S == null) return;
                S.MapOpened -= Refresh;
                S.StructureChanged -= Refresh;
                S.SelectionChanged -= Refresh;
                S.LayerViewChanged -= OnLayerView;
            });
        }

        // While the opacity slider is being dragged the rows stay as they are (rebuilding them would drop the drag).
        private void OnLayerView(int index)
        {
            if (!_sliding) Refresh();
        }

        private void Refresh()
        {
            _bar.Clear();
            _list.Clear();
            _rows.Clear();
            var m = S?.Map;
            if (m == null) { _list.Add(Ui.Hint("Abre un mapa para ver sus capas.").Margin(10, 8, 10, 8)); return; }
            int active = Mathf.Clamp(S.ActiveLayer, 0, m.Layers.Count - 1);
            _bar.With(Ui.IconButton("mas", () => S.AddLayer(), "Añadir una capa encima de la elegida"),
                Ui.IconButton("arriba", () => S.MoveLayer(active, +1), "Subir la capa (se dibuja más encima)"),
                Ui.IconButton("abajo", () => S.MoveLayer(active, -1), "Bajar la capa"),
                Ui.IconButton("papelera", () => _shell.Confirm("Quitar capa", $"¿Quitar la capa «{m.Layers[active].Name}»? Se puede deshacer con Ctrl+Z.",
                    "Quitar", () => S.RemoveLayer(active), danger: true), "Quitar la capa elegida"),
                Ui.Spacer(),
                Ui.Text($"{m.Layers.Count} capas", 0.85f, dim: true).NoShrink().Margin(0, 0, 4, 0));

            // With automatic layers each tile goes to the layer of its kind: say so, or choosing a row looks broken.
            if (S.AutoLayers)
            {
                var note = Ui.Row(6).Pad(8, 5).Bg("panel_alt");
                note.With(Ui.Hint("Capas automáticas: cada tile va solo a su capa (suelo, detalles, encima). Elige una capa para pintar solo en ella.").Grow(),
                    Ui.Button("Manual", () => S.SetAutoLayers(false), Ui.ButtonKind.Flat, "Pintar en la capa elegida (Ctrl+L cambia)"));
                _list.Add(note);
            }
            string[] roles = { "", "suelo", "detalles", "encima" };
            for (int i = m.Layers.Count - 1; i >= 0; i--)
            {
                int index = i;
                var l = m.Layers[i];
                var row = Ui.Row(2).Pad(4, 2);
                if (i == active && !S.AutoLayers) row.style.backgroundColor = Ui.C("seleccion");
                row.style.borderTopWidth = 2; row.style.borderBottomWidth = 2;
                row.style.borderTopColor = new Color(0, 0, 0, 0); row.style.borderBottomColor = new Color(0, 0, 0, 0);

                var grip = Icons.Element("asa", Ui.IconSize, Ui.C("texto_suave"));
                grip.pickingMode = PickingMode.Position;
                grip.tooltip = "Arrastra para cambiar el orden";
                grip.style.marginLeft = 2; grip.style.marginRight = 2;
                RegisterDrag(grip, index);

                var visible = Ui.IconButton(l.Visible ? "ver" : "oculto", () => S.SetLayerView(index, visible: !l.Visible),
                    l.Visible ? "Visible (clic: ocultar; solo en el editor)" : "Oculta (clic: mostrar)");
                if (!l.Visible) visible.style.opacity = 0.55f;
                var locked = Ui.IconButton(l.Locked ? "bloqueado" : "desbloqueado", () => S.SetLayerView(index, locked: !l.Locked),
                    l.Locked ? "Bloqueada: las herramientas no la cambian (clic: desbloquear)" : "Desbloqueada (clic: bloquear)");
                if (!l.Locked) locked.style.opacity = 0.45f;

                var names = Ui.Column(0).Grow();
                names.pickingMode = PickingMode.Ignore;
                var name = Ui.Text(l.Name, bold: i == active);
                name.pickingMode = PickingMode.Ignore;
                names.Add(name);
                if (l.Role != LayerRole.Custom)
                {
                    var role = Ui.Text(roles[(int)l.Role], 0.8f, dim: true);
                    role.pickingMode = PickingMode.Ignore;
                    names.Add(role);
                }
                names.style.marginLeft = 4;

                var opacity = Ui.Range(l.Opacity, 0f, 1f, v => S.SetLayerView(index, opacity: v), "Opacidad en el editor",
                    width: 64, dragging: d => { _sliding = d; if (!d) Refresh(); });
                row.With(grip, visible, locked, names, opacity);

                row.RegisterCallback<PointerUpEvent>(e =>
                {
                    if (e.button == 0 && _dragIndex < 0 && (e.target == row || e.target == names)) Choose(index);
                });
                row.RegisterCallback<ClickEvent>(e =>
                {
                    if (e.clickCount == 2 && (e.target == row || e.target == names))
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
                _rows.Add((row, index));
            }
            _list.Add(Ui.Hint("Arriba, la que se dibuja encima. Arrastra el asa para reordenar. Doble clic: cambiar el nombre. Clic derecho: para qué es (suelo, detalles, encima) — con capas automáticas cada tile va solo a la suya.")
                .Margin(10, 8, 10, 8));
        }

        /// <summary>Choosing a layer means «paint here»: automatic layers are switched off (and it says so).</summary>
        private void Choose(int index)
        {
            if (S.AutoLayers)
            {
                S.SetAutoLayers(false);
                _shell.Info($"Ahora pintas en la capa «{S.Map.Layers[index].Name}». Capas automáticas desactivadas (Ctrl+L para volver).");
            }
            S.SetActiveLayer(index);
        }

        private void RegisterDrag(VisualElement grip, int index)
        {
            grip.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                grip.CapturePointer(e.pointerId);
                _dragIndex = index;
                _dropIndex = index;
                e.StopPropagation();
            });
            grip.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!grip.HasPointerCapture(e.pointerId)) return;
                // The row under the pointer is where the layer goes; a line marks it (above or below that row).
                _dropIndex = _dragIndex;
                foreach (var (row, i) in _rows)
                {
                    var r = row.worldBound;
                    bool over = e.position.y >= r.yMin && e.position.y < r.yMax;
                    if (over) _dropIndex = i;
                    var line = Ui.C("acento");
                    var none = new Color(0, 0, 0, 0);
                    // Higher index = higher in the list: moving up shows the line on top, moving down at the bottom.
                    row.style.borderTopColor = over && i > _dragIndex ? line : none;
                    row.style.borderBottomColor = over && i < _dragIndex ? line : none;
                }
            });
            grip.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!grip.HasPointerCapture(e.pointerId)) return;
                grip.ReleasePointer(e.pointerId);
                int from = _dragIndex, to = _dropIndex;
                _dragIndex = -1;
                _dropIndex = -1;
                e.StopPropagation();
                if (to >= 0 && to != from) S.MoveLayer(from, to - from);
                else Refresh();
            });
        }
    }
}
