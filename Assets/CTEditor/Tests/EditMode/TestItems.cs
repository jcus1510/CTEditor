using CTEditor.GameDefinition.Domain.Conditions;
using CTEditor.GameDefinition.Domain.Effects;

namespace CTEditor.Tests.EditMode
{
    /// <summary>Short builders of effect blocks for the tests (pure domain: they also run in Tools/probar_dominio).</summary>
    public static class TestItems
    {
        public static EffectBlock OnUse(EffectAction action, float amount = 0f, string reference = null)
            => new EffectBlock(EffectTrigger.OnUse, action, amount, reference);

        public static EffectBlock EndOfTurnHeal(float percent) => new EffectBlock(EffectTrigger.EndOfTurn, EffectAction.HealPercent, percent);

        public static EffectBlock LowHpHeal(float threshold, float amount, bool percent = false)
            => new EffectBlock(EffectTrigger.LowHp, percent ? EffectAction.HealPercent : EffectAction.HealHp, amount, threshold: threshold, consumes: true);

        public static EffectBlock Power(float multiplier, params Condition[] conditions)
            => new EffectBlock(EffectTrigger.Passive, EffectAction.PowerMultiplier, multiplier, conditions: conditions);

        public static EffectBlock Stat(string stat, float multiplier) => new EffectBlock(EffectTrigger.Passive, EffectAction.MultiplyStat, multiplier, stat);
    }
}
