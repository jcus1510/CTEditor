using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;

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

        public StatusConditionDefinition(
            StatusId id,
            string displayName,
            Percentage residualDamagePercent,
            Percentage actionPreventionChance,
            bool clearedOnSwitch = false,
            IReadOnlyList<StatPassiveModifier> passiveModifiers = null,
            bool progressiveResidual = false,
            int durationTurns = 0)
        {
            Id = id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id.Value : displayName;
            ResidualDamagePercent = residualDamagePercent;
            ActionPreventionChance = actionPreventionChance;
            ClearedOnSwitch = clearedOnSwitch;
            ProgressiveResidual = progressiveResidual;
            DurationTurns = durationTurns < 0 ? 0 : durationTurns;
            // Copia defensiva: la ficha es inmutable.
            PassiveModifiers = passiveModifiers == null
                ? Array.Empty<StatPassiveModifier>()
                : new List<StatPassiveModifier>(passiveModifiers);
        }
    }
}
