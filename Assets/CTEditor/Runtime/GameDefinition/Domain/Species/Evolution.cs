using System;
using CTEditor.SharedKernel.ValueObjects;

namespace CTEditor.GameDefinition.Domain.Species
{
    /// <summary>
    /// Una evolución posible de una especie: EN QUÉ se convierte y BAJO QUÉ CONDICIÓN.
    ///
    /// Una especie puede tener VARIAS evoluciones (piensa en criaturas que ramifican según el
    /// caso), por eso Species guarda una lista de Evolution.
    ///
    /// SEAM A FUTURO (importante para tu meta de libertad): por ahora la condición es solo
    /// "alcanzar el nivel N", que es el caso más común. Cuando construyamos el sistema de
    /// Condiciones componibles (Parte E: ICondition), la evolución por objeto, por amistad,
    /// por intercambio, etc., entrarán reemplazando este 'RequiredLevel' por una condición
    /// general. No lo abrimos hoy para no construir media pieza (J.6: añade la frontera cuando
    /// duela no tenerla); lo dejamos honesto y listo para crecer.
    /// </summary>
    public sealed class Evolution
    {
        /// <summary>La especie resultante, referenciada por id (M.4).</summary>
        public Id<Species> Target { get; }

        /// <summary>Nivel mínimo para que la evolución pueda dispararse.</summary>
        public int RequiredLevel { get; }

        public Evolution(Id<Species> target, int requiredLevel)
        {
            if (requiredLevel < 1)
                throw new ArgumentOutOfRangeException(nameof(requiredLevel), "El nivel de evolución empieza en 1.");
            Target = target;
            RequiredLevel = requiredLevel;
        }
    }
}
