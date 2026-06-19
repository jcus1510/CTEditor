using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.Events;

namespace CTEditor.Bootstrap.Platform
{
    /// <summary>
    /// El "tablón de anuncios" concreto (A.6): la implementación real de IEventBus. Quien publica no
    /// sabe quién escucha; quien escucha no sabe quién publicó. Vive en Platform (el borde): es un
    /// detalle de infraestructura que el Bootstrap inyecta. El dominio solo conoce la interfaz.
    ///
    /// Es C# puro (sin Unity), pero lo dejamos aquí porque es donde el composition root lo ensambla.
    ///
    /// Detalle técnico: al publicar despachamos por el tipo REAL del evento (GetType()), no por el
    /// genérico del sitio de llamada. Así, aunque recorras una lista de IDomainEvent y publiques cada
    /// uno, cada suscriptor recibe los suyos correctamente.
    /// </summary>
    public sealed class EventBus : IEventBus
    {
        // Tipo de evento -> lista de handlers. Cada handler se guarda envuelto como Action<IDomainEvent>
        // que castea al tipo concreto al despachar.
        private readonly Dictionary<Type, List<Action<IDomainEvent>>> _handlers
            = new Dictionary<Type, List<Action<IDomainEvent>>>();

        public void Publish<TEvent>(TEvent domainEvent) where TEvent : IDomainEvent
        {
            if (domainEvent == null) return;

            var type = domainEvent.GetType(); // tipo REAL, no el genérico
            if (!_handlers.TryGetValue(type, out var list)) return;

            // Copiamos la lista para que un handler pueda des-suscribirse durante el despacho sin romper el bucle.
            foreach (var handler in list.ToArray())
                handler(domainEvent);
        }

        public IDisposable Subscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IDomainEvent
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            return Subscribe<TEvent>(handler.Handle);
        }

        public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : IDomainEvent
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            var type = typeof(TEvent);
            // El envoltorio castea el IDomainEvent genérico al TEvent concreto que espera el handler.
            Action<IDomainEvent> wrapper = e => handler((TEvent)e);

            if (!_handlers.TryGetValue(type, out var list))
            {
                list = new List<Action<IDomainEvent>>();
                _handlers[type] = list;
            }
            list.Add(wrapper);

            // Devolvemos un IDisposable: hacer Dispose() cancela la suscripción.
            return new Subscription(list, wrapper);
        }

        private sealed class Subscription : IDisposable
        {
            private List<Action<IDomainEvent>> _list;
            private Action<IDomainEvent> _wrapper;

            public Subscription(List<Action<IDomainEvent>> list, Action<IDomainEvent> wrapper)
            {
                _list = list;
                _wrapper = wrapper;
            }

            public void Dispose()
            {
                if (_list == null) return;
                _list.Remove(_wrapper);
                _list = null;
                _wrapper = null;
            }
        }
    }
}
