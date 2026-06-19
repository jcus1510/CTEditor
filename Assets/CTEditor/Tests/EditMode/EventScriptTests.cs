using System.Collections.Generic;
using NUnit.Framework;
using CTEditor.GameDefinition.Domain.Effects;
using CTEditor.GameContracts.Commands;
using CTEditor.Eventing.Domain;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// Valida la ESPINA DE EFECTOS (I.3): un EventScript compuesto de efectos declarativos se ejecuta
    /// a través del dispatcher y termina aplicándose en los contextos dueños — sin que el script sepa
    /// nada de cómo se cura un equipo o se da un objeto. Eso es "componer comportamiento sin código".
    ///
    /// Los "fakes" de abajo hacen de doble de los manejadores de comandos reales (que vivirán en las
    /// capas de aplicación de Party e Inventory). Para probar la espina, basta con que registren que
    /// el comando llegó: el cableado efecto -> comando es lo que estamos verificando.
    /// </summary>
    public sealed class EventScriptTests
    {
        [Test]
        public void EventScript_heals_party_and_gives_item_through_commands()
        {
            var party = new FakePartyCommands();
            var inventory = new FakeInventoryCommands();
            var dispatcher = new EffectDispatcher(party, inventory);

            // El "contenido" que un autor compondría: curar 20 y dar 1 poción. Aquí en código;
            // en producción vendría de un asset.
            var script = new EventScript(new IEffect[]
            {
                new HealPartyEffect(20),
                new GiveItemEffect("potion", 1)
            });

            dispatcher.Run(script);

            // El efecto de cura terminó en el comando de Party.
            Assert.AreEqual(20, party.LastHealAmount);
            // El efecto de objeto terminó en el comando de Inventory.
            Assert.IsTrue(inventory.Given.ContainsKey("potion"));
            Assert.AreEqual(1, inventory.Given["potion"]);
        }

        // --- Dobles de prueba (stand-ins de los handlers reales de Party/Inventory) ---

        private sealed class FakePartyCommands : IPartyCommands
        {
            public int LastHealAmount { get; private set; } = -1;
            public void HealParty(int amount) => LastHealAmount = amount;
        }

        private sealed class FakeInventoryCommands : IInventoryCommands
        {
            public Dictionary<string, int> Given { get; } = new Dictionary<string, int>();
            public void GiveItem(string itemId, int quantity)
            {
                Given.TryGetValue(itemId, out var current);
                Given[itemId] = current + quantity;
            }
        }
    }
}
