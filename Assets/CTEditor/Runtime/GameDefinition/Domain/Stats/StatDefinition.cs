namespace CTEditor.GameDefinition.Domain.Stats
{
    /// <summary>
    /// La definición autorable de una estadística: lo que hace que un stat "exista" en el juego.
    /// Los 6 clásicos vienen predefinidos; el autor registra StatDefinition nuevas para inventar.
    /// POCO puro (sin Unity). Por ahora es delgada a propósito (J.6): añadiremos campos —p.ej.
    /// "¿admite etapas de stat en combate?"— cuando esa parte llegue, no por si acaso.
    /// </summary>
    public sealed class StatDefinition
    {
        public StatId Id { get; }
        public string DisplayName { get; }

        public StatDefinition(StatId id, string displayName)
        {
            Id = id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id.Value : displayName;
        }
    }
}
