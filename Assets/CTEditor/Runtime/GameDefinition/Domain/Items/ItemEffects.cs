using System.Collections.Generic;
using System.Linq;
using CTEditor.GameDefinition.Domain.Conditions;
using CTEditor.GameDefinition.Domain.Effects;

namespace CTEditor.GameDefinition.Domain.Items
{
    /// <summary>Small helpers to build common item conditions and to recognise berries.</summary>
    public static class ItemEffects
    {
        /// <summary>«The move is super effective against [subject]».</summary>
        public static Condition SuperEffectiveOn(ConditionSubject subject)
            => new Condition(ConditionKind.MoveEffectiveness, subject, Comparison.Greater, 1f);

        /// <summary>«The holder has full HP».</summary>
        public static Condition FullHp() => new Condition(ConditionKind.HpPercent, ConditionSubject.Self, Comparison.GreaterOrEqual, 100f);

        /// <summary>«The holder is (not) of this type».</summary>
        public static Condition IsType(string type, bool negate = false) => new Condition(ConditionKind.IsType, ConditionSubject.Self, text: type, negate: negate);

        /// <summary>«The move is of this type».</summary>
        public static Condition MoveType(string type) => new Condition(ConditionKind.MoveType, text: type);

        /// <summary>Berries are eaten: pinch berries, status berries and resist berries.</summary>
        public static bool LooksLikeBerry(IEnumerable<EffectBlock> blocks) => blocks.Any(b => b.Consumes
            && (b.Trigger == EffectTrigger.LowHp || b.Trigger == EffectTrigger.OnStatus
                || b.Trigger == EffectTrigger.BeforeHit && b.Action == EffectAction.DamageTakenMultiplier));
    }
}
