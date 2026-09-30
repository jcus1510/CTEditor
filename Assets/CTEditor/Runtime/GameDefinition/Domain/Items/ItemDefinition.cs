using System;
using System.Collections.Generic;
using CTEditor.GameDefinition.Domain.Conditions;

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
        Other
    }

    /// <summary>
    /// La FICHA de un objeto: TODO lo que hace sale de aquí (curar, revivir, capturar, subir stats,
    /// evolucionar, efectos al llevarlo equipado...). Un objeto puede combinar varias cosas: por ejemplo,
    /// "Poción Máxima" = curar 100% + nada más; "Restaurar Todo" = curar 100% + curar estados.
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
        /// <summary>¿Se gasta al usarlo? (las piedras y medicinas sí; los objetos clave no).</summary>
        public bool Consumable { get; }

        // --- Uso sobre un monstruo (dentro o fuera del combate) ---
        public int HealHp { get; }             // PS fijos (Poción = 20)
        public float HealPercent { get; }      // % de PS máx (Poción Máxima = 100)
        public bool CuresAllStatus { get; }    // Cura Total
        public string CuresStatusId { get; }   // un estado concreto (Antídoto = "poison"). Vacío = ninguno
        public bool Revives { get; }
        public float ReviveHpPercent { get; }  // Revivir = 50, Revivir Máximo = 100
        public int RestorePp { get; }          // PP a recuperar (Éter = 10). 0 = nada
        public bool RestorePpAllMoves { get; } // false = solo el primer movimiento gastado (Éter); true = todos (Elixir)
        public int FriendshipChange { get; }   // +/- amistad (bayas, vitaminas)

        // --- En combate ---
        public float CatchMultiplier { get; }  // 0 = no es una bola; 1 = Poké Ball, 1,5 = Super Ball, 255 = Master Ball
        public string BattleStatId { get; }    // Ataque X: "attack"
        public int BattleStages { get; }       // +1, +2...

        // --- Equipado (lo lleva un monstruo) ---
        public IReadOnlyList<PowerModifier> HeldPowerModifiers { get; }   // Carbón: ×1,2 a Fuego
        public float HeldEndOfTurnHealPercent { get; }                     // Restos: 6,25 por turno
        public float HeldTriggerHpPercent { get; }                         // Bayas: se activan con este % de PS o menos (0 = no)
        public int HeldTriggerHealHp { get; }                              // Baya Aranja: +10 PS
        public float HeldTriggerHealPercent { get; }                       // Baya Zidra: +25% PS máx
        public bool HeldConsumedOnTrigger { get; }                         // las bayas se consumen

        public ItemDefinition(string id, string displayName, ItemCategory category,
            string description = "", int price = 0, bool usableInBattle = false, bool usableOutsideBattle = false,
            bool consumable = true, int healHp = 0, float healPercent = 0f, bool curesAllStatus = false,
            string curesStatusId = null, bool revives = false, float reviveHpPercent = 0f, int restorePp = 0,
            bool restorePpAllMoves = false, int friendshipChange = 0, float catchMultiplier = 0f,
            string battleStatId = null, int battleStages = 0, IReadOnlyList<PowerModifier> heldPowerModifiers = null,
            float heldEndOfTurnHealPercent = 0f, float heldTriggerHpPercent = 0f, int heldTriggerHealHp = 0,
            float heldTriggerHealPercent = 0f, bool heldConsumedOnTrigger = true)
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
            HealHp = Math.Max(0, healHp);
            HealPercent = Clamp(healPercent);
            CuresAllStatus = curesAllStatus;
            CuresStatusId = curesStatusId ?? "";
            Revives = revives;
            ReviveHpPercent = Clamp(reviveHpPercent);
            RestorePp = Math.Max(0, restorePp);
            RestorePpAllMoves = restorePpAllMoves;
            FriendshipChange = friendshipChange;
            CatchMultiplier = Math.Max(0f, catchMultiplier);
            BattleStatId = battleStatId ?? "";
            BattleStages = battleStages;
            HeldPowerModifiers = heldPowerModifiers == null ? Array.Empty<PowerModifier>() : new List<PowerModifier>(heldPowerModifiers);
            HeldEndOfTurnHealPercent = Clamp(heldEndOfTurnHealPercent);
            HeldTriggerHpPercent = Clamp(heldTriggerHpPercent);
            HeldTriggerHealHp = Math.Max(0, heldTriggerHealHp);
            HeldTriggerHealPercent = Clamp(heldTriggerHealPercent);
            HeldConsumedOnTrigger = heldConsumedOnTrigger;
        }

        private static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);

        public bool IsBall => CatchMultiplier > 0f;
        public bool CuresStatus => CuresAllStatus || CuresStatusId.Length > 0;

        /// <summary>¿Cura este estado? (CuresStatusId admite varios separados por '|': "poison|toxic").</summary>
        public bool CuresThis(string statusId)
        {
            if (CuresAllStatus) return true;
            foreach (var s in CuresStatusId.Split('|'))
                if (s.Trim().Length > 0 && s.Trim() == statusId) return true;
            return false;
        }
        public bool HealsHp => HealHp > 0 || HealPercent > 0f;
        public bool HasHeldEffect => HeldPowerModifiers.Count > 0 || HeldEndOfTurnHealPercent > 0f || HeldTriggerHpPercent > 0f;
    }
}
