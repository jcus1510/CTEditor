using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using CTEditor.Art.Domain;
using CTEditor.Project;

namespace CTEditor.App
{
    /// <summary>
    /// CORTE LIBRE: rectangles drawn by hand on the image, with pixel precision and a magnifier, for big tiles and loose
    /// objects of ripped sheets that do not follow the grid. Each piece becomes a block of whole tiles under the grid of
    /// the tileset (see FreePieceSet), so the palette and the maps use it like any other tile.
    /// </summary>
    public sealed class FreeSliceDialog
    {
        private static readonly Color[] PieceColors =
        {
            new Color(0.95f, 0.55f, 0.2f), new Color(0.3f, 0.75f, 0.95f), new Color(0.55f, 0.85f, 0.35f), new Color(0.9f, 0.4f, 0.7f),
            new Color(0.95f, 0.85f, 0.3f), new Color(0.6f, 0.5f, 0.95f), new Color(0.35f, 0.85f, 0.75f), new Color(0.95f, 0.4f, 0.4f),
        };

        private const int LoupePixels = 21; // odd: the pixel under the pointer is in the middle
        private const float LoupeSize = 168;

        private enum Drag { None, Create, Move, Resize }

        private readonly AppShell _shell;
        private readonly string _path;
        private readonly PixelImage _image;
        private readonly SliceFile _file;
        private readonly SliceSettings _settings;
        private readonly FreePieceSet _set;
        private readonly int _columns, _gridRows;
        private readonly List<TextureStrip> _strips;
        private readonly AppShell.Dialog _dialog;

        private float _zoom = 2f;
        private bool _snap, _showGrid = true;
        private FreePiece _selected;
        private Drag _drag;
        private Vector2Int _dragStart;
        private PixelRect _dragOrigin, _draft;

        private ScrollView _view;
        private VisualElement _canvas, _gridLayer, _pieceLayer, _draftBox;
        private VisualElement _list, _details;
        private Image _loupe;
        private Label _loupeInfo, _status;

        public static void Show(AppShell shell, string imagePath)
        {
            try
            {
                var file = SliceFile.LoadFor(imagePath);
                if (file == null || file.Kind != SheetKind.Tileset)
                {
                    shell.Warn("Primero corta la imagen como tileset (Recursos → Cortar…); luego añade las piezas libres.");
                    return;
                }
                new FreeSliceDialog(shell, imagePath, file);
            }
            catch (Exception e)
            {
                shell.Error($"No se pudo abrir «{Path.GetFileName(imagePath)}»: {e.Message}");
            }
        }

        private FreeSliceDialog(AppShell shell, string path, SliceFile file)
        {
            _shell = shell;
            _path = path;
            _file = file;
            _settings = file.Settings;
            _image = Png.Read(path);
            _columns = _settings.ColumnsFor(_image.Width);
            _gridRows = _settings.RowsFor(_image.Height);
            _set = file.Free.Clone();
            _set.Repair(_settings, _columns);
            _strips = Textures.Strips(_image);
            _zoom = _image.Width * 2 <= 900 ? 2f : 1f;

            _dialog = shell.ShowDialog($"Corte libre de «{Path.GetFileName(path)}» ({_image.Width} × {_image.Height} px, tiles de {_settings.TileWidth} × {_settings.TileHeight})", 94, 90);
            _dialog.OnClose = () => Textures.Destroy(_strips);
            Build();
            Redraw();
            RefreshList();
        }

        // ── Layout ───────────────────────────────────────────────────────────────────────────────

