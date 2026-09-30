using System;
using System.Collections.Generic;

namespace CTEditor.GameDefinition.Domain.Trainers
{
    /// <summary>Cómo elige el MOVIMIENTO cada turno. Solo se añaden valores al final.</summary>
    public enum MoveBrain
    {
        Random,      // al azar entre los que puede usar
        Aggressive,  // el que más daño hace según los tipos (evita lo que no afecta)
        Expert,      // daño real (etapas, habilidades, clima), remata, estados y mejoras con cabeza
        Predictor    // como el experto, pero ANTICIPA tu jugada: cambia al que resiste tu golpe, castiga tus cambios
    }

    /// <summary>
    /// QUÉ SABE DE TI (bloque «Conocimiento»). Solo se añaden valores al final.
    /// Un buen jugador solo sabe lo que ha visto; el nivel Injusto lo sabe todo.
    /// </summary>
    public enum AiKnowledge
    {
        None,       // solo ve tu Pokémon (especie, tipos y barra de PS): supone ataques de su tipo
        Battle,     // además recuerda lo visto EN ESTE combate: tus movimientos y cuánto daño hacen / reciben
        Memory,     // y lo GUARDA para la revancha: movimientos vistos e IVs/EVs estimados por el daño
        Omniscient  // lo sabe TODO desde el principio: movimientos, IVs, EVs y naturaleza (injusto)
    }

    /// <summary>Objetos equipados que reparte a su equipo (bloque «Equipo»). Solo se añaden valores al final.</summary>
    public enum HeldItemStyle
    {
        None,        // ninguno (salvo los escritos por el autor)
        Basic,       // útiles: Restos, bayas, objetos que potencian su tipo
        Competitive  // de competición: Elección, Vidasfera, Banda Focus, Chaleco Asalto, Mineral Evolutivo...
    }

    /// <summary>Cuándo se cura con objetos. Solo se añaden valores al final.</summary>
    public enum HealStyle
    {
        Never,   // nunca se cura
        Simple,  // se cura en cuanto baja del umbral
        Smart    // solo si le sirve: si el rival lo tumba igual (o le quita más de lo que cura), ataca
    }

    /// <summary>
    /// UN NIVEL DE IA (1 a 7), editable por el autor. Se arma con CUATRO BLOQUES que se combinan libremente:
    ///
    ///   🧠 Conocimiento  qué sabe de ti: nada · lo visto en el combate · memoria entre combates · todo.
    ///   🎯 Decisión      cómo elige: al azar · lo más eficaz · experto (daño real) · predictor (anticipa).
    ///   🛡️ Gestión       curación (nunca / simple / inteligente), cambios de Pokémon, % de despistes.
    ///   🎒 Equipo        movimientos (clásico → MT → tutor → huevo → competitivo), objetos equipados,
    ///                    entrenamiento (IVs/EVs/naturaleza de competición) y mochila por defecto.
    ///
    /// Las 7 plantillas clásicas:
    ///   1 Novato · 2 Aficionado · 3 Veterano · 4 Élite · 5 Campeón · 6 Maestro · 7 Injusto.
    /// El entrenador elige su nivel y puede ajustar lo suyo (mochila, umbral de cura, si cambia...).
    /// </summary>
    public sealed class AiProfile
    {
        public const int MinLevel = 1, MaxLevel = 7;

        /// <summary>Nivel 1-7.</summary>
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
        /// <summary>A los miembros sin objeto equipado les pone uno (ver HeldItems).</summary>
        public bool AutoHeldItems => HeldItems != HeldItemStyle.None;
        /// <summary>Qué objetos equipados reparte: ninguno, básicos o de competición.</summary>
        public HeldItemStyle HeldItems { get; }
        /// <summary>Qué sabe de tu equipo (ver AiKnowledge).</summary>
        public AiKnowledge Knowledge { get; }
        /// <summary>Entrenamiento de competición: IVs perfectos, 252 EVs en sus dos mejores estadísticas y naturaleza a juego.</summary>
        public bool CompetitiveTraining { get; }
        /// <summary>% de turnos en que actúa según su PREDICCIÓN de tu jugada (solo con Decisión = Predictor).</summary>
        public int PredictPercent { get; }
        /// <summary>Mochila por defecto si el entrenador no trae la suya.</summary>
        public IReadOnlyList<(string itemId, int quantity)> DefaultBag { get; }

