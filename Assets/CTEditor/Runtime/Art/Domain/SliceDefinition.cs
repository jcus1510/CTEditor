namespace CTEditor.Art.Domain
{
    /// <summary>Para qué se corta una imagen.</summary>
    public enum SheetKind
    {
        /// <summary>Tiles para pintar mapas.</summary>
        Tileset = 0,
        /// <summary>Hoja de personaje: filas = direcciones, columnas = pasos.</summary>
        Character = 1,
        /// <summary>Fotogramas sueltos (iconos, efectos...).</summary>
        Sprites = 2
    }

    /// <summary>Cómo se corta una imagen y qué es: lo que decide el asistente de corte.</summary>
    public sealed class SliceDefinition
    {
        public SliceSettings Settings { get; set; }
        public SheetKind Kind { get; set; }
        /// <summary>Solo personajes: nombre de la plantilla de hoja (CharacterSheetLayout.Name). Vacío = ninguna.</summary>
        public string CharacterLayout { get; set; } = "";
        /// <summary>Pieces cut by hand with pixel precision (free slicing), laid out under the grid.</summary>
        public FreePieceSet Free { get; set; } = new FreePieceSet();

        public SliceDefinition(SliceSettings settings, SheetKind kind = SheetKind.Tileset)
        {
            Settings = settings;
            Kind = kind;
        }

        /// <summary>La plantilla de personaje elegida (null si no hay o no es un personaje).</summary>
        public CharacterSheetLayout Layout
        {
            get
            {
                if (Kind != SheetKind.Character) return null;
                foreach (var l in CharacterSheetLayout.Presets) if (l.Name == CharacterLayout) return l;
                return null;
            }
        }
    }
}
