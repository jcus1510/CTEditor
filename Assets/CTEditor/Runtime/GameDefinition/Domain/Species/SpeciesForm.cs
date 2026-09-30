using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.GameDefinition.Domain.Abilities;

namespace CTEditor.GameDefinition.Domain.Species
{
    /// <summary>
    /// Una FORMA DE COMBATE de una especie (Modo Daruma, Aegislash Filo, Meloetta Danza, Giratina Origen, las megas...):
    /// el mismo individuo cambia EN COMBATE de tipos, estadísticas base o habilidad, y al acabar vuelve a su forma
    /// normal. Lo que no se indica, se queda como en la especie. Los PS máximos nunca cambian en combate.
    ///
    /// (Las VARIANTES que se eligen fuera del combate —Rotom, Deoxys, las regionales— no son formas: son especies
    /// propias enlazadas con «es forma de», ver <see cref="Species.FormOf"/>.)
    /// </summary>
    public sealed class SpeciesForm
    {
        /// <summary>Id de la forma dentro de su especie (p. ej. "zen", "blade", "mega").</summary>
        public string Id { get; }
        public string DisplayName { get; }
        /// <summary>Tipos de la forma. Vacía = los de la especie.</summary>
        public IReadOnlyList<Id<ElementType>> Types { get; }
        /// <summary>Estadísticas base de la forma. Null = las de la especie.</summary>
        public StatBlock BaseStats { get; }
        /// <summary>Habilidad de la forma. Null = la del individuo.</summary>
        public AbilityId? Ability { get; }
        /// <summary>true = al retirarse vuelve a la forma normal (Modo Daruma, Aegislash). false = se queda hasta el final.</summary>
        public bool RevertsOnSwitch { get; }

        public SpeciesForm(string id, string displayName, IReadOnlyList<Id<ElementType>> types = null, StatBlock baseStats = null,
            AbilityId? ability = null, bool revertsOnSwitch = false)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Una forma necesita id.", nameof(id));
            Id = id.Trim();
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Id : displayName;
            Types = types == null ? Array.Empty<Id<ElementType>>() : new List<Id<ElementType>>(types);
            BaseStats = baseStats;
            Ability = ability;
            RevertsOnSwitch = revertsOnSwitch;
        }
    }

    /// <summary>Qué provoca un cambio de forma en combate.</summary>
    public enum FormTrigger
    {
        /// <summary>Lleva un objeto (Giratina con el Griseo Orbe, Arceus con una tabla): al entrar al campo.</summary>
        HeldItem = 0,
        /// <summary>Usa un movimiento concreto (Meloetta con Canto Arcaico, Aegislash con Escudo Real).</summary>
        UseMove = 1,
        /// <summary>Usa cualquier movimiento que haga daño (Aegislash pasa a Filo).</summary>
        DamagingMove = 2,
        /// <summary>Sus PS bajan de un % (Modo Daruma por debajo del 50 %): al final del turno.</summary>
        HpBelow = 3,
        /// <summary>Sus PS están en un % o más (vuelve de Modo Daruma): al final del turno.</summary>
        HpAtLeast = 4,
        /// <summary>Hace un clima (Castform, Cherrim). Vacío = sin clima. Al entrar y al final del turno.</summary>
        Weather = 5,
        /// <summary>Megaevolución: la pide el entrenador con su megapiedra (necesita la mecánica activa en las reglas).</summary>
        MegaEvolution = 6,
    }

    /// <summary>
    /// Una REGLA de cambio de forma: «de la forma X pasa a la forma Y cuando...». "" = la forma normal; "*" en 'From' =
    /// desde cualquier forma. Si pide habilidad, solo funciona con esa habilidad activa (Cambio Táctico, Modo Daruma).
    /// </summary>
    public sealed class FormChange
    {
        public const string AnyForm = "*";

        public string From { get; }
        public string To { get; }
        public FormTrigger Trigger { get; }
        /// <summary>Objeto (HeldItem, MegaEvolution: la megapiedra).</summary>
        public string Item { get; }
        /// <summary>Movimiento (UseMove).</summary>
        public string Move { get; }
        /// <summary>% de PS (HpBelow, HpAtLeast).</summary>
        public int HpPercent { get; }
        /// <summary>Clima (Weather). Vacío = sin clima.</summary>
        public string Weather { get; }
        /// <summary>Habilidad necesaria (vacío = ninguna).</summary>
        public string RequiredAbility { get; }
        /// <summary>Con movimientos: true = cambia DESPUÉS de usarlo (Meloetta); false = antes (Aegislash).</summary>
        public bool AfterMove { get; }

        public FormChange(string from, string to, FormTrigger trigger, string item = "", string move = "", int hpPercent = 50,
            string weather = "", string requiredAbility = "", bool afterMove = false)
        {
            From = from?.Trim() ?? "";
            To = to?.Trim() ?? "";
            Trigger = trigger;
            Item = item?.Trim() ?? "";
            Move = move?.Trim() ?? "";
            HpPercent = Math.Max(0, Math.Min(100, hpPercent));
            Weather = weather?.Trim() ?? "";
            RequiredAbility = requiredAbility?.Trim() ?? "";
            AfterMove = afterMove;
        }

        /// <summary>¿Se aplica estando en la forma 'current' ("" = normal)?</summary>
        public bool AppliesFrom(string current)
            => From == AnyForm ? !string.Equals(current ?? "", To, StringComparison.OrdinalIgnoreCase)
                               : string.Equals(current ?? "", From, StringComparison.OrdinalIgnoreCase);
    }
}
