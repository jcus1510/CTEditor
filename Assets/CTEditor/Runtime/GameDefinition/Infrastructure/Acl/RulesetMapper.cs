using System;
using System.Collections.Generic;
using CTEditor.GameDefinition.Domain.Rules.Mechanics;
using CTEditor.GameDefinition.Domain.Rules;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Infrastructure.Acl
{
    /// <summary>La ACL para reglas: RulesetData (Unity) -> Ruleset (dominio puro).</summary>
    public static class RulesetMapper
    {
        /// <summary>
        /// 'mechanics' resuelve cada id de mecánica activa a su definición (null = ninguna se resuelve). Los ids que no
        /// existen se ignoran: el validador del editor los avisa.
        /// </summary>
        public static Ruleset ToDomain(RulesetData data, Func<string, MechanicDefinition> mechanics = null)
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
                    data.HealOnBlackout),
                new GenerationRules(data.Generation, data.CategoryByType, data.SpecialTypes, data.SingleSpecialStat,
                    data.Abilities, data.HeldItems, data.Natures, data.Genders),
                ResolveMechanics(data.MechanicIds, mechanics));
        }

        private static List<MechanicDefinition> ResolveMechanics(string[] ids, Func<string, MechanicDefinition> resolve)
        {
            var list = new List<MechanicDefinition>();
            if (ids == null || resolve == null) return list;
            foreach (var id in ids)
            {
                if (string.IsNullOrWhiteSpace(id)) continue;
                MechanicDefinition m = null;
                try { m = resolve(id.Trim()); } catch (Exception) { /* ficha rota: el validador la reporta */ }
                if (m != null) list.Add(m);
            }
            return list;
        }
    }
}
