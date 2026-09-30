using System.Collections.Generic;
using CTEditor.GameDefinition.Domain.Stats;

namespace CTEditor.Battle.Domain
{
    /// <summary>
    /// Reglas del combate que vienen del Ruleset del autor (tabla de críticos, stats del daño por
    /// categoría). El TurnResolver las recibe ya traducidas: así el dominio de combate no depende del
    /// formato del Ruleset. Sin reglas = las clásicas modernas.
    /// </summary>
    public sealed class BattleRules
    {
        public IReadOnlyList<int> CritDenominators { get; }
        public float CritMultiplier { get; }
        public StatId PhysicalAttack { get; }
        public StatId PhysicalDefense { get; }
        public StatId SpecialAttack { get; }
        public StatId SpecialDefense { get; }

        // --- Reglas de huida, captura y experiencia (de AdventureRules) ---
        /// <summary>true = huir de un salvaje funciona siempre; false = fórmula clásica por velocidad.</summary>
        public bool FleeAlwaysWorks { get; }
        public bool CanFleeTrainerBattles { get; }
        public bool CanCatchTrainerMonsters { get; }
        public float CatchRateMultiplier { get; }
        public bool ExpShareAll { get; }
        public float ExpShareOthersPercent { get; }

        public BattleRules(IReadOnlyList<int> critDenominators = null, float critMultiplier = 1.5f,
            string physicalAttack = "attack", string physicalDefense = "defense",
            string specialAttack = "sp_attack", string specialDefense = "sp_defense",
            bool fleeAlwaysWorks = false, bool canFleeTrainerBattles = false, bool canCatchTrainerMonsters = false,
            float catchRateMultiplier = 1f, bool expShareAll = false, float expShareOthersPercent = 50f)
        {
            FleeAlwaysWorks = fleeAlwaysWorks;
            CanFleeTrainerBattles = canFleeTrainerBattles;
            CanCatchTrainerMonsters = canCatchTrainerMonsters;
            CatchRateMultiplier = catchRateMultiplier <= 0f ? 1f : catchRateMultiplier;
            ExpShareAll = expShareAll;
            ExpShareOthersPercent = expShareOthersPercent < 0f ? 0f : expShareOthersPercent > 100f ? 100f : expShareOthersPercent;
            CritDenominators = critDenominators == null || critDenominators.Count == 0
                ? Formulas.ClassicDamageFormula.DefaultCritTable : new List<int>(critDenominators);
            CritMultiplier = critMultiplier <= 0f ? 1.5f : critMultiplier;
            PhysicalAttack = new StatId(string.IsNullOrWhiteSpace(physicalAttack) ? "attack" : physicalAttack);
            PhysicalDefense = new StatId(string.IsNullOrWhiteSpace(physicalDefense) ? "defense" : physicalDefense);
            SpecialAttack = new StatId(string.IsNullOrWhiteSpace(specialAttack) ? "sp_attack" : specialAttack);
            SpecialDefense = new StatId(string.IsNullOrWhiteSpace(specialDefense) ? "sp_defense" : specialDefense);
        }

        public static readonly BattleRules Default = new BattleRules();

        /// <summary>Traduce las reglas del autor (Ruleset) a reglas de combate.</summary>
        public static BattleRules From(CTEditor.GameDefinition.Domain.Rules.Ruleset r)
            => r == null ? Default : new BattleRules(r.CritDenominators, r.CritMultiplier,
                r.PhysicalAttackStat, r.PhysicalDefenseStat, r.SpecialAttackStat, r.SpecialDefenseStat,
                r.Adventure.FleeAlwaysWorks, r.Adventure.CanFleeTrainerBattles, r.Adventure.CanCatchTrainerMonsters,
                r.Adventure.CatchRateMultiplier, r.Adventure.ExpShareAll, r.Adventure.ExpShareOthersPercent);
    }
}
