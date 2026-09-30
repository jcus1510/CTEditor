using System;
using System.Collections.Generic;
using CTEditor.GameDefinition.Domain.Trainers;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Infrastructure.Acl
{
    /// <summary>Ficha de nivel de IA (Unity) → AiProfile (dominio puro).</summary>
    public static class AiLevelMapper
    {
        public static AiProfile ToDomain(AiLevelData d)
        {
            if (d == null) throw new ArgumentNullException(nameof(d));
            var bag = new List<(string, int)>();
            if (d.DefaultBag != null)
                foreach (var it in d.DefaultBag)
                    if (it != null && !string.IsNullOrWhiteSpace(it.itemId)) bag.Add((it.itemId.Trim(), Math.Max(1, it.quantity)));
            return new AiProfile(d.Level, d.DisplayName, d.Description, d.Brain, d.MistakePercent, d.ItemUsePercent, d.Heal,
                d.HealBelowPercent, d.CanSwitch, d.Moveset, d.Synergies, d.UseMachineMoves, d.UseTutorMoves, d.UseEggMoves,
                d.AutoHeldItems, bag);
        }
    }
}
