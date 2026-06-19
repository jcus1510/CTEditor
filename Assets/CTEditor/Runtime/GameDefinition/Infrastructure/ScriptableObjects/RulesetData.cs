using UnityEngine;

namespace CTEditor.GameDefinition.Infrastructure.ScriptableObjects
{
    /// <summary>
    /// La ficha del autor para un Ruleset. El autor crea uno "Clásico" y, si quiere, copias con otras
    /// perillas. Mínima y plana, igual que el Ruleset del dominio (tu decisión). La fórmula se nombra
    /// por texto ("classic"), coherente con FormulaId: la ficha tampoco conoce la clase de la fórmula.
    /// </summary>
    [CreateAssetMenu(menuName = "CTEditor/Ruleset", fileName = "NewRuleset")]
    public sealed class RulesetData : ScriptableObject
    {
        [SerializeField, Min(1)] private int maxPartySize = 6;
        [SerializeField, Min(1)] private int maxMovesPerMonster = 4;
        [SerializeField, Min(1)] private int levelCap = 100;
        [SerializeField] private string damageFormulaId = "classic";

        public int MaxPartySize => maxPartySize;
        public int MaxMovesPerMonster => maxMovesPerMonster;
        public int LevelCap => levelCap;
        public string DamageFormulaId => damageFormulaId;
    }
}
