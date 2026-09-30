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
    /// ASISTENTE DE CORTE: la imagen con la rejilla encima y, al lado, el tamaño del tile, el desplazamiento, la
    /// separación, las sugerencias y el resumen (columnas, filas, vacíos, repetidos). Para personajes, la plantilla (RPG
    /// Maker XP 4×4 o VX/MV 3×4), el nombre de cada fila y una vista previa andando en las cuatro direcciones.
    /// Guarda «imagen.corte.json» conservando las propiedades de tile que ya tuviera.
    /// </summary>
    public sealed class SliceWizard
    {
        private static readonly string[] DirectionNames = { "Abajo", "Izquierda", "Derecha", "Arriba" };

        private readonly AppShell _shell;
        private readonly string _path;
        private readonly PixelImage _image;
        private readonly SliceFile _file;
        private readonly List<TextureStrip> _strips;
        private readonly AppShell.Dialog _dialog;

        private int _tw, _th, _ox, _oy, _sx, _sy;
        private bool _linked = true;
        private string _kind;
        private CharacterSheetLayout _layout;
        private float _zoom = 2f;
        private bool _showEmpty = true, _showDuplicates = true, _showGrid = true;
        private TileSheet _sheet;

        private ScrollView _preview;
        private VisualElement _canvas, _gridLayer, _markLayer, _labelLayer;
        private VisualElement _controls;
        private Label _hover;
        private Button _save;
        private Texture2D _markTexture;
        private readonly List<Texture2D> _frameTextures = new List<Texture2D>();
        private IVisualElementScheduledItem _animation;

        public static void Show(AppShell shell, string imagePath, AssetKind kind)
        {
            try
            {
                new SliceWizard(shell, imagePath, kind);
            }
            catch (Exception e)
            {
                shell.Error($"No se pudo abrir «{Path.GetFileName(imagePath)}»: {e.Message}");
            }
        }

        private SliceWizard(AppShell shell, string path, AssetKind kind)
        {
            _shell = shell;
            _path = path;
            _image = Png.Read(path);
            _strips = Textures.Strips(_image);
            _file = SliceFile.LoadFor(path);

            if (_file != null)
            {
                _kind = _file.Kind;
                var s = _file.Settings;
                (_tw, _th, _ox, _oy, _sx, _sy) = (s.TileWidth, s.TileHeight, s.OffsetX, s.OffsetY, s.SpacingX, s.SpacingY);
                _layout = CharacterSheetLayout.Presets.FirstOrDefault(l => l.Name == _file.CharacterLayout);
                _linked = _tw == _th;
            }
            else
            {
                _file = new SliceFile(null);
                _kind = kind == AssetKind.Character ? SliceFile.KindCharacter : kind == AssetKind.Tileset ? SliceFile.KindTileset : SliceFile.KindSprites;
                int size = shell.Project?.TileSize ?? ProjectSettings.DefaultTileSize;
                (_tw, _th) = (size, size);
                if (_kind == SliceFile.KindCharacter && (_layout = CharacterSheetLayout.Detect(_image.Width, _image.Height)) != null)
                {
                    var s = _layout.SliceFor(_image.Width, _image.Height);
                    (_tw, _th) = (s.TileWidth, s.TileHeight);
                    _linked = _tw == _th;
                }
                else
                {
                    var first = TileSizeSuggester.Suggest(_image.Width, _image.Height, size).FirstOrDefault();
                    if (first != null) (_tw, _th) = (first.Width, first.Height);
                }
            }
            _zoom = _image.Width * 2 <= 900 ? 2f : 1f;

            _dialog = shell.ShowDialog($"Cortar «{Path.GetFileName(path)}» ({_image.Width} × {_image.Height} px)", 94, 90);
            _dialog.OnClose = Cleanup;
            BuildLayout();
            Recompute();
        }

        private SliceSettings Settings => new SliceSettings(_tw, _th, _ox, _oy, _sx, _sy);

        // ── Layout ───────────────────────────────────────────────────────────────────────────────

        private void BuildLayout()
        {
            var main = Ui.Row(12).Grow();
            main.style.alignItems = Align.Stretch;

            _preview = Ui.Scroll(ScrollViewMode.VerticalAndHorizontal).Grow();
            _preview.Bg("fondo").Border(1, "borde", 4);
            _canvas = new VisualElement();
            _canvas.style.flexShrink = 0;
            // Checkerboard-ish backdrop so transparent pixels are visible.
            _canvas.style.backgroundColor = Ui.Mix(Ui.C("fondo"), Color.white, 0.06f);
            foreach (var strip in _strips)
            {
                var img = new Image { image = strip.Texture, scaleMode = ScaleMode.StretchToFill };
                img.pickingMode = PickingMode.Ignore;
                img.userData = strip;
                _canvas.Add(img);
            }
            _markLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            _gridLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            _labelLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            _canvas.Add(_markLayer);
            _canvas.Add(_gridLayer);
            _canvas.Add(_labelLayer);
            _canvas.RegisterCallback<PointerMoveEvent>(e => Hover(e.localPosition));
            _canvas.RegisterCallback<PointerLeaveEvent>(_ => _hover.text = "Pasa el ratón por la imagen para ver cada tile.");
            _canvas.RegisterCallback<WheelEvent>(e =>
            {
                if (!e.altKey) return;
                SetZoom(_zoom * (e.delta.y < 0 ? 1.25f : 0.8f));
                e.StopPropagation();
            });
            _preview.Add(_canvas);
            main.Add(_preview);

            var side = Ui.Scroll();
            side.style.width = 340;
            side.style.flexShrink = 0;
            _controls = Ui.Column(12).Pad(2, 0);
            side.Add(_controls);
            main.Add(side);

            _dialog.Body.Add(main);
            _hover = Ui.Hint("Pasa el ratón por la imagen para ver cada tile.");
            _hover.style.flexShrink = 0;
            _dialog.Body.Add(_hover);

            _save = Ui.Button("Guardar corte", Save, Ui.ButtonKind.Primary);
            _dialog.Buttons.With(Ui.Hint("Alt + rueda: zoom"), Ui.Spacer(), Ui.Button("Cancelar", () => _shell.CloseDialog(_dialog)), _save);
        }

        private void BuildControls()
        {
            _controls.Clear();

            // Kind.
            var kinds = Ui.Row(6);
            kinds.With(
                Ui.Chip("Tileset", _kind == SliceFile.KindTileset, () => SetKind(SliceFile.KindTileset)),
                Ui.Chip("Personaje", _kind == SliceFile.KindCharacter, () => SetKind(SliceFile.KindCharacter)),
                Ui.Chip("Sprites", _kind == SliceFile.KindSprites, () => SetKind(SliceFile.KindSprites)));
            _controls.With(Section("Qué es", kinds));

            if (_kind == SliceFile.KindCharacter)
            {
                var layouts = Ui.Column(6);
                foreach (var l in CharacterSheetLayout.Presets)
                {
                    var layout = l;
                    var fits = layout.SliceFor(_image.Width, _image.Height) != null;
                    var chip = Ui.Chip(layout.Name + (fits ? "" : " (no encaja)"), _layout == layout, () => UseLayout(layout),
                        "Filas: abajo, izquierda, derecha, arriba");
                    chip.style.alignSelf = Align.FlexStart;
                    layouts.Add(chip);
                }
                layouts.Add(Ui.Hint("Cada fila es una dirección y cada columna un paso."));
                _controls.Add(Section("Plantilla de hoja", layouts));
                _controls.Add(Section("Andando", WalkPreview()));
            }

            // Size.
            var size = Ui.Column(6);
            size.With(
                Ui.NumberBox("Ancho", _tw, 1, _image.Width, v => { _tw = v; if (_linked) _th = v; Changed(); }),
                Ui.NumberBox("Alto", _th, 1, _image.Height, v => { _th = v; if (_linked) _tw = v; Changed(); }),
                Ui.Check("Cuadrado (ancho = alto)", _linked, v => { _linked = v; if (v) { _th = _tw; Changed(); } }));
            _controls.Add(Section("Tamaño del tile", size));

            var suggestions = Ui.Row(6);
            suggestions.style.flexWrap = Wrap.Wrap;
            foreach (var s in TileSizeSuggester.Suggest(_image.Width, _image.Height, _shell.Project?.TileSize ?? 0))
            {
                var sug = s;
                var chip = Ui.Chip($"{s.Width} × {s.Height}", _tw == s.Width && _th == s.Height, () =>
                {
                    (_tw, _th) = (sug.Width, sug.Height);
                    Changed();
                }, s.Reason);
                chip.Margin(0, 0, 6, 6);
                suggestions.Add(chip);
            }
            if (suggestions.childCount == 0) suggestions.Add(Ui.Hint("Ningún tamaño habitual encaja exacto."));
            _controls.Add(Section("Sugerencias", suggestions));

            var offsets = Ui.Column(6);
            offsets.With(
                Ui.NumberBox("Desplaz. X", _ox, 0, _image.Width - 1, v => { _ox = v; Changed(); }, "Margen del borde izquierdo"),
                Ui.NumberBox("Desplaz. Y", _oy, 0, _image.Height - 1, v => { _oy = v; Changed(); }, "Margen del borde de arriba"),
                Ui.NumberBox("Separación X", _sx, 0, 256, v => { _sx = v; Changed(); }, "Píxeles entre tiles"),
                Ui.NumberBox("Separación Y", _sy, 0, 256, v => { _sy = v; Changed(); }, "Píxeles entre tiles"));
            _controls.Add(Section("Margen y separación", offsets));

            var view = Ui.Column(6);
            var zooms = Ui.Row(4);
            zooms.style.flexWrap = Wrap.Wrap;
            foreach (var z in new[] { 0.5f, 1f, 2f, 3f, 4f })
            {
                var zoom = z;
                zooms.Add(Ui.Chip(Mathf.RoundToInt(z * 100) + " %", Mathf.Approximately(_zoom, z), () => SetZoom(zoom)).Margin(0, 0, 4, 4));
            }
            zooms.Add(Ui.Chip("Ajustar", false, FitZoom).Margin(0, 0, 4, 4));
            view.With(zooms,
                Ui.Check("Rejilla", _showGrid, v => { _showGrid = v; Redraw(); }),
                Ui.Check("Marcar tiles vacíos (azul)", _showEmpty, v => { _showEmpty = v; Redraw(); }),
                Ui.Check("Marcar tiles repetidos (naranja)", _showDuplicates, v => { _showDuplicates = v; Redraw(); }));
            _controls.Add(Section("Vista", view));

            _controls.Add(Section("Resumen", Summary()));
        }

        private static VisualElement Section(string title, VisualElement content)
        {
            var box = Ui.Column(6).Bg("panel_alt").Border(1, "borde", 6).Pad(10);
            box.Add(Ui.Text(title, 0.95f, bold: true));
            box.Add(content);
            return box;
        }

        private VisualElement Summary()
        {
            var box = Ui.Column(4);
            if (!_sheet.Ok)
            {
                foreach (var p in _sheet.Problems) box.Add(Ui.Text(p, wrap: true).Colored("error"));
                return box;
            }
            box.Add(Ui.Text($"{_sheet.Columns} columnas × {_sheet.Rows} filas = {_sheet.Cells.Count} {(_kind == SliceFile.KindCharacter ? "fotogramas" : "tiles")}"));
            box.Add(Ui.Text($"Vacíos: {_sheet.EmptyCount} (no salen en la paleta)", dim: _sheet.EmptyCount == 0));
            box.Add(Ui.Text($"Repetidos: {_sheet.DuplicateCount} (se ven igual que otro)", dim: _sheet.DuplicateCount == 0));
            box.Add(Ui.Text($"Distintos: {_sheet.UniqueCount}", bold: true).Colored("exito"));
            foreach (var w in _sheet.Warnings) box.Add(Ui.Text(w, wrap: true).Colored("aviso"));
            int projectTile = _shell.Project?.TileSize ?? 0;
            if (_kind == SliceFile.KindTileset && projectTile > 0 && (_tw != projectTile || _th != projectTile))
                box.Add(Ui.Text($"Ojo: el proyecto usa tiles de {projectTile} px y este corte es de {_tw} × {_th}.", wrap: true).Colored("aviso"));
            if (_kind == SliceFile.KindCharacter && _layout != null && (_sheet.Columns != _layout.Columns || _sheet.Rows != _layout.Rows))
                box.Add(Ui.Text($"La plantilla espera {_layout.Columns} × {_layout.Rows} y el corte da {_sheet.Columns} × {_sheet.Rows}.", wrap: true).Colored("aviso"));
            return box;
        }

        // ── Changes ──────────────────────────────────────────────────────────────────────────────

        private void SetKind(string kind)
        {
            _kind = kind;
            if (kind == SliceFile.KindCharacter && _layout == null)
            {
                var detected = CharacterSheetLayout.Detect(_image.Width, _image.Height);
                if (detected != null) { UseLayout(detected); return; }
            }
            Changed();
        }

        private void UseLayout(CharacterSheetLayout layout)
        {
            _layout = layout;
            var s = layout.SliceFor(_image.Width, _image.Height);
            if (s == null)
            {
                _shell.Warn($"La imagen ({_image.Width} × {_image.Height}) no se reparte exacta en {layout.Columns} × {layout.Rows}.");
            }
            else
            {
                (_tw, _th, _ox, _oy, _sx, _sy) = (s.TileWidth, s.TileHeight, 0, 0, 0, 0);
                _linked = _tw == _th;
            }
            Changed();
        }

        private void Changed() => Recompute();

        private void Recompute()
        {
            _sheet = TileSlicer.Slice(_image, Settings);
            BuildControls();
            Redraw();
            _save.SetEnabledLook(_sheet.Ok);
        }

        private void SetZoom(float zoom)
        {
            _zoom = Mathf.Clamp(zoom, 0.25f, 8f);
            BuildControls();
            Redraw();
        }

        private void FitZoom()
        {
            float w = _preview.contentViewport.layout.width - 8;
            if (w <= 0) return;
            SetZoom(Mathf.Max(0.25f, Mathf.Floor(w / _image.Width * 4f) / 4f));
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
            foreach (var layer in new[] { _markLayer, _gridLayer, _labelLayer })
            {
                layer.Clear();
                layer.Absolute(0, 0, _image.Width * _zoom, _image.Height * _zoom);
            }
            if (!_sheet.Ok) return;
            DrawMarks();
            if (_showGrid) DrawGrid();
            if (_kind == SliceFile.KindCharacter) DrawRowLabels();
        }

        private void DrawMarks()
        {
            if (_markTexture != null) UnityEngine.Object.Destroy(_markTexture);
            _markTexture = null;
            if (!_showEmpty && !_showDuplicates) return;
            int cols = _sheet.Columns, rows = _sheet.Rows;
            var px = new Color32[cols * rows];
            var empty = Ui.WithAlpha(new Color(0.25f, 0.55f, 1f), 0.38f);
            var dup = Ui.WithAlpha(new Color(1f, 0.6f, 0.15f), 0.38f);
            bool any = false;
            foreach (var cell in _sheet.Cells)
            {
                Color c = new Color(0, 0, 0, 0);
                if (cell.IsEmpty && _showEmpty) c = empty;
                else if (cell.IsDuplicate && _showDuplicates) c = dup;
                if (c.a > 0) any = true;
                px[(rows - 1 - cell.Row) * cols + cell.Column] = c;
            }
            if (!any) return;
            _markTexture = new Texture2D(cols, rows, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            _markTexture.SetPixels32(px);
            _markTexture.Apply(false, false);
            var s = Settings;
            float w = cols * s.TileWidth + (cols - 1) * s.SpacingX, h = rows * s.TileHeight + (rows - 1) * s.SpacingY;
            var img = new Image { image = _markTexture, scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            img.Absolute(s.OffsetX * _zoom, s.OffsetY * _zoom, w * _zoom, h * _zoom);
            _markLayer.Add(img);
        }

        private void DrawGrid()
        {
            var s = Settings;
            var color = Ui.C("rejilla");
            float fullW = _image.Width * _zoom, fullH = _image.Height * _zoom;
            var xs = new SortedSet<float>();
            var ys = new SortedSet<float>();
            for (int c = 0; c < _sheet.Columns; c++)
            {
                float x = s.OffsetX + c * (s.TileWidth + s.SpacingX);
                xs.Add(x);
                xs.Add(x + s.TileWidth);
            }
            for (int r = 0; r < _sheet.Rows; r++)
            {
                float y = s.OffsetY + r * (s.TileHeight + s.SpacingY);
                ys.Add(y);
                ys.Add(y + s.TileHeight);
            }
            // Very tall sheets: skip row lines that would be closer than 3 px on screen.
            float minGap = 3f;
            float lastY = float.MinValue;
            foreach (var y in ys)
            {
                float py = y * _zoom;
                if (py - lastY < minGap) continue;
                lastY = py;
                var line = new VisualElement { pickingMode = PickingMode.Ignore };
                line.Absolute(0, Mathf.Min(py, fullH - 1), fullW, 1);
                line.style.backgroundColor = color;
                _gridLayer.Add(line);
            }
            float lastX = float.MinValue;
            foreach (var x in xs)
            {
                float px = x * _zoom;
                if (px - lastX < minGap) continue;
                lastX = px;
                var line = new VisualElement { pickingMode = PickingMode.Ignore };
                line.Absolute(Mathf.Min(px, fullW - 1), 0, 1, fullH);
                line.style.backgroundColor = color;
                _gridLayer.Add(line);
            }
        }

        private void DrawRowLabels()
        {
            if (_layout == null) return;
            var s = Settings;
            for (int r = 0; r < Math.Min(_sheet.Rows, _layout.Rows); r++)
            {
                var tag = Ui.Text(DirectionNames[(int)_layout.RowDirections[r]], 0.85f, bold: true);
                tag.style.color = Color.white;
                tag.style.backgroundColor = new Color(0, 0, 0, 0.6f);
                tag.Pad(4, 1).Round(3);
                tag.style.position = Position.Absolute;
                tag.style.left = 2;
                tag.style.top = (s.OffsetY + r * (s.TileHeight + s.SpacingY)) * _zoom + 2;
                _labelLayer.Add(tag);
            }
        }

        private VisualElement WalkPreview()
        {
            _animation?.Pause();
            foreach (var t in _frameTextures) UnityEngine.Object.Destroy(t);
            _frameTextures.Clear();
            var row = Ui.Row(8);
            if (_layout == null || _layout.SliceFor(_image.Width, _image.Height) == null)
            {
                row.Add(Ui.Hint("Elige una plantilla que encaje para ver la animación."));
                return row;
            }
            var views = new List<(Image view, List<Texture2D> frames)>();
            foreach (FacingDirection dir in Enum.GetValues(typeof(FacingDirection)))
            {
                var frames = new List<Texture2D>();
                for (int step = 0; step < Math.Max(1, _layout.WalkCycle.Count); step++)
                {
                    var rect = _layout.FrameRect(_image.Width, _image.Height, dir, step);
                    if (rect.Width == 0) continue;
                    var tex = Textures.FromImage(_image.Crop(rect));
                    _frameTextures.Add(tex);
                    frames.Add(tex);
                }
                if (frames.Count == 0) continue;
                var box = Ui.Column(2);
                box.style.alignItems = Align.Center;
                var view = new Image { image = frames[0], scaleMode = ScaleMode.ScaleToFit };
                float scale = Mathf.Max(1f, Mathf.Floor(64f / Mathf.Max(frames[0].width, frames[0].height)));
                view.style.width = frames[0].width * scale;
                view.style.height = frames[0].height * scale;
                box.With(view, Ui.Text(DirectionNames[(int)dir], 0.8f, dim: true));
                row.Add(box);
                views.Add((view, frames));
            }
            int tick = 0;
            _animation = row.schedule.Execute(() =>
            {
                tick++;
                foreach (var (view, frames) in views) view.image = frames[tick % frames.Count];
            }).Every(160);
            return row;
        }

        private void Hover(Vector2 local)
        {
            if (!_sheet.Ok) return;
            var s = Settings;
            float x = local.x / _zoom - s.OffsetX, y = local.y / _zoom - s.OffsetY;
            int col = Mathf.FloorToInt(x / (s.TileWidth + s.SpacingX)), row = Mathf.FloorToInt(y / (s.TileHeight + s.SpacingY));
            bool inside = x >= 0 && y >= 0 && col < _sheet.Columns && row < _sheet.Rows
                          && x - col * (s.TileWidth + s.SpacingX) < s.TileWidth && y - row * (s.TileHeight + s.SpacingY) < s.TileHeight;
            if (!inside)
            {
                _hover.text = $"Píxel ({Mathf.FloorToInt(local.x / _zoom)}, {Mathf.FloorToInt(local.y / _zoom)}): fuera de los tiles (margen o separación).";
                return;
            }
            var cell = _sheet.At(col, row);
            string state = cell.IsEmpty ? "vacío" : cell.IsDuplicate ? $"repetido del n.º {cell.DuplicateOf}" : "único";
            string what = _kind == SliceFile.KindCharacter && _layout != null && row < _layout.Rows
                ? $" · {DirectionNames[(int)_layout.RowDirections[row]]}, paso {col + 1}"
                : "";
            _hover.text = $"Tile n.º {cell.Index} · columna {col + 1}, fila {row + 1}{what} · {state}";
        }

        // ── Save / close ─────────────────────────────────────────────────────────────────────────

        private void Save()
        {
            if (!_sheet.Ok) return;
            try
            {
                _file.Settings = Settings;
                _file.Kind = _kind;
                _file.CharacterLayout = _kind == SliceFile.KindCharacter && _layout != null ? _layout.Name : "";
                _file.SaveFor(_path);
                _shell.NotifyAssetsChanged();
                _shell.Success($"Corte guardado: {Path.GetFileName(_path)} ({_sheet.Columns} × {_sheet.Rows}, {_sheet.UniqueCount} distintos).");
                _shell.CloseDialog(_dialog);
            }
            catch (Exception e)
            {
                _shell.Error("No se pudo guardar el corte: " + e.Message);
            }
        }

        private void Cleanup()
        {
            _animation?.Pause();
            Textures.Destroy(_strips);
            foreach (var t in _frameTextures) UnityEngine.Object.Destroy(t);
            _frameTextures.Clear();
            if (_markTexture != null) UnityEngine.Object.Destroy(_markTexture);
        }
    }
}
