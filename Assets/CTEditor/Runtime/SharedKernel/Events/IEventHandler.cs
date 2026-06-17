namespace CTEditor.SharedKernel.Events
{
    /// <summary>
    /// Un oyente del "tablón de anuncios": reacciona a un tipo concreto de evento.
    /// Lo implementan las políticas/proyecciones que escuchan (A.6).
    /// </summary>
    public interface IEventHandler<in TEvent> where TEvent : IDomainEvent
    {
        void Handle(TEvent domainEvent);
    }
}
