using System;

namespace CTEditor.GameDefinition.Domain.Rules
{
    /// <summary>
    /// Reglas de la AVENTURA: lo que pasa alrededor de un combate (huir, capturar, dinero, derrota,
    /// amistad, repartir experiencia...). Viven dentro del <see cref="Ruleset"/> y cada perilla trae
    /// el valor CLÁSICO por defecto: si el autor no toca nada, el juego se comporta como un Pokémon
    /// de toda la vida. Cambiar una perilla = cambiar solo esa regla.
    ///
    /// Inmutable (como el Ruleset): se crea una vez y la partida juega con ella.
    /// </summary>
    public sealed class AdventureRules
    {
        // ---------------- Huir ----------------

        /// <summary>
        /// false (clásico) = huir de un salvaje usa la fórmula de los juegos: si eres igual o más rápido
        /// escapas siempre; si no, depende de la velocidad y de cuántas veces lo intentaste.
        /// true = huir de un salvaje funciona siempre.
        /// </summary>
        public bool FleeAlwaysWorks { get; }

        /// <summary>¿Se puede huir de un combate contra un ENTRENADOR? Clásico: no.</summary>
        public bool CanFleeTrainerBattles { get; }

        // ---------------- Capturar ----------------

        /// <summary>¿Se pueden capturar los monstruos de un entrenador? Clásico: no ("¡No seas ladrón!").</summary>
        public bool CanCatchTrainerMonsters { get; }

        /// <summary>Multiplicador GLOBAL de captura (1 = clásico; 2 = el doble de fácil).</summary>
        public float CatchRateMultiplier { get; }

        /// <summary>Si el equipo está lleno, ¿los capturados van al PC? (false = se liberan).</summary>
        public bool SendToBoxWhenFull { get; }

        // ---------------- Experiencia y amistad ----------------

        /// <summary>
        /// "Repartir Experiencia" para todo el equipo (Gen VI+). false (clásico) = solo quienes lucharon.
        /// true = los que no lucharon reciben <see cref="ExpShareOthersPercent"/> de lo que ganaría un luchador.
        /// </summary>
        public bool ExpShareAll { get; }
        public float ExpShareOthersPercent { get; }

        /// <summary>¿Al subir de nivel aprende los movimientos de su lista? Clásico: sí.</summary>
        public bool LearnMovesOnLevelUp { get; }

        /// <summary>¿Evoluciona al terminar el combate si cumple la condición? Clásico: sí (se puede cancelar).</summary>
        public bool EvolveAfterBattle { get; }

        /// <summary>Amistad que gana al subir un nivel. Clásico: +3 aprox.</summary>
        public int FriendshipPerLevelUp { get; }

        /// <summary>Amistad que pierde al debilitarse. Clásico: −1.</summary>
        public int FriendshipLostOnFaint { get; }

        // ---------------- Dinero y derrota ----------------

        /// <summary>Dinero con el que empieza una partida nueva. Clásico: 3000.</summary>
        public int StartingMoney { get; }

        /// <summary>% del dinero que se pierde al perder un combate (quedarse sin monstruos). Clásico: 50.</summary>
        public float MoneyLostOnBlackoutPercent { get; }

        /// <summary>¿Al perder, el equipo aparece curado en el Centro? Clásico: sí.</summary>
        public bool HealOnBlackout { get; }

        public AdventureRules(
            bool fleeAlwaysWorks = false,
            bool canFleeTrainerBattles = false,
            bool canCatchTrainerMonsters = false,
            float catchRateMultiplier = 1f,
            bool sendToBoxWhenFull = true,
            bool expShareAll = false,
            float expShareOthersPercent = 50f,
            bool learnMovesOnLevelUp = true,
            bool evolveAfterBattle = true,
            int friendshipPerLevelUp = 3,
            int friendshipLostOnFaint = 1,
            int startingMoney = 3000,
            float moneyLostOnBlackoutPercent = 50f,
            bool healOnBlackout = true)
        {
            FleeAlwaysWorks = fleeAlwaysWorks;
            CanFleeTrainerBattles = canFleeTrainerBattles;
            CanCatchTrainerMonsters = canCatchTrainerMonsters;
            CatchRateMultiplier = catchRateMultiplier <= 0f ? 1f : catchRateMultiplier;
            SendToBoxWhenFull = sendToBoxWhenFull;
            ExpShareAll = expShareAll;
            ExpShareOthersPercent = Math.Max(0f, Math.Min(100f, expShareOthersPercent));
            LearnMovesOnLevelUp = learnMovesOnLevelUp;
            EvolveAfterBattle = evolveAfterBattle;
            FriendshipPerLevelUp = Math.Max(0, friendshipPerLevelUp);
            FriendshipLostOnFaint = Math.Max(0, friendshipLostOnFaint);
            StartingMoney = Math.Max(0, startingMoney);
            MoneyLostOnBlackoutPercent = Math.Max(0f, Math.Min(100f, moneyLostOnBlackoutPercent));
            HealOnBlackout = healOnBlackout;
        }

        /// <summary>Las reglas clásicas (las que se usan si el autor no dice otra cosa).</summary>
        public static readonly AdventureRules Classic = new AdventureRules();
    }
}