        private void Build()
        {
            var main = Ui.Row(12).Grow();
            main.style.alignItems = Align.Stretch;

            _view = Ui.Scroll(ScrollViewMode.VerticalAndHorizontal).Grow();
            _view.Bg("fondo").Border(1, "borde", 4);
            _canvas = new VisualElement { focusable = true };
            _canvas.style.flexShrink = 0;
            _canvas.style.backgroundColor = Ui.Mix(Ui.C("fondo"), Color.white, 0.06f);
            foreach (var strip in _strips)
            {
                var img = new Image { image = strip.Texture, scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore, userData = strip };
                _canvas.Add(img);
            }
            _gridLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            _pieceLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            _draftBox = new VisualElement { pickingMode = PickingMode.Ignore };
            _draftBox.style.position = Position.Absolute;
            _draftBox.Border(1, "acento");
            _draftBox.style.backgroundColor = Ui.WithAlpha(Ui.C("acento"), 0.18f);
            _draftBox.Show(false);
            _canvas.With(_gridLayer, _pieceLayer, _draftBox);
            _canvas.RegisterCallback<PointerDownEvent>(OnDown);
            _canvas.RegisterCallback<PointerMoveEvent>(OnMove);
            _canvas.RegisterCallback<PointerUpEvent>(OnUp);
            _canvas.RegisterCallback<KeyDownEvent>(OnKey);
            _canvas.RegisterCallback<WheelEvent>(e =>
            {
                if (!e.altKey) return;
                SetZoom(_zoom * (e.delta.y < 0 ? 1.25f : 0.8f));
                e.StopPropagation();
            });
            _view.Add(_canvas);
            main.Add(_view);

            var side = Ui.Column(8);
            side.style.width = 330;
            side.style.flexShrink = 0;

            // Magnifier: the pixels around the pointer, big, with the one under it marked.
            var loupeBox = new VisualElement().Border(1, "borde", 4);
            loupeBox.style.width = LoupeSize;
            loupeBox.style.height = LoupeSize;
            loupeBox.style.alignSelf = Align.Center;
            loupeBox.style.overflow = Overflow.Hidden;
            loupeBox.style.backgroundColor = Ui.Mix(Ui.C("fondo"), Color.white, 0.06f);
            _loupe = new Image { scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            _loupe.Fill();
            float cell = LoupeSize / LoupePixels;
            var cross = new VisualElement { pickingMode = PickingMode.Ignore }.Absolute(cell * (LoupePixels / 2), cell * (LoupePixels / 2), cell, cell);
            cross.Border(1, "acento");
            loupeBox.With(_loupe, cross);
            _loupeInfo = Ui.Hint("Pasa el ratón por la imagen: la lupa enseña los píxeles.");
            _loupeInfo.style.unityTextAlign = TextAnchor.MiddleCenter;
            side.With(Ui.SectionTitle("Lupa"), loupeBox, _loupeInfo);

            var zoomRow = Ui.Row(6);
            zoomRow.With(
                Ui.IconButton("menos", () => SetZoom(_zoom / 1.25f), "Alejar (Alt + rueda)"),
                Ui.IconButton("mas", () => SetZoom(_zoom * 1.25f), "Acercar (Alt + rueda)"),
                Ui.IconToggle("rejilla", _showGrid, on => { _showGrid = on; Redraw(); }, "Ver la rejilla de tiles"),
                Ui.Check("Ajustar a la rejilla", _snap, on => _snap = on));
            side.Add(zoomRow);

            var tools = Ui.Row(2);
            tools.With(
                Ui.IconButton("relleno", AutotileFromFile, "Autotile desde otra imagen (RPG Maker XP 3×4, VX/MV 2×3 o 47 piezas): se detecta por las medidas"),
                Ui.IconButton("dado", DetectPieces, "Detectar objetos: busca los grupos de píxeles sueltos y crea una pieza para cada uno (luego ajústalas)"));
            side.Add(Ui.SectionTitle("Piezas", tools));
            var listScroll = Ui.Scroll().Grow();
            _list = Ui.Column(2);
            listScroll.Add(_list);
            side.Add(listScroll);
            _details = Ui.Column(4);
            side.Add(_details);
            main.Add(side);
            _dialog.Body.Add(main);

            _status = Ui.Hint("Arrastra en la imagen para cortar una pieza. Arrastra dentro de una pieza para moverla y desde su esquina para cambiar su tamaño. Flechas: mover 1 píxel (Mayús: cambiar el tamaño). Supr: quitar.");
            _status.style.flexShrink = 0;
            _dialog.Body.Add(_status);

            _dialog.Buttons.With(Ui.Hint("Las piezas se añaden debajo de los tiles, en la paleta."), Ui.Spacer(),
                Ui.Button("Cancelar", () => _shell.CloseDialog(_dialog)),
                Ui.Button("Guardar piezas", Save, Ui.ButtonKind.Primary));
        }

        private void SetZoom(float zoom)
        {
            _zoom = Mathf.Clamp(zoom, 0.5f, 12f);
            Redraw();
        }

        // ── Drawing ──────────────────────────────────────────────────────────────────────────────

        private void Redraw()
        {
            _canvas.style.width = _image.Width * _zoom;
            _canvas.style.height = _image.Height * _zoom;
            foreach (var img in _canvas.Children().OfType<Image>())
            {
                var strip = (TextureStrip)img.userData;
                img.Absolute(0, strip.Y * _zoom, _image.Width * _zoom, strip.Height * _zoom);
            }
            _gridLayer.Fill();
            _gridLayer.Clear();
            if (_showGrid)
            {
                var line = Ui.WithAlpha(Ui.C("texto"), 0.18f);
                for (int c = 0; c <= _columns; c++)
                {
                    float x = (_settings.OffsetX + c * (_settings.TileWidth + _settings.SpacingX)) * _zoom;
                    var v = new VisualElement { pickingMode = PickingMode.Ignore }.Absolute(x, 0, 1, _image.Height * _zoom);
                    v.style.backgroundColor = line;
                    _gridLayer.Add(v);
                }
                for (int r = 0; r <= _gridRows; r++)
                {
                    float y = (_settings.OffsetY + r * (_settings.TileHeight + _settings.SpacingY)) * _zoom;
                    var h = new VisualElement { pickingMode = PickingMode.Ignore }.Absolute(0, y, _image.Width * _zoom, 1);
                    h.style.backgroundColor = line;
                    _gridLayer.Add(h);
                }
            }
            DrawPieces();
        }

        private Color ColorOf(FreePiece p) => PieceColors[Math.Max(0, _set.Pieces.ToList().IndexOf(p)) % PieceColors.Length];

        private void DrawPieces()
        {
            _pieceLayer.Fill();
            _pieceLayer.Clear();
            foreach (var p in _set.Pieces.Where(x => string.IsNullOrEmpty(x.ImagePath)))
            {
                var color = ColorOf(p);
                var r = p.Source;
                var box = new VisualElement { pickingMode = PickingMode.Ignore }.Absolute(r.X * _zoom, r.Y * _zoom, r.Width * _zoom, r.Height * _zoom);
                float w = p == _selected ? 2 : 1;
                box.style.borderTopWidth = box.style.borderBottomWidth = box.style.borderLeftWidth = box.style.borderRightWidth = w;
                box.style.borderTopColor = box.style.borderBottomColor = box.style.borderLeftColor = box.style.borderRightColor = color;
                box.style.backgroundColor = Ui.WithAlpha(color, p == _selected ? 0.2f : 0.08f);
                var name = Ui.Text(p.Name, 0.8f);
                name.style.position = Position.Absolute;
                name.style.left = 0;
                name.style.top = -16;
                name.style.color = color;
                name.pickingMode = PickingMode.Ignore;
                box.Add(name);
                if (p == _selected)
                {
                    var handle = new VisualElement { pickingMode = PickingMode.Ignore }.Absolute(r.Width * _zoom - 5, r.Height * _zoom - 5, 8, 8);
                    handle.style.backgroundColor = color;
                    box.Add(handle);
                }
                _pieceLayer.Add(box);
            }
        }

        private void ShowDraft(PixelRect r)
        {
            _draftBox.Show(r.Width > 0 && r.Height > 0);
            _draftBox.Absolute(r.X * _zoom, r.Y * _zoom, r.Width * _zoom, r.Height * _zoom);
            _status.text = $"({r.X}, {r.Y}) · {r.Width} × {r.Height} px · {Blocks(r)}";
        }

        private string Blocks(PixelRect r) =>
            $"{(r.Width + _settings.TileWidth - 1) / _settings.TileWidth} × {(r.Height + _settings.TileHeight - 1) / _settings.TileHeight} tiles";

        // ── Pointer ──────────────────────────────────────────────────────────────────────────────

        private Vector2Int PixelAt(Vector2 local) =>
            new Vector2Int(Mathf.Clamp(Mathf.FloorToInt(local.x / _zoom), 0, _image.Width), Mathf.Clamp(Mathf.FloorToInt(local.y / _zoom), 0, _image.Height));

        private int SnapX(int x) => !_snap ? x : _settings.OffsetX + Mathf.RoundToInt((x - _settings.OffsetX) / (float)(_settings.TileWidth + _settings.SpacingX)) * (_settings.TileWidth + _settings.SpacingX);
        private int SnapY(int y) => !_snap ? y : _settings.OffsetY + Mathf.RoundToInt((y - _settings.OffsetY) / (float)(_settings.TileHeight + _settings.SpacingY)) * (_settings.TileHeight + _settings.SpacingY);

        private void OnDown(PointerDownEvent e)
        {
            if (e.button != 0) return;
            _canvas.Focus();
            var p = PixelAt(e.localPosition);
            _dragStart = p;
            if (_selected != null)
            {
                var s = _selected.Source;
                float hx = s.Right * _zoom, hy = s.Bottom * _zoom;
                if (Mathf.Abs(e.localPosition.x - hx) <= 8 && Mathf.Abs(e.localPosition.y - hy) <= 8)
                {
                    _drag = Drag.Resize;
                    _dragOrigin = s;
                    _canvas.CapturePointer(e.pointerId);
                    return;
                }
            }
            var hit = _set.Pieces.LastOrDefault(pc => string.IsNullOrEmpty(pc.ImagePath) && p.x >= pc.Source.X && p.x < pc.Source.Right && p.y >= pc.Source.Y && p.y < pc.Source.Bottom);
            if (hit != null)
            {
                Select(hit);
                _drag = Drag.Move;
                _dragOrigin = hit.Source;
            }
            else _drag = Drag.Create;
            _canvas.CapturePointer(e.pointerId);
        }

        private void OnMove(PointerMoveEvent e)
        {
            var p = PixelAt(e.localPosition);
            UpdateLoupe(p);
            if (_drag == Drag.None) return;
            switch (_drag)
            {
                case Drag.Create:
                {
                    int x0 = SnapX(Math.Min(_dragStart.x, p.x)), y0 = SnapY(Math.Min(_dragStart.y, p.y));
                    int x1 = SnapX(Math.Max(_dragStart.x, p.x) + (_snap ? 0 : 1)), y1 = SnapY(Math.Max(_dragStart.y, p.y) + (_snap ? 0 : 1));
                    _draft = Clamp(new PixelRect(x0, y0, x1 - x0, y1 - y0));
                    break;
                }
                case Drag.Move:
                {
                    int x = SnapX(_dragOrigin.X + p.x - _dragStart.x), y = SnapY(_dragOrigin.Y + p.y - _dragStart.y);
                    x = Mathf.Clamp(x, 0, _image.Width - _dragOrigin.Width);
                    y = Mathf.Clamp(y, 0, _image.Height - _dragOrigin.Height);
                    _draft = new PixelRect(x, y, _dragOrigin.Width, _dragOrigin.Height);
                    break;
                }
                case Drag.Resize:
                {
                    int right = SnapX(Math.Max(_dragOrigin.X + 1, p.x)), bottom = SnapY(Math.Max(_dragOrigin.Y + 1, p.y));
                    _draft = Clamp(new PixelRect(_dragOrigin.X, _dragOrigin.Y, right - _dragOrigin.X, bottom - _dragOrigin.Y));
                    break;
                }
            }
            ShowDraft(_draft);
        }

        private void OnUp(PointerUpEvent e)
        {
            if (_canvas.HasPointerCapture(e.pointerId)) _canvas.ReleasePointer(e.pointerId);
            var drag = _drag;
            _drag = Drag.None;
            _draftBox.Show(false);
            if (drag == Drag.Create)
            {
                if (_draft.Width >= 2 && _draft.Height >= 2) AddPiece(_draft);
            }
            else if ((drag == Drag.Move || drag == Drag.Resize) && _selected != null && _draft.Width > 0 && !_draft.Equals(_selected.Source))
                Reshape(_selected, _draft);
            _draft = default;
        }

        private void OnKey(KeyDownEvent e)
        {
            if (_selected == null) return;
            if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) { Remove(_selected); e.StopPropagation(); return; }
            int dx = e.keyCode == KeyCode.LeftArrow ? -1 : e.keyCode == KeyCode.RightArrow ? 1 : 0;
            int dy = e.keyCode == KeyCode.UpArrow ? -1 : e.keyCode == KeyCode.DownArrow ? 1 : 0;
            if (dx == 0 && dy == 0) return;
            var r = _selected.Source;
            var next = e.shiftKey
                ? new PixelRect(r.X, r.Y, Math.Max(1, r.Width + dx), Math.Max(1, r.Height + dy))
                : new PixelRect(r.X + dx, r.Y + dy, r.Width, r.Height);
            if (next.X >= 0 && next.Y >= 0 && next.Right <= _image.Width && next.Bottom <= _image.Height) Reshape(_selected, next);
            e.StopPropagation();
        }

