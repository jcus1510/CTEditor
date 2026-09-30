using System;
using System.Collections.Generic;

namespace CTEditor.Art.Domain
{
    /// <summary>
    /// Cómo se corta una imagen en tiles (o en fotogramas): tamaño de cada tile, margen del borde (desplazamiento) y
    /// separación entre tiles. Es lo que el autor elige en el asistente de corte y lo que se guarda junto a la imagen.
    /// </summary>
    public sealed class SliceSettings
    {
        public int TileWidth { get; }
        public int TileHeight { get; }
        public int OffsetX { get; }
        public int OffsetY { get; }
        public int SpacingX { get; }
        public int SpacingY { get; }

        public SliceSettings(int tileWidth, int tileHeight, int offsetX = 0, int offsetY = 0, int spacingX = 0, int spacingY = 0)
        {
            TileWidth = tileWidth;
            TileHeight = tileHeight;
            OffsetX = offsetX;
            OffsetY = offsetY;
            SpacingX = spacingX;
            SpacingY = spacingY;
        }

        /// <summary>Tiles cuadrados sin margen ni separación.</summary>
        public static SliceSettings Square(int size) => new SliceSettings(size, size);

        /// <summary>Columnas enteras que caben en ese ancho.</summary>
        public int ColumnsFor(int imageWidth) => Fit(imageWidth - OffsetX, TileWidth, SpacingX);
        /// <summary>Filas enteras que caben en ese alto.</summary>
        public int RowsFor(int imageHeight) => Fit(imageHeight - OffsetY, TileHeight, SpacingY);

        public PixelRect CellRect(int column, int row) =>
            new PixelRect(OffsetX + column * (TileWidth + SpacingX), OffsetY + row * (TileHeight + SpacingY), TileWidth, TileHeight);

        /// <summary>Errores que impiden cortar (vacío = se puede).</summary>
        public IReadOnlyList<string> Problems(int imageWidth, int imageHeight)
        {
            var list = new List<string>();
            if (TileWidth <= 0 || TileHeight <= 0) list.Add("El tamaño del tile debe ser mayor que 0.");
            if (OffsetX < 0 || OffsetY < 0) list.Add("El desplazamiento no puede ser negativo.");
            if (SpacingX < 0 || SpacingY < 0) list.Add("La separación no puede ser negativa.");
            if (list.Count == 0 && (ColumnsFor(imageWidth) == 0 || RowsFor(imageHeight) == 0))
                list.Add($"Ningún tile de {TileWidth}×{TileHeight} cabe en la imagen ({imageWidth}×{imageHeight}) con ese desplazamiento.");
            return list;
        }

        private static int Fit(int length, int size, int spacing)
        {
            if (size <= 0 || length < size) return 0;
            return (length + spacing) / (size + spacing);
        }

        public override string ToString() =>
            $"{TileWidth}×{TileHeight}" + (OffsetX != 0 || OffsetY != 0 ? $" desde ({OffsetX},{OffsetY})" : "")
            + (SpacingX != 0 || SpacingY != 0 ? $" separación {SpacingX}×{SpacingY}" : "");
    }

    /// <summary>Un tile (o fotograma) de la imagen cortada.</summary>
    public sealed class TileCell
    {
        /// <summary>Posición en la lista: fila a fila, de izquierda a derecha.</summary>
        public int Index { get; }
        public int Column { get; }
        public int Row { get; }
        public PixelRect Rect { get; }
        /// <summary>Todo transparente: el editor lo omite en la paleta.</summary>
        public bool IsEmpty { get; }
        /// <summary>Índice del primer tile que se ve igual (-1 = es el primero o está vacío).</summary>
        public int DuplicateOf { get; internal set; } = -1;
        public ulong Fingerprint { get; }

        public TileCell(int index, int column, int row, PixelRect rect, bool isEmpty, ulong fingerprint)
        {
            Index = index;
            Column = column;
            Row = row;
            Rect = rect;
            IsEmpty = isEmpty;
            Fingerprint = fingerprint;
        }

