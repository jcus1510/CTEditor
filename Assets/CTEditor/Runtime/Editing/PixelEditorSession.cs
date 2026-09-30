using System;
using System.Collections.Generic;
using System.Linq;
using CTEditor.Art.Domain;
using CTEditor.SharedKernel.Editing;

namespace CTEditor.Editing
{
    public enum PixelTool
    {
        Pencil = 0,
        Eraser = 1,
        Fill = 2,
        Line = 3,
        Rectangle = 4,
        FilledRectangle = 5,
        Picker = 6,
        /// <summary>Clic en un color: lo cambia por el color elegido en toda la imagen (o en el tile, en modo tile).</summary>
        Replace = 7
    }

    /// <summary>
    /// CASO DE USO «retocar una imagen» (editor de píxeles): la imagen abierta, herramienta, colores, grosor, modo tile,
    /// deshacer/rehacer y guardar. Sin Unity: el panel solo dibuja la imagen y reenvía el ratón. Al guardar, la carpeta
    /// del proyecto avisa del cambio y el mapa y el juego se actualizan solos.
    /// </summary>
    public sealed class PixelEditorSession
    {
        private readonly IImageRepository _images;
        private PixelStroke _stroke;
        private (int x, int y)? _dragStart;
        private List<PixelChange> _preview = new List<PixelChange>();

        public string Path { get; private set; }
        public PixelImage Image { get; private set; }
        public CommandHistory History { get; } = new CommandHistory(300);

        public PixelTool Tool { get; private set; } = PixelTool.Pencil;
        public Rgba32 Primary { get; private set; } = new Rgba32(0, 0, 0);
        public Rgba32 Secondary { get; private set; } = Rgba32.Transparent;
        public int BrushSize { get; private set; } = 1;
        /// <summary>Colores usados hace poco (el primero, el último).</summary>
        public List<Rgba32> Recent { get; } = new List<Rgba32>();

        /// <summary>Tamaño de la rejilla de tiles (del corte de la imagen; 0 = sin rejilla).</summary>
        public int TileWidth { get; set; }
        public int TileHeight { get; set; }
        /// <summary>Modo tile: el relleno y el reemplazo no salen de este tile (null = toda la imagen).</summary>
        public PixelRect? TileLimit { get; private set; }

        public bool IsDirty => History.IsDirty;

        public event Action ImageOpened;
        /// <summary>Cambiaron píxeles dentro de ese rectángulo.</summary>
        public event Action<PixelRect> PixelsChanged;
        public event Action SelectionChanged;
        public event Action DirtyChanged;
        public event Action<string, string> Message;

        public PixelEditorSession(IImageRepository images)
        {
            _images = images ?? throw new ArgumentNullException(nameof(images));
            History.Changed += () => DirtyChanged?.Invoke();
        }

        // ── Open / save ──────────────────────────────────────────────────────────────────────────

        public bool Open(string path, int tileWidth = 0, int tileHeight = 0)
        {
            try
            {
                Image = _images.Load(path);
            }
            catch (Exception e)
            {
                Message?.Invoke($"No se pudo abrir la imagen: {e.Message}", "error");
                return false;
            }
            Path = path;
            TileWidth = tileWidth;
            TileHeight = tileHeight;
            TileLimit = null;
            _stroke = null;
            _dragStart = null;
            _preview.Clear();
            History.Clear();
            ImageOpened?.Invoke();
            SelectionChanged?.Invoke();
            return true;
        }

        /// <summary>Opens (or keeps) an image and focuses one tile of it (tile mode).</summary>
        public bool OpenTile(string path, PixelRect tile, int tileWidth, int tileHeight)
        {
            if (Path != path && !Open(path, tileWidth, tileHeight)) return false;
            TileWidth = tileWidth;
            TileHeight = tileHeight;
            SetTileLimit(tile);
            return true;
        }

        public void Close()
        {
            Image = null;
            Path = null;
            History.Clear();
            ImageOpened?.Invoke();
        }

        public bool Save()
        {
            if (Image == null) return false;
            try
            {
                _images.Save(Path, Image);
                History.MarkSaved();
                Message?.Invoke("Imagen guardada.", "exito");
                return true;
            }
            catch (Exception e)
            {
                Message?.Invoke("No se pudo guardar la imagen: " + e.Message, "error");
                return false;
            }
        }

        // ── Settings ─────────────────────────────────────────────────────────────────────────────

        public void SetTool(PixelTool tool)
        {
            CancelPreview();
            Tool = tool;
            SelectionChanged?.Invoke();
        }

        public void SetPrimary(Rgba32 color)
        {
            Primary = color;
            NoteRecent(color);
            SelectionChanged?.Invoke();
        }

        public void SetSecondary(Rgba32 color)
        {
            Secondary = color;
            SelectionChanged?.Invoke();
        }

        public void SwapColors()
        {
            (Primary, Secondary) = (Secondary, Primary);
            SelectionChanged?.Invoke();
        }

        public void SetBrushSize(int size)
        {
            BrushSize = Math.Max(1, Math.Min(16, size));
            SelectionChanged?.Invoke();
        }

        public void SetTileLimit(PixelRect? tile)
        {
            TileLimit = tile;
            SelectionChanged?.Invoke();
        }

