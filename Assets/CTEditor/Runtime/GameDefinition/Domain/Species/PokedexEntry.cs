using System;

namespace CTEditor.GameDefinition.Domain.Species
{
    /// <summary>
    /// LA FICHA DE POKÉDEX de una especie: número, categoría («Pokémon Semilla»), altura, peso, color,
    /// descripción, proporción de hembras y si es legendaria. No cambia el combate (salvo lo que en el
    /// futuro use el peso, como Patada Baja): es para la Pokédex, el resumen y las búsquedas.
    /// </summary>
    public sealed class PokedexEntry
    {
        /// <summary>Número en la Pokédex (0 = sin número).</summary>
        public int Number { get; }
        /// <summary>Categoría: «Semilla» (se muestra como «Pokémon Semilla»).</summary>
        public string Category { get; }
        /// <summary>Altura en metros.</summary>
        public float HeightM { get; }
        /// <summary>Peso en kilos.</summary>
        public float WeightKg { get; }
        /// <summary>Color principal (verde, rojo...), como en la búsqueda de la Pokédex.</summary>
        public string Color { get; }
        public string Description { get; }
        /// <summary>% de hembras (0-100). -1 = sin género.</summary>
        public float FemalePercent { get; }
        public bool Legendary { get; }

        public PokedexEntry(int number, string category, float heightM, float weightKg, string color = "", string description = "",
            float femalePercent = 50f, bool legendary = false)
        {
            Number = Math.Max(0, number);
            Category = category ?? "";
            HeightM = Math.Max(0f, heightM);
            WeightKg = Math.Max(0f, weightKg);
            Color = color ?? "";
            Description = description ?? "";
            FemalePercent = femalePercent < 0 ? -1f : Math.Min(100f, femalePercent);
            Legendary = legendary;
        }

        public static readonly PokedexEntry Empty = new PokedexEntry(0, "", 0, 0);

        public bool IsGenderless => FemalePercent < 0;

        /// <summary>«Nº 001» (3 cifras como en los juegos clásicos; más si hace falta).</summary>
        public string NumberText => Number <= 0 ? "Nº ---" : "Nº " + Number.ToString(Number < 1000 ? "000" : "0");

        /// <summary>Texto corto para el resumen: «Nº 001 · Pokémon Semilla · 0,7 m · 6,9 kg».</summary>
        public string Summary(string categoryPrefix = "Pokémon")
        {
            string cat = string.IsNullOrWhiteSpace(Category) ? "" : $" · {categoryPrefix} {Category}".Replace("  ", " ");
            string size = HeightM > 0 || WeightKg > 0 ? $" · {HeightM:0.0#} m · {WeightKg:0.0#} kg" : "";
            return NumberText + cat + size;
        }
    }
}
