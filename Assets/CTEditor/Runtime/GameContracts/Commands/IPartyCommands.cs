namespace CTEditor.GameContracts.Commands
{
    /// <summary>
    /// El contrato DELGADO de comandos que Party expone a otros contextos (E.2 / J.3). Vive aquí, en
    /// GameContracts (el "vocabulario neutral" que cruza contextos), por una razón precisa: el
    /// EffectDispatcher (en Eventing) necesita pedirle a Party "cura el equipo" SIN referenciar a
    /// Party. La interfaz está en terreno neutral; Party la IMPLEMENTA; Eventing la USA. Ninguno
    /// referencia al otro. Por eso nace GameContracts ahora: este es el primer momento en que dos
    /// contextos necesitan hablarse.
    ///
    /// 🟥 Mantener esta interfaz DELGADA y estable (E.2): si crece sin control, el acoplamiento vuelve
    /// por la puerta de atrás.
    /// </summary>
    public interface IPartyCommands
    {
        /// <summary>Restaura PS a cada miembro del equipo.</summary>
        void HealParty(int amount);
    }
}