        private PixelRect Clamp(PixelRect r)
        {
            int x0 = Mathf.Clamp(r.X, 0, _image.Width), y0 = Mathf.Clamp(r.Y, 0, _image.Height);
            int x1 = Mathf.Clamp(r.Right, 0, _image.Width), y1 = Mathf.Clamp(r.Bottom, 0, _image.Height);
            return new PixelRect(x0, y0, Math.Max(0, x1 - x0), Math.Max(0, y1 - y0));
        }

        private void UpdateLoupe(Vector2Int p)
        {
            int half = LoupePixels / 2;
            var strip = _strips.FirstOrDefault(s => p.y >= s.Y && p.y < s.Y + s.Height) ?? _strips.FirstOrDefault();
            if (strip == null) return;
            float w = strip.Texture.width, h = strip.Texture.height;
            int localY = p.y - strip.Y;
            // Texture rows start at the bottom.
            _loupe.image = strip.Texture;
            _loupe.uv = new Rect((p.x - half) / w, (h - (localY + half + 1)) / h, LoupePixels / w, LoupePixels / h);
            string color = _image.Contains(p.x, p.y) ? _image[p.x, p.y].ToString() : "—";
            _loupeInfo.text = $"Píxel ({p.x}, {p.y}) · {color}";
        }

