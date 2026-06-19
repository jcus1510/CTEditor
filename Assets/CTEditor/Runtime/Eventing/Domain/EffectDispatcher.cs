using System;
using CTEditor.GameDefinition.Domain.Effects;
using CTEditor.GameContracts.Commands;

namespace CTEditor.Eventing.Domain
{
    /// <summary>
    /// EL CABLEADO (E.2): el ÚNICO lugar que sabe qué comando corresponde a cada efecto. Recibe un
    /// efecto declarativo, mira su tipo, y llama al comando del contexto dueño a través de su interfaz
    /// delgada. Es lo que permite que el motor de efectos escale sin convertirse en un dios-sistema:
    /// el efecto no toca nada por su cuenta; pasa por la puerta del agregado dueño (que valida sus
    /// invariantes).
    ///
    /// Clave: NO referencia a Party ni a Inventory. Solo a sus INTERFACES (IPartyCommands,
    /// IInventoryCommands), cuyas implementaciones se le INYECTAN. El Bootstrap conectará las
    /// implementaciones reales; aquí el dispatcher es puro y agnóstico.
    /// </summary>
    public sealed class EffectDispatcher
    {
        private readonly IPartyCommands _party;
        private readonly IInventoryCommands _inventory;

        public EffectDispatcher(IPartyCommands party, IInventoryCommands inventory)
        {
            _party = party ?? throw new ArgumentNullException(nameof(party));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        }

        /// <summary>Traduce UN efecto a su comando. El 'switch' por tipo es el "mapa" de E.2.</summary>
        public void Dispatch(IEffect effect)
        {
            if (effect == null) throw new ArgumentNullException(nameof(effect));

            switch (effect)
            {
                case HealPartyEffect heal:
                    _party.HealParty(heal.Amount);
                    break;
                case GiveItemEffect give:
                    _inventory.GiveItem(give.ItemId, give.Quantity);
                    break;
                default:
                    throw new NotSupportedException($"Efecto no soportado por el dispatcher: {effect.GetType().Name}");
            }
        }

        /// <summary>Ejecuta un EventScript completo, efecto por efecto, en orden.</summary>
        public void Run(EventScript script)
        {
            if (script == null) throw new ArgumentNullException(nameof(script));
            foreach (var effect in script.Effects)
                Dispatch(effect);
        }
    }
}