        public bool IsDuplicate => DuplicateOf >= 0;
    }

    /// <summary>Resultado de cortar una imagen.</summary>
    public sealed class TileSheet
    {
        public SliceSettings Settings { get; }
        public int Columns { get; }
        public int Rows { get; }
        public IReadOnlyList<TileCell> Cells { get; }
        /// <summary>Píxeles que sobran a la derecha / abajo (no forman un tile entero).</summary>
        public int LeftoverX { get; }
        public int LeftoverY { get; }
        /// <summary>Errores que impidieron cortar (entonces no hay celdas).</summary>
        public IReadOnlyList<string> Problems { get; }
        /// <summary>Avisos que no impiden cortar (píxeles sobrantes...).</summary>
        public IReadOnlyList<string> Warnings { get; }

        internal TileSheet(SliceSettings settings, int columns, int rows, IReadOnlyList<TileCell> cells, int leftoverX, int leftoverY,
            IReadOnlyList<string> problems, IReadOnlyList<string> warnings)
        {
            Settings = settings;
            Columns = columns;
            Rows = rows;
            Cells = cells;
            LeftoverX = leftoverX;
            LeftoverY = leftoverY;
            Problems = problems;
            Warnings = warnings;
        }

        public bool Ok => Problems.Count == 0;
        public TileCell At(int column, int row) => Cells[row * Columns + column];

        public int EmptyCount { get { int n = 0; foreach (var c in Cells) if (c.IsEmpty) n++; return n; } }
        public int DuplicateCount { get { int n = 0; foreach (var c in Cells) if (c.IsDuplicate) n++; return n; } }
        /// <summary>Tiles que el autor verá en la paleta: ni vacíos ni repetidos.</summary>
        public int UniqueCount => Cells.Count - EmptyCount - DuplicateCount;
    }

    /// <summary>Corta una imagen en una rejilla de tiles y marca los vacíos y los repetidos.</summary>
    public static class TileSlicer
    {
        public static TileSheet Slice(PixelImage image, SliceSettings settings)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            var problems = settings.Problems(image.Width, image.Height);
            if (problems.Count > 0)
                return new TileSheet(settings, 0, 0, Array.Empty<TileCell>(), 0, 0, problems, Array.Empty<string>());

            int cols = settings.ColumnsFor(image.Width), rows = settings.RowsFor(image.Height);
            int usedW = settings.OffsetX + cols * settings.TileWidth + (cols - 1) * settings.SpacingX;
            int usedH = settings.OffsetY + rows * settings.TileHeight + (rows - 1) * settings.SpacingY;
            int leftX = image.Width - usedW, leftY = image.Height - usedH;
            // The spacing after the last tile is not "left over": it is just the gutter.
            if (leftX <= settings.SpacingX) leftX = 0;
            if (leftY <= settings.SpacingY) leftY = 0;

            var warnings = new List<string>();
            if (leftX > 0) warnings.Add($"Sobran {leftX} píxeles a la derecha: ¿el tamaño del tile es correcto?");
            if (leftY > 0) warnings.Add($"Sobran {leftY} píxeles abajo: ¿el tamaño del tile es correcto?");

            var cells = new List<TileCell>(cols * rows);
            var firstByPrint = new Dictionary<ulong, List<int>>();
            for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var rect = settings.CellRect(c, r);
                bool empty = image.IsTransparent(rect);
                ulong print = empty ? 0 : image.Fingerprint(rect);
                var cell = new TileCell(cells.Count, c, r, rect, empty, print);
                if (!empty)
                {
                    if (!firstByPrint.TryGetValue(print, out var same)) firstByPrint[print] = same = new List<int>();
                    foreach (int i in same)
                        if (image.SameContent(cells[i].Rect, rect)) { cell.DuplicateOf = i; break; }
                    if (!cell.IsDuplicate) same.Add(cell.Index);
                }
                cells.Add(cell);
            }
            return new TileSheet(settings, cols, rows, cells, leftX, leftY, Array.Empty<string>(), warnings);
        }
    }
}
