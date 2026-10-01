using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using CTEditor.Art.Domain;
using CTEditor.Editing;

namespace CTEditor.App
{
    /// <summary>
    /// Panel RETOQUE: un editor de píxeles pequeño para corregir tiles y sprites sin salir de la aplicación.
    ///   - Herramientas: lápiz, goma, relleno, línea, rectángulo (borde o relleno), cuentagotas, reemplazar un color.
    ///   - Clic izquierdo = color principal; derecho = secundario. Rueda = zoom; botón central = mover la vista.
    ///   - Rejilla de píxeles y de tiles; MODO TILE: el relleno y el reemplazo no salen del tile y se ve repetido 3 × 3
    ///     para comprobar que encaja sin costuras.
    /// Al guardar, la carpeta avisa del cambio y el mapa y el juego se actualizan solos.
    /// </summary>
    public sealed class RetouchPanel : VisualElement
    {
        private static readonly float[] ZoomLevels = { 1, 2, 3, 4, 6, 8, 12, 16, 24, 32 };
        private static readonly (PixelTool tool, string label, string action)[] Tools =
        {
            (PixelTool.Pencil, "Lápiz", "lapiz"), (PixelTool.Eraser, "Goma", "goma"), (PixelTool.Fill, "Relleno", "relleno"),
            (PixelTool.Line, "Línea", "linea"), (PixelTool.Rectangle, "Rectángulo", "rectangulo"), (PixelTool.FilledRectangle, "Rect. relleno", null),
            (PixelTool.Picker, "Cuentagotas", "cuentagotas"), (PixelTool.Replace, "Reemplazar color", null),
        };

        private static string IconOf(PixelTool tool) => tool switch
        {
            PixelTool.Pencil => "lapiz",
            PixelTool.Eraser => "goma",
            PixelTool.Fill => "relleno",
            PixelTool.Line => "linea",
            PixelTool.Rectangle => "rectangulo",
            PixelTool.FilledRectangle => "rect_relleno",
            PixelTool.Picker => "cuentagotas",
            _ => "reemplazar",
        };

        private readonly AppShell _shell;
        private PixelEditorSession S => _shell.Pixels;
        private readonly VisualElement _bar1, _bar2, _side, _canvas, _grid, _marks, _empty;
        private readonly ScrollView _scroll;
        private readonly Label _status;
        private readonly List<(Texture2D texture, int y, int height, Image view)> _strips = new List<(Texture2D, int, int, Image)>();
        private Texture2D _tileTexture;
        private readonly List<Image> _tilePreview = new List<Image>();
        private int _zoomIndex = 5;
        private bool _pixelGrid = true, _tileGrid = true;
        private bool _painting, _panning;
        private Vector2 _panStart, _panScroll;
        private (int x, int y)? _hover;

