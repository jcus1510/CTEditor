using System;
using System.Collections.Generic;
using CTEditor.GameDefinition.Domain.Abilities;

namespace CTEditor.GameDefinition.Domain.Items
{
    /// <summary>
    /// LEGACY input: the old per-feature held-item fields. Only used to build items the old way (old assets, old code);
    /// <see cref="ItemLegacy"/> turns them into effect blocks, which is what the engine runs.
    /// </summary>
    public sealed class ItemExtras
    {
        public static readonly ItemExtras None = new ItemExtras();

        /// <summary>Multiplica estadísticas con condiciones (Cinta Elección: Ataque ×1,5; Chaleco Asalto: Def. Esp. ×1,5; Mineral Evolutivo...).</summary>
        public IReadOnlyList<ConditionalStat> StatMultipliers { get; set; } = Array.Empty<ConditionalStat>();
        /// <summary>Tras usar un movimiento, solo puede usar ese hasta que se retire (objetos Elección).</summary>
        public bool ChoiceLock { get; set; }
        /// <summary>% de sus PS máx. que pierde cada vez que hace daño (Vidasfera: 10).</summary>
        public float AttackRecoilPercent { get; set; }
        /// <summary>Con los PS al máximo, un golpe que lo debilitaría lo deja con 1 PS (Banda Focus; se gasta).</summary>
        public bool SurviveFromFullHp { get; set; }
        /// <summary>% de PS máx. que pierde quien le golpea con contacto (Casco Dentado: 16,67).</summary>
        public float ContactDamagePercent { get; set; }
        /// <summary>Cambios de etapa al recibir un golpe que cumpla las condiciones (Seguro Debilidad).</summary>
        public IReadOnlyList<OnHitStat> OnHitStats { get; set; } = Array.Empty<OnHitStat>();
        /// <summary>El objeto se gasta al activar OnHitStats.</summary>
        public bool OnHitConsumed { get; set; }
        /// <summary>Inmune a Tierra hasta que le golpeen (Globo Helio; se revienta).</summary>
        public bool AirBalloon { get; set; }
        /// <summary>No puede usar movimientos de estado (Chaleco Asalto).</summary>
        public bool BlocksStatusMoves { get; set; }
        /// <summary>+ índice de crítico (Periscopio: 1).</summary>
        public int CritStageBonus { get; set; }
        /// <summary>Multiplica la precisión de sus movimientos (Lupa: 1,1).</summary>
        public float AccuracyMultiplier { get; set; } = 1f;
        /// <summary>Multiplica la precisión de los que le atacan (Polvo Brillo: 0,9).</summary>
        public float EvasionMultiplier { get; set; } = 1f;
        /// <summary>Baya que reduce a la mitad un golpe MUY EFICAZ de ese tipo y se gasta (Baya Caoca: fire). «normal»: cualquier golpe Normal.</summary>
        public string ResistBerryType { get; set; } = "";
        /// <summary>Se cura de cualquier estado en cuanto lo sufre y se gasta (Baya Ziuela).</summary>
        public bool CuresAnyStatus { get; set; }
        /// <summary>Al final del turno le pone este estado si no tiene ninguno (Llamasfera: burn; Toxisfera: toxic).</summary>
        public string SelfStatusEndOfTurn { get; set; } = "";
        /// <summary>% de hacer retroceder con sus ataques (Roca del Rey: 10).</summary>
        public float FlinchChance { get; set; }
        /// <summary>Recupera este % del daño que hace (Campana Concha: 12,5).</summary>
        public float HealOnDamagePercent { get; set; }
        /// <summary>Turnos extra del clima que pone (Roca Lluvia/Calor/Lisa/Helada: 3).</summary>
        public int WeatherTurnsBonus { get; set; }
        /// <summary>Turnos extra de sus pantallas (Refleluz: 3).</summary>
        public int ScreenTurnsBonus { get; set; }
        /// <summary>Si es de tipo Veneno cura 1/16 por turno; si no, pierde 1/8 (Lodo Negro).</summary>
        public bool BlackSludge { get; set; }
        /// <summary>% de actuar el primero dentro de su prioridad (Garra Rápida: 20).</summary>
        public float QuickClawChance { get; set; }
        /// <summary>Multiplica el daño de sus golpes MUY EFICACES (Cinta Experto: 1,2).</summary>
        public float SuperEffectiveBoost { get; set; } = 1f;

        public bool DoesSomething => StatMultipliers.Count > 0 || ChoiceLock || AttackRecoilPercent > 0 || SurviveFromFullHp || ContactDamagePercent > 0
            || OnHitStats.Count > 0 || AirBalloon || BlocksStatusMoves || CritStageBonus != 0 || AccuracyMultiplier != 1f || EvasionMultiplier != 1f
            || ResistBerryType.Length > 0 || CuresAnyStatus || SelfStatusEndOfTurn.Length > 0 || FlinchChance > 0 || HealOnDamagePercent > 0
            || WeatherTurnsBonus != 0 || ScreenTurnsBonus != 0 || BlackSludge || QuickClawChance > 0 || SuperEffectiveBoost != 1f;
    }
}
