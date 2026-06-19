using System;
using System.Collections.Generic;
using CTEditor.GameDefinition.Domain.Effects;

namespace CTEditor.Eventing.Domain
{
    /// <summary>
    /// Un EventScript: una secuencia de efectos que se ejecutan en orden. Es lo que corre un NPC al
    /// hablarle, un trigger al pisarse, un objeto al usarse (E.1). Crear contenido nuevo = componer
    /// efectos, sin código.
    ///
    /// Por ahora es una lista plana de efectos. Las CONDICIONES por efecto ("aplica esto solo si...")
    /// y las ramificaciones llegan después (Parte E); las dejamos fuera para el mínimo del sprint.
    /// </summary>
    public sealed class EventScript
    {
        public IReadOnlyList<IEffect> Effects { get; }

        public EventScript(IReadOnlyList<IEffect> effects)
        {
            // Copia defensiva: el script no comparte su lista con quien lo construyó.
            Effects = effects == null
                ? Array.Empty<IEffect>()
                : new List<IEffect>(effects);
        }
    }
}
