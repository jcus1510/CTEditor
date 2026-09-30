using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Types;

namespace CTEditor.GameDefinition.Domain.Status
{
    /// <summary>
    /// La FICHA de un estado alterado (E.4): contenido autorable que describe CÓMO se comporta un
    /// estado, sin que el combate lo tenga hardcodeado. "Quemado" no es un 'if' en el código: es esta
    /// ficha. Inventar un estado nuevo = rellenar una de estas.
    ///
    /// Ahora cubre el repertorio clásico completo y deja inventar libremente:
    /// - daño por turno (fijo o PROGRESIVO, como el tóxico que escala cada turno);
    /// - probabilidad de impedir la acción (parálisis, sueño, congelado);
    /// - modificadores PASIVOS de stats (quemado baja Ataque, parálisis baja Velocidad);
    /// - duración en turnos (el sueño se va solo tras N turnos; 0 = permanente hasta curar).
    /// </summary>
    public sealed class StatusConditionDefinition
    {
        public StatusId Id { get; }
        public string DisplayName { get; }

        /// <summary>
        /// Daño por turno, como % de los PS MÁXIMOS. Si ProgressiveResidual es true, este es el % del
        /// PRIMER turno y se multiplica por el número de turnos transcurridos (tóxico: n×base).
        /// </summary>
        public Percentage ResidualDamagePercent { get; }

        /// <summary>Si true, el "daño" residual en realidad CURA cada turno (regeneración).</summary>
        public bool ResidualHeals { get; }

        /// <summary>Si true, el daño residual escala con los turnos activos (envenenamiento grave).</summary>
        public bool ProgressiveResidual { get; }

        /// <summary>Probabilidad por turno de no poder actuar (parálisis/sueño/congelado). 0% = nunca.</summary>
        public Percentage ActionPreventionChance { get; }

        /// <summary>Modificadores pasivos de stats mientras el estado dura (p.ej. Attack ×0.5).</summary>
        public IReadOnlyList<StatPassiveModifier> PassiveModifiers { get; }

        /// <summary>Duración en turnos. 0 = permanente (hasta curar). &gt;0 = se quita solo tras esos turnos.</summary>
        public int DurationTurns { get; }

        /// <summary>Si true, el estado se quita al cambiar de monstruo (estados volátiles).</summary>
        public bool ClearedOnSwitch { get; }

        /// <summary>Probabilidad por turno de recuperarse solo (dormido/confuso despiertan al azar). 0% = solo por duración.</summary>
        public Percentage RecoveryChancePerTurn { get; }

        /// <summary>Si el estado impide la acción, daño que el portador se hace a sí mismo (% de PS máx). Confusión &gt; 0; dormido = 0.</summary>
        public Percentage SelfDamageOnPreventedPercent { get; }

        /// <summary>Al terminar, en vez de curarse se CONVIERTE en este estado (somnoliento -&gt; dormido). Null = se cura.</summary>
        public StatusId? TransformsToStatus { get; }

        /// <summary>
        /// Tipos que NO pueden sufrir este estado (clásico: Fuego no se quema, Eléctrico no se paraliza,
        /// Veneno y Acero no se envenenan, Hielo no se congela). Vacío = cualquiera puede sufrirlo.
        /// </summary>
        public IReadOnlyList<Id<ElementType>> ImmuneTypes { get; }

        // ---------------- Lote B: estados VOLÁTILES y comportamientos especiales ----------------

        /// <summary>
        /// false = estado PRINCIPAL (quemado, parálisis, sueño...): solo uno a la vez y no se pisan.
        /// true = VOLÁTIL (confusión, atrapado, drenadoras...): se apila con el principal y con otros
        /// volátiles distintos. Al retirarse del combate, los volátiles se van.
        /// </summary>
        public bool IsVolatile { get; }

        /// <summary>Si &gt; DurationTurns, la duración se sortea entre DurationTurns y este valor (Atadura: 4-5).</summary>
        public int DurationMaxTurns { get; }

        /// <summary>El portador no puede cambiarse ni huir (Atadura, Mal de Ojo).</summary>
        public bool PreventsSwitch { get; }

        /// <summary>Los movimientos del RIVAL dirigidos al portador fallan (Protección). Se usa con duración 1.</summary>
        public bool BlocksIncomingMoves { get; }

        /// <summary>Un golpe que lo debilitaría lo deja con 1 PS (Aguante).</summary>
        public bool SurvivesLethalHit { get; }

        /// <summary>El daño residual CURA al rival que está en el campo (Drenadoras).</summary>
        public bool ResidualHealsOpponent { get; }

        /// <summary>
        /// Si se aplica turnos SEGUIDOS, cada vez es más difícil (Protección clásica: 1/3, 1/9...).
        /// Solo para estados que el portador se pone a sí mismo.
        /// </summary>
        public bool HarderWhenRepeated { get; }

        /// <summary>
        /// Cuánto facilita la CAPTURA tener este estado (×1 = nada). Clásico: dormido y congelado ×2,5;
        /// paralizado, envenenado y quemado ×1,5.
        /// </summary>
        public float CatchMultiplier { get; }

        /// <summary>Comportamientos de la 3.ª y 4.ª generación (Mofa, Canto Mortal, Carga...). Nunca null.</summary>
        public StatusExtras Extras { get; }

        public StatusConditionDefinition(
            StatusId id,
            string displayName,
            Percentage residualDamagePercent,
            Percentage actionPreventionChance,
            bool clearedOnSwitch = false,
            IReadOnlyList<StatPassiveModifier> passiveModifiers = null,
            bool progressiveResidual = false,
            int durationTurns = 0,
            Percentage recoveryChancePerTurn = default,
            Percentage selfDamageOnPreventedPercent = default,
            StatusId? transformsToStatus = null,
            bool residualHeals = false,
            IReadOnlyList<Id<ElementType>> immuneTypes = null,
            bool isVolatile = false,
            int durationMaxTurns = 0,
            bool preventsSwitch = false,
            bool blocksIncomingMoves = false,
            bool survivesLethalHit = false,
            bool residualHealsOpponent = false,
            bool harderWhenRepeated = false,
            float catchMultiplier = 1f,
            StatusExtras extras = null)
        {
            Extras = extras ?? StatusExtras.None;
            IsVolatile = isVolatile;
            DurationMaxTurns = durationMaxTurns;
            PreventsSwitch = preventsSwitch;
            BlocksIncomingMoves = blocksIncomingMoves;
            SurvivesLethalHit = survivesLethalHit;
            ResidualHealsOpponent = residualHealsOpponent;
            HarderWhenRepeated = harderWhenRepeated;
            CatchMultiplier = catchMultiplier <= 0f ? 1f : catchMultiplier;
            ImmuneTypes = immuneTypes == null ? Array.Empty<Id<ElementType>>() : new List<Id<ElementType>>(immuneTypes);
            Id = id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id.Value : displayName;
            ResidualDamagePercent = residualDamagePercent;
            ActionPreventionChance = actionPreventionChance;
            ClearedOnSwitch = clearedOnSwitch;
            ProgressiveResidual = progressiveResidual;
            DurationTurns = durationTurns < 0 ? 0 : durationTurns;
            RecoveryChancePerTurn = recoveryChancePerTurn;
            SelfDamageOnPreventedPercent = selfDamageOnPreventedPercent;
            TransformsToStatus = transformsToStatus;
            ResidualHeals = residualHeals;
            // Copia defensiva: la ficha es inmutable.
            PassiveModifiers = passiveModifiers == null
                ? Array.Empty<StatPassiveModifier>()
                : new List<StatPassiveModifier>(passiveModifiers);
        }
    }
}
