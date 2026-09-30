using System;
using CTEditor.GameDefinition.Domain.Rules;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Infrastructure.Acl
{
    /// <summary>La ACL para reglas: RulesetData (Unity) -> Ruleset (dominio puro).</summary>
    public static class RulesetMapper
    {
        public static Ruleset ToDomain(RulesetData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            return new Ruleset(
                data.MaxPartySize,
                data.MaxMovesPerMonster,
                data.LevelCap,
                new FormulaId(data.DamageFormulaId),
                data.MaxIv,
                data.MaxEvPerStat,
                data.MaxEvTotal,
                data.UsePp,
                data.StruggleMoveId,
                data.CritDenominators,
                data.CritMultiplier,
                data.PhysicalAttackStat,
                data.PhysicalDefenseStat,
                data.SpecialAttackStat,
                data.SpecialDefenseStat,
                new AdventureRules(
                    data.FleeAlwaysWorks, data.CanFleeTrainerBattles, data.CanCatchTrainerMonsters,
                    data.CatchRateMultiplier, data.SendToBoxWhenFull, data.ExpShareAll, data.ExpShareOthersPercent,
                    data.LearnMovesOnLevelUp, data.EvolveAfterBattle, data.FriendshipPerLevelUp,
                    data.FriendshipLostOnFaint, data.StartingMoney, data.MoneyLostOnBlackoutPercent,
                    data.HealOnBlackout));
        }
    }
}