        // ── Pieces ───────────────────────────────────────────────────────────────────────────────

        private string NextName()
        {
            for (int i = _set.Pieces.Count + 1; ; i++)
                if (_set.Pieces.All(p => p.Name != "Pieza " + i)) return "Pieza " + i;
        }

        private bool AddPiece(PixelRect r, bool quiet = false)
        {
            var problems = FreePieceSet.Check(r, _image.Width, _image.Height, _settings, _columns);
            if (problems.Count > 0) { if (!quiet) _shell.Warn(problems[0]); return false; }
            var piece = _set.Add(NextName(), r, _settings, _columns, _gridRows);
            if (piece == null) return false;
            if (!quiet) Select(piece);
            return true;
        }

        private void Reshape(FreePiece piece, PixelRect r)
        {
            var problems = FreePieceSet.Check(r, _image.Width, _image.Height, _settings, _columns);
            if (problems.Count > 0 || !_set.Reshape(piece, r, _settings, _columns)) { _shell.Warn(problems.FirstOrDefault() ?? "Esa forma no cabe."); return; }
            Redraw();
            RefreshList();
        }

        private void Remove(FreePiece piece)
        {
            _set.Remove(piece);
            if (_selected == piece) _selected = null;
            Redraw();
            RefreshList();
        }

