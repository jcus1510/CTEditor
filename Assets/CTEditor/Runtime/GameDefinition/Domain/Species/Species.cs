using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.GameDefinition.Domain.Abilities;
using CTEditor.GameDefinition.Domain.Growth;

namespace CTEditor.GameDefinition.Domain.Species
{
    /// <summary>
    /// La DEFINICIÓN de una criatura: su "ficha de enciclopedia". Es inmutable y COMPARTIDA:
    /// existe UNA sola "Salamandra de Fuego" en el juego, y de ella nacen muchos individuos
    /// concretos en partida. Esa es la frontera maestra del motor (C.1):
    ///
    ///   Species (esto)      = el plano: tipos, stats BASE, qué aprende, en qué evoluciona.
    ///   MonsterInstance     = un individuo concreto: "Chispa", nivel 34, con SUS PS y SUS movs.
    ///                         (lo construiremos en Party.Domain)
    ///
    /// Una Species es de SOLO LECTURA y se reutiliza para mil instancias; el estado mutable de
    /// la partida vive en las instancias, NUNCA aquí. (Esto es lo que evita el anti-patrón más
    /// peligroso, M.6: mutar una definición en runtime corrompería a todos los individuos.)
    ///
    /// 'sealed' (no se hereda) y POCO puro (cero Unity).
    /// </summary>
    public sealed class Species
    {
        public Id<Species> Id { get; }
        public string DisplayName { get; }

        /// <summary>
        /// Los tipos de la especie, por id. Es una LISTA, no uno o dos campos fijos: así soportamos
        /// monotipo, doble tipo... o más, si el autor lo quiere (la cantidad máxima es una decisión
        /// de reglas/validación, no una limitación cableada aquí). Otra forma de "crear cada cosa".
        ///
        /// IReadOnlyList = quien la reciba puede recorrerla e indexarla, pero NO añadir/quitar:
        /// protege la inmutabilidad de la ficha.
        /// </summary>
        public IReadOnlyList<Id<ElementType>> Types { get; }

        /// <summary>Estadísticas BASE (no las del individuo: estas son el punto de partida del plano).</summary>
        public StatBlock BaseStats { get; }

        /// <summary>Qué movimientos aprende y a qué nivel.</summary>
        public IReadOnlyList<LearnableMove> Learnset { get; }

        /// <summary>Sus posibles evoluciones (0, 1 o varias).</summary>
        public IReadOnlyList<Evolution> Evolutions { get; }

        /// <summary>La habilidad de la especie (null = ninguna). Rasgo pasivo que se aplica en combate.</summary>
        public AbilityId? Ability { get; }

        /// <summary>Curva de experiencia de la especie (id de un GrowthCurve). Null = usar la de por defecto.</summary>
        public Id<GrowthCurve>? GrowthCurveId { get; }

        /// <summary>
        /// Rendimiento base de experiencia: cuánta XP "vale" derrotar a un ejemplar de esta especie
        /// (la fórmula lo escala por nivel). En los clásicos, cada especie tiene el suyo (p. ej. 64).
        /// </summary>
        public int BaseExpYield { get; }

        /// <summary>EVs que otorga derrotar a esta especie (clásico: 1-3 puntos en 1-2 stats).</summary>
        public IReadOnlyList<EvYieldEntry> EvYield { get; }

        /// <summary>
        /// Ratio de captura (1-255). Cuanto más alto, más fácil de capturar: Caterpie 255, Pikachu 190,
        /// iniciales 45, legendarios 3. Es la "a" de la fórmula clásica de captura.
        /// </summary>
        public int CatchRate { get; }

        /// <summary>Amistad con la que empieza un individuo recién obtenido (0-255, clásico 70).</summary>
        public int BaseFriendship { get; }

        /// <summary>Datos de la Pokédex (número, categoría, altura, peso...). Nunca null.</summary>
        public PokedexEntry Dex { get; }

        /// <summary>Segunda habilidad posible (null = solo tiene una). Cada individuo sale con la 1.ª o la 2.ª al azar.</summary>
        public AbilityId? SecondAbility { get; }

        /// <summary>Habilidad OCULTA (null = ninguna). Solo la tienen los individuos que el autor elija.</summary>
        public AbilityId? HiddenAbility { get; }

        /// <summary>Movimientos que puede aprender por MT/MO.</summary>
        public IReadOnlyList<Id<Moves.Move>> MachineMoves { get; }

        /// <summary>Movimientos que le puede enseñar un tutor.</summary>
        public IReadOnlyList<Id<Moves.Move>> TutorMoves { get; }

