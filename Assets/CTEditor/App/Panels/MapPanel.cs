using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using CTEditor.Editing;
using CTEditor.World.Domain;

namespace CTEditor.App
{
    /// <summary>
    /// Panel MAPA: el mapa abierto dibujado con el mismo renderizador que el juego, con las herramientas del editor.
    ///   - Clic izquierdo: la herramienta (lápiz, rectángulo, relleno, goma, cuentagotas, inicio del jugador).
    ///   - Clic derecho (y arrastrar): coger tiles del mapa como sello, como en RPG Maker.
    ///   - Botón central o Alt + arrastrar: mover la vista. Rueda: zoom hacia el ratón.
    ///   - Ctrl + clic: retocar ese tile en el editor de píxeles.
    /// Todo lo que cambia pasa por MapEditorSession (deshacer, autoguardado); este panel solo dibuja y reenvía el ratón.
    /// </summary>
    public sealed class MapPanel : VisualElement
    {
        private static readonly float[] ZoomLevels = { 0.25f, 0.5f, 1f, 1.5f, 2f, 3f, 4f, 6f, 8f };
        private static readonly (MapTool tool, string label, string action)[] Tools =
        {
            (MapTool.Pencil, "Lápiz", "lapiz"), (MapTool.Rectangle, "Rectángulo", "rectangulo"), (MapTool.Fill, "Relleno", "relleno"),
            (MapTool.Eraser, "Goma", "goma"), (MapTool.Picker, "Cuentagotas", "cuentagotas"), (MapTool.PlayerStart, "Inicio", null),
        };

        private readonly AppShell _shell;
        private MapEditorSession S => _shell.Maps;
        private MapRenderer _renderer;
        private TilesetAtlas _atlas;
        private readonly VisualElement _toolbar, _viewport, _overlay, _empty;
        private readonly Image _image;
        private readonly Label _status;
        private bool _showGrid = true;
        private int _zoomIndex = 4;
        private bool _needsFit = true;

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
            Add(_toolbar);
            Add(Ui.Separator());

            _viewport = new VisualElement().Grow();
            _viewport.style.overflow = Overflow.Hidden;
            _viewport.style.backgroundColor = Ui.Mix(Ui.C("fondo"), Color.black, 0.3f);
            _image = new Image { scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore }.Fill();
            _overlay = new VisualElement { pickingMode = PickingMode.Ignore }.Fill();
            _empty = Ui.Column(8).Fill();
            _empty.style.alignItems = Align.Center;
            _empty.style.justifyContent = Justify.Center;
            _viewport.Add(_image);
            _viewport.Add(_overlay);
            _viewport.Add(_empty);
            Add(_viewport);

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
            _viewport.RegisterCallback<PointerLeaveEvent>(_ => { if (!_painting && !_panning) { _cursor = null; if (S != null) S.Cursor = null; RedrawOverlay(); UpdateStatus(); } });
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
            S.SelectionChanged += OnSelectionChanged;
            S.TilesetChanged += OnTilesetChanged;
            S.DirtyChanged += UpdateStatus;
            _shell.PlayingChanged += OnPlaying;
            _shell.ActionRequested += OnAction;
            _renderer = new MapRenderer("Editor de mapas", MapRenderer.EditorLayer, Ui.Mix(Ui.C("fondo"), Color.black, 0.3f));
            OnMapOpened();
        }

        private void Detach()
        {
            if (S != null)
            {
                S.MapOpened -= OnMapOpened;
                S.TilesChanged -= OnTilesChanged;
                S.StructureChanged -= OnStructureChanged;
                S.SelectionChanged -= OnSelectionChanged;
                S.TilesetChanged -= OnTilesetChanged;
                S.DirtyChanged -= UpdateStatus;
            }
            _shell.PlayingChanged -= OnPlaying;
            _shell.ActionRequested -= OnAction;
            _renderer?.Dispose();
            _renderer = null;
            _atlas?.Dispose();
            _atlas = null;
        }

