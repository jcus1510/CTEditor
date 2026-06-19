namespace CTEditor.GameContracts.Commands
{
    /// <summary>
    /// El contrato delgado de comandos de Inventory. Igual que IPartyCommands: el dispatcher lo usa,
    /// Inventory lo implementa, en terreno neutral.
    /// </summary>
    public interface IInventoryCommands
    {
        /// <summary>Añade una cantidad de un objeto a la mochila.</summary>
        void GiveItem(string itemId, int quantity);
    }
}
