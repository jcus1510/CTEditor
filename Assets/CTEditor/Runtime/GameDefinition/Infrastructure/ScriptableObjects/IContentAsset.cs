namespace CTEditor.GameDefinition.Infrastructure.ScriptableObjects
{
    /// <summary>
    /// Contrato común de toda ficha de contenido identificable (movimiento, especie, estado, curva,
    /// naturaleza...). Gracias a él, los editores y los desplegables funcionan con CUALQUIER tipo de
    /// ficha sin escribir código específico para cada uno (un solo desplegable genérico, una sola
    /// ventana base). Es "programar contra la interfaz, no contra la clase".
    /// </summary>
    public interface IContentAsset
    {
        /// <summary>Id estable: la clave con la que el resto del contenido referencia esta ficha.</summary>
        string Id { get; }

        /// <summary>Nombre legible para el autor/jugador.</summary>
        string DisplayName { get; }
    }
}