        private void OnPlaying(bool playing)
        {
            if (_renderer != null) _renderer.Camera.enabled = !playing;
        }

        private void OnAction(string id)
        {
            if (id == "rejilla") { _showGrid = !_showGrid; BuildToolbar(); RedrawOverlay(); }
        }

        // ── Session events ───────────────────────────────────────────────────────────────────────

        private void OnMapOpened()
        {
            LoadAtlas();
            _renderer?.SetMap(S?.Map, _atlas);
            ApplyLayerView();
            _needsFit = true;
            FitMap();
            BuildToolbar();
            ShowEmptyState();
            UpdateStatus();
        }

        private void LoadAtlas()
        {
            _atlas?.Dispose();
            _atlas = S?.Map == null ? null : TilesetAtlas.TryLoad(S.Tileset, _shell.ProjectRoot);
        }

        private void OnTilesChanged(int x, int y, int w, int h)
        {
            _renderer?.RefreshTiles(x, y, w, h);
            UpdateStatus();
        }

        private void OnStructureChanged()
        {
            if (S?.Map != null && (_atlas == null ? !string.IsNullOrEmpty(S.Map.TilesetId) : _atlas.Tileset.Id != S.Map.TilesetId)) LoadAtlas();
            _renderer?.SetMap(S?.Map, _atlas);
            ApplyLayerView();
            BuildToolbar();
            ShowEmptyState();
            RedrawOverlay();
            UpdateStatus();
        }

        private void OnTilesetChanged(string id)
        {
            if (S?.Map == null || (id != null && id != S.Map.TilesetId)) return;
            LoadAtlas();
            _renderer?.SetMap(S.Map, _atlas);
            ApplyLayerView();
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
            _renderer.ActiveLayer = S.ActiveLayer;
            _renderer.DimOthers = S.DimOtherLayers;
            _renderer.RefreshAllLayerViews();
        }

        private void ShowEmptyState()
        {
            _empty.Clear();
            bool noMap = S?.Map == null;
            _empty.Show(noMap || _atlas == null);
            _image.Show(!noMap);
            if (noMap)
            {
                _empty.pickingMode = PickingMode.Position;
                _empty.With(Ui.Title("Sin mapa abierto").Colored("texto_suave"),
                    Ui.Hint("Crea uno o ábrelo desde el panel Mapas."),
                    Ui.Button("Nuevo mapa…", () => MapTreePanel.NewMapDialog(_shell, ""), Ui.ButtonKind.Primary));
            }
            else if (_atlas == null)
            {
                _empty.pickingMode = PickingMode.Ignore;
                var box = Ui.Column(6).Bg("panel").Border(1, "aviso", 6).Pad(12);
                box.pickingMode = PickingMode.Position;
                box.With(Ui.Heading("Este mapa no tiene un tileset cortado"),
                    Ui.Hint("Corta una imagen de graficos/tilesets en el panel Recursos y elígela en Propiedades."));
                _empty.Add(box);
            }
        }

        // ── Toolbar and status ───────────────────────────────────────────────────────────────────