        public AiProfile(int level, string displayName, string description, MoveBrain brain, int mistakePercent, int itemUsePercent,
            HealStyle heal, int healBelowPercent, bool canSwitch, MovesetStyle moveset, bool synergies,
            bool useMachineMoves, bool useTutorMoves, bool useEggMoves, bool autoHeldItems,
            IReadOnlyList<(string itemId, int quantity)> defaultBag = null,
            AiKnowledge knowledge = AiKnowledge.Battle, HeldItemStyle? heldItems = null, bool competitiveTraining = false,
            int predictPercent = 0)
        {
            Knowledge = knowledge;
            HeldItems = heldItems ?? (autoHeldItems ? HeldItemStyle.Basic : HeldItemStyle.None);
            CompetitiveTraining = competitiveTraining;
            PredictPercent = Math.Max(0, Math.Min(100, predictPercent));
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
            var bag = new List<(string, int)>();
            if (defaultBag != null)
                foreach (var (id, q) in defaultBag)
                    if (!string.IsNullOrWhiteSpace(id) && q > 0) bag.Add((id.Trim(), q));
            DefaultBag = bag;
        }

        /// <summary>El TrainerAi antiguo (3 valores) que corresponde a este nivel: para lo que aún lo usa.</summary>
        public TrainerAi LegacyAi => Brain == MoveBrain.Random ? TrainerAi.Random : Brain == MoveBrain.Aggressive ? TrainerAi.Smart : TrainerAi.Expert;

        /// <summary>¿Piensa con el daño real (experto o predictor)?</summary>
        public bool ThinksLikeExpert => Brain == MoveBrain.Expert || Brain == MoveBrain.Predictor;

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
                case 5: return "Campeón";
                case 6: return "Maestro";
                default: return "Injusto";
            }
        }

        /// <summary>Los 7 niveles clásicos (lo que se usa si el autor no creó sus fichas de nivel).</summary>
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
                        new[] { ("hyper_potion", 2), ("full_heal", 1) }, AiKnowledge.Battle, HeldItemStyle.Basic);
                case 5:
                    return new AiProfile(5, "Campeón", "Alto Mando y Campeones: sin fallos y te RECUERDA: en la revancha conoce tus movimientos y tus IVs/EVs estimados.",
                        MoveBrain.Expert, 0, 100, HealStyle.Smart, 45, true, MovesetStyle.Strong, true, true, true, true, true,
                        new[] { ("full_restore", 2), ("full_heal", 1) }, AiKnowledge.Memory, HeldItemStyle.Basic);
                case 6:
                    return new AiProfile(6, "Maestro", "Jugador de competición: memoria, PREDICCIONES (cambia al que resiste tu golpe y castiga tus cambios), sets y objetos de competición.",
                        MoveBrain.Predictor, 0, 100, HealStyle.Smart, 45, true, MovesetStyle.Competitive, true, true, true, true, true,
                        new[] { ("full_restore", 2), ("full_heal", 1) }, AiKnowledge.Memory, HeldItemStyle.Competitive, true, 60);
                default:
                    return new AiProfile(7, "Injusto", "Sabe TODO desde el principio: tus movimientos, IVs y EVs. Predice siempre y juega con sets y objetos de competición.",
                        MoveBrain.Predictor, 0, 100, HealStyle.Smart, 50, true, MovesetStyle.Competitive, true, true, true, true, true,
                        new[] { ("full_restore", 3), ("full_heal", 2) }, AiKnowledge.Omniscient, HeldItemStyle.Competitive, true, 100);
            }
        }
    }
}
