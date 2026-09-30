using System;
using System.Collections.Generic;

namespace CTEditor.GameDefinition.Domain.Trainers
{
    /// <summary>Cómo elige el MOVIMIENTO cada turno. Solo se añaden valores al final.</summary>
    public enum MoveBrain
    {
        Random,      // al azar entre los que puede usar
        Aggressive,  // el que más daño hace según los tipos (evita lo que no afecta)
        Expert       // daño real (etapas, habilidades, clima), remata, estados y mejoras con cabeza
    }

    /// <summary>Cuándo se cura con objetos. Solo se añaden valores al final.</summary>
    public enum HealStyle
    {
        Never,   // nunca se cura
        Simple,  // se cura en cuanto baja del umbral
        Smart    // solo si le sirve: si el rival lo tumba igual (o le quita más de lo que cura), ataca
    }

    /// <summary>
    /// UN NIVEL DE IA (1 a 5), editable por el autor: cómo piensa un entrenador de ese nivel y a qué
    /// movimientos tiene acceso. Los 5 clásicos:
    ///
    ///   1 Novato      movimientos al azar, se olvida a menudo de sus objetos, no cambia.
    ///   2 Aficionado  prefiere lo eficaz, cura simple; ya usa movimientos de MT.
    ///   3 Veterano    tipos + estados; cura solo si sirve; + movimientos de tutor.
    ///   4 Élite       movesets con sinergias, cambia de monstruo, lleva objetos equipados; + movimientos huevo.
    ///   5 Campeón     todo lo anterior y sin fallos al azar.
    ///
    /// El entrenador elige su nivel y puede ajustar lo suyo (mochila, umbral de cura, si cambia...).
    /// </summary>
    public sealed class AiProfile
    {
        public const int MinLevel = 1, MaxLevel = 5;

        /// <summary>Nivel 1-5.</summary>
        public int Level { get; }
        public string DisplayName { get; }
        public string Description { get; }

        public MoveBrain Brain { get; }
        /// <summary>% de turnos en que «se equivoca» y usa un movimiento al azar (0 = nunca).</summary>
        public int MistakePercent { get; }
        /// <summary>% de veces que se acuerda de usar sus objetos cuando debería (100 = siempre).</summary>
        public int ItemUsePercent { get; }
        public HealStyle Heal { get; }
        /// <summary>Umbral de PS (%) a partir del cual piensa en curarse.</summary>
        public int HealBelowPercent { get; }
        /// <summary>¿Retira a su monstruo si pierde claramente el duelo?</summary>
        public bool CanSwitch { get; }

        /// <summary>Cómo arma los movimientos de los miembros que no los traen escritos.</summary>
        public MovesetStyle Moveset { get; }
        /// <summary>¿Busca combinaciones (Hipnosis + Comesueños, Danza Lluvia + Agua, Danza Espada + físico...)?</summary>
        public bool Synergies { get; }
        public bool UseMachineMoves { get; }
        public bool UseTutorMoves { get; }
        public bool UseEggMoves { get; }
        /// <summary>A los miembros sin objeto equipado les pone uno útil (Restos, bayas, objetos de tipo) si existen.</summary>
        public bool AutoHeldItems { get; }
        /// <summary>Mochila por defecto si el entrenador no trae la suya.</summary>
        public IReadOnlyList<(string itemId, int quantity)> DefaultBag { get; }

