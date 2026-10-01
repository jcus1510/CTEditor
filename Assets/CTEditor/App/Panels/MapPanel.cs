using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using CTEditor.Workspace;
using CTEditor.Editing;
using CTEditor.World.Domain;

namespace CTEditor.App
{
    /// <summary>
    /// Panel MAPA: el tramo abierto dibujado con el mismo renderizador que el juego (y sus tramos vecinos del mundo,
    /// atenuados, para ver cómo encaja), con las herramientas del editor.
    ///   - Clic izquierdo: la herramienta (lápiz, rectángulo, relleno, goma, cuentagotas, selección, pegar, zona de
    ///     encuentros, inicio del jugador). Con capas automáticas cada tile va solo a su capa.
    ///   - Clic derecho: coger tiles del mapa como sello (en la herramienta Zona, quitar casillas de la zona).
    ///   - Botón central o Alt + arrastrar: mover la vista. Rueda: zoom hacia el ratón. Ctrl + clic: retocar ese tile.
    ///   - Doble clic en un tramo vecino: abrirlo.
    /// Todo lo que cambia pasa por MapEditorSession (deshacer, autoguardado); este panel solo dibuja y reenvía el ratón.
    /// </summary>
    public sealed class MapPanel : VisualElement
    {
        private static readonly float[] ZoomLevels = { 0.25f, 0.5f, 1f, 1.5f, 2f, 3f, 4f, 6f, 8f };
        private static readonly (MapTool tool, string label, string action, string help)[] Tools =
        {
            (MapTool.Pencil, "Lápiz", "lapiz", "Pinta el sello elegido en Tiles"),
            (MapTool.Rectangle, "Rectángulo", "rectangulo", "Rellena un rectángulo con el sello"),
            (MapTool.Fill, "Relleno", "relleno", "Rellena la zona del mismo tile"),
            (MapTool.Eraser, "Goma", "goma", "Borra (con capas automáticas, lo de más arriba)"),
            (MapTool.Picker, "Cuentagotas", "cuentagotas", "Coge tiles del mapa como sello (también con clic derecho)"),
            (MapTool.Select, "Selección", "seleccion", "Selecciona una zona: Ctrl+C copiar, Ctrl+X cortar, Supr borrar"),
            (MapTool.Paste, "Pegar", "pegar", "Haz clic donde quieras pegar lo copiado"),
            (MapTool.EncounterPaint, "Zona", "zona", "Pinta la zona de encuentros activa (clic derecho quita)"),
            (MapTool.PlayerStart, "Inicio", "inicio", "Coloca el inicio del jugador"),
        };

        private readonly AppShell _shell;
        private readonly VisualElement _main;
        private readonly GridOverlay _grid;
        private const string ToolbarPref = "barra_mapa";
        /// <summary>Where the toolbar goes: «arriba», «izquierda», «derecha» or «abajo».</summary>
        private string ToolbarSide => _shell.Workspace.Pref(ToolbarPref, "izquierda");
        private bool VerticalToolbar => ToolbarSide == "izquierda" || ToolbarSide == "derecha";
        /// <summary>Texture pixels per local point: interface scale × the zoom of this window (Alt + wheel).</summary>
        private float Ppp => _shell.PixelsPerPoint * Ui.ScaleOf(_viewport);
        private MapEditorSession S => _shell.Maps;
        private MapRenderer _renderer;
        private AtlasCache _atlases;
        private readonly VisualElement _toolbar, _viewport, _overlay, _empty;
        private readonly Image _image;
        private readonly Label _status;
        private bool _showGrid = true, _showNeighbors = true;
        private int _zoomIndex = 4;
        private bool _needsFit = true;
        private List<MapDefinition> _neighbors = new List<MapDefinition>();

        // Pointer state.
        private bool _painting, _panning, _picking;
        private MapTool _toolBeforePick;
        private Vector2 _panStart;
        private Vector2 _panCenter;
        private (int x, int y)? _cursor;

