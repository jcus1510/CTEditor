using CTEditor.SharedKernel.ValueObjects;

namespace CTEditor.GameDefinition.Domain.Status
{
    /// <summary>
    /// La FICHA de un estado alterado (E.4): contenido autorable que describe CÓMO se comporta un
    /// estado, sin que el combate lo tenga hardcodeado. "Quemado" no es un 'if' en el código: es esta
    /// ficha. Inventar un estado nuevo = rellenar una de estas.
    ///
    /// Esta es la versión inicial con los dos comportamientos más icónicos. Más adelante crecerá con
    /// modificadores pasivos de stats (p.ej. quemado baja el Ataque) y hooks OnApply/OnRemove.
    /// </summary>
    public sealed class StatusConditionDefinition
    {
        public StatusId Id { get; }
        public string DisplayName { get; }

        /// <summary>
        /// Daño residual por turno, como % de los PS MÁXIMOS (quemado ≈ 6.25%, veneno ≈ 12.5%).
        /// 0% = no hace daño por turno.
        /// </summary>
        public Percentage ResidualDamagePercent { get; }

        /// <summary>
        /// Probabilidad de que el portador NO pueda actuar en su turno (parálisis ≈ 25%, dormido alto,
        /// congelado muy alto). 0% = nunca impide actuar. Es una primera aproximación: el sueño con
        /// duración por turnos y el descongelarse llegarán con los hooks por turno.
        /// </summary>
        public Percentage ActionPreventionChance { get; }

        /// <summary>Si es true, el estado se limpia al cambiar de monstruo (estados "volátiles" como confusión).</summary>
        public bool ClearedOnSwitch { get; }

        public StatusConditionDefinition(
            StatusId id,
            string displayName,
            Percentage residualDamagePercent,
            Percentage actionPreventionChance,
            bool clearedOnSwitch = false)
        {
            Id = id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id.Value : displayName;
            ResidualDamagePercent = residualDamagePercent;
            ActionPreventionChance = actionPreventionChance;
            ClearedOnSwitch = clearedOnSwitch;
        }
    }
}