        public AiProfile(int level, string displayName, string description, MoveBrain brain, int mistakePercent, int itemUsePercent,
            HealStyle heal, int healBelowPercent, bool canSwitch, MovesetStyle moveset, bool synergies,
            bool useMachineMoves, bool useTutorMoves, bool useEggMoves, bool autoHeldItems,
            IReadOnlyList<(string itemId, int quantity)> defaultBag = null)
        {
            Level = Math.Max(MinLevel, Math.Min(MaxLevel, level));
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Nivel " + Level : displayName;
            Description = description ?? "";
            Brain = brain;
            MistakePercent = Math.Max(0, Math.Min(100, mistakePercent));
            ItemUsePercent = Math.Max(0, Math.Min(100, itemUsePercent));
            Heal = heal;
            HealBelowPercent = Math.Max(1, Math.Min(100, healBelowPercent));
            CanSwitch = canSwitch;
            Moveset = moveset == MovesetStyle.ByAi ? MovesetStyle.Balanced : moveset;
            Synergies = synergies;
            UseMachineMoves = useMachineMoves;
            UseTutorMoves = useTutorMoves;
            UseEggMoves = useEggMoves;
            AutoHeldItems = autoHeldItems;
            var bag = new List<(string, int)>();
            if (defaultBag != null)
                foreach (var (id, q) in defaultBag)
                    if (!string.IsNullOrWhiteSpace(id) && q > 0) bag.Add((id.Trim(), q));
            DefaultBag = bag;
        }

        /// <summary>El TrainerAi antiguo (3 valores) que corresponde a este nivel: para lo que aún lo usa.</summary>
        public TrainerAi LegacyAi => Brain == MoveBrain.Random ? TrainerAi.Random : Brain == MoveBrain.Expert ? TrainerAi.Expert : TrainerAi.Smart;

        /// <summary>Nivel equivalente a una IA antigua: novato = 1, listo = 2, experto = 4.</summary>
        public static int LevelFromLegacy(TrainerAi ai) => ai == TrainerAi.Random ? 1 : ai == TrainerAi.Expert ? 4 : 2;

        public static string ClassicName(int level)
        {
            switch (level)
            {
                case 1: return "Novato";
                case 2: return "Aficionado";
                case 3: return "Veterano";
                case 4: return "Élite";
                default: return "Campeón";
            }
        }

        /// <summary>Los 5 niveles clásicos (lo que se usa si el autor no creó sus fichas de nivel).</summary>
        public static AiProfile Classic(int level)
        {
            level = Math.Max(MinLevel, Math.Min(MaxLevel, level));
            switch (level)
            {
                case 1:
                    return new AiProfile(1, "Novato", "Jóvenes y Cazabichos: movimientos al azar, casi no usa objetos y nunca cambia.",
                        MoveBrain.Random, 0, 40, HealStyle.Simple, 20, false, MovesetStyle.Classic, false, false, false, false, false);
                case 2:
                    return new AiProfile(2, "Aficionado", "Entrenadores de ruta: prefiere lo eficaz, a veces se equivoca, cura simple. Usa movimientos de MT.",
                        MoveBrain.Aggressive, 20, 80, HealStyle.Simple, 25, false, MovesetStyle.Balanced, false, true, false, false, false,
                        new[] { ("potion", 1) });
                case 3:
                    return new AiProfile(3, "Veterano", "Entrenadores guay y Team Rocket: tipos y estados, cura solo si le sirve. MT y tutor.",
                        MoveBrain.Expert, 10, 100, HealStyle.Smart, 35, false, MovesetStyle.Balanced, true, true, true, false, false,
                        new[] { ("super_potion", 2) });
                case 4:
                    return new AiProfile(4, "Élite", "Líderes de gimnasio: movesets con sinergias, cambia de monstruo, objetos equipados y movimientos huevo.",
                        MoveBrain.Expert, 4, 100, HealStyle.Smart, 40, true, MovesetStyle.Strong, true, true, true, true, true,
                        new[] { ("hyper_potion", 2), ("full_heal", 1) });
                default:
                    return new AiProfile(5, "Campeón", "Alto Mando y Campeones: todo lo anterior, sin fallos al azar.",
                        MoveBrain.Expert, 0, 100, HealStyle.Smart, 45, true, MovesetStyle.Strong, true, true, true, true, true,
                        new[] { ("full_restore", 2), ("full_heal", 1) });
            }
        }
    }
}