        private void Select(FreePiece piece)
        {
            _selected = piece;
            DrawPieces();
            RefreshList();
        }

        private void DetectPieces()
        {
            var found = FreePieceSet.Detect(_image, Math.Max(2, Math.Min(_settings.TileWidth, _settings.TileHeight) / 4));
            int added = 0;
            foreach (var r in found)
            {
                // Skip what is already cut and plain grid tiles (the grid already has them).
                if (_set.Pieces.Any(p => Overlaps(p.Source, r))) continue;
                if (r.Width <= _settings.TileWidth && r.Height <= _settings.TileHeight && SameCell(r)) continue;
                if (AddPiece(r, quiet: true)) added++;
            }
            Redraw();
            RefreshList();
            if (added == 0) _shell.Info("No se encontraron objetos nuevos: los que hay ya están cortados o caben en un tile de la rejilla.");
            else _shell.Success($"{added} piezas nuevas. Revísalas: quita las que sobren y ajusta las demás.");
        }

        private bool SameCell(PixelRect r)
        {
            int pw = _settings.TileWidth + _settings.SpacingX, ph = _settings.TileHeight + _settings.SpacingY;
            return (r.X - _settings.OffsetX) / pw == (r.Right - 1 - _settings.OffsetX) / pw
                   && (r.Y - _settings.OffsetY) / ph == (r.Bottom - 1 - _settings.OffsetY) / ph;
        }

