using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using CTEditor.Art.Domain;
using CTEditor.Editing;
using CTEditor.Project;
using CTEditor.World.Domain;

namespace CTEditor.App
{
    /// <summary>
    /// Panel MUNDO: todos los tramos exteriores (pueblos, rutas...) colocados en un lienzo, dibujados como en el juego.
    /// Cada tramo sigue siendo su propio mapa:
    ///   - arrastrarlo lo mueve (casilla a casilla; avisa si se pisa con otro);
    ///   - doble clic lo abre en el panel Mapa;
    ///   - en la lista: Ver / Bloq. / Solo, como las capas, para centrarse en un tramo;
    ///   - «Nuevo tramo al lado» lo crea pegado al elegido; «Mapa de la región» genera la imagen para el juego.
    /// </summary>
    public sealed class WorldPanel : VisualElement
    {
        private static readonly float[] ZoomLevels = { 0.05f, 0.1f, 0.15f, 0.25f, 0.35f, 0.5f, 0.75f, 1f, 1.5f, 2f };
        public static readonly string[] CategoryNames = { "Pueblo", "Ciudad", "Ruta", "Bosque", "Cueva", "Agua", "Montaña", "Edificio", "Especial" };

        private readonly AppShell _shell;
        private MapEditorSession S => _shell.Maps;
        private MapRenderer _renderer;
        private AtlasCache _atlases;
        private readonly VisualElement _toolbar, _viewport, _overlay, _side;
        private readonly Image _image;
        private int _zoomIndex = 3;
        private bool _needsFit = true;
        private string _selected;

        // Pointer.
        private bool _panning, _dragging;
        private Vector2 _panStart, _panCenter;
        private string _dragId;
        private Vector2 _dragStartCell;
        private (int x, int y) _dragOrigin, _dragNow;

        public WorldPanel(AppShell shell)
        {
            _shell = shell;
            style.flexGrow = 1;
            _toolbar = Ui.Row(4).Pad(6, 4);
            _toolbar.style.flexShrink = 0;
            _toolbar.style.flexWrap = Wrap.Wrap;
            Add(_toolbar);
            Add(Ui.Separator());

            var main = Ui.Row(0).Grow();
            main.style.alignItems = Align.Stretch;
            _viewport = new VisualElement().Grow();
            _viewport.style.overflow = Overflow.Hidden;
            _viewport.style.backgroundColor = new Color(0.12f, 0.22f, 0.35f);
            _image = new Image { scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore }.Fill();
            _overlay = new VisualElement { pickingMode = PickingMode.Ignore }.Fill();
            _viewport.Add(_image);
            _viewport.Add(_overlay);
            main.Add(_viewport);
            var sideScroll = Ui.Scroll();
            sideScroll.style.width = 280;
            sideScroll.style.flexShrink = 0;
            sideScroll.Bg("panel");
            sideScroll.style.borderLeftWidth = 1;
            sideScroll.style.borderLeftColor = Ui.C("borde");
            _side = Ui.Column(4).Pad(8);
            sideScroll.Add(_side);
            main.Add(sideScroll);
            Add(main);

            _viewport.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                Resize();
                if (_needsFit) Fit();
                RedrawOverlay();
            });
            _viewport.RegisterCallback<PointerDownEvent>(OnDown);
            _viewport.RegisterCallback<PointerMoveEvent>(OnMove);
            _viewport.RegisterCallback<PointerUpEvent>(OnUp);
            _viewport.RegisterCallback<ClickEvent>(OnClick);
            _viewport.RegisterCallback<WheelEvent>(OnWheel);

