using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.GameDefinition.Domain.Abilities;

namespace CTEditor.Battle.Domain
{
    /// <summary>
    /// Una forma de combate YA CALCULADA para un individuo (sus estadísticas con su nivel, IVs, EVs y naturaleza). La
    /// arma el orquestador al hacer la foto del combate (BattleParticipant), igual que las estadísticas normales: así
    /// el combate no necesita conocer la fórmula de crecimiento.
    /// </summary>
    public sealed class BattleForm
    {
        public string Id { get; }
        public string DisplayName { get; }
        /// <summary>Estadísticas de la forma (los PS son siempre los de la forma normal).</summary>
        public StatBlock Stats { get; }
        /// <summary>Tipos de la forma. Vacía = los de la forma normal.</summary>
        public IReadOnlyList<Id<ElementType>> Types { get; }
        /// <summary>Habilidad de la forma. Null = la de la forma normal.</summary>
        public AbilityId? Ability { get; }
        public bool RevertsOnSwitch { get; }

        public BattleForm(string id, string displayName, StatBlock stats, IReadOnlyList<Id<ElementType>> types = null,
            AbilityId? ability = null, bool revertsOnSwitch = false)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Una forma necesita id.", nameof(id));
            Id = id.Trim();
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Id : displayName;
            Stats = stats;
            Types = types == null ? Array.Empty<Id<ElementType>>() : new List<Id<ElementType>>(types);
            Ability = ability;
            RevertsOnSwitch = revertsOnSwitch;
        }
    }
}