        private static bool Overlaps(PixelRect a, PixelRect b) => a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;

        private void RefreshList()
        {
            _list.Clear();
            if (_set.Pieces.Count == 0)
                _list.Add(Ui.EmptyState("pieza", "Sin piezas libres",
                    "Arrastra un rectángulo sobre la imagen, o pulsa el dado para detectar los objetos sueltos.",
                    "Detectar objetos", DetectPieces));
            foreach (var p in _set.Pieces)
            {
                var piece = p;
                var row = Ui.Row(6).Pad(4, 2).Round();
                row.style.alignItems = Align.Center;
                if (p == _selected) row.style.backgroundColor = Ui.WithAlpha(Ui.C("acento"), 0.18f);
                row.RegisterCallback<ClickEvent>(_ => Select(piece));
                var text = Ui.Column();
                text.Grow();
                text.With(Ui.Text(p.Name), Ui.Hint(p.IsAutotile
                    ? $"Autotile {FormatNames[(int)p.Format]}" + (string.IsNullOrEmpty(p.ImagePath) ? "" : $" · {p.ImagePath}")
                    : $"{p.Source.Width} × {p.Source.Height} px · {Blocks(p.Source)}"));
                row.With(Ui.Swatch(ColorOf(p), 12), text, Ui.IconButton("papelera", () => Remove(piece), "Quitar la pieza"));
                _list.Add(row);
            }

            _details.Clear();
            if (_selected == null) return;
            var sel = _selected;
            _details.Add(Ui.SectionTitle("Pieza elegida"));
            _details.Add(Ui.TextBox("Nombre", sel.Name, v => { sel.Name = v; DrawPieces(); }, delayed: true));
            // Normal piece or autotile (its 47 pieces are built from it and join by themselves when painting).
            var kinds = Ui.Row(4).Wrap();
            for (int f = 0; f < FormatNames.Length; f++)
            {
                var format = (AutotileFormat)f;
                kinds.Add(Ui.Chip(f == 0 ? "Pieza" : "Autotile " + FormatNames[f], sel.Format == format, () => SetFormat(sel, format)));
            }
            _details.Add(kinds);
            var detected = AutotileLayout.Detect(sel.Source.Width, sel.Source.Height, out _);
            if (detected != AutotileFormat.None && sel.Format == AutotileFormat.None)
                _details.Add(Ui.Hint($"Por las medidas parece un autotile {FormatNames[(int)detected]}."));
            if (sel.IsAutotile)
            {
                _details.Add(Ui.Hint($"En la paleta: sus 47 piezas, desde el tile n.º {_set.FirstTile(sel, _columns, _gridRows)}. Pinta con cualquiera: los bordes salen solos."));
                return;
            }
            var r = sel.Source;
            var a = Ui.Row(6);
            a.With(Ui.NumberBox("X", r.X, 0, _image.Width - 1, v => Reshape(sel, new PixelRect(v, sel.Source.Y, sel.Source.Width, sel.Source.Height)), labelWidth: 20),
                   Ui.NumberBox("Y", r.Y, 0, _image.Height - 1, v => Reshape(sel, new PixelRect(sel.Source.X, v, sel.Source.Width, sel.Source.Height)), labelWidth: 20));
            var b = Ui.Row(6);
            b.With(Ui.NumberBox("An.", r.Width, 1, _image.Width, v => Reshape(sel, new PixelRect(sel.Source.X, sel.Source.Y, v, sel.Source.Height)), "Ancho en píxeles", 28),
                   Ui.NumberBox("Al.", r.Height, 1, _image.Height, v => Reshape(sel, new PixelRect(sel.Source.X, sel.Source.Y, sel.Source.Width, v)), "Alto en píxeles", 28));
            _details.With(a, b, Ui.Hint($"En la paleta: {Blocks(r)}, desde el tile n.º {_set.FirstTile(sel, _columns, _gridRows)}. La pieza se apoya abajo del bloque."));
        }

