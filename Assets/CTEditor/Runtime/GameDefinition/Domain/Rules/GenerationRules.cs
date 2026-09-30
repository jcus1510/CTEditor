using System;
using System.Collections.Generic;

namespace CTEditor.GameDefinition.Domain.Rules
{
    /// <summary>
    /// Las REGLAS DE GENERACIÓN: lo que cambia de un juego clásico a otro y no es «contenido» (especies,
    /// movimientos...) sino cómo funciona el combate. Viven dentro del <see cref="Ruleset"/>.
    ///
    /// Cada perilla es independiente: <see cref="ForGeneration"/> da el punto de partida fiel de cada generación,
    /// pero el autor puede mezclar (p. ej. 1.ª generación con habilidades). Por defecto: moderno (todo encendido).
    /// Inmutable, como el Ruleset.
    /// </summary>
    public sealed class GenerationRules
    {
        /// <summary>Generación de referencia (informativa: la usan los asistentes y el editor). 0 = personalizada.</summary>
        public int Generation { get; }

        /// <summary>
        /// true (1.ª-3.ª gen.) = la categoría Físico/Especial la decide el TIPO del movimiento, no el movimiento.
        /// Los de estado siguen siendo de estado.
        /// </summary>
        public bool CategoryByType { get; }

        /// <summary>Con <see cref="CategoryByType"/>: los tipos cuyos movimientos son ESPECIALES (el resto, físicos).</summary>
        public IReadOnlyCollection<string> SpecialTypes => _special;
        private readonly HashSet<string> _special;

        /// <summary>
        /// true (1.ª gen.) = una sola estadística «Especial»: lo que sube o baja el Ataque Especial también sube o
        /// baja la Defensa Especial, y al revés (Amnesia sube las dos).
        /// </summary>
        public bool SingleSpecialStat { get; }

        /// <summary>¿Hay habilidades? (desde 3.ª gen.). Apagado = ninguna habilidad hace nada en combate.</summary>
        public bool Abilities { get; }

        /// <summary>¿Se pueden equipar objetos? (desde 2.ª gen.). Apagado = los objetos equipados no hacen nada.</summary>
        public bool HeldItems { get; }

        /// <summary>¿Hay naturalezas? (desde 3.ª gen.). Apagado = todos neutros.</summary>
        public bool Natures { get; }

        /// <summary>¿Hay géneros? (desde 2.ª gen.). Apagado = todos sin género.</summary>
        public bool Genders { get; }

        /// <summary>Los tipos especiales clásicos (1.ª-3.ª gen.).</summary>
        public static readonly IReadOnlyList<string> ClassicSpecialTypes =
            new[] { "fire", "water", "grass", "electric", "ice", "psychic", "dragon", "dark" };

        public GenerationRules(int generation = 0, bool categoryByType = false, IEnumerable<string> specialTypes = null,
            bool singleSpecialStat = false, bool abilities = true, bool heldItems = true, bool natures = true, bool genders = true)
        {
            Generation = generation < 0 ? 0 : generation;
            CategoryByType = categoryByType;
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in specialTypes ?? ClassicSpecialTypes)
                if (!string.IsNullOrWhiteSpace(t)) set.Add(t.Trim());
            _special = set;
            SingleSpecialStat = singleSpecialStat;
            Abilities = abilities;
            HeldItems = heldItems;
            Natures = natures;
            Genders = genders;
        }

        /// <summary>Moderno: todo encendido, categoría por movimiento.</summary>
        public static GenerationRules Modern => new GenerationRules();

        /// <summary>¿Los movimientos de este tipo son especiales? (solo con <see cref="CategoryByType"/>).</summary>
        public bool IsSpecialType(string typeId) => typeId != null && _special.Contains(typeId);

        /// <summary>
        /// El punto de partida FIEL de cada generación (1 a 9). 0 o fuera de rango = moderno. Después el autor puede
        /// cambiar cualquier perilla.
        /// </summary>
        public static GenerationRules ForGeneration(int generation)
        {
            switch (generation)
            {
                case 1: return new GenerationRules(1, categoryByType: true, singleSpecialStat: true,
                    abilities: false, heldItems: false, natures: false, genders: false);
                case 2: return new GenerationRules(2, categoryByType: true, abilities: false, natures: false);
                case 3: return new GenerationRules(3, categoryByType: true);
                default: return new GenerationRules(generation >= 4 && generation <= 9 ? generation : 0);
            }
        }

        /// <summary>Copia cambiando solo lo indicado.</summary>
        public GenerationRules With(int? generation = null, bool? categoryByType = null, IEnumerable<string> specialTypes = null,
            bool? singleSpecialStat = null, bool? abilities = null, bool? heldItems = null, bool? natures = null, bool? genders = null)
            => new GenerationRules(generation ?? Generation, categoryByType ?? CategoryByType, specialTypes ?? SpecialTypes,
                singleSpecialStat ?? SingleSpecialStat, abilities ?? Abilities, heldItems ?? HeldItems, natures ?? Natures, genders ?? Genders);
    }
}
