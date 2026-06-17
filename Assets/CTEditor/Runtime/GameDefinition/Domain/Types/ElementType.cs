using CTEditor.SharedKernel.ValueObjects;

namespace CTEditor.GameDefinition.Domain.Types
{
    /// <summary>
    /// Un TIPO elemental (Fuego, Agua, Planta... o cualquiera que el autor invente).
    ///
    /// En este motor los tipos son CONTENIDO, no algo cableado en el código: por eso un
    /// ElementType no es un enum fijo, sino una ficha con un id estable y un nombre visible.
    /// "Abrir los tipos" era barato y muy potente (Parte D), así que lo hacemos desde el inicio.
    ///
    /// 'sealed' = nadie hereda de esta clase (no la pensamos extender).
    /// Es un POCO puro: ni una mención a Unity (vive en un assembly con "No Engine References").
    /// </summary>
    public sealed class ElementType
    {
        // Propiedades de SOLO LECTURA ({ get; } sin set): se fijan en el constructor y ya no cambian.
        // Identificamos el tipo por un Id<ElementType> ESTABLE (M.4), no por la referencia del objeto:
        // así un guardado o una tabla de tipos sigue siendo válido aunque muevas o renombres el asset.
        public Id<ElementType> Id { get; }

        public string DisplayName { get; }

        public ElementType(Id<ElementType> id, string displayName)
        {
            Id = id;
            // Si no dan nombre visible, caemos al texto del id para no quedar con cadena vacía.
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id.Value : displayName;
        }
    }
}