            RegisterCallback<AttachToPanelEvent>(_ => Attach());
            RegisterCallback<DetachFromPanelEvent>(_ => Detach());
        }

        private void Attach()
        {
            if (S == null) return;
            S.WorldChanged += Rebuild;
            S.TreeChanged += Rebuild;
            S.StructureChanged += Rebuild;
            S.TilesetChanged += OnTileset;
            S.TilesChanged += OnTiles;
            S.MapOpened += Rebuild;
            _shell.PlayingChanged += OnPlaying;
            _atlases = new AtlasCache(_shell.ProjectRoot, S.TilesetFor);
            _renderer = new MapRenderer("Mundo", MapRenderer.WorldLayer, new Color(0.12f, 0.22f, 0.35f)) { TileSize = _shell.Project.TileSize };
            _selected = S.Map?.Id;
            Rebuild();
        }

        private void Detach()
        {
            if (S != null)
            {
                S.WorldChanged -= Rebuild;
                S.TreeChanged -= Rebuild;
                S.StructureChanged -= Rebuild;
                S.TilesetChanged -= OnTileset;
                S.TilesChanged -= OnTiles;
                S.MapOpened -= Rebuild;
            }
            _shell.PlayingChanged -= OnPlaying;
            _renderer?.Dispose();
            _renderer = null;
            _atlases?.Dispose();
            _atlases = null;
        }

        private void OnPlaying(bool playing) { if (_renderer != null) _renderer.Camera.enabled = !playing; }
        private void OnTileset(string _) { _atlases?.Clear(); Rebuild(); }
        private void OnTiles(int x, int y, int w, int h) { if (S.Map != null) _renderer?.RefreshTiles(S.Map.Id, x, y, w, h); }

        private IEnumerable<MapDefinition> Visible() =>
            S.World().Sections.Select(s => s.Map).Where(m => S.Tree.Find(m.Id)?.HiddenInWorld != true);

        private void Rebuild()
        {
            if (_renderer == null || S == null) return;
            var views = Visible().Select(m => new SectionView
            {
                Map = m, Atlases = _atlases.For(m), Sets = S.TilesetsOf(m),
                Offset = new Vector2Int(m.WorldX, m.WorldY),
                Alpha = S.Tree.Find(m.Id)?.LockedInWorld == true ? 0.7f : 1f,
            });
            _renderer.SetSections(views);
            BuildToolbar();
            BuildSide();
            RedrawOverlay();
        }

        // ── Toolbar and list ─────────────────────────────────────────────────────────────────────

        private void BuildToolbar()
        {
            _toolbar.Clear();
            var sel = S?.Find(_selected);
            _toolbar.With(
                Ui.Button("Nuevo tramo…", () => MapTreePanel.NewMapDialog(_shell, "", sel?.InWorld == true ? sel.Id : null), Ui.ButtonKind.Primary,
                    "Crea un pueblo, ruta, cueva... pegado al tramo elegido"),
                Ui.Button("Mapa de la región…", () => RegionMapDialog.Show(_shell), Ui.ButtonKind.Normal, "Genera la imagen del mapa de la región"),
                Ui.Separator(vertical: true).Margin(4, 2, 4, 2),
                Ui.IconButton("menos", () => SetZoom(_zoomIndex - 1), "Alejar (rueda del ratón)"),
                Ui.Text(Mathf.RoundToInt(ZoomLevels[_zoomIndex] * 100) + " %"),
                Ui.IconButton("mas", () => SetZoom(_zoomIndex + 1), "Acercar (rueda del ratón)"),
                Ui.Button("Ver todo", Fit, Ui.ButtonKind.Flat),
                Ui.Button("Mostrar todos", () => { foreach (var e in S.Tree.Entries) S.SetSectionView(e.Id, hidden: false); }, Ui.ButtonKind.Flat),
                Ui.Spacer(),
                Ui.Hint("Arrastra un tramo para moverlo · doble clic: editarlo"));
        }

        private void BuildSide()
        {
            _side.Clear();
            if (S == null) return;
            var overlaps = S.World().Overlaps().ToList();
            if (overlaps.Count > 0)
            {
                var warn = Ui.Column(4).Bg("panel_alt").Border(1, "error", 6).Pad(8);
                warn.Add(Ui.Text("Tramos que se pisan", bold: true).Colored("error"));
                foreach (var (a, b) in overlaps) warn.Add(Ui.Text($"{a.Name} y {b.Name}", 0.9f, wrap: true));
                warn.Add(Ui.Hint("Sepáralos para que se pueda pasar de uno a otro."));
                _side.Add(warn);
            }
            _side.Add(Ui.Text("Exteriores", bold: true));
            foreach (var (e, _) in S.Tree.Walk())
            {
                var m = S.Find(e.Id);
                if (m == null || m.Kind != MapKind.Exterior) continue;
                _side.Add(Row(e, m));
            }
            var interiors = S.Tree.Walk().Select(w => S.Find(w.entry.Id)).Where(m => m != null && m.Kind == MapKind.Interior).ToList();
            if (interiors.Count > 0)
            {
                _side.Add(Ui.Text("Interiores (se entra por puertas)", bold: true).Margin(0, 8, 0, 0));
                foreach (var m in interiors)
                {
                    var row = Ui.Row(6).Pad(4, 2);
                    var id = m.Id;
                    row.With(Ui.Text(m.Name).Grow(), Ui.Button("Abrir", () => OpenInMap(id), Ui.ButtonKind.Flat));
                    _side.Add(row);
                }
            }
        }

        private VisualElement Row(MapEntry e, MapDefinition m)
        {
            var id = e.Id;
            var box = Ui.Column(2).Pad(6, 4).Round(4);
            if (id == _selected) box.style.backgroundColor = Ui.C("seleccion");
            var top = Ui.Row(4);
            var name = Ui.Text(m.Name, bold: id == _selected).Grow();
            name.pickingMode = PickingMode.Ignore;
            top.Add(Swatch(m.Category));
            top.Add(name);
            box.Add(top);
            var actions = Ui.Row(4);
            if (m.InWorld)
            {
                actions.With(
                    Ui.Chip("Ver", !e.HiddenInWorld, () => S.SetSectionView(id, hidden: !e.HiddenInWorld), "Mostrar u ocultar en el mundo"),
                    Ui.Chip("Bloq.", e.LockedInWorld, () => S.SetSectionView(id, locked: !e.LockedInWorld), "No se puede mover"),
                    Ui.Chip("Solo", S.Tree.Entries.All(o => o.Id == id ? !o.HiddenInWorld : o.HiddenInWorld), () => S.Solo(id), "Ver solo este tramo (otra vez: todos)"),
                    Ui.Button("Abrir", () => OpenInMap(id), Ui.ButtonKind.Flat));
            }
            else
            {
                actions.With(Ui.Text("sin colocar", 0.85f, dim: true),
                    Ui.Button("Colocar", () =>
                    {
                        var anchor = S.Find(_selected);
                        var w = S.World();
                        var (x, y) = anchor != null && anchor.InWorld && anchor.Id != id ? w.PlaceNextTo(anchor, FacingDirection.Right, m.Width, m.Height) : w.FreeSpot(m.Width, m.Height);
                        S.MoveSection(id, x, y);
                        _selected = id;
                        CenterOn(m);
                    }, Ui.ButtonKind.Primary, "Ponerlo en el mundo (junto al tramo elegido)"));
            }
            box.Add(actions);
            box.RegisterCallback<PointerUpEvent>(ev =>
            {
                if (ev.target != box && ev.target != top && ev.target != name) return;
                _selected = id;
                if (m.InWorld) CenterOn(m);
                BuildSide();
                BuildToolbar();
                RedrawOverlay();
            });
            return box;
        }

        private static VisualElement Swatch(SectionCategory c)
        {
            var color = RegionMapBuilder.CategoryColors[c];
            var sw = Ui.Swatch(new Color32(color.R, color.G, color.B, 255), 12);
            sw.tooltip = CategoryNames[(int)c];
            return sw;
        }

        private void OpenInMap(string id)
        {
            S.OpenMap(id);
            _shell.Workspace.Layout.Open(Workspace.PanelCatalog.Map);
            _shell.SetLayout(_shell.Workspace.Layout);
        }

        // ── View ─────────────────────────────────────────────────────────────────────────────────

        private void Resize()
        {
            if (_renderer == null) return;
            float ppp = _shell.PixelsPerPoint;
            _renderer.Resize(Mathf.RoundToInt(_viewport.layout.width * ppp), Mathf.RoundToInt(_viewport.layout.height * ppp));
            _image.image = _renderer.Target;
            _renderer.Zoom = ZoomLevels[_zoomIndex] * ppp;
            _renderer.UpdateCamera();
        }

        private void SetZoom(int i)
        {
            _zoomIndex = Mathf.Clamp(i, 0, ZoomLevels.Length - 1);
            if (_renderer == null) return;
            _renderer.Zoom = ZoomLevels[_zoomIndex] * _shell.PixelsPerPoint;
            _renderer.UpdateCamera();
            BuildToolbar();
            RedrawOverlay();
        }

        private void Fit()
        {
            if (_renderer == null || S == null || _viewport.layout.width <= 0 || float.IsNaN(_viewport.layout.width)) return;
            _needsFit = false;
            var (bx, by, bw, bh) = S.World().Bounds;
            if (bw == 0) { _renderer.Center = Vector2.zero; _renderer.UpdateCamera(); return; }
            float tile = _shell.Project.TileSize;
            float fit = Mathf.Min(_viewport.layout.width / ((bw + 4) * tile), _viewport.layout.height / ((bh + 4) * tile));
            int best = 0;
            for (int i = 0; i < ZoomLevels.Length; i++) if (ZoomLevels[i] <= fit) best = i;
            _zoomIndex = best;
            _renderer.Zoom = ZoomLevels[best] * _shell.PixelsPerPoint;
            _renderer.Center = MapRenderer.CenterOf(bx, by, bw, bh);
            _renderer.UpdateCamera();
            BuildToolbar();
            RedrawOverlay();
        }

        private void CenterOn(MapDefinition m)
        {
            if (_renderer == null) return;
            _renderer.Center = MapRenderer.CenterOf(m.WorldX, m.WorldY, m.Width, m.Height);
            _renderer.UpdateCamera();
        }

        private Vector2 CellAt(Vector2 local) => _renderer.PixelToCell(local * _shell.PixelsPerPoint);
        private Vector2 P(float x, float y) => _renderer.CellToPixel(x, y) / _shell.PixelsPerPoint;

        private MapDefinition SectionAt(Vector2 cell) =>
            Visible().FirstOrDefault(m => new WorldSection(m).Contains(Mathf.FloorToInt(cell.x), Mathf.FloorToInt(cell.y)));

        // ── Pointer ──────────────────────────────────────────────────────────────────────────────

        private void OnDown(PointerDownEvent e)
        {
            if (_renderer?.Target == null) return;
            _viewport.CapturePointer(e.pointerId);
            if (e.button == 2 || (e.button == 0 && e.altKey))
            {
                _panning = true;
                _panStart = e.localPosition;
                _panCenter = _renderer.Center;
                return;
            }
            if (e.button != 0) return;
            var cell = CellAt(e.localPosition);
            var m = SectionAt(cell);
            _selected = m?.Id;
            BuildSide();
            BuildToolbar();
            if (m != null && S.Tree.Find(m.Id)?.LockedInWorld != true)
            {
                _dragging = true;
                _dragId = m.Id;
                _dragStartCell = cell;
                _dragOrigin = _dragNow = (m.WorldX, m.WorldY);
            }
            RedrawOverlay();
        }

        private void OnMove(PointerMoveEvent e)
        {
            if (_renderer?.Target == null) return;
            if (_panning)
            {
                var d = (Vector2)e.localPosition - _panStart;
                float ppu = _renderer.PixelsPerUnit / _shell.PixelsPerPoint;
                _renderer.Center = _panCenter + new Vector2(-d.x / ppu, d.y / ppu);
                _renderer.UpdateCamera();
                RedrawOverlay();
                return;
            }
            if (!_dragging) return;
            var c = CellAt(e.localPosition);
            var now = (_dragOrigin.x + Mathf.RoundToInt(c.x - _dragStartCell.x), _dragOrigin.y + Mathf.RoundToInt(c.y - _dragStartCell.y));
            if (now == _dragNow) return;
            _dragNow = now;
            RedrawOverlay();
        }

        private void OnUp(PointerUpEvent e)
        {
            if (_viewport.HasPointerCapture(e.pointerId)) _viewport.ReleasePointer(e.pointerId);
            if (_panning) { _panning = false; return; }
            if (!_dragging) return;
            _dragging = false;
            if (_dragNow != _dragOrigin) S.MoveSection(_dragId, _dragNow.x, _dragNow.y);
            _dragId = null;
            RedrawOverlay();
        }

        private void OnClick(ClickEvent e)
        {
            if (e.clickCount != 2 || _renderer?.Target == null) return;
            var m = SectionAt(CellAt(e.localPosition));
            if (m != null) OpenInMap(m.Id);
        }

        private void OnWheel(WheelEvent e)
        {
            if (e.ctrlKey) return;
            SetZoom(_zoomIndex + (e.delta.y < 0 ? 1 : -1));
            e.StopPropagation();
        }

        // ── Overlay ──────────────────────────────────────────────────────────────────────────────

        private void RedrawOverlay()
        {
            _overlay.Clear();
            if (_renderer?.Target == null || S == null) return;
            var overlapping = new HashSet<string>(S.World().Overlaps().SelectMany(p => new[] { p.a.Id, p.b.Id }));
            foreach (var m in Visible())
            {
                bool sel = m.Id == _selected;
                bool locked = S.Tree.Find(m.Id)?.LockedInWorld == true;
                var color = overlapping.Contains(m.Id) ? Ui.C("error") : sel ? Ui.C("acento") : Ui.WithAlpha(Color.white, 0.6f);
                var a = P(m.WorldX, m.WorldY);
                var b = P(m.WorldX + m.Width, m.WorldY + m.Height);
                Box(a, b, color, sel ? 3 : 1, sel ? 0.1f : 0f);
                var tag = Ui.Text(m.Name + (locked ? " (bloqueado)" : "") + (m.Id == S.Map?.Id ? " · abierto" : ""), 0.85f, bold: true);
                tag.style.color = Color.white;
                tag.style.backgroundColor = new Color(0, 0, 0, 0.6f);
                tag.Pad(4, 1).Round(3);
                tag.style.position = Position.Absolute;
                tag.style.left = a.x + 3;
                tag.style.top = a.y + 3;
                tag.pickingMode = PickingMode.Ignore;
                _overlay.Add(tag);
            }
            if (_dragging && _dragId != null)
            {
                var m = S.Find(_dragId);
                var free = S.World().IsFree(_dragNow.x, _dragNow.y, m.Width, m.Height, m);
                Box(P(_dragNow.x, _dragNow.y), P(_dragNow.x + m.Width, _dragNow.y + m.Height), Ui.C(free ? "exito" : "error"), 3, 0.2f);
            }
        }

        private void Box(Vector2 a, Vector2 b, Color c, float width, float fill)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.Absolute(a.x, a.y, Mathf.Max(1, b.x - a.x), Mathf.Max(1, b.y - a.y));
            e.style.borderLeftWidth = width; e.style.borderRightWidth = width; e.style.borderTopWidth = width; e.style.borderBottomWidth = width;
            e.style.borderLeftColor = c; e.style.borderRightColor = c; e.style.borderTopColor = c; e.style.borderBottomColor = c;
            if (fill > 0) e.style.backgroundColor = Ui.WithAlpha(c, fill);
            _overlay.Add(e);
        }
    }

    /// <summary>
    /// Genera el MAPA DE LA REGIÓN a partir del mundo: esquemático (colores por tipo de tramo) o reducido (colores reales),
    /// con la escala elegida. Se guarda en graficos/interfaz/mapa_region.png (retocable) y datos/mapa_region.json.
    /// </summary>
    public static class RegionMapDialog
    {
        public static void Show(AppShell shell)
        {
            var s = shell.Maps;
            if (s == null) return;
            if (s.World().Sections.Count == 0) { shell.Warn("Aún no hay tramos colocados en el mundo."); return; }
            var d = shell.ShowDialog("Mapa de la región", 70, 80);
            var style = RegionMapStyle.Schematic;
            int scale = 4;
            Texture2D tex = null;
            var options = Ui.Column(8);
            options.style.flexShrink = 0;
            var preview = Ui.Scroll(ScrollViewMode.VerticalAndHorizontal).Grow();
            preview.Bg("fondo");
            var info = Ui.Hint("");
            (PixelImage image, List<RegionMapArea> areas) result = default;

            void Build()
            {
                options.Clear();
                var st = Ui.Row(6);
                st.With(Ui.Text("Estilo", bold: true),
                    Ui.Chip("Esquemático (colores por tipo)", style == RegionMapStyle.Schematic, () => { style = RegionMapStyle.Schematic; Build(); }),
                    Ui.Chip("Reducido (colores reales)", style == RegionMapStyle.Reduced, () => { style = RegionMapStyle.Reduced; Build(); }));
                var sc = Ui.Row(6);
                sc.Add(Ui.Text("Escala", bold: true));
                foreach (var v in new[] { 1, 2, 4, 8, 16 })
                {
                    int value = v;
                    sc.Add(Ui.Chip(v == 1 ? "1 tile = 1 píxel" : $"{v} tiles = 1 píxel", scale == v, () => { scale = value; Build(); }));
                }
                options.With(st, sc);
                result = s.BuildRegionMap(style, scale);
                if (tex != null) UnityEngine.Object.Destroy(tex);
                tex = Textures.FromImage(result.image);
                preview.Clear();
                int zoom = Mathf.Max(1, Mathf.FloorToInt(600f / Mathf.Max(result.image.Width, result.image.Height)));
                var img = new Image { image = tex, scaleMode = ScaleMode.StretchToFill };
                img.style.width = result.image.Width * zoom;
                img.style.height = result.image.Height * zoom;
                img.style.flexShrink = 0;
                preview.Add(img);
                info.text = $"{result.image.Width} × {result.image.Height} px · {result.areas.Count} tramos. Los que tengan «Sale en el mapa de la región» desactivado no aparecen.";
            }

            Build();
            d.Body.With(options, preview, info);
            d.OnClose = () => { if (tex != null) UnityEngine.Object.Destroy(tex); };
            d.Buttons.With(Ui.Button("Cancelar", () => shell.CloseDialog(d)), Ui.Button("Guardar", () =>
            {
                try
                {
                    RegionMapFiles.Save(shell.ProjectRoot, result.image, result.areas);
                    shell.NotifyAssetsChanged();
                    shell.Success("Mapa de la región guardado en " + RegionMapFiles.ImagePath + ". Puedes retocarlo en Retoque.");
                    shell.CloseDialog(d);
                }
                catch (Exception e) { shell.Error("No se pudo guardar: " + e.Message); }
            }, Ui.ButtonKind.Primary));
        }
    }
}
