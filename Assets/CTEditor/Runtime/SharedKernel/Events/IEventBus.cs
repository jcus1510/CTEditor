using System;

namespace CTEditor.SharedKernel.Events
{
    /// <summary>
    /// El "tablón de anuncios" (A.6). Quien publica no sabe quién escucha; quien escucha
    /// no sabe quién publicó. Esto es solo el CONTRATO; la implementación concreta vive en
    /// Bootstrap/Platform (J.4), no aquí. SharedKernel es mecanismo abstracto, no detalle.
    /// </summary>
    public interface IEventBus
    {
        /// <summary>Publica un hecho consumado. No puede rechazarse (A.7).</summary>
        void Publish<TEvent>(TEvent domainEvent) where TEvent : IDomainEvent;

        /// <summary>Suscribe un handler. El IDisposable devuelto cancela la suscripción.</summary>
        IDisposable Subscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IDomainEvent;

        /// <summary>Atajo para suscribir con una lambda en vez de una clase handler.</summary>
        IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : IDomainEvent;
    }
}
