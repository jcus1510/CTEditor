using System;
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
    /// Panel TILES: el tileset del mapa abierto como paleta y, como en la base de datos de RPG Maker XP, sus propiedades
    /// pintadas encima.
    ///   - Pintar: clic = un tile; arrastrar = un bloque de tiles (el sello del lápiz).
    ///   - Paso: clic en el centro = bloquear / desbloquear entero; clic cerca de un borde = bloquear solo ese lado.
    ///   - Prioridad: clic sube (0-5), clic derecho baja. Terreno: clic pone el elegido, clic derecho lo quita.
    ///   - Arbusto / Mostrador: clic activa o desactiva.
    /// Arrastrar aplica lo mismo a varios tiles. Todo se deshace con Ctrl+Z y se guarda en el «.corte.json» del tileset.
    /// </summary>
    public sealed class TilesPanel : VisualElement
    {
        private enum Mode { Paint, Passage, Priority, Terrain, Bush, Counter, Piece }

        private static readonly (Mode mode, string label, string help)[] Modes =
        {
            (Mode.Paint, "Pintar", "Elige el tile (o arrastra para un bloque) con el que pinta el lápiz."),
            (Mode.Passage, "Paso", "Por dónde se puede andar. Rojo = no se pasa. Clic en el centro: bloquear todo; clic junto a un borde: bloquear solo ese lado (vallas, bordillos)."),
            (Mode.Priority, "Prioridad", "Si el tile se dibuja encima del jugador: 0 = debajo (suelo); 1-5 = encima (copas de árbol, tejados, marcos de puerta). Clic sube, clic derecho baja."),
            (Mode.Terrain, "Terreno", "Qué es el suelo: hierba de encuentros, agua para surfear, saliente, hielo... Elige uno abajo; clic lo pone, clic derecho lo quita."),
            (Mode.Bush, "Arbusto", "El jugador se ve medio hundido al pisarlo, como en la hierba alta o un charco: la parte de abajo del personaje se vuelve transparente. Clic activa o desactiva."),
            (Mode.Counter, "Mostrador", "Se puede hablar a través de este tile con quien esté al otro lado, como la enfermera del Centro o el dependiente de la tienda. Clic activa o desactiva."),
            (Mode.Piece, "Pieza", "A qué capa va el tile con capas automáticas: verde = suelo, amarillo = detalle, azul = encima (claro = deducido solo). Clic cambia, clic derecho = automático."),
        };
        private static readonly string[] PieceNames = { "automático", "suelo", "detalle", "encima" };

        private readonly AppShell _shell;
        private MapEditorSession S => _shell.Maps;
        private readonly VisualElement _header, _modes, _terrainRow, _canvas, _marks, _selection, _hoverBox;
        private readonly ScrollView _scroll;
        private readonly Label _info;
        private TilesetAtlas _atlas;
        private Texture2D _markTexture;
        private Mode _mode = Mode.Paint;
        private int _terrain = 2;
        private float _zoom = 1f;

        // Drag state.
        private (int c, int r)? _dragStart;
        private (int c, int r) _dragNow;
        private readonly HashSet<int> _dragTiles = new HashSet<int>();
        private Action<TileProperties> _dragChange;
        private string _dragLabel;
        private readonly List<VisualElement> _dragMarks = new List<VisualElement>();
        private (int c, int r, int w, int h)? _stampRect;

        public TilesPanel(AppShell shell)
        {
            _shell = shell;
            style.flexGrow = 1;

            _header = Ui.Row(0).Pad(6, 4);
            _header.style.flexShrink = 0;
            _header.style.flexWrap = Wrap.Wrap;
            Add(_header);
            _modes = Ui.Row(0).Pad(6, 2);
            _modes.style.flexShrink = 0;
            _modes.style.flexWrap = Wrap.Wrap;
            Add(_modes);
            _terrainRow = Ui.Row(4).Pad(8, 2);
            _terrainRow.style.flexShrink = 0;
            _terrainRow.style.flexWrap = Wrap.Wrap;
            Add(_terrainRow);
            Add(Ui.Separator());

            _scroll = Ui.Scroll(ScrollViewMode.VerticalAndHorizontal).Grow();
            _scroll.Bg("fondo");
            _canvas = new VisualElement();
            _canvas.style.flexShrink = 0;
            _marks = new VisualElement { pickingMode = PickingMode.Ignore };
            _selection = new VisualElement { pickingMode = PickingMode.Ignore };
            _hoverBox = new VisualElement { pickingMode = PickingMode.Ignore };
            _scroll.Add(_canvas);
            Add(_scroll);

            _info = Ui.Hint("");
            _info.Pad(8, 4);
            _info.style.flexShrink = 0;
            Add(_info);

            _canvas.RegisterCallback<PointerDownEvent>(OnDown);
            _canvas.RegisterCallback<PointerMoveEvent>(OnMove);
            _canvas.RegisterCallback<PointerUpEvent>(OnUp);
            _canvas.RegisterCallback<PointerLeaveEvent>(_ => _hoverBox.Show(false));

            RegisterCallback<AttachToPanelEvent>(_ => Attach());
            RegisterCallback<DetachFromPanelEvent>(_ => Detach());
        }

        private Tileset Tileset => _atlas?.Tileset;

        private void Attach()
        {
            if (S == null) return;
            S.MapOpened += Reload;
            S.StructureChanged += OnStructure;
            S.TilesetChanged += OnTilesetChanged;
            S.SelectionChanged += DrawSelection;
            _shell.ActionRequested += OnAction;
            Reload();
        }

        private void Detach()
        {
            if (S != null)
            {
                S.MapOpened -= Reload;
                S.StructureChanged -= OnStructure;
                S.TilesetChanged -= OnTilesetChanged;
                S.SelectionChanged -= DrawSelection;
            }
            _shell.ActionRequested -= OnAction;
            _atlas?.Dispose();
            _atlas = null;
            if (_markTexture != null) UnityEngine.Object.Destroy(_markTexture);
            _markTexture = null;
        }

        private void OnStructure()
        {
            if (S?.Tileset?.Id != Tileset?.Id) Reload();
            else BuildHeader();
        }

        /// <summary>Another tileset tab, a new image (id null) → reload; only its properties changed → redraw the marks.</summary>
        private void OnTilesetChanged(string id)
        {
            if (id == null || S?.Tileset?.Id != Tileset?.Id) Reload();
            else { BuildHeader(); DrawMarks(); }
        }

        /// <summary>Loads the map's tileset (or keeps the canvas empty with an explanation).</summary>
        private void Reload()
        {
            _atlas?.Dispose();
            _atlas = S?.Map == null ? null : TilesetAtlas.TryLoad(S.Tileset, _shell.ProjectRoot);
            _stampRect = null;
            BuildHeader();
            BuildCanvas();
        }

        private void BuildHeader()
        {
            _header.Clear();
            _modes.Clear();
            _terrainRow.Clear();
            if (S?.Map == null) { _header.Add(Ui.Hint("Abre un mapa para ver sus tiles.")); return; }
            // One small numbered tab per tileset of the map (a map can mix several; the name is in the tooltip and next
            // to the tabs) and «+» to add another one.
            for (int i = 0; i < S.Map.TilesetIds.Count; i++)
            {
                int slot = i;
                var ts = S.TilesetFor(S.Map.TilesetIds[i]);
                var tab = Ui.Chip((slot + 1).ToString(), slot == S.PaletteSlot,
                    () => S.UsePaletteTileset(S.Map.TilesetIds[slot]), $"Tileset {slot + 1}: {ts?.Name ?? S.Map.TilesetIds[slot] + " (falta)"}");
                tab.style.minWidth = Ui.ControlHeight;
                tab.style.paddingLeft = 6; tab.style.paddingRight = 6;
                _header.Add(tab.Margin(0, 0, 3, 0));
            }
            var add = Ui.IconButton("mas", null, "Usar otro tileset en este mapa");
            add.clicked += () =>
            {
                var items = S.AvailableTilesets().Where(id => !S.Map.TilesetIds.Contains(id))
                    .Select(id => new MenuItem(S.TilesetFor(id)?.Name ?? id, () => S.UsePaletteTileset(id))).ToList();
                if (items.Count == 0) items.Add(new MenuItem("No hay más tilesets cortados (Recursos → Cortar)", null, enabled: false));
                var r = add.worldBound;
                _shell.ShowMenu(new Vector2(r.x, r.yMax + 2), items);
            };
            _header.Add(add);
            if (Tileset == null)
            {
                _header.Add(Ui.Hint("Este mapa aún no tiene un tileset cortado: córtalo en Recursos y añádelo con «+»."));
                return;
            }
            // The name, discreet: small, dim, cut with «…».
            var name = Ui.Text(Tileset.Name, 0.85f, dim: true).Grow();
            name.tooltip = $"{Tileset.Name} · {Tileset.Columns} × {Tileset.Rows} tiles de {Tileset.TileWidth} px";
            name.style.marginLeft = 6;
            var zoom = Ui.Text(Mathf.RoundToInt(_zoom * 100) + " %", 0.85f, dim: true).NoShrink();
            zoom.style.minWidth = Ui.FontSize * 2.8f;
            zoom.style.unityTextAlign = TextAnchor.MiddleCenter;
            _header.With(name,
                Ui.IconButton("menos", () => SetZoom(Step(-1)), "Alejar la paleta"),
                zoom,
                Ui.IconButton("mas", () => SetZoom(Step(+1)), "Acercar la paleta"),
                Ui.IconButton("retocar", RetouchSelected, "Retocar: abrir el tile elegido en el editor de píxeles"));
            foreach (var (mode, label, help) in Modes)
            {
                var m = mode;
                var b = Ui.IconButton(IconOf(m), () => SetMode(m), label + ": " + help, _mode == m).Margin(0, 0, 2, 0);
                // The help also appears in the line under the tiles while the mouse is over the button.
                var text = label + ": " + help;
                b.RegisterCallback<PointerEnterEvent>(_ => _info.text = text);
                b.RegisterCallback<PointerLeaveEvent>(_ => _info.text = Modes.First(x => x.mode == _mode).help);
                _modes.Add(b);
            }
            _modes.Add(Ui.Text(Modes.First(x => x.mode == _mode).label, 0.9f, bold: true).Margin(6, 0, 0, 0));
            if (_mode == Mode.Terrain)
            {
                foreach (var t in S.Terrains.All.Where(t => t.Id != 0))
                {
                    var id = t.Id;
                    // Small chips: a colour dot and a short text (the full name in the tooltip).
                    var chip = Ui.Chip(t.Label, _terrain == id, () => { _terrain = id; BuildHeader(); }, $"{t.Label} (n.º {t.Id})");
                    chip.style.fontSize = Mathf.Round(Ui.FontSize * 0.82f);
                    chip.style.height = Ui.ControlHeight - 9;
                    chip.style.minHeight = Ui.ControlHeight - 9;
                    chip.style.paddingLeft = 6; chip.style.paddingRight = 7;
                    chip.style.maxWidth = 150;
                    chip.style.borderLeftWidth = 4;
                    chip.style.borderLeftColor = TerrainColor(id);
                    _terrainRow.Add(chip.Margin(0, 0, 3, 3));
                }
            }
            _info.text = Modes.First(x => x.mode == _mode).help;
        }

        /// <summary>Keys 1-7: the modes (paint, passage, priority, terrain, bush, counter, piece).</summary>
        private void OnAction(string id)
        {
            Mode? m = id switch
            {
                "modo_pintar" => Mode.Paint,
                "modo_paso" => Mode.Passage,
                "modo_prioridad" => Mode.Priority,
                "modo_terreno" => Mode.Terrain,
                "modo_arbusto" => Mode.Bush,
                "modo_mostrador" => Mode.Counter,
                "modo_pieza" => Mode.Piece,
                _ => null,
            };
            if (m.HasValue && Tileset != null) SetMode(m.Value);
        }

        private static readonly float[] ZoomSteps = { 0.5f, 1f, 1.5f, 2f, 3f, 4f };

        private float Step(int dir)
        {
            int i = Array.FindIndex(ZoomSteps, z => Mathf.Approximately(z, _zoom));
            if (i < 0) i = 1;
            return ZoomSteps[Mathf.Clamp(i + dir, 0, ZoomSteps.Length - 1)];
        }

        private static string IconOf(Mode m) => m switch
        {
            Mode.Paint => "lapiz",
            Mode.Passage => "paso",
            Mode.Priority => "prioridad",
            Mode.Terrain => "terreno",
            Mode.Bush => "arbusto",
            Mode.Counter => "mostrador",
            _ => "pieza",
        };

        private void SetMode(Mode m)
        {
            _mode = m;
            BuildHeader();
            DrawMarks();
            DrawSelection();
        }

        private void SetZoom(float z)
        {
            _zoom = z;
            BuildHeader();
            BuildCanvas();
        }

        private void BuildCanvas()
        {
            _canvas.Clear();
            if (Tileset == null) { _canvas.style.width = 0; _canvas.style.height = 0; return; }
            float scale = _zoom;
            _canvas.style.width = _atlas.Image.Width * scale;
            _canvas.style.height = _atlas.Image.Height * scale;
            foreach (var (texture, y, height) in _atlas.Strips)
            {
                var img = new Image { image = texture, scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
                img.Absolute(0, y * scale, _atlas.Image.Width * scale, height * scale);
                _canvas.Add(img);
            }
            _canvas.Add(_marks);
            _canvas.Add(_selection);
            _canvas.Add(_hoverBox);
            _hoverBox.Show(false);
            DrawMarks();
            DrawSelection();
        }

        // ── Drawing ──────────────────────────────────────────────────────────────────────────────

        private Rect CellRect(int c, int r)
        {
            var rect = Tileset.Slice.CellRect(c, r);
            return new Rect(rect.X * _zoom, rect.Y * _zoom, rect.Width * _zoom, rect.Height * _zoom);
        }

        private static Color TerrainColor(int id)
        {
            float hue = (id * 0.618034f) % 1f;
            var c = Color.HSVToRGB(hue, 0.75f, 0.95f);
            c.a = 0.55f;
            return c;
        }

        /// <summary>The properties of every tile as a small texture (3 × 3 texels per tile) stretched over the tileset.</summary>
        private void DrawMarks()
        {
            _marks.Clear();
            if (_markTexture != null) UnityEngine.Object.Destroy(_markTexture);
            _markTexture = null;
            if (Tileset == null || _mode == Mode.Paint) return;
            int cols = Tileset.Columns, rows = Tileset.Rows;
            var px = new Color32[cols * 3 * rows * 3];
            var red = new Color32(230, 60, 60, 210);
            var free = new Color32(90, 210, 120, 110);
            void Set(int c, int r, int dx, int dy, Color32 color) => px[((rows - 1 - r) * 3 + (2 - dy)) * cols * 3 + c * 3 + dx] = color;
            for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var p = Tileset.Properties(r * cols + c);
                switch (_mode)
                {
                    case Mode.Passage:
                        if (p.Blocked == PassageBlock.All) { for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++) Set(c, r, x, y, red); break; }
                        Set(c, r, 1, 1, p.Blocked == PassageBlock.None ? free : new Color32(0, 0, 0, 0));
                        if ((p.Blocked & PassageBlock.Up) != 0) Set(c, r, 1, 0, red);
                        if ((p.Blocked & PassageBlock.Down) != 0) Set(c, r, 1, 2, red);
                        if ((p.Blocked & PassageBlock.Left) != 0) Set(c, r, 0, 1, red);
                        if ((p.Blocked & PassageBlock.Right) != 0) Set(c, r, 2, 1, red);
                        break;
                    case Mode.Priority:
                        if (p.Priority > 0)
                        {
                            var col = (Color32)new Color(0.3f, 0.5f, 1f, 0.15f + p.Priority * 0.14f);
                            for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++) Set(c, r, x, y, col);
                        }
                        break;
                    case Mode.Terrain:
                        if (p.TerrainTag != 0) { var col = (Color32)TerrainColor(p.TerrainTag); for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++) Set(c, r, x, y, col); }
                        break;
                    case Mode.Bush:
                        if (p.Bush) for (int y = 1; y < 3; y++) for (int x = 0; x < 3; x++) Set(c, r, x, y, new Color32(60, 200, 90, 150));
                        break;
                    case Mode.Counter:
                        if (p.Counter) for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++) Set(c, r, x, y, new Color32(80, 140, 255, 150));
                        break;
                    case Mode.Piece:
                    {
                        var piece = Tileset.PieceOf(r * cols + c);
                        byte a = (byte)(p.Piece == TilePiece.Auto ? 70 : 170);
                        var col = piece == TilePiece.Ground ? new Color32(80, 200, 110, a) : piece == TilePiece.Detail ? new Color32(240, 200, 70, a) : new Color32(80, 140, 255, a);
                        for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++) Set(c, r, x, y, col);
                        break;
                    }
                }
            }
            _markTexture = new Texture2D(cols * 3, rows * 3, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            _markTexture.SetPixels32(px);
            _markTexture.Apply(false, false);
            var s = Tileset.Slice;
            float w = cols * s.TileWidth + (cols - 1) * s.SpacingX, h = rows * s.TileHeight + (rows - 1) * s.SpacingY;
            var img = new Image { image = _markTexture, scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            img.Absolute(s.OffsetX * _zoom, s.OffsetY * _zoom, w * _zoom, h * _zoom);
            _marks.Add(img);
        }

        /// <summary>Frame around the tiles of the current stamp (paint mode).</summary>
        private void DrawSelection()
        {
            _selection.Clear();
            if (Tileset == null || _mode != Mode.Paint || S == null) return;
            var (c, r, w, h) = _stampRect ?? FindStampInTileset();
            if (w == 0) return;
            var a = CellRect(c, r);
            var b = CellRect(c + w - 1, r + h - 1);
            var box = new VisualElement { pickingMode = PickingMode.Ignore };
            box.Absolute(a.x, a.y, b.xMax - a.x, b.yMax - a.y);
            box.Border(2, "acento");
            box.style.backgroundColor = Ui.WithAlpha(Ui.C("acento"), 0.18f);
            _selection.Add(box);
        }

        /// <summary>When the stamp came from the map (picker), show it here if it is a plain block of this tileset.</summary>
        private (int, int, int, int) FindStampInTileset()
        {
            var st = S.Stamp;
            int first = st[0, 0];
            if (first < 0 || MapTile.Slot(first) != S.PaletteSlot || !Tileset.Contains(MapTile.Index(first))) return (0, 0, 0, 0);
            int idx = MapTile.Index(first);
            int c0 = idx % Tileset.Columns, r0 = idx / Tileset.Columns;
            for (int y = 0; y < st.Height; y++)
            for (int x = 0; x < st.Width; x++)
                if (st[x, y] != MapTile.Encode(S.PaletteSlot, (r0 + y) * Tileset.Columns + c0 + x)) return (0, 0, 0, 0);
            return (c0, r0, st.Width, st.Height);
        }

        // ── Pointer ──────────────────────────────────────────────────────────────────────────────

        private bool CellAt(Vector2 local, out int c, out int r, out Vector2 inside)
        {
            c = r = 0;
            inside = Vector2.zero;
            if (Tileset == null) return false;
            var s = Tileset.Slice;
            float x = local.x / _zoom - s.OffsetX, y = local.y / _zoom - s.OffsetY;
            c = Mathf.FloorToInt(x / (s.TileWidth + s.SpacingX));
            r = Mathf.FloorToInt(y / (s.TileHeight + s.SpacingY));
            if (x < 0 || y < 0 || c >= Tileset.Columns || r >= Tileset.Rows) return false;
            inside = new Vector2((x - c * (s.TileWidth + s.SpacingX)) / s.TileWidth, (y - r * (s.TileHeight + s.SpacingY)) / s.TileHeight);
            return inside.x < 1 && inside.y < 1;
        }

        private void OnDown(PointerDownEvent e)
        {
            if (!CellAt(e.localPosition, out int c, out int r, out var inside)) return;
            _shell.ActiveEditor = "mapa";
            _canvas.CapturePointer(e.pointerId);
            _dragStart = (c, r);
            _dragNow = (c, r);
            _dragTiles.Clear();
            ClearDragMarks();
            if (_mode == Mode.Paint) { _stampRect = (c, r, 1, 1); DrawSelection(); return; }

            // The first tile decides the value; the rest of the drag gets the same.
            var p = Tileset.Properties(r * Tileset.Columns + c);
            bool right = e.button == 1;
            switch (_mode)
            {
                case Mode.Passage:
                {
                    var side = EdgeOf(inside);
                    if (right) { _dragChange = x => x.Blocked = PassageBlock.None; _dragLabel = "desbloquear"; }
                    else if (side == PassageBlock.None)
                    {
                        var v = p.Blocked == PassageBlock.All ? PassageBlock.None : PassageBlock.All;
                        _dragChange = x => x.Blocked = v;
                        _dragLabel = v == PassageBlock.All ? "bloquear" : "desbloquear";
                    }
                    else
                    {
                        bool on = (p.Blocked & side) == 0;
                        _dragChange = x => x.Blocked = on ? (x.Blocked == PassageBlock.All ? PassageBlock.All : x.Blocked | side) : (x.Blocked & ~side);
                        _dragLabel = on ? "bloquear un lado" : "desbloquear un lado";
                    }
                    break;
                }
                case Mode.Priority:
                {
                    int v = right ? (p.Priority + 5) % 6 : (p.Priority + 1) % 6;
                    _dragChange = x => x.Priority = v;
                    _dragLabel = "prioridad " + v;
                    break;
                }
                case Mode.Terrain:
                {
                    int v = right ? 0 : _terrain;
                    _dragChange = x => x.TerrainTag = v;
                    _dragLabel = "terreno";
                    break;
                }
                case Mode.Bush:
                {
                    bool v = right ? false : !p.Bush;
                    _dragChange = x => x.Bush = v;
                    _dragLabel = "arbusto";
                    break;
                }
                case Mode.Counter:
                {
                    bool v = right ? false : !p.Counter;
                    _dragChange = x => x.Counter = v;
                    _dragLabel = "mostrador";
                    break;
                }
                case Mode.Piece:
                {
                    var v = right ? TilePiece.Auto : (TilePiece)(((int)Tileset.PieceOf(r * Tileset.Columns + c)) % 3 + 1);
                    _dragChange = x => x.Piece = v;
                    _dragLabel = "pieza: " + PieceNames[(int)v];
                    break;
                }
            }
            AddDragTile(c, r);
        }

        private static PassageBlock EdgeOf(Vector2 inside)
        {
            const float band = 0.28f;
            float dl = inside.x, dr = 1 - inside.x, dt = inside.y, db = 1 - inside.y;
            float min = Mathf.Min(Mathf.Min(dl, dr), Mathf.Min(dt, db));
            if (min > band) return PassageBlock.None;
            if (min == dt) return PassageBlock.Up;
            if (min == db) return PassageBlock.Down;
            if (min == dl) return PassageBlock.Left;
            return PassageBlock.Right;
        }

        private void AddDragTile(int c, int r)
        {
            if (!_dragTiles.Add(r * Tileset.Columns + c)) return;
            var rect = CellRect(c, r);
            var m = new VisualElement { pickingMode = PickingMode.Ignore };
            m.Absolute(rect.x, rect.y, rect.width, rect.height);
            m.Border(2, "aviso");
            _selection.Add(m);
            _dragMarks.Add(m);
        }

        private void ClearDragMarks()
        {
            foreach (var m in _dragMarks) m.RemoveFromHierarchy();
            _dragMarks.Clear();
        }

        private void OnMove(PointerMoveEvent e)
        {
            bool ok = CellAt(e.localPosition, out int c, out int r, out _);
            if (ok)
            {
                var rect = CellRect(c, r);
                _hoverBox.Absolute(rect.x, rect.y, rect.width, rect.height);
                _hoverBox.Border(1, "texto");
                _hoverBox.Show(true);
                ShowTileInfo(r * Tileset.Columns + c);
            }
            if (!_dragStart.HasValue || !_canvas.HasPointerCapture(e.pointerId) || !ok || (c, r) == _dragNow) return;
            _dragNow = (c, r);
            if (_mode == Mode.Paint)
            {
                var (c0, r0) = _dragStart.Value;
                _stampRect = (Mathf.Min(c0, c), Mathf.Min(r0, r), Mathf.Abs(c - c0) + 1, Mathf.Abs(r - r0) + 1);
                DrawSelection();
            }
            else AddDragTile(c, r);
        }

        private void OnUp(PointerUpEvent e)
        {
            if (_canvas.HasPointerCapture(e.pointerId)) _canvas.ReleasePointer(e.pointerId);
            if (!_dragStart.HasValue || Tileset == null) return;
            _dragStart = null;
            if (_mode == Mode.Paint)
            {
                var (c, r, w, h) = _stampRect.Value;
                S.SetStamp(TileStamp.FromTileset(Tileset, c, r, w, h, S.PaletteSlot));
                return;
            }
            ClearDragMarks();
            if (_dragChange != null && _dragTiles.Count > 0)
                S.ChangeTileProperties(Tileset.Id, _dragTiles.ToList(), _dragChange, _dragLabel);
            _dragChange = null;
            DrawMarks();
        }

        private void ShowTileInfo(int tile)
        {
            var p = Tileset.Properties(tile);
            string pass = p.Blocked == PassageBlock.All ? "no se pasa" : p.Blocked == PassageBlock.None ? "se pasa" : "bloquea: " + string.Join(", ",
                new[] { (PassageBlock.Up, "arriba"), (PassageBlock.Down, "abajo"), (PassageBlock.Left, "izquierda"), (PassageBlock.Right, "derecha") }
                    .Where(x => (p.Blocked & x.Item1) != 0).Select(x => x.Item2));
            _info.text = $"Tile n.º {tile} · {pass} · prioridad {p.Priority} · terreno: {S.Terrains.LabelOf(p.TerrainTag)}"
                         + (p.Bush ? " · arbusto" : "") + (p.Counter ? " · mostrador" : "")
                         + $" · pieza: {PieceNames[(int)Tileset.PieceOf(tile)]}" + (p.Piece == TilePiece.Auto ? " (deducida)" : "");
        }

        private void RetouchSelected()
        {
            if (Tileset == null) return;
            var (c, r, _, _) = _stampRect ?? FindStampInTileset();
            var rect = Tileset.Slice.CellRect(c, r);
            _shell.OpenRetouch(Path.Combine(_shell.ProjectRoot, Tileset.ImagePath), rect, Tileset.TileWidth, Tileset.TileHeight);
        }
    }
}
