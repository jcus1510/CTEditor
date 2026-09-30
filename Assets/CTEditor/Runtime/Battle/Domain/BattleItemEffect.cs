using CTEditor.GameDefinition.Domain.Items;
using CTEditor.GameDefinition.Domain.Stats;

namespace CTEditor.Battle.Domain
{
    /// <summary>
    /// Qué hace un objeto USADO en combate sobre un miembro del equipo, ya traducido: Battle no conoce las
    /// fichas de objetos, solo este resumen. Se construye a mano o desde la ficha con From(item).
    /// </summary>
    public sealed class BattleItemEffect
    {
        public int HealAmount { get; }
        public float HealPercent { get; }
        public bool CuresStatus { get; }
        /// <summary>Si no está vacío, solo cura ESTE estado (Antídoto). Vacío + CuresStatus = cualquiera.</summary>
        public string CuresStatusId { get; }
        public bool Revives { get; }
        public float ReviveHpPercent { get; }
        public StatId? Stat { get; }
        public int Stages { get; }
        public int RestorePp { get; }
        public bool RestorePpAllMoves { get; }

        public BattleItemEffect(int healAmount = 0, bool curesStatus = false, bool revives = false,
            float healPercent = 0f, string curesStatusId = null, float reviveHpPercent = 0f,
            StatId? stat = null, int stages = 0, int restorePp = 0, bool restorePpAllMoves = false)
        {
            HealAmount = healAmount < 0 ? 0 : healAmount;
            HealPercent = healPercent < 0f ? 0f : healPercent;
            CuresStatus = curesStatus;
            CuresStatusId = curesStatusId ?? "";
            Revives = revives;
            ReviveHpPercent = reviveHpPercent;
            Stat = stat;
            Stages = stages;
            RestorePp = restorePp < 0 ? 0 : restorePp;
            RestorePpAllMoves = restorePpAllMoves;
        }

        /// <summary>Traduce la ficha de un objeto a su efecto en combate.</summary>
        public static BattleItemEffect From(ItemDefinition item)
            => item == null ? new BattleItemEffect() : new BattleItemEffect(
                item.HealHp, item.CuresStatus, item.Revives, item.HealPercent, item.CuresAllStatus ? "" : item.CuresStatusId,
                item.ReviveHpPercent, string.IsNullOrEmpty(item.BattleStatId) ? (StatId?)null : new StatId(item.BattleStatId),
                item.BattleStages, item.RestorePp, item.RestorePpAllMoves);
    }
}
