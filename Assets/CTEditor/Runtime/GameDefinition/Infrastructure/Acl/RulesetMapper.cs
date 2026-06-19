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
                new FormulaId(data.DamageFormulaId));
        }
    }
}
