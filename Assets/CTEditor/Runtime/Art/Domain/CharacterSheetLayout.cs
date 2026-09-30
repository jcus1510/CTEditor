using System;
using System.Collections.Generic;

namespace CTEditor.Art.Domain
{
    /// <summary>Hacia dónde mira un personaje.</summary>
    public enum FacingDirection
    {
        Down = 0,
        Left = 1,
        Right = 2,
        Up = 3
    }

    /// <summary>
    /// Cómo está ordenada una hoja de personaje: cuántas columnas (pasos) y filas (direcciones), qué dirección es cada fila,
    /// qué fotograma es «quieto» y en qué orden se recorren los pasos al andar.
    ///   - RPG Maker XP (y Essentials): 4 × 4, filas abajo / izquierda / derecha / arriba, quieto = 1.er fotograma.
    ///   - RPG Maker VX / MV: 3 × 4, mismas filas, quieto = el del centro, pasos 1-0-1-2.
    /// El autor puede definir la suya.
    /// </summary>
    public sealed class CharacterSheetLayout
    {
        public string Name { get; }
        public int Columns { get; }
        public int Rows { get; }
        /// <summary>Dirección de cada fila (Rows elementos).</summary>
        public IReadOnlyList<FacingDirection> RowDirections { get; }
        /// <summary>Columna del fotograma «quieto».</summary>
        public int IdleColumn { get; }
        /// <summary>Columnas que se recorren al andar, en orden.</summary>
        public IReadOnlyList<int> WalkCycle { get; }

        public CharacterSheetLayout(string name, int columns, int rows, IReadOnlyList<FacingDirection> rowDirections, int idleColumn,
            IReadOnlyList<int> walkCycle)
        {
            if (columns <= 0 || rows <= 0) throw new ArgumentOutOfRangeException(nameof(columns), "Hacen falta columnas y filas.");
            if (rowDirections == null || rowDirections.Count != rows) throw new ArgumentException("Una dirección por fila.", nameof(rowDirections));
            Name = name;
            Columns = columns;
            Rows = rows;
            RowDirections = rowDirections;
            IdleColumn = idleColumn;
            WalkCycle = walkCycle ?? Array.Empty<int>();
        }

        private static readonly FacingDirection[] ClassicRows =
            { FacingDirection.Down, FacingDirection.Left, FacingDirection.Right, FacingDirection.Up };

        public static readonly CharacterSheetLayout RpgMakerXp =
            new CharacterSheetLayout("RPG Maker XP (4 × 4)", 4, 4, ClassicRows, 0, new[] { 0, 1, 2, 3 });

        public static readonly CharacterSheetLayout RpgMakerVx =
            new CharacterSheetLayout("RPG Maker VX / MV (3 × 4)", 3, 4, ClassicRows, 1, new[] { 1, 0, 1, 2 });

        public static IReadOnlyList<CharacterSheetLayout> Presets { get; } = new[] { RpgMakerXp, RpgMakerVx };

        /// <summary>Tamaño de cada fotograma para una hoja de ese tamaño (0 si no se reparte exacto).</summary>
        public (int width, int height) FrameSize(int sheetWidth, int sheetHeight) =>
            sheetWidth % Columns == 0 && sheetHeight % Rows == 0 ? (sheetWidth / Columns, sheetHeight / Rows) : (0, 0);

        public SliceSettings SliceFor(int sheetWidth, int sheetHeight)
        {
            var (w, h) = FrameSize(sheetWidth, sheetHeight);
            return w == 0 ? null : new SliceSettings(w, h);
        }

        /// <summary>Fila de una dirección (-1 si la hoja no la tiene).</summary>
        public int RowOf(FacingDirection d)
        {
            for (int i = 0; i < RowDirections.Count; i++) if (RowDirections[i] == d) return i;
            return -1;
        }

        /// <summary>Rectángulo del fotograma 'step' (índice en el ciclo de andar; -1 = quieto) mirando a 'd'.</summary>
        public PixelRect FrameRect(int sheetWidth, int sheetHeight, FacingDirection d, int step = -1)
        {
            var (w, h) = FrameSize(sheetWidth, sheetHeight);
            int row = RowOf(d);
            if (w == 0 || row < 0) return new PixelRect(0, 0, 0, 0);
            int col = step < 0 || WalkCycle.Count == 0 ? IdleColumn : WalkCycle[step % WalkCycle.Count];
            return new PixelRect(col * w, row * h, w, h);
        }

        /// <summary>
        /// La plantilla que mejor encaja con una hoja de ese tamaño (null = ninguna). Si encajan las dos (anchos múltiplos
        /// de 12), gana la que da fotogramas del ancho más habitual (orden de TileSizeSuggester.CommonSizes); si empatan, XP.
        /// </summary>
        public static CharacterSheetLayout Detect(int sheetWidth, int sheetHeight)
        {
            CharacterSheetLayout best = null;
            int bestScore = -1;
            foreach (var p in Presets)
            {
                var (w, h) = p.FrameSize(sheetWidth, sheetHeight);
                if (w == 0) continue;
                int common = Array.IndexOf(TileSizeSuggester.CommonSizes, w);
                int score = common >= 0 ? TileSizeSuggester.CommonSizes.Length - common : 0;
                if (score > bestScore) { best = p; bestScore = score; }
            }
            return best;
        }
    }
}