        private static readonly string[] FormatNames = { "pieza", "XP", "VX / MV", "47 piezas" };

        private void SetFormat(FreePiece piece, AutotileFormat format)
        {
            if (piece.Format == format) return;
            if (format != AutotileFormat.None)
            {
                if (_columns < AutotileLayout.BlockWidth) { _shell.Warn($"Un autotile necesita un tileset de al menos {AutotileLayout.BlockWidth} columnas."); return; }
                var problem = AutotileLayout.Problem(piece.Source, format);
                if (problem != null) { _shell.Warn(problem); return; }
            }
            var old = piece.Format;
            piece.Format = format;
            if (!_set.Reshape(piece, piece.Source, _settings, _columns)) { piece.Format = old; _shell.Warn("No cabe."); return; }
            if (format != AutotileFormat.None && piece.Name.StartsWith("Pieza ")) piece.Name = "Autotile " + (_set.Pieces.Count(p => p.IsAutotile));
            Redraw();
            RefreshList();
        }

        private void AutotileFromFile()
        {
            if (_columns < AutotileLayout.BlockWidth) { _shell.Warn($"Un autotile necesita un tileset de al menos {AutotileLayout.BlockWidth} columnas."); return; }
            FolderBrowser.PickFile(_shell, "Autotile (RPG Maker XP 3×4, VX/MV 2×3 o 47 piezas)", string.Join(",", ImageFile.ImportExtensions), file =>
            {
                try
                {
                    var img = ImageFile.Read(file);
                    var format = AutotileLayout.Detect(img.Width, img.Height, out _);
                    if (format == AutotileFormat.None)
                    {
                        _shell.Warn($"No se reconoce como autotile ({img.Width} × {img.Height}): XP mide 3 × 4 tiles, VX/MV 2 × 3 y el de 47 piezas 8 × 6.");
                        return;
                    }
                    var rel = ProjectLayout.Normalize(Path.GetRelativePath(_shell.ProjectRoot, file));
                    if (rel.StartsWith("..")) { _shell.Warn("La imagen tiene que estar dentro del proyecto (impórtala antes en Recursos)."); return; }
                    // XP animated autotiles repeat the frame to the right: the first frame is used.
                    int st = AutotileLayout.SourceTile(new PixelRect(0, 0, img.Width, img.Height), format);
                    int w = format == AutotileFormat.Xp ? st * 3 : img.Width;
                    var piece = _set.Add(Path.GetFileNameWithoutExtension(file), new PixelRect(0, 0, w, img.Height), _settings, _columns, _gridRows, format, rel);
                    Select(piece);
                    Redraw();
                    _shell.Success($"Autotile {FormatNames[(int)format]} añadido: «{piece.Name}».");
                }
                catch (Exception e) { _shell.Error("No se pudo leer el autotile: " + e.Message); }
            });
        }

        // ── Save ─────────────────────────────────────────────────────────────────────────────────

        private void Save()
        {
            try
            {
                var removed = _file.Free.Pieces.Count(o => _set.Pieces.All(p => p.Name != o.Name || p.Column != o.Column || p.Row != o.Row));
                _file.Free = _set;
                _file.SaveFor(_path);
                _shell.NotifyAssetsChanged();
                _shell.Success($"Piezas libres guardadas: {_set.Pieces.Count}."
                               + (removed > 0 ? " Si algún mapa usaba una pieza quitada o movida, esas casillas cambian: revisa la ventana Problemas." : ""));
                _shell.CloseDialog(_dialog);
            }
            catch (Exception e)
            {
                _shell.Error("No se pudieron guardar las piezas: " + e.Message);
            }
        }
    }
}
