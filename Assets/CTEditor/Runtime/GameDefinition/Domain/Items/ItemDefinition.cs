using System;
using System.Collections.Generic;
using System.Linq;
using CTEditor.GameDefinition.Domain.Conditions;
using CTEditor.GameDefinition.Domain.Effects;

namespace CTEditor.GameDefinition.Domain.Items
{
    /// <summary>Para qué sirve un objeto (organiza la mochila y el editor). Solo se añaden valores AL FINAL.</summary>
    public enum ItemCategory
    {
        Medicine,     // curan PS (Poción)
        Revive,       // reviven (Revivir)
        StatusCure,   // curan estados (Antídoto, Cura Total)
        PpRestore,    // recuperan PP (Éter, Elixir)
        Ball,         // capturan (Poké Ball)
        Evolution,    // hacen evolucionar (Piedra Fuego)
        BattleBoost,  // suben stats en combate (Ataque X)
        Held,         // se equipan (Restos, Carbón, bayas)
        Vitamin,      // cambian amistad u otros valores fuera de combate
        Key,          // objetos clave (bicicleta, mapa...)
        Other,
        Machine,      // MT / MO (enseñan un movimiento)
        Berry,        // bayas
    }

    /// <summary>
    /// An ITEM: identity + where it can be used + a list of EFFECT BLOCKS («when / if / then»). Everything it does comes from
    /// <see cref="Effects"/>. The convenience properties below (HealHp, CatchMultiplier...) are read-only VIEWS of the blocks
    /// for the bag, the AI and the battle session.
    /// </summary>
    public sealed class ItemDefinition
    {
        public string Id { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public ItemCategory Category { get; }
        public int Price { get; }
        public bool UsableInBattle { get; }
        public bool UsableOutsideBattle { get; }
        /// <summary>¿Se gasta al usarlo desde la mochila? (las MT dependen además de las reglas).</summary>
        public bool Consumable { get; }
        /// <summary>Is it a berry? (Unnerve stops eating it, Harvest/Pluck/Ripen/Gluttony work with it).</summary>
        public bool IsBerry { get; }
        /// <summary>Everything the item does.</summary>
        public IReadOnlyList<EffectBlock> Effects { get; }

        public ItemDefinition(string id, string displayName, ItemCategory category,
            string description = "", int price = 0, bool usableInBattle = false, bool usableOutsideBattle = false,
            bool consumable = true, IReadOnlyList<EffectBlock> effects = null, bool? isBerry = null)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("El objeto necesita un id.", nameof(id));
            Id = id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName;
            Category = category;
            Description = description ?? "";
            Price = Math.Max(0, price);
            UsableInBattle = usableInBattle;
            UsableOutsideBattle = usableOutsideBattle;
            Consumable = consumable;
            Effects = effects == null ? Array.Empty<EffectBlock>() : new List<EffectBlock>(effects.Where(e => e != null));
            IsBerry = isBerry ?? (category == ItemCategory.Berry || ItemEffects.LooksLikeBerry(Effects));
        }

        // ---------------- Views of the blocks (for the bag, the AI and the old callers) ----------------

        private EffectBlock Use(EffectAction a) => Effects.FirstOrDefault(e => e.Trigger == EffectTrigger.OnUse && e.Action == a);
        private float UseAmount(EffectAction a) => Use(a)?.Amount ?? 0f;

        public int HealHp => (int)UseAmount(EffectAction.HealHp);
        public float HealPercent => Clamp(UseAmount(EffectAction.HealPercent));
        public bool CuresAllStatus => Use(EffectAction.CureStatus) is EffectBlock b && b.Ref.Length == 0;
        public string CuresStatusId => Use(EffectAction.CureStatus)?.Ref ?? "";
        public bool Revives => Use(EffectAction.Revive) != null;
        public float ReviveHpPercent => Clamp(UseAmount(EffectAction.Revive));
        public int RestorePp => (int)((Use(EffectAction.RestorePp) ?? Use(EffectAction.RestorePpAll))?.Amount ?? 0f);
        public bool RestorePpAllMoves => Use(EffectAction.RestorePp) == null && Use(EffectAction.RestorePpAll) != null;
        public int FriendshipChange => (int)UseAmount(EffectAction.Friendship);
        public float CatchMultiplier => Math.Max(0f, UseAmount(EffectAction.Catch));
        public string BattleStatId => Use(EffectAction.ChangeStage)?.Ref ?? "";
        public int BattleStages => (int)UseAmount(EffectAction.ChangeStage);

        /// <summary>Unconditional-trigger power multipliers (Charcoal: ×1.2 to Fire).</summary>
        public IReadOnlyList<PowerModifier> HeldPowerModifiers => Effects
            .Where(e => e.Trigger == EffectTrigger.Passive && e.Action == EffectAction.PowerMultiplier)
            .Select(e => new PowerModifier(e.Amount, e.Conditions)).ToList();

        public float HeldEndOfTurnHealPercent => Effects.FirstOrDefault(e => e.Trigger == EffectTrigger.EndOfTurn
            && e.Action == EffectAction.HealPercent && e.Conditions.Count == 0)?.Amount ?? 0f;

        private EffectBlock LowHpHeal => Effects.FirstOrDefault(e => e.Trigger == EffectTrigger.LowHp
            && (e.Action == EffectAction.HealHp || e.Action == EffectAction.HealPercent));
        public float HeldTriggerHpPercent => LowHpHeal?.Threshold ?? 0f;
        public int HeldTriggerHealHp => LowHpHeal is EffectBlock b && b.Action == EffectAction.HealHp ? (int)b.Amount : 0;
        public float HeldTriggerHealPercent => LowHpHeal is EffectBlock b && b.Action == EffectAction.HealPercent ? b.Amount : 0f;
        public bool HeldConsumedOnTrigger => LowHpHeal?.Consumes ?? true;

        private static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);

        public bool IsBall => CatchMultiplier > 0f;
        public bool CuresStatus => Use(EffectAction.CureStatus) != null;

        /// <summary>¿Cura este estado al usarlo? (CuresStatusId admite varios separados por '|': "poison|toxic").</summary>
        public bool CuresThis(string statusId) => Use(EffectAction.CureStatus)?.Cures(statusId) ?? false;
        public bool HealsHp => HealHp > 0 || HealPercent > 0f;
        /// <summary>Does it do anything while HELD?</summary>
        public bool HasHeldEffect => Effects.Any(e => e.Trigger != EffectTrigger.OnUse && e.Trigger != EffectTrigger.OnWalk);
        /// <summary>Blocks for one trigger.</summary>
        public IEnumerable<EffectBlock> On(EffectTrigger t) => Effects.Where(e => e.Trigger == t);
    }
}
