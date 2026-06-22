namespace CTEditor.Battle.Domain
{
    /// <summary>
    /// Lo que un objeto HACE a un combatiente, en términos que Battle entiende — sin que Battle
    /// dependa del motor de efectos (Eventing) ni de las fichas de objetos (GameDefinition.Items).
    ///
    /// ¿Por qué una representación propia y no el IEffect general? Porque Battle.Domain es PURO y no
    /// referencia a Eventing. El orquestador (Bootstrap), que sí conoce ambos mundos, traduce la
    /// ficha del objeto (Poción, Revivir, Cura Total...) a este payload sencillo y lo mete en la
    /// acción. Battle solo lo aplica. Cubre lo clásico de combate: curar PS, revivir, quitar estado.
    /// </summary>
    public sealed class BattleItemEffect
    {
        /// <summary>PS a restaurar (absolutos). Para "cura total" basta un número grande: se recorta al máximo.</summary>
        public int HealAmount { get; }

        /// <summary>Si true, quita el estado alterado del objetivo (Cura Total, Antídoto...).</summary>
        public bool CuresStatus { get; }

        /// <summary>Si true, revive a un objetivo DEBILITADO (lo deja con HealAmount PS).</summary>
        public bool Revives { get; }

        public BattleItemEffect(int healAmount = 0, bool curesStatus = false, bool revives = false)
        {
            HealAmount = healAmount < 0 ? 0 : healAmount;
            CuresStatus = curesStatus;
            Revives = revives;
        }
    }
}