        /// <summary>Movimientos HUEVO: los hereda al nacer (para la crianza futura) y los usan los entrenadores de élite.</summary>
        public IReadOnlyList<Id<Moves.Move>> EggMoves { get; }

        /// <summary>Grupos huevo (ids: "monster", "dragon"...). Vacío = no definido; "no_eggs" = no puede criar.</summary>
        public IReadOnlyList<string> EggGroups { get; }

        /// <summary>La habilidad de un individuo según su «ranura»: 0 = la primera, 1 = la segunda, 2 = la oculta.</summary>
        public AbilityId? AbilityFor(int slot)
            => slot == 2 ? (HiddenAbility ?? Ability) : slot == 1 ? (SecondAbility ?? Ability) : Ability;

        /// <summary>¿Puede aprender este movimiento de alguna forma (nivel, MT, tutor o huevo)?</summary>
        public bool CanLearn(Id<Moves.Move> move)
        {
            foreach (var l in Learnset) if (l.Move == move) return true;
            foreach (var m in MachineMoves) if (m == move) return true;
            foreach (var m in TutorMoves) if (m == move) return true;
            foreach (var m in EggMoves) if (m == move) return true;
            return false;
        }

        public Species(
            Id<Species> id,
            string displayName,
            IReadOnlyList<Id<ElementType>> types,
            StatBlock baseStats,
            IReadOnlyList<LearnableMove> learnset,
            IReadOnlyList<Evolution> evolutions,
            AbilityId? ability = null,
            Id<GrowthCurve>? growthCurveId = null,
            int baseExpYield = 64,
            IReadOnlyList<EvYieldEntry> evYield = null,
            int baseFriendship = 70,
            int catchRate = 45,
            PokedexEntry dex = null,
            AbilityId? secondAbility = null,
            AbilityId? hiddenAbility = null,
            IReadOnlyList<Id<Moves.Move>> machineMoves = null,
            IReadOnlyList<Id<Moves.Move>> tutorMoves = null,
            IReadOnlyList<Id<Moves.Move>> eggMoves = null,
            IReadOnlyList<string> eggGroups = null)
        {
            SecondAbility = secondAbility;
            HiddenAbility = hiddenAbility;
            MachineMoves = CopyOrEmpty(machineMoves);
            TutorMoves = CopyOrEmpty(tutorMoves);
            EggMoves = CopyOrEmpty(eggMoves);
            EggGroups = CopyOrEmpty(eggGroups);
            Dex = dex ?? PokedexEntry.Empty;
            CatchRate = Math.Max(1, Math.Min(255, catchRate));
            BaseFriendship = Math.Max(0, Math.Min(255, baseFriendship));
            // Una criatura sin ningún tipo no tiene sentido en el modelo de combate.
            if (types is null || types.Count == 0)
                throw new ArgumentException("Una especie necesita al menos un tipo.", nameof(types));
            if (baseStats is null)
                throw new ArgumentNullException(nameof(baseStats));
            // Nota: que los stats base incluyan los 6 clásicos NO se valida aquí. Eso es una
            // comprobación AMIGABLE de Content Authoring (L.8), con mensajes para el autor; el
            // dominio solo blinda lo imposible, no educa al usuario.

            Id = id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id.Value : displayName;

            // COPIAS DEFENSIVAS: copiamos las listas que nos pasan a listas propias. Si no lo
            // hiciéramos, quien construyó la Species podría seguir modificando "su" lista por fuera
            // y alterar la ficha a nuestras espaldas. Al copiar y exponer como IReadOnlyList,
            // la ficha queda sellada de verdad.
            Types = CopyOrEmpty(types);
            BaseStats = baseStats;          // StatBlock ya es inmutable, no hace falta copiar
            Learnset = CopyOrEmpty(learnset);
            Evolutions = CopyOrEmpty(evolutions);
            Ability = ability;
            GrowthCurveId = growthCurveId;
            BaseExpYield = baseExpYield < 1 ? 1 : baseExpYield;
            EvYield = CopyOrEmpty(evYield);
        }

        /// <summary>¿Tiene más de un tipo?</summary>
        public bool IsDualType => Types.Count >= 2;

        /// <summary>¿Puede evolucionar en algo?</summary>
        public bool CanEvolve => Evolutions.Count > 0;

        // Helper genérico de copia. 'Array.Empty&lt;T&gt;()' devuelve un arreglo vacío YA cacheado
        // (no asigna memoria nueva cada vez) y, como los arreglos también son IReadOnlyList, sirve
        // perfecto cuando la lista venía en null.
        private static IReadOnlyList<T> CopyOrEmpty<T>(IReadOnlyList<T> source)
            => source is null ? Array.Empty<T>() : new List<T>(source);
    }
}
