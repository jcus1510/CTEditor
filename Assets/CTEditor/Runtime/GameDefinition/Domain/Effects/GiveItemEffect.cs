namespace CTEditor.GameDefinition.Domain.Effects
{
    /// <summary>
    /// "Da un objeto". Por ahora el objeto se referencia por un id de texto, porque aún no construimos
    /// el tipo Item (cuando exista, pasará a un Id&lt;Item&gt; tipado). Mismo principio: dato declarativo,
    /// sin lógica; el dispatcher lo traduce a un comando de Inventory.
    /// </summary>
    public sealed class GiveItemEffect : IEffect
    {
        public string ItemId { get; }
        public int Quantity { get; }

        public GiveItemEffect(string itemId, int quantity)
        {
            ItemId = itemId;
            Quantity = quantity;
        }
    }
}