        private void BuildToolbar()
        {
            _toolbar.Clear();
            if (S?.Map == null) { _toolbar.Add(Ui.Text("Mapa", bold: true)); return; }
            foreach (var (tool, label, action) in Tools)
            {
                var t = tool;
                var keys = action == null ? "" : _shell.Workspace.Shortcuts.KeysFor(action);
                var chip = Ui.Chip(label, S.Tool == t, () => { _shell.ActiveEditor = "mapa"; S.SetTool(t); },
                    keys.Length > 0 ? $"{label} ({keys})" : t == MapTool.PlayerStart ? "Colocar el inicio del jugador" : label);
                _toolbar.Add(chip.Margin(0, 0, 4, 0));
            }
            _toolbar.Add(Ui.Separator(vertical: true).Margin(4, 2, 8, 2));

            var layer = S.ActiveLayer < S.Map.Layers.Count ? S.Map.Layers[S.ActiveLayer] : null;
            _toolbar.Add(Ui.Button("<", () => S.SetActiveLayer(S.ActiveLayer - 1), Ui.ButtonKind.Flat, "Capa anterior"));
            var ln = Ui.Text("Capa: " + (layer?.Name ?? "-") + (layer?.Locked == true ? " (bloqueada)" : ""), bold: true);
            ln.style.minWidth = 110;
            _toolbar.Add(ln);
            _toolbar.Add(Ui.Button(">", () => S.SetActiveLayer(S.ActiveLayer + 1), Ui.ButtonKind.Flat, "Capa siguiente"));
            _toolbar.Add(Ui.Separator(vertical: true).Margin(4, 2, 8, 2));

            _toolbar.Add(Ui.Button("-", () => SetZoom(_zoomIndex - 1), Ui.ButtonKind.Flat, "Alejar (rueda)"));
            _toolbar.Add(Ui.Text(Mathf.RoundToInt(ZoomLevels[_zoomIndex] * 100) + " %"));
            _toolbar.Add(Ui.Button("+", () => SetZoom(_zoomIndex + 1), Ui.ButtonKind.Flat, "Acercar (rueda)"));
            _toolbar.Add(Ui.Button("Ajustar", FitMap, Ui.ButtonKind.Flat, "Ver el mapa entero"));
            _toolbar.Add(Ui.Check("Rejilla", _showGrid, v => { _showGrid = v; RedrawOverlay(); }).Margin(6, 0, 0, 0));
            _toolbar.Add(Ui.Check("Atenuar las otras capas", S.DimOtherLayers, v => S.SetDimOtherLayers(v)).Margin(6, 0, 0, 0));
            _toolbar.Add(Ui.Spacer());
            _toolbar.Add(Ui.Button("Probar aquí", () => _shell.RunAction("probar_aqui"), Ui.ButtonKind.Normal,
                "Jugar desde la casilla del ratón (" + _shell.Workspace.Shortcuts.KeysFor("probar_aqui") + ")"));
        }

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
                var ts = S.Tileset;
                int tag = Passability.TerrainAt(m, ts, x, y);
                where = $"Casilla {x}, {y}" + (t >= 0 ? $" · tile {t} (capa {m.Layers[li].Name})" : " · vacía")
                        + (tag != 0 ? " · " + S.Terrains.LabelOf(tag) : "")
                        + (ts != null && !Passability.Passable(m, ts, x, y, PassageBlock.All) ? " · no se puede pasar" : "") + " · ";
            }
            _status.text = $"{where}{m.Name} · {m.Width} × {m.Height} · {m.Layers.Count} capas · {saved}";
        }

        // ── View ─────────────────────────────────────────────────────────────────────────────────

        private void ResizeTarget()
        {
            if (_renderer == null) return;
            float ppp = _shell.PixelsPerPoint;
            _renderer.Resize(Mathf.RoundToInt(_viewport.layout.width * ppp), Mathf.RoundToInt(_viewport.layout.height * ppp));
            _image.image = _renderer.Target;
            _renderer.UpdateCamera();
        }

        private void SetZoom(int index, Vector2? anchorPixel = null)
        {
            index = Mathf.Clamp(index, 0, ZoomLevels.Length - 1);
            if (_renderer == null || index == _zoomIndex && anchorPixel == null) { _zoomIndex = index; BuildToolbar(); return; }
            // Keep the world point under the mouse in place.
            Vector2 before = Vector2.zero;
            if (anchorPixel.HasValue && _renderer.Target != null) before = PixelToWorld(anchorPixel.Value);
            _zoomIndex = index;
            _renderer.Zoom = ZoomLevels[index] * _shell.PixelsPerPoint;
            if (anchorPixel.HasValue && _renderer.Target != null)
            {
                var after = PixelToWorld(anchorPixel.Value);
                _renderer.Center += before - after;
            }
            _renderer.UpdateCamera();
            BuildToolbar();
            RedrawOverlay();
        }

        private Vector2 PixelToWorld(Vector2 pixel)
        {
            float ppu = _renderer.PixelsPerUnit;
            return new Vector2(_renderer.Center.x + (pixel.x - _renderer.Target.width / 2f) / ppu,
                _renderer.Center.y + (_renderer.Target.height / 2f - pixel.y) / ppu);
        }

        private void FitMap()
        {
            if (_renderer == null || S?.Map == null || _viewport.layout.width <= 0 || float.IsNaN(_viewport.layout.width)) return;
            _needsFit = false;
            var ts = _atlas?.Tileset;
            float tw = ts?.TileWidth ?? _shell.Project.TileSize, th = ts?.TileHeight ?? tw;
            float fit = Mathf.Min(_viewport.layout.width / (S.Map.Width * tw), _viewport.layout.height / (S.Map.Height * th));
            int best = 0;
            for (int i = 0; i < ZoomLevels.Length; i++) if (ZoomLevels[i] <= fit) best = i;
            _zoomIndex = best;
            _renderer.Zoom = ZoomLevels[best] * _shell.PixelsPerPoint;
            _renderer.Center = _renderer.MapCenter;
            _renderer.UpdateCamera();
            BuildToolbar();
            RedrawOverlay();
        }

        // ── Pointer ──────────────────────────────────────────────────────────────────────────────

        private Vector2 LocalToPixel(Vector2 local) => local * _shell.PixelsPerPoint;

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
            if (e.button == 2 || (e.button == 0 && e.altKey))
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
            if (e.button == 1)
            {
                _picking = true;
                _toolBeforePick = S.Tool;
                S.SetTool(MapTool.Picker);
            }
            else if (e.button != 0) return;
            _painting = true;
            S.PointerDown(cell.x, cell.y);
            RedrawOverlay();
        }

        private void OnPointerMove(PointerMoveEvent e)
        {
            if (S?.Map == null || _renderer?.Target == null) return;
            if (_panning)
            {
                var delta = (Vector2)e.localPosition - _panStart;
                float ppu = _renderer.PixelsPerUnit / _shell.PixelsPerPoint;
                _renderer.Center = _panCenter + new Vector2(-delta.x / ppu, delta.y / ppu);
                _renderer.UpdateCamera();
                RedrawOverlay();
                return;
            }
            var cell = CellAt(e.localPosition);
            if (_cursor != cell)
            {
                _cursor = cell;
                S.Cursor = S.Map.Contains(cell.x, cell.y) ? cell : ((int, int)?)null;
                if (_painting) S.PointerDrag(cell.x, cell.y);
                RedrawOverlay();
                UpdateStatus();
            }
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
                if (_toolBeforePick != MapTool.Picker && _toolBeforePick != MapTool.Eraser && _toolBeforePick != MapTool.PlayerStart)
                    S.SetTool(_toolBeforePick);
            }
            RedrawOverlay();
        }

        private void OnWheel(WheelEvent e)
        {
            if (_renderer?.Target == null || e.ctrlKey) return;
            SetZoom(_zoomIndex + (e.delta.y < 0 ? 1 : -1), LocalToPixel(e.localMousePosition));
            e.StopPropagation();
        }

        private void RetouchTileAt((int x, int y) cell)
        {
            if (_atlas == null || !S.Map.Contains(cell.x, cell.y)) return;
            int t = S.Map.TopTile(cell.x, cell.y, out _);
            if (t < 0) { _shell.Info("No hay ningún tile en esa casilla."); return; }
            var ts = _atlas.Tileset;
            _shell.OpenRetouch(Path.Combine(_shell.ProjectRoot, ts.Id), ts.RectOf(t), ts.TileWidth, ts.TileHeight);
        }

        // ── Overlay ──────────────────────────────────────────────────────────────────────────────

        private void RedrawOverlay()
        {
            _overlay.Clear();
            if (_renderer?.Target == null || S?.Map == null) return;
            var m = S.Map;
            float ppp = _shell.PixelsPerPoint;
            Vector2 P(float x, float y) => _renderer.CellToPixel(x, y) / ppp;

            // Map border.
            var tl = P(0, 0);
            var br = P(m.Width, m.Height);
            Box(tl, br, Ui.WithAlpha(Ui.C("texto_suave"), 0.8f), 1, false);

            // Grid (only the visible part, only if cells are big enough).
            float cellPx = (P(1, 0) - P(0, 0)).x;
            if (_showGrid && cellPx >= 6)
            {
                var grid = Ui.C("rejilla");
                var v0 = _renderer.PixelToCell(Vector2.zero);
                var v1 = _renderer.PixelToCell(new Vector2(_renderer.Target.width, _renderer.Target.height));
                int x0 = Mathf.Max(0, Mathf.FloorToInt(v0.x)), x1 = Mathf.Min(m.Width, Mathf.CeilToInt(v1.x));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(v0.y)), y1 = Mathf.Min(m.Height, Mathf.CeilToInt(v1.y));
                for (int x = x0; x <= x1; x++) Line(P(x, y0), P(x, y1), grid);
                for (int y = y0; y <= y1; y++) Line(P(x0, y), P(x1, y), grid);
            }

            // Player start.
            var (sm, sx, sy) = S.PlayerStart;
            if (sm == m.Id)
            {
                var a = P(sx, sy);
                var b = P(sx + 1, sy + 1);
                Box(a, b, Ui.C("exito"), 2, true);
                var tag = Ui.Text("Inicio", 0.8f, bold: true);
                tag.style.color = Color.white;
                tag.style.backgroundColor = Ui.WithAlpha(Ui.C("exito"), 0.9f);
                tag.Pad(3, 0).Round(3);
                tag.style.position = Position.Absolute;
                tag.style.left = a.x;
                tag.style.top = a.y - Ui.FontSize - 4;
                tag.pickingMode = PickingMode.Ignore;
                _overlay.Add(tag);
            }

            // Drag rectangle (rectangle tool / picker).
            if (S.DragRect.HasValue)
            {
                var (dx0, dy0, dx1, dy1) = S.DragRect.Value;
                Box(P(Mathf.Min(dx0, dx1), Mathf.Min(dy0, dy1)), P(Mathf.Max(dx0, dx1) + 1, Mathf.Max(dy0, dy1) + 1),
                    Ui.C(S.Tool == MapTool.Picker ? "aviso" : "acento"), 2, true);
            }
            else if (_cursor.HasValue && m.Contains(_cursor.Value.x, _cursor.Value.y))
            {
                var (cx, cy) = _cursor.Value;
                int w = 1, h = 1;
                if (S.Tool == MapTool.Pencil) { w = S.Stamp.Width; h = S.Stamp.Height; }
                Box(P(cx, cy), P(cx + w, cy + h), Ui.C(S.Tool == MapTool.Eraser ? "error" : "acento"), 2, true);
            }
        }

        private void Box(Vector2 a, Vector2 b, Color color, float width, bool fill)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.Absolute(a.x, a.y, Mathf.Max(1, b.x - a.x), Mathf.Max(1, b.y - a.y));
            e.style.borderLeftWidth = width; e.style.borderRightWidth = width; e.style.borderTopWidth = width; e.style.borderBottomWidth = width;
            e.style.borderLeftColor = color; e.style.borderRightColor = color; e.style.borderTopColor = color; e.style.borderBottomColor = color;
            if (fill) e.style.backgroundColor = Ui.WithAlpha(color, 0.15f);
            _overlay.Add(e);
        }

        private void Line(Vector2 a, Vector2 b, Color color)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            if (Mathf.Approximately(a.x, b.x)) e.Absolute(a.x, Mathf.Min(a.y, b.y), 1, Mathf.Abs(b.y - a.y));
            else e.Absolute(Mathf.Min(a.x, b.x), a.y, Mathf.Abs(b.x - a.x), 1);
            e.style.backgroundColor = color;
            _overlay.Add(e);
        }
    }
}