        public MapPanel(AppShell shell)
        {
            _shell = shell;
            style.flexGrow = 1;

            _toolbar = Ui.Row(4).Pad(6, 4);
            _toolbar.style.flexShrink = 0;
            _toolbar.style.flexWrap = Wrap.Wrap;
            // Right click on the toolbar: put it on top, left, right or bottom (remembered).
            _toolbar.RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.button != 1) return;
                ToolbarSideMenu(e.position);
                e.StopPropagation();
            });
            _main = new VisualElement().Grow();
            Add(_main);

            _viewport = new VisualElement().Grow();
            _viewport.style.overflow = Overflow.Hidden;
            _viewport.style.backgroundColor = Ui.Mix(Ui.C("fondo"), Color.black, 0.3f);
            _image = new Image { scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore }.Fill();
            _overlay = new VisualElement { pickingMode = PickingMode.Ignore }.Fill();
            _grid = new GridOverlay().Fill();
            _empty = Ui.Column(8).Fill();
            _empty.style.alignItems = Align.Center;
            _empty.style.justifyContent = Justify.Center;
            _viewport.Add(_image);
            _viewport.Add(_grid);
            _viewport.Add(_overlay);
            _viewport.Add(_empty);
            PlaceToolbar();

            _status = Ui.Text("", 0.9f, dim: true);
            _status.Pad(8, 3);
            _status.style.flexShrink = 0;
            Add(_status);

            _viewport.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                ResizeTarget();
                if (_needsFit) FitMap();
                RedrawOverlay();
            });
            _viewport.RegisterCallback<PointerDownEvent>(OnPointerDown);
            _viewport.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            _viewport.RegisterCallback<PointerUpEvent>(OnPointerUp);
            _viewport.RegisterCallback<ClickEvent>(OnClick);
            _viewport.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (_painting || _panning) return;
                _cursor = null;
                if (S != null) S.Cursor = null;
                RedrawOverlay();
                UpdateStatus();
            });
            _viewport.RegisterCallback<WheelEvent>(OnWheel);

            RegisterCallback<AttachToPanelEvent>(_ => Attach());
            RegisterCallback<DetachFromPanelEvent>(_ => Detach());
        }

        // ── Life cycle ───────────────────────────────────────────────────────────────────────────

        private void Attach()
        {
            if (S == null) return;
            S.MapOpened += OnMapOpened;
            S.TilesChanged += OnTilesChanged;
            S.StructureChanged += OnStructureChanged;
            S.LayerViewChanged += OnLayerViewChanged;
            S.SelectionChanged += OnSelectionChanged;
            S.TilesetChanged += OnTilesetChanged;
            S.EncountersChanged += RedrawOverlay;
            S.WorldChanged += OnWorldChanged;
            S.PlayerStartChanged += RedrawOverlay;
            S.DirtyChanged += UpdateStatus;
            _shell.PlayingChanged += OnPlaying;
            _shell.ActionRequested += OnAction;
            _atlases = new AtlasCache(_shell.ProjectRoot, S.TilesetFor);
            _renderer = new MapRenderer("Editor de mapas", MapRenderer.EditorLayer, Ui.Mix(Ui.C("fondo"), Color.black, 0.3f))
                { TileSize = _shell.Project.TileSize };
            OnMapOpened();
        }

        private void Detach()
        {
            if (S != null)
            {
                S.MapOpened -= OnMapOpened;
                S.TilesChanged -= OnTilesChanged;
                S.StructureChanged -= OnStructureChanged;
                S.LayerViewChanged -= OnLayerViewChanged;
                S.SelectionChanged -= OnSelectionChanged;
                S.TilesetChanged -= OnTilesetChanged;
                S.EncountersChanged -= RedrawOverlay;
                S.WorldChanged -= OnWorldChanged;
                S.PlayerStartChanged -= RedrawOverlay;
                S.DirtyChanged -= UpdateStatus;
            }
            _shell.PlayingChanged -= OnPlaying;
            _shell.ActionRequested -= OnAction;
            _renderer?.Dispose();
            _renderer = null;
            _atlases?.Dispose();
            _atlases = null;
        }

        private void OnPlaying(bool playing)
        {
            if (_renderer != null) _renderer.Camera.enabled = !playing;
        }

        private void OnAction(string id)
        {
            if (id == "rejilla") { _showGrid = !_showGrid; BuildToolbar(); RedrawOverlay(); return; }
            if (S?.Map == null || _shell.ActiveEditor != "mapa") return;
            switch (id)
            {
                case "vecinos": _showNeighbors = !_showNeighbors; RebuildSections(); BuildToolbar(); RedrawOverlay(); break;
                case "acercar": SetZoom(_zoomIndex + 1, CursorPixel()); break;
                case "alejar": SetZoom(_zoomIndex - 1, CursorPixel()); break;
                case "encuadrar": FitMap(); break;
            }
        }

        /// <summary>Zoom with the keyboard keeps the cell under the mouse in place (or the centre if the mouse is away).</summary>
        private Vector2? CursorPixel()
        {
            if (!_cursor.HasValue || _renderer?.Target == null) return null;
            return _renderer.CellToPixel(_cursor.Value.x + 0.5f, _cursor.Value.y + 0.5f);
        }

        // ── Session events ───────────────────────────────────────────────────────────────────────

        private void OnMapOpened()
        {
            RebuildSections();
            _needsFit = true;
            FitMap();
            BuildToolbar();
            ShowEmptyState();
            UpdateStatus();
        }

        /// <summary>The open map at (0,0) and, around it, its neighbours in the world (faded).</summary>
        private void RebuildSections()
        {
            if (_renderer == null) return;
            var map = S?.Map;
            if (map == null) { _renderer.SetSections(new SectionView[0]); _neighbors.Clear(); return; }
            var views = new List<SectionView> { View(map, Vector2Int.zero, 1f) };
            _neighbors = _showNeighbors && map.InWorld && map.Kind == MapKind.Exterior
                ? S.World().Neighbors(map).Where(n => S.Tree.Find(n.Id)?.HiddenInWorld != true).ToList()
                : new List<MapDefinition>();
            foreach (var n in _neighbors) views.Add(View(n, new Vector2Int(n.WorldX - map.WorldX, n.WorldY - map.WorldY), 0.45f));
            _renderer.SetSections(views);
            ApplyLayerView();
        }

        private SectionView View(MapDefinition m, Vector2Int offset, float alpha) =>
            new SectionView { Map = m, Atlases = _atlases.For(m), Sets = S.TilesetsOf(m), Offset = offset, Alpha = alpha };

        private void OnTilesChanged(int x, int y, int w, int h)
        {
            _renderer?.RefreshTiles(x, y, w, h);
            UpdateStatus();
        }

        private void OnStructureChanged()
        {
            RebuildSections();
            BuildToolbar();
            ShowEmptyState();
            RedrawOverlay();
            UpdateStatus();
        }

        private void OnLayerViewChanged(int index)
        {
            _renderer?.RefreshLayerView(index);
            if (!S.AutoLayers) BuildToolbar(); // the «(bloqueada)» note of the active layer
        }

        private void OnWorldChanged()
        {
            RebuildSections();
            RedrawOverlay();
        }

        private void OnTilesetChanged(string id)
        {
            _atlases?.Clear();
            RebuildSections();
            ShowEmptyState();
        }

        private void OnSelectionChanged()
        {
            ApplyLayerView();
            BuildToolbar();
            RedrawOverlay();
        }

        private void ApplyLayerView()
        {
            if (_renderer == null || S == null) return;
            _renderer.ActiveLayer = S.AutoLayers ? -1 : S.ActiveLayer;
            _renderer.DimOthers = S.DimOtherLayers && !S.AutoLayers;
            _renderer.RefreshAllLayerViews();
        }

        private void ShowEmptyState()
        {
            _empty.Clear();
            bool noMap = S?.Map == null;
            bool noTiles = !noMap && S.Tilesets.IsEmpty;
            _empty.Show(noMap || noTiles);
            _image.Show(!noMap);
            if (noMap)
            {
                _empty.pickingMode = PickingMode.Position;
                _empty.With(Ui.Title("Sin mapa abierto").Colored("texto_suave"),
                    Ui.Hint("Crea un tramo (pueblo, ruta, cueva...) o ábrelo desde Mapas o desde Mundo."),
                    Ui.Button("Nuevo mapa…", () => MapTreePanel.NewMapDialog(_shell, ""), Ui.ButtonKind.Primary));
            }
            else if (noTiles)
            {
                _empty.pickingMode = PickingMode.Ignore;
                var box = Ui.Column(6).Bg("panel").Border(1, "aviso", 6).Pad(12);
                box.pickingMode = PickingMode.Position;
                box.With(Ui.Heading("Este mapa no tiene un tileset cortado"),
                    Ui.Hint("Importa y corta una imagen en Recursos (graficos/tilesets) y elígela en Tiles o en Propiedades."));
                _empty.Add(box);
            }
        }

        // ── Toolbar and status ───────────────────────────────────────────────────────────────────

        private void BuildToolbar()
        {
            _toolbar.Clear();
            if (S?.Map == null) { _toolbar.Add(Ui.Text("Mapa", bold: true)); return; }
            foreach (var (tool, label, action, help) in Tools)
            {
                if (tool == MapTool.Paste && S.Clipboard == null) continue;
                if (tool == MapTool.EncounterPaint && S.ActiveArea == null) continue;
                var t = tool;
                var keys = action == null ? "" : ShortcutMap.Pretty(_shell.Workspace.Shortcuts.KeysFor(action));
                var button = Ui.IconButton(IconOf(t), () => { _shell.ActiveEditor = "mapa"; S.SetTool(t); },
                    label + ": " + help + (keys.Length > 0 ? $" ({keys})" : ""), S.Tool == t, BigIcon);
                _toolbar.Add(button.Margin(0, 2, 2, 2));
            }
            _toolbar.Add(ToolbarGap());

            bool vertical = VerticalToolbar;
            if (vertical)
                _toolbar.Add(Ui.IconToggle("pieza", S.AutoLayers, v => S.SetAutoLayers(v),
                    "Capas automáticas: cada tile va solo a su capa (suelo, detalles, encima). Apagado = se pinta en la capa elegida (Ctrl+L)", BigIcon));
            else
                _toolbar.Add(Ui.Check("Capas automáticas", S.AutoLayers, v => S.SetAutoLayers(v)).Margin(0, 0, 6, 0));
            if (!S.AutoLayers && vertical)
            {
                var layer = S.ActiveLayer < S.Map.Layers.Count ? S.Map.Layers[S.ActiveLayer] : null;
                _toolbar.Add(Ui.IconButton("arriba", () => S.SetActiveLayer(S.ActiveLayer + 1), "Capa de encima", false, BigIcon));
                var ln = Ui.Text((S.ActiveLayer + 1).ToString(), 0.85f, bold: true);
                ln.tooltip = "Pintas en la capa «" + (layer?.Name ?? "-") + "»" + (layer?.Locked == true ? " (bloqueada)" : "");
                ln.style.unityTextAlign = TextAnchor.MiddleCenter;
                _toolbar.Add(ln);
                _toolbar.Add(Ui.IconButton("abajo", () => S.SetActiveLayer(S.ActiveLayer - 1), "Capa de debajo", false, BigIcon));
                _toolbar.Add(Ui.IconToggle("oculto", S.DimOtherLayers, v => S.SetDimOtherLayers(v), "Atenuar las otras capas", BigIcon));
            }
            else if (!S.AutoLayers)
            {
                var layer = S.ActiveLayer < S.Map.Layers.Count ? S.Map.Layers[S.ActiveLayer] : null;
                _toolbar.Add(Ui.IconButton("anterior", () => S.SetActiveLayer(S.ActiveLayer - 1), "Capa anterior", false, BigIcon));
                var ln = Ui.Text("Capa: " + (layer?.Name ?? "-") + (layer?.Locked == true ? " (bloqueada)" : ""), bold: true);
                ln.style.minWidth = 100;
                _toolbar.Add(ln);
                _toolbar.Add(Ui.IconButton("siguiente", () => S.SetActiveLayer(S.ActiveLayer + 1), "Capa siguiente", false, BigIcon));
                _toolbar.Add(Ui.Check("Atenuar las otras", S.DimOtherLayers, v => S.SetDimOtherLayers(v)).Margin(6, 0, 0, 0));
            }
            _toolbar.Add(ToolbarGap());

            // Vertical: + on top, % and − below (bigger up, smaller down).
            var zoom = Ui.Text(Mathf.RoundToInt(ZoomLevels[_zoomIndex] * 100) + " %", vertical ? 0.78f : 1f).NoShrink();
            zoom.style.minWidth = vertical ? 0 : Ui.FontSize * 3.2f;
            zoom.style.unityTextAlign = TextAnchor.MiddleCenter;
            var minus = Ui.IconButton("menos", () => SetZoom(_zoomIndex - 1), "Alejar (rueda del ratón, Mayús+Z)", false, BigIcon);
            var plus = Ui.IconButton("mas", () => SetZoom(_zoomIndex + 1), "Acercar (rueda del ratón, Z)", false, BigIcon);
            if (vertical) _toolbar.With(plus, zoom, minus);
            else _toolbar.With(minus, zoom, plus);
            _toolbar.Add(Ui.IconButton("ajustar", FitMap, "Ajustar: ver el tramo entero", false, BigIcon));
            _toolbar.Add(Ui.IconToggle("rejilla", _showGrid, v => { _showGrid = v; RedrawOverlay(); }, "Rejilla", BigIcon));
            if (S.Map.InWorld)
                _toolbar.Add(Ui.IconToggle("vecinos", _showNeighbors, v => { _showNeighbors = v; RebuildSections(); RedrawOverlay(); },
                    "Vecinos: ver los tramos de alrededor (doble clic en uno lo abre)", BigIcon));
            _toolbar.Add(Ui.Spacer());
            var play = Ui.Button("", () => _shell.RunAction("probar_aqui"), Ui.ButtonKind.Normal,
                "Jugar desde la casilla del ratón (" + ShortcutMap.Pretty(_shell.Workspace.Shortcuts.KeysFor("probar_aqui")) + ")");
            play.style.flexDirection = FlexDirection.Row;
            play.style.alignItems = Align.Center;
            play.Add(Icons.Element("jugar", Ui.IconSize * 0.8f, Ui.C("exito")));
            if (vertical) { play.style.width = Ui.ControlHeight; play.style.paddingLeft = 0; play.style.paddingRight = 0; play.style.justifyContent = Justify.Center; }
            else play.Add(Ui.Text("Probar aquí").Margin(6, 0, 0, 0));
            _toolbar.Add(play);
            // Where the bar goes (also with a right click on the bar).
            Button place = null;
            place = Ui.IconButton("asa", () => { var r = place.worldBound; ToolbarSideMenu(new Vector2(r.xMax + 2, r.y)); },
                "Colocar la barra: arriba, a la izquierda, a la derecha o abajo (también con clic derecho en la barra)", false, BigIcon);
            place.style.opacity = 0.6f;
            _toolbar.Add(place);
            // Bigger tool buttons on the map bar.
            foreach (var child in _toolbar.Children())
                if (child is Button btn && btn.childCount == 1 && string.IsNullOrEmpty(btn.text)) Big(btn);
        }

        private VisualElement ToolbarGap() => VerticalToolbar ? Ui.Separator().Margin(4, 6, 4, 6) : Ui.Separator(vertical: true).Margin(6, 4, 6, 4);

        private void ToolbarSideMenu(Vector2 at)
        {
            string now = ToolbarSide;
            _shell.ShowMenu(at, new List<MenuItem>
            {
                new MenuItem("Barra arriba", () => SetToolbarSide("arriba"), isChecked: now == "arriba"),
                new MenuItem("Barra a la izquierda (recomendado)", () => SetToolbarSide("izquierda"), isChecked: now == "izquierda"),
                new MenuItem("Barra a la derecha", () => SetToolbarSide("derecha"), isChecked: now == "derecha"),
                new MenuItem("Barra abajo", () => SetToolbarSide("abajo"), isChecked: now == "abajo"),
            });
        }

        private static float BigIcon => Mathf.Round(Ui.IconSize * 1.25f);

        /// <summary>A tool button of the map bar: a bit bigger than the ones in lists, with a bigger icon.</summary>
        private static Button Big(Button b)
        {
            float size = Ui.ControlHeight + 6;
            b.style.width = size;
            b.style.height = size;
            b.style.minHeight = size;
            return b;
        }

        private void SetToolbarSide(string side)
        {
            _shell.Workspace.SetPref(ToolbarPref, side);
            _shell.SaveWorkspaceSoon();
            PlaceToolbar();
            BuildToolbar();
        }

        /// <summary>Puts the toolbar on its side of the map: a row on top or bottom, a column on the left or right.</summary>
        private void PlaceToolbar()
        {
            _main.Clear();
            bool vertical = VerticalToolbar;
            _main.style.flexDirection = vertical ? FlexDirection.Row : FlexDirection.Column;
            _toolbar.style.flexDirection = vertical ? FlexDirection.Column : FlexDirection.Row;
            _toolbar.style.alignItems = Align.Center;
            _toolbar.Pad(vertical ? 4 : 6, vertical ? 6 : 4);
            var line = vertical ? Ui.Separator(vertical: true) : Ui.Separator();
            bool first = ToolbarSide == "arriba" || ToolbarSide == "izquierda";
            if (first) { _main.Add(_toolbar); _main.Add(line); _main.Add(_viewport); }
            else { _main.Add(_viewport); _main.Add(line); _main.Add(_toolbar); }
        }

        private static string IconOf(MapTool tool) => tool switch
        {
            MapTool.Pencil => "lapiz",
            MapTool.Rectangle => "rectangulo",
            MapTool.Fill => "relleno",
            MapTool.Eraser => "goma",
            MapTool.Picker => "cuentagotas",
            MapTool.Select => "seleccion",
            MapTool.Paste => "pegar",
            MapTool.EncounterPaint => "zona",
            _ => "inicio",
        };

        private void UpdateStatus()
        {
            if (S?.Map == null) { _status.text = ""; return; }
            var m = S.Map;
            string saved = S.IsDirty ? "sin guardar (se guarda solo)" : "guardado";
            string where = "";
            if (_cursor.HasValue && m.Contains(_cursor.Value.x, _cursor.Value.y))
            {
                var (x, y) = _cursor.Value;
                int t = m.TopTile(x, y, out int li);
                var sets = S.Tilesets;
                int tag = Passability.TerrainAt(m, sets, x, y);
                var areas = m.Encounters.Where(a => !a.WholeMap && a.Contains(x, y)).Select(a => a.Name).ToList();
                where = $"Casilla {x}, {y}" + (t >= 0 ? $" · tile {MapTile.Index(t)} ({m.Layers[li].Name})" : " · vacía")
                        + (tag != 0 ? " · " + S.Terrains.LabelOf(tag) : "")
                        + (!sets.IsEmpty && !Passability.Passable(m, sets, x, y, PassageBlock.All) ? " · no se puede pasar" : "")
                        + (areas.Count > 0 ? " · zona: " + string.Join(", ", areas) : "") + " · ";
            }
            string kind = m.Kind == MapKind.Interior ? "interior" : m.InWorld ? $"en el mundo ({m.WorldX}, {m.WorldY})" : "exterior sin colocar";
            _status.text = $"{where}{m.Name} · {m.Width} × {m.Height} · {kind} · {saved}";
        }

        // ── View ─────────────────────────────────────────────────────────────────────────────────

        private void ResizeTarget()
        {
            if (_renderer == null) return;
            float ppp = Ppp;
            _renderer.Resize(Mathf.RoundToInt(_viewport.layout.width * ppp), Mathf.RoundToInt(_viewport.layout.height * ppp));
            _image.image = _renderer.Target;
            _renderer.Zoom = ZoomLevels[_zoomIndex] * ppp; // the window's own zoom may have changed
            _renderer.UpdateCamera();
        }

        private void SetZoom(int index, Vector2? anchorPixel = null)
        {
            index = Mathf.Clamp(index, 0, ZoomLevels.Length - 1);
            if (_renderer?.Target == null) { _zoomIndex = index; BuildToolbar(); return; }
            Vector2 before = anchorPixel.HasValue ? _renderer.PixelToCell(anchorPixel.Value) : Vector2.zero;
            _zoomIndex = index;
            _renderer.Zoom = ZoomLevels[index] * Ppp;
            if (anchorPixel.HasValue)
            {
                var after = _renderer.PixelToCell(anchorPixel.Value);
                _renderer.Center += new Vector2(before.x - after.x, -(before.y - after.y));
            }
            _renderer.UpdateCamera();
            BuildToolbar();
            RedrawOverlay();
        }

        private void FitMap()
        {
            if (_renderer == null || S?.Map == null || _viewport.layout.width <= 0 || float.IsNaN(_viewport.layout.width)) return;
            _needsFit = false;
            float tile = _shell.Project.TileSize;
            float fit = Mathf.Min(_viewport.layout.width / (S.Map.Width * tile), _viewport.layout.height / (S.Map.Height * tile));
            int best = 0;
            for (int i = 0; i < ZoomLevels.Length; i++) if (ZoomLevels[i] <= fit) best = i;
            _zoomIndex = best;
            _renderer.Zoom = ZoomLevels[best] * Ppp;
            _renderer.Center = MapRenderer.CenterOf(0, 0, S.Map.Width, S.Map.Height);
            _renderer.UpdateCamera();
            BuildToolbar();
            RedrawOverlay();
        }

        // ── Pointer ──────────────────────────────────────────────────────────────────────────────

        private Vector2 LocalToPixel(Vector2 local) => local * Ppp;

        private (int x, int y) CellAt(Vector2 local)
        {
            var c = _renderer.PixelToCell(LocalToPixel(local));
            return (Mathf.FloorToInt(c.x), Mathf.FloorToInt(c.y));
        }

        private void OnPointerDown(PointerDownEvent e)
        {
            if (S?.Map == null || _renderer?.Target == null) return;
            _shell.ActiveEditor = "mapa";
            _viewport.CapturePointer(e.pointerId);
            var cell = CellAt(e.localPosition);
            if (e.button == 2 || (e.button == 0 && (e.altKey || _shell.SpaceHeld)))
            {
                _panning = true;
                _panStart = e.localPosition;
                _panCenter = _renderer.Center;
                return;
            }
            if (e.button == 0 && (e.ctrlKey || e.commandKey))
            {
                RetouchTileAt(cell);
                return;
            }
            bool secondary = false;
            if (e.button == 1)
            {
                if (S.Tool == MapTool.EncounterPaint) secondary = true;
                else
                {
                    _picking = true;
                    _toolBeforePick = S.Tool;
                    S.SetTool(MapTool.Picker);
                }
            }
            else if (e.button != 0) return;
            if (e.button == 0 && e.shiftKey && S.PaintLine(cell.x, cell.y))
            {
                _viewport.ReleasePointer(e.pointerId);
                RedrawOverlay();
                return;
            }
            _painting = true;
            S.PointerDown(cell.x, cell.y, secondary);
            RedrawOverlay();
        }

        private void OnPointerMove(PointerMoveEvent e)
        {
            if (S?.Map == null || _renderer?.Target == null) return;
            if (_panning)
            {
                var delta = (Vector2)e.localPosition - _panStart;
                float ppu = _renderer.PixelsPerUnit / Ppp;
                _renderer.Center = _panCenter + new Vector2(-delta.x / ppu, delta.y / ppu);
                _renderer.UpdateCamera();
                RedrawOverlay();
                return;
            }
            var cell = CellAt(e.localPosition);
            if (_cursor == cell) return;
            _cursor = cell;
            S.Cursor = S.Map.Contains(cell.x, cell.y) ? cell : ((int, int)?)null;
            if (_painting) S.PointerDrag(cell.x, cell.y);
            RedrawOverlay();
            UpdateStatus();
        }

        private void OnPointerUp(PointerUpEvent e)
        {
            if (_viewport.HasPointerCapture(e.pointerId)) _viewport.ReleasePointer(e.pointerId);
            if (_panning) { _panning = false; return; }
            if (!_painting || S?.Map == null) return;
            _painting = false;
            var cell = CellAt(e.localPosition);
            S.PointerUp(cell.x, cell.y);
            if (_picking)
            {
                _picking = false;
                if (_toolBeforePick == MapTool.Rectangle || _toolBeforePick == MapTool.Fill) S.SetTool(_toolBeforePick);
            }
            RedrawOverlay();
        }

        /// <summary>Double click on a faded neighbour opens it.</summary>
        private void OnClick(ClickEvent e)
        {
            if (e.clickCount != 2 || S?.Map == null || _renderer?.Target == null) return;
            var (x, y) = CellAt(e.localPosition);
            if (S.Map.Contains(x, y)) return;
            var m = S.Map;
            var n = _neighbors.FirstOrDefault(o => new WorldSection(o).Contains(m.WorldX + x, m.WorldY + y));
            if (n != null) S.OpenMap(n.Id);
        }

        private void OnWheel(WheelEvent e)
        {
            if (_renderer?.Target == null || e.ctrlKey) return;
            SetZoom(_zoomIndex + (e.delta.y < 0 ? 1 : -1), LocalToPixel(e.localMousePosition));
            e.StopPropagation();
        }

        private void RetouchTileAt((int x, int y) cell)
        {
            if (!S.Map.Contains(cell.x, cell.y)) return;
            int t = S.Map.TopTile(cell.x, cell.y, out _);
            var ts = S.Tilesets.For(t);
            if (ts == null) { _shell.Info("No hay ningún tile en esa casilla."); return; }
            _shell.OpenRetouch(Path.Combine(_shell.ProjectRoot, ts.ImagePath), ts.RectOf(MapTile.Index(t)), ts.TileWidth, ts.TileHeight);
        }

        // ── Overlay ──────────────────────────────────────────────────────────────────────────────

        private Vector2 P(float x, float y) => _renderer.CellToPixel(x, y) / Ppp;

        private void RedrawOverlay()
        {
            _overlay.Clear();
            _grid.ClearLines();
            if (_renderer?.Target == null || S?.Map == null) return;
            var m = S.Map;

            // Neighbour names.
            foreach (var n in _neighbors)
            {
                var a = P(n.WorldX - m.WorldX, n.WorldY - m.WorldY);
                Tag(n.Name + " (doble clic: abrir)", a + new Vector2(4, 4), Ui.C("texto_suave"));
            }

            // Map border.
            Box(P(0, 0), P(m.Width, m.Height), Ui.WithAlpha(Ui.C("texto"), 0.8f), 1, 0f);

            // Grid (only the visible part, only if cells are big enough).
            float cellPx = (P(1, 0) - P(0, 0)).x;
            if (_showGrid && cellPx >= 6)
            {
                var grid = Ui.C("rejilla");
                var v0 = _renderer.PixelToCell(Vector2.zero);
                var v1 = _renderer.PixelToCell(new Vector2(_renderer.Target.width, _renderer.Target.height));
                int x0 = Mathf.Max(0, Mathf.FloorToInt(v0.x)), x1 = Mathf.Min(m.Width, Mathf.CeilToInt(v1.x));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(v0.y)), y1 = Mathf.Min(m.Height, Mathf.CeilToInt(v1.y));
                _grid.Color = grid;
                _grid.Shadow = new Color(0, 0, 0, Mathf.Min(0.45f, grid.a + 0.1f));
                _grid.PixelsPerPoint = Ppp;
                for (int x = x0; x <= x1; x++) _grid.Add(P(x, y0), P(x, y1));
                for (int y = y0; y <= y1; y++) _grid.Add(P(x0, y), P(x1, y));
                _grid.Commit();
            }

            // Encounter areas: the painted ones as coloured rectangles (the active one stronger).
            // Encounter zones: the chosen one stands out; the others are faint, or all shown alike with
            // «highlight all» (Encuentros window).
            bool encounterTool = S.Tool == MapTool.EncounterPaint;
            bool all = S.HighlightAllAreas;
            foreach (var area in m.Encounters)
            {
                ColorUtility.TryParseHtmlString(area.Color, out var c);
                bool active = area.Id == S.ActiveAreaId;
                bool strong = active || all;
                if (area.WholeMap)
                {
                    if ((active && encounterTool) || all) Tag("Todo el tramo: " + area.Name, P(0, 0) + new Vector2(4, -Ui.FontSize - 6), c);
                    continue;
                }
                float fill = strong ? (active && encounterTool ? 0.45f : 0.32f) : 0.08f;
                var rects = area.ToRectangles().ToList();
                foreach (var (x, y, w, h) in rects)
                    Box(P(x, y), P(x + w, y + h), Ui.WithAlpha(c, strong ? 0.9f : 0.25f), strong ? 1 : 0, fill);
                if (all && rects.Count > 0) Tag(area.Name, P(rects[0].x, rects[0].y) + new Vector2(2, 2), c, filled: true);
            }
            // Optional (Encuentros → ajustes): painted cells where no method of the chosen zone would work.
            if (_shell.Workspace.Pref("encuentros_aviso_terreno", "no") == "si" && S.ActiveArea != null)
                foreach (var (x, y) in EncounterChecks.CellsOffTerrain(m, S.Tilesets, S.ActiveArea, S.Methods))
                    Box(P(x + 0.2f, y + 0.2f), P(x + 0.8f, y + 0.8f), Ui.C("error"), 2, 0.25f);

            // Objects (the player start for now).
            foreach (var o in m.Objects)
            {
                var a = P(o.X, o.Y);
                bool start = o.Kind == MapObject.PlayerStartKind;
                var c = Ui.C(start ? "exito" : "acento");
                Box(a, P(o.X + o.Width, o.Y + o.Height), c, 2, 0.15f);
                Tag(start ? "Inicio" : o.Name, a + new Vector2(0, -Ui.FontSize - 4), c, filled: true);
            }

            // Selection.
            if (S.Selection.HasValue)
            {
                var (sx0, sy0, sx1, sy1) = S.Selection.Value;
                Box(P(sx0, sy0), P(sx1 + 1, sy1 + 1), Ui.C("aviso"), 2, 0.12f);
            }

            // Drag rectangle (rectangle / picker / selection) or the cursor with the stamp preview.
            if (S.DragRect.HasValue)
            {
                var (dx0, dy0, dx1, dy1) = S.DragRect.Value;
                Box(P(Mathf.Min(dx0, dx1), Mathf.Min(dy0, dy1)), P(Mathf.Max(dx0, dx1) + 1, Mathf.Max(dy0, dy1) + 1),
                    Ui.C(S.Tool == MapTool.Rectangle ? "acento" : "aviso"), 2, 0.15f);
            }
            else if (_cursor.HasValue && m.Contains(_cursor.Value.x, _cursor.Value.y) && !_panning)
            {
                var (cx, cy) = _cursor.Value;
                int w = 1, h = 1;
                if (S.Tool == MapTool.Pencil)
                {
                    w = S.Stamp.Width;
                    h = S.Stamp.Height;
                    StampPreview(cx, cy);
                }
                else if (S.Tool == MapTool.Paste && S.Clipboard != null) { w = S.Clipboard.Width; h = S.Clipboard.Height; }
                string token = S.Tool == MapTool.Eraser ? "error" : S.Tool == MapTool.EncounterPaint || S.Tool == MapTool.Paste ? "aviso" : "acento";
                Box(P(cx, cy), P(cx + w, cy + h), Ui.C(token), 2, 0f);
            }
        }

        /// <summary>The stamp drawn half-transparent under the mouse before painting.</summary>
        private void StampPreview(int cx, int cy)
        {
            var st = S.Stamp;
            if (st.Width * st.Height > 256) return;
            for (int y = 0; y < st.Height; y++)
            for (int x = 0; x < st.Width; x++)
            {
                int cell = st[x, y];
                int slot = MapTile.Slot(cell);
                if (slot < 0 || slot >= S.Map.TilesetIds.Count) continue;
                var uv = _atlases.Get(S.Map.TilesetIds[slot])?.UvFor(MapTile.Index(cell));
                if (!uv.HasValue) continue;
                var a = P(cx + x, cy + y);
                var b = P(cx + x + 1, cy + y + 1);
                var img = new Image { image = uv.Value.texture, uv = uv.Value.uv, scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
                img.Absolute(a.x, a.y, b.x - a.x, b.y - a.y);
                img.style.opacity = 0.65f;
                int flags = MapTile.Flags(cell);
                if (flags != 0)
                {
                    var (deg, sx) = MapRenderer.UiTransformOf(flags);
                    img.style.rotate = new Rotate(new Angle(deg, AngleUnit.Degree));
                    img.style.scale = new Scale(new Vector3(sx, 1, 1));
                }
                _overlay.Add(img);
            }
        }

        private void Box(Vector2 a, Vector2 b, Color color, float width, float fill)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.Absolute(a.x, a.y, Mathf.Max(1, b.x - a.x), Mathf.Max(1, b.y - a.y));
            e.style.borderLeftWidth = width; e.style.borderRightWidth = width; e.style.borderTopWidth = width; e.style.borderBottomWidth = width;
            e.style.borderLeftColor = color; e.style.borderRightColor = color; e.style.borderTopColor = color; e.style.borderBottomColor = color;
            if (fill > 0) e.style.backgroundColor = Ui.WithAlpha(color, fill);
            _overlay.Add(e);
        }

        private void Tag(string text, Vector2 at, Color color, bool filled = false)
        {
            var tag = Ui.Text(text, 0.8f, bold: true);
            tag.style.color = filled ? Color.white : color;
            tag.style.backgroundColor = filled ? Ui.WithAlpha(color, 0.9f) : new Color(0, 0, 0, 0.55f);
            tag.Pad(3, 0).Round(3);
            tag.style.position = Position.Absolute;
            tag.style.left = at.x;
            tag.style.top = at.y;
            tag.pickingMode = PickingMode.Ignore;
            _overlay.Add(tag);
        }
    }
}