        public RetouchPanel(AppShell shell)
        {
            _shell = shell;
            style.flexGrow = 1;
            _bar1 = Bar();
            _bar2 = Bar();
            Add(_bar1);
            Add(_bar2);
            Add(Ui.Separator());

            var main = Ui.Row(0).Grow();
            main.style.alignItems = Align.Stretch;
            _scroll = Ui.Scroll(ScrollViewMode.VerticalAndHorizontal).Grow();
            _scroll.style.backgroundColor = Ui.Mix(Ui.C("fondo"), Color.black, 0.3f);
            _canvas = new VisualElement();
            _canvas.style.flexShrink = 0;
            _canvas.style.backgroundColor = Ui.Mix(Ui.C("fondo"), Color.white, 0.12f);
            _marks = new VisualElement { pickingMode = PickingMode.Ignore };
            _grid = new VisualElement { pickingMode = PickingMode.Ignore };
            _scroll.Add(_canvas);
            main.Add(_scroll);
            _side = Ui.Column(10).Pad(10).Bg("panel");
            _side.style.width = 230;
            _side.style.flexShrink = 0;
            _side.style.borderLeftWidth = 1;
            _side.style.borderLeftColor = Ui.C("borde");
            main.Add(_side);
            Add(main);

            _empty = Ui.Column(8).Fill();
            _empty.style.alignItems = Align.Center;
            _empty.style.justifyContent = Justify.Center;
            _empty.With(Ui.Title("Retoque").Colored("texto_suave"),
                Ui.Hint("Abre una imagen con «Retocar» en Recursos o en Tiles, o con Ctrl + clic en un tile del mapa."));
            main.Add(_empty);

            _status = Ui.Text("", 0.9f, dim: true);
            _status.Pad(8, 3);
            _status.style.flexShrink = 0;
            Add(_status);

            _scroll.verticalScroller.valueChanged += _ => DrawGrid();
            _scroll.horizontalScroller.valueChanged += _ => DrawGrid();
            _canvas.RegisterCallback<PointerDownEvent>(OnDown);
            _canvas.RegisterCallback<PointerMoveEvent>(OnMove);
            _canvas.RegisterCallback<PointerUpEvent>(OnUp);
            _canvas.RegisterCallback<WheelEvent>(OnWheel);
            _canvas.RegisterCallback<PointerLeaveEvent>(_ => { _hover = null; UpdateStatus(); });

            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                if (S == null) return;
                S.ImageOpened += OnOpened;
                S.PixelsChanged += OnPixels;
                S.SelectionChanged += OnSelection;
                S.DirtyChanged += BuildBars;
                _shell.ActionRequested += OnAction;
                OnOpened();
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (S != null)
                {
                    S.ImageOpened -= OnOpened;
                    S.PixelsChanged -= OnPixels;
                    S.SelectionChanged -= OnSelection;
                    S.DirtyChanged -= BuildBars;
                }
                _shell.ActionRequested -= OnAction;
                DestroyTextures();
            });
        }

        private static VisualElement Bar()
        {
            var b = Ui.Row(4).Pad(6, 3);
            b.style.flexShrink = 0;
            b.style.flexWrap = Wrap.Wrap;
            return b;
        }

        private float Zoom => ZoomLevels[_zoomIndex];

        // ── Session events ───────────────────────────────────────────────────────────────────────

        private void OnOpened()
        {
            DestroyTextures();
            _canvas.Clear();
            bool has = S?.Image != null;
            _empty.Show(!has);
            _scroll.Show(has);
            _side.Show(has);
            if (has)
            {
                int max = Math.Max(256, Math.Min(SystemInfo.maxTextureSize, 8192));
                for (int y = 0; y < S.Image.Height; y += max)
                {
                    int h = Math.Min(max, S.Image.Height - y);
                    var part = y == 0 && h == S.Image.Height ? S.Image : S.Image.Crop(new PixelRect(0, y, S.Image.Width, h));
                    var tex = Textures.FromImage(part);
                    var view = new Image { image = tex, scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
                    _canvas.Add(view);
                    _strips.Add((tex, y, h, view));
                }
                _canvas.Add(_marks);
                _canvas.Add(_grid);
                FitZoom();
            }
            BuildBars();
            BuildSide();
            UpdateStatus();
        }

        private void OnPixels(PixelRect r)
        {
            if (S?.Image == null) return;
            foreach (var (texture, y0, h, _) in _strips)
            {
                int ya = Math.Max(r.Y, y0), yb = Math.Min(r.Bottom, y0 + h);
                int xa = Math.Max(0, r.X), xb = Math.Min(S.Image.Width, r.Right);
                if (yb <= ya || xb <= xa) continue;
                int w = xb - xa, hh = yb - ya;
                var colors = new Color32[w * hh];
                for (int ty = 0; ty < hh; ty++)
                {
                    int iy = yb - 1 - ty;
                    for (int x = 0; x < w; x++) colors[ty * w + x] = Ui.ToColor32(S.Image[xa + x, iy]);
                }
                texture.SetPixels32(xa, h - (yb - y0), w, hh, colors);
                texture.Apply(false, false);
            }
            UpdateTilePreview();
        }

        private void OnSelection()
        {
            BuildBars();
            BuildSide();
            DrawGrid();
        }

        private void OnAction(string id)
        {
            if (S?.Image == null) return;
            if (id == "linea") { _shell.ActiveEditor = "retoque"; S.SetTool(PixelTool.Line); }
            else if (id == "rejilla") { _pixelGrid = !_pixelGrid; DrawGrid(); BuildBars(); }
        }

        // ── Bars ─────────────────────────────────────────────────────────────────────────────────

        private void BuildBars()
        {
            _bar1.Clear();
            _bar2.Clear();
            if (S?.Image == null)
            {
                _bar1.With(Ui.Text("Retoque", bold: true), Ui.Spacer(), Ui.Button("Abrir…", OpenDialog));
                return;
            }
            string name = Path.GetFileName(S.Path) + (S.IsDirty ? " *" : "");
            _bar1.With(Ui.Text(name, bold: true), Ui.Text($"{S.Image.Width} × {S.Image.Height}", 0.9f, dim: true), Ui.Spacer(),
                Ui.Button("Deshacer", () => S.Undo(), Ui.ButtonKind.Flat, "Ctrl+Z"),
                Ui.Button("Rehacer", () => S.Redo(), Ui.ButtonKind.Flat, "Ctrl+Y"),
                Ui.Button("Guardar", () => S.Save(), S.IsDirty ? Ui.ButtonKind.Primary : Ui.ButtonKind.Normal, "Ctrl+S"),
                Ui.Button("Abrir…", OpenDialog, Ui.ButtonKind.Flat),
                Ui.Button("Cerrar", CloseImage, Ui.ButtonKind.Flat));

            foreach (var (tool, label, action) in Tools)
            {
                var t = tool;
                var keys = action == null ? "" : _shell.Workspace.Shortcuts.KeysFor(action);
                _bar2.Add(Ui.IconButton(IconOf(t), () => { _shell.ActiveEditor = "retoque"; S.SetTool(t); },
                    keys.Length > 0 ? $"{label} ({keys})" : label, S.Tool == t).Margin(0, 2, 2, 2));
            }
            _bar2.Add(Ui.NumberBox("Grosor", S.BrushSize, 1, 16, v => S.SetBrushSize(v)).Margin(8, 0, 8, 4));
            _bar2.Add(Ui.IconButton("menos", () => SetZoom(_zoomIndex - 1), "Alejar (rueda del ratón)"));
            _bar2.Add(Ui.Text(Zoom + "×").NoShrink());
            _bar2.Add(Ui.IconButton("mas", () => SetZoom(_zoomIndex + 1), "Acercar (rueda del ratón)"));
            _bar2.Add(Ui.Check("Rejilla de píxeles", _pixelGrid, v => { _pixelGrid = v; DrawGrid(); }).Margin(8, 0, 0, 0));
            if (S.TileWidth > 0) _bar2.Add(Ui.Check("Rejilla de tiles", _tileGrid, v => { _tileGrid = v; DrawGrid(); }).Margin(8, 0, 0, 0));
        }

        private void BuildSide()
        {
            _side.Clear();
            if (S?.Image == null) return;

            var colors = Ui.Row(8);
            var primary = ColorButton(S.Primary, 34, "Color principal (clic izquierdo)");
            var secondary = ColorButton(S.Secondary, 26, "Color secundario (clic derecho)");
            colors.With(primary, secondary, Ui.Button("Cambiar", () => S.SwapColors(), Ui.ButtonKind.Flat, "Intercambiar los dos colores"));
            var hex = Ui.TextBox("Principal", Hex(S.Primary), null, delayed: true);
            hex.RegisterValueChangedCallback(e =>
            {
                var v = e.newValue.Trim();
                if (!v.StartsWith("#")) v = "#" + v;
                if (ColorUtility.TryParseHtmlString(v, out var c)) S.SetPrimary(From(c));
                else _shell.Warn($"«{e.newValue}» no es un color (#RRGGBB o #RRGGBBAA).");
            });
            _side.With(Ui.Text("Colores", bold: true), colors, hex,
                Ui.Button("Transparente como principal", () => S.SetPrimary(Rgba32.Transparent), Ui.ButtonKind.Flat));

            _side.Add(Ui.Text(S.TileLimit.HasValue ? "Paleta del tile" : "Paleta de la imagen", bold: true));
            _side.Add(Swatches(S.Palette(48)));
            if (S.Recent.Count > 0)
            {
                _side.Add(Ui.Text("Recientes", bold: true));
                _side.Add(Swatches(S.Recent));
            }

            _side.Add(Ui.Separator());
            _side.Add(Ui.Text("Modo tile", bold: true));
            if (S.TileWidth <= 0)
                _side.Add(Ui.Hint("La imagen no está cortada: no hay tiles. Córtala en Recursos para usar el modo tile."));
            else
            {
                _side.Add(Ui.Check("Trabajar en un solo tile", S.TileLimit.HasValue, v =>
                {
                    if (!v) { S.SetTileLimit(null); return; }
                    var t = S.TileAt(_hover?.x ?? 0, _hover?.y ?? 0) ?? S.TileAt(0, 0);
                    S.SetTileLimit(t);
                    if (t.HasValue) ScrollTo(t.Value);
                }));
                _side.Add(Ui.Hint("Con el modo tile, Alt + clic elige otro tile. El relleno y el reemplazo no salen de él."));
                var preview = new VisualElement();
                preview.style.flexDirection = FlexDirection.Row;
                preview.style.flexWrap = Wrap.Wrap;
                _tilePreview.Clear();
                if (S.TileLimit.HasValue)
                {
                    var t = S.TileLimit.Value;
                    float cell = Mathf.Min(64, 200f / 3f / t.Width * t.Width);
                    preview.style.width = cell * 3;
                    for (int i = 0; i < 9; i++)
                    {
                        var img = new Image { scaleMode = ScaleMode.StretchToFill };
                        img.style.width = cell;
                        img.style.height = cell * t.Height / t.Width;
                        preview.Add(img);
                        _tilePreview.Add(img);
                    }
                    _side.Add(Ui.Text("Repetido 3 × 3 (¿encaja?)", 0.9f, dim: true));
                    _side.Add(preview);
                    UpdateTilePreview();
                }
            }
        }

        private VisualElement Swatches(IEnumerable<Rgba32> colors)
        {
            var box = new VisualElement();
            box.style.flexDirection = FlexDirection.Row;
            box.style.flexWrap = Wrap.Wrap;
            foreach (var c in colors)
            {
                var color = c;
                var sw = Ui.Swatch(ToColor(c), 20);
                sw.style.marginRight = 3;
                sw.style.marginBottom = 3;
                sw.tooltip = Hex(c) + " · clic: principal · clic derecho: secundario";
                sw.RegisterCallback<PointerUpEvent>(e =>
                {
                    if (e.button == 1) S.SetSecondary(color);
                    else S.SetPrimary(color);
                });
                box.Add(sw);
            }
            return box;
        }

        private VisualElement ColorButton(Rgba32 c, float size, string tip)
        {
            var sw = Ui.Swatch(c.IsTransparent ? Ui.WithAlpha(Ui.C("texto_suave"), 0.2f) : ToColor(c), size);
            sw.tooltip = tip + (c.IsTransparent ? " — transparente" : " — " + Hex(c));
            return sw;
        }

        private static Color ToColor(Rgba32 c) => new Color32(c.R, c.G, c.B, c.A);
        private static Rgba32 From(Color c) { Color32 k = c; return new Rgba32(k.r, k.g, k.b, k.a); }
        private static string Hex(Rgba32 c) => c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : c.ToString();

        private void UpdateTilePreview()
        {
            if (S?.Image == null || !S.TileLimit.HasValue || _tilePreview.Count == 0) return;
            var t = S.TileLimit.Value;
            if (!S.Image.Contains(t)) return;
            if (_tileTexture != null) UnityEngine.Object.Destroy(_tileTexture);
            _tileTexture = Textures.FromImage(S.Image.Crop(t));
            foreach (var img in _tilePreview) img.image = _tileTexture;
        }

        // ── View ─────────────────────────────────────────────────────────────────────────────────

        private void SetZoom(int index)
        {
            _zoomIndex = Mathf.Clamp(index, 0, ZoomLevels.Length - 1);
            Layout();
            BuildBars();
        }

        private void FitZoom()
        {
            float w = Mathf.Max(200, _scroll.layout.width), h = Mathf.Max(200, _scroll.layout.height);
            if (float.IsNaN(w) || float.IsNaN(h)) { w = 600; h = 400; }
            int best = 0;
            for (int i = 0; i < ZoomLevels.Length; i++)
                if (S.Image.Width * ZoomLevels[i] <= w && S.Image.Height * ZoomLevels[i] <= h) best = i;
            _zoomIndex = best;
            Layout();
        }

        private void Layout()
        {
            if (S?.Image == null) return;
            _canvas.style.width = S.Image.Width * Zoom;
            _canvas.style.height = S.Image.Height * Zoom;
            foreach (var (_, y, h, view) in _strips) view.Absolute(0, y * Zoom, S.Image.Width * Zoom, h * Zoom);
            DrawGrid();
        }

        private void ScrollTo(PixelRect r)
        {
            _zoomIndex = ZoomLevels.Length - 1;
            while (_zoomIndex > 0 && r.Width * ZoomLevels[_zoomIndex] > Mathf.Max(200, _scroll.layout.width) * 0.7f) _zoomIndex--;
            Layout();
            BuildBars();
            _scroll.schedule.Execute(() => _scroll.scrollOffset = new Vector2(r.X * Zoom - 40, r.Y * Zoom - 40)).ExecuteLater(30);
        }

        /// <summary>Pixel grid (only over the visible part), tile grid and the frame of the tile being edited.</summary>
        private void DrawGrid()
        {
            _grid.Clear();
            _marks.Clear();
            if (S?.Image == null) return;
            float z = Zoom;
            _grid.Absolute(0, 0, S.Image.Width * z, S.Image.Height * z);
            _marks.Absolute(0, 0, S.Image.Width * z, S.Image.Height * z);
            var view = _scroll.contentViewport.layout;
            var off = _scroll.scrollOffset;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(off.x / z)), y0 = Mathf.Max(0, Mathf.FloorToInt(off.y / z));
            int x1 = Mathf.Min(S.Image.Width, Mathf.CeilToInt((off.x + (float.IsNaN(view.width) ? 800 : view.width)) / z) + 1);
            int y1 = Mathf.Min(S.Image.Height, Mathf.CeilToInt((off.y + (float.IsNaN(view.height) ? 600 : view.height)) / z) + 1);
            if (_pixelGrid && z >= 6)
            {
                var c = Ui.WithAlpha(Ui.C("rejilla"), 0.35f);
                for (int x = x0; x <= x1; x++) Line(x * z, y0 * z, 1, (y1 - y0) * z, c);
                for (int y = y0; y <= y1; y++) Line(x0 * z, y * z, (x1 - x0) * z, 1, c);
            }
            if (_tileGrid && S.TileWidth > 0 && S.TileHeight > 0)
            {
                var c = Ui.WithAlpha(Ui.C("acento"), 0.7f);
                for (int x = x0 / S.TileWidth * S.TileWidth; x <= x1; x += S.TileWidth) Line(x * z, y0 * z, 1, (y1 - y0) * z, c);
                for (int y = y0 / S.TileHeight * S.TileHeight; y <= y1; y += S.TileHeight) Line(x0 * z, y * z, (x1 - x0) * z, 1, c);
            }
            if (S.TileLimit.HasValue)
            {
                var t = S.TileLimit.Value;
                var frame = new VisualElement { pickingMode = PickingMode.Ignore };
                frame.Absolute(t.X * z - 2, t.Y * z - 2, t.Width * z + 4, t.Height * z + 4);
                frame.Border(2, "aviso");
                _marks.Add(frame);
            }
        }

        private void Line(float x, float y, float w, float h, Color c)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.Absolute(x, y, w, h);
            e.style.backgroundColor = c;
            _grid.Add(e);
        }

        // ── Pointer ──────────────────────────────────────────────────────────────────────────────

        private (int x, int y) PixelAt(Vector2 local) => (Mathf.FloorToInt(local.x / Zoom), Mathf.FloorToInt(local.y / Zoom));

        private void OnDown(PointerDownEvent e)
        {
            if (S?.Image == null) return;
            _shell.ActiveEditor = "retoque";
            _canvas.CapturePointer(e.pointerId);
            var (x, y) = PixelAt(e.localPosition);
            if (e.button == 2)
            {
                _panning = true;
                _panStart = e.position;
                _panScroll = _scroll.scrollOffset;
                return;
            }
            if (e.altKey && S.TileLimit.HasValue)
            {
                S.SetTileLimit(S.TileAt(x, y) ?? S.TileLimit);
                return;
            }
            if (S.TileLimit.HasValue && (S.Tool == PixelTool.Pencil || S.Tool == PixelTool.Eraser))
            {
                var t = S.TileLimit.Value;
                if (x < t.X || y < t.Y || x >= t.Right || y >= t.Bottom) return; // tile mode: stay inside the tile
            }
            _painting = true;
            S.PointerDown(x, y, secondary: e.button == 1);
        }

        private void OnMove(PointerMoveEvent e)
        {
            if (S?.Image == null) return;
            if (_panning)
            {
                _scroll.scrollOffset = _panScroll - ((Vector2)e.position - _panStart);
                DrawGrid();
                return;
            }
            var p = PixelAt(e.localPosition);
            if (_hover != p)
            {
                _hover = p;
                if (_painting) S.PointerDrag(p.x, p.y);
                UpdateStatus();
            }
        }

        private void OnUp(PointerUpEvent e)
        {
            if (_canvas.HasPointerCapture(e.pointerId)) _canvas.ReleasePointer(e.pointerId);
            if (_panning) { _panning = false; return; }
            if (!_painting || S?.Image == null) return;
            _painting = false;
            var (x, y) = PixelAt(e.localPosition);
            S.PointerUp(x, y);
            BuildSide();
        }

        private void OnWheel(WheelEvent e)
        {
            if (S?.Image == null || e.ctrlKey) return;
            if (e.shiftKey) return; // Shift + wheel scrolls sideways (ScrollView)
            SetZoom(_zoomIndex + (e.delta.y < 0 ? 1 : -1));
            e.StopPropagation();
        }

        private void UpdateStatus()
        {
            if (S?.Image == null) { _status.text = ""; return; }
            string at = "";
            if (_hover.HasValue && S.Image.Contains(_hover.Value.x, _hover.Value.y))
            {
                var (x, y) = _hover.Value;
                var c = S.Image[x, y];
                at = $"Píxel {x}, {y} · {(c.IsTransparent ? "transparente" : Hex(c))} · ";
                var tile = S.TileAt(x, y);
                if (tile.HasValue && S.TileWidth > 0)
                    at += $"tile n.º {tile.Value.Y / S.TileHeight * (S.Image.Width / S.TileWidth) + tile.Value.X / S.TileWidth} · ";
            }
            _status.text = at + "Clic: principal · clic derecho: secundario · botón central: mover · rueda: zoom";
        }

        // ── Files ────────────────────────────────────────────────────────────────────────────────

        private void OpenDialog()
        {
            FolderBrowser.PickFile(_shell, "Abrir una imagen para retocar", ".png", path => _shell.OpenRetouch(path), _shell.GraphicsFolder);
        }

        private void CloseImage()
        {
            if (S.IsDirty)
            {
                _shell.Confirm("Cerrar la imagen", "Hay cambios sin guardar. ¿Guardarlos?", "Guardar y cerrar", () => { if (S.Save()) S.Close(); });
                return;
            }
            S.Close();
        }

        private void DestroyTextures()
        {
            foreach (var (texture, _, _, _) in _strips) if (texture != null) UnityEngine.Object.Destroy(texture);
            _strips.Clear();
            if (_tileTexture != null) UnityEngine.Object.Destroy(_tileTexture);
            _tileTexture = null;
        }
    }
}