        /// <summary>The tile under (x, y) with the current grid (null without a grid).</summary>
        public PixelRect? TileAt(int x, int y)
        {
            if (Image == null || TileWidth <= 0 || TileHeight <= 0 || !Image.Contains(x, y)) return null;
            return new PixelRect(x / TileWidth * TileWidth, y / TileHeight * TileHeight, TileWidth, TileHeight);
        }

        public List<Rgba32> Palette(int max = 64) => Image == null ? new List<Rgba32>() : PixelTools.Palette(Image, max, TileLimit);

        private void NoteRecent(Rgba32 c)
        {
            Recent.RemoveAll(r => r == c);
            Recent.Insert(0, c);
            if (Recent.Count > 16) Recent.RemoveAt(Recent.Count - 1);
        }

        // ── Pointer (image pixels) ───────────────────────────────────────────────────────────────

        /// <summary>'secondary' = botón derecho (pinta con el color secundario; en el cuentagotas, lo elige).</summary>
        public void PointerDown(int x, int y, bool secondary = false)
        {
            if (Image == null) return;
            var color = Tool == PixelTool.Eraser ? Rgba32.Transparent : secondary ? Secondary : Primary;
            switch (Tool)
            {
                case PixelTool.Pencil:
                case PixelTool.Eraser:
                    _stroke = new PixelStroke(Image);
                    _dragColor = color;
                    _last = (x, y);
                    StrokeTo(x, y);
                    break;
                case PixelTool.Line:
                case PixelTool.Rectangle:
                case PixelTool.FilledRectangle:
                    _dragStart = (x, y);
                    _dragColor = color;
                    UpdatePreview(x, y);
                    break;
                case PixelTool.Fill:
                    Commit(PixelTools.Fill(Image, x, y, color, TileLimit), "rellenar");
                    break;
                case PixelTool.Replace:
                    if (Image.Contains(x, y)) Commit(PixelTools.ReplaceColor(Image, Image[x, y], color, TileLimit), "reemplazar color");
                    break;
                case PixelTool.Picker:
                    if (!Image.Contains(x, y)) return;
                    if (secondary) SetSecondary(Image[x, y]);
                    else SetPrimary(Image[x, y]);
                    break;
            }
        }

        private Rgba32 _dragColor;
        private (int x, int y) _last;

        public void PointerDrag(int x, int y)
        {
            if (Image == null) return;
            if (_stroke != null) StrokeTo(x, y);
            else if (_dragStart.HasValue) UpdatePreview(x, y);
        }

        public void PointerUp(int x, int y)
        {
            if (Image == null) return;
            if (_stroke != null)
            {
                var cmd = _stroke.ToCommand(Tool == PixelTool.Eraser ? "borrar" : "pintar");
                _stroke = null;
                if (cmd.Changes.Count > 0) History.Record(cmd);
            }
            else if (_dragStart.HasValue)
            {
                UpdatePreview(x, y);
                var changes = _preview;
                _preview = new List<PixelChange>();
                _dragStart = null;
                if (changes.Count > 0)
                    History.Record(new PixelPaintCommand(Image, changes, Tool == PixelTool.Line ? "línea" : "rectángulo"));
            }
        }

        /// <summary>Lápiz: une el punto anterior con el nuevo (un arrastre rápido no deja huecos).</summary>
        private void StrokeTo(int x, int y)
        {
            var changes = PixelTools.Line(Image, _last.x, _last.y, x, y, _dragColor, BrushSize);
            _last = (x, y);
            if (changes.Count == 0) return;
            _stroke.Add(changes);
            PixelsChanged?.Invoke(PixelTools.Bounds(changes));
        }

        private void UpdatePreview(int x, int y)
        {
            var (x0, y0) = _dragStart.Value;
            var old = _preview;
            PixelTools.Apply(Image, old, undo: true);
            _preview = Tool == PixelTool.Line
                ? PixelTools.Line(Image, x0, y0, x, y, _dragColor, BrushSize)
                : PixelTools.Rectangle(Image, x0, y0, x, y, _dragColor, Tool == PixelTool.FilledRectangle);
            PixelTools.Apply(Image, _preview);
            var all = old.Concat(_preview).ToList();
            if (all.Count > 0) PixelsChanged?.Invoke(PixelTools.Bounds(all));
        }

        private void CancelPreview()
        {
            if (Image != null && _preview.Count > 0)
            {
                PixelTools.Apply(Image, _preview, undo: true);
                PixelsChanged?.Invoke(PixelTools.Bounds(_preview));
            }
            _preview = new List<PixelChange>();
            _dragStart = null;
        }

        private void Commit(List<PixelChange> changes, string label)
        {
            if (changes.Count == 0) return;
            History.Execute(new PixelPaintCommand(Image, changes, label));
            PixelsChanged?.Invoke(PixelTools.Bounds(changes));
        }

        public void Undo()
        {
            CancelPreview();
            if (!History.Undo()) { Message?.Invoke("No hay nada que deshacer.", "texto"); return; }
            PixelsChanged?.Invoke(new PixelRect(0, 0, Image.Width, Image.Height));
        }

        public void Redo()
        {
            CancelPreview();
            if (!History.Redo()) { Message?.Invoke("No hay nada que rehacer.", "texto"); return; }
            PixelsChanged?.Invoke(new PixelRect(0, 0, Image.Width, Image.Height));
        }
    }
}
