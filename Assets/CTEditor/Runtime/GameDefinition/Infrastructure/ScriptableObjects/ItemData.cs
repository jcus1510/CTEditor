using UnityEngine;
using CTEditor.GameDefinition.Domain.Items;

namespace CTEditor.GameDefinition.Infrastructure.ScriptableObjects
{
    /// <summary>
    /// The ITEM asset the author fills in: identity, where it can be used and a list of EFFECT BLOCKS
    /// («when / if / then»). The old per-feature fields are kept hidden only to convert old assets (ItemMapper merges
    /// whatever is left in them, and the item editor moves them into blocks). ItemMapper turns it into an ItemDefinition.
    /// </summary>
    [CreateAssetMenu(menuName = "CTEditor/Objeto", fileName = "NuevoObjeto")]
    public sealed class ItemData : ScriptableObject, IContentAsset
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [Tooltip("Nombre en INGLÉS (el de Showdown): sirve para importar y exportar equipos. Vacío = se deduce del id.")]
        [SerializeField] private string englishName = "";
        [Tooltip("Texto que verá el jugador en la mochila.")]
        [SerializeField, TextArea(2, 4)] private string description = "";
        [SerializeField] private ItemCategory category = ItemCategory.Medicine;
        [Tooltip("Icono para la mochila y la tienda (opcional).")]
        [SerializeField] private Sprite icon;
        [SerializeField, Min(0)] private int price = 0;
        [SerializeField] private bool usableInBattle = true;
        [SerializeField] private bool usableOutsideBattle = true;
        [Tooltip("Se gasta al usarlo. Desmárcalo para objetos clave o reutilizables.")]
        [SerializeField] private bool consumable = true;

        [Tooltip("¿Es una baya? (Nerviosismo impide comerla; Cosecha, Picotazo, Carrillo y Gula funcionan con ella).")]
        [SerializeField] private bool isBerry;
        [Tooltip("TODO lo que hace el objeto: una lista de efectos «cuándo / si / entonces».")]
        [SerializeField] private EffectBlockData[] effects = new EffectBlockData[0];

        // ---- Campos ANTIGUOS: solo para convertir fichas viejas a efectos (el editor lo hace solo). No se usan al editar. ----
        [LegacyField, HideInInspector, SerializeField, Min(0)] private int healHp = 0;
        [LegacyField, HideInInspector, SerializeField, Range(0f, 100f)] private float healPercent = 0f;
        [LegacyField, HideInInspector, SerializeField] private bool curesAllStatus = false;
        [LegacyField, HideInInspector, SerializeField] private string curesStatusId = "";
        [LegacyField, HideInInspector, SerializeField] private bool revives = false;
        [LegacyField, HideInInspector, SerializeField, Range(0f, 100f)] private float reviveHpPercent = 50f;
        [LegacyField, HideInInspector, SerializeField, Min(0)] private int restorePp = 0;
        [LegacyField, HideInInspector, SerializeField] private bool restorePpAllMoves = false;
        [LegacyField, HideInInspector, SerializeField] private int friendshipChange = 0;

        [LegacyField, HideInInspector, SerializeField, Min(0f)] private float catchMultiplier = 0f;
        [LegacyField, HideInInspector, StatIdReference, SerializeField] private string battleStatId = "";
        [LegacyField, HideInInspector, SerializeField, Range(-6, 6)] private int battleStages = 0;

        [LegacyField, HideInInspector, SerializeField] private PowerModifierData[] heldPowerModifiers = new PowerModifierData[0];
        [LegacyField, HideInInspector, SerializeField, Range(0f, 100f)] private float heldEndOfTurnHealPercent = 0f;
        [LegacyField, HideInInspector, SerializeField, Range(0f, 100f)] private float heldTriggerHpPercent = 0f;
        [LegacyField, HideInInspector, SerializeField, Min(0)] private int heldTriggerHealHp = 0;
        [LegacyField, HideInInspector, SerializeField, Range(0f, 100f)] private float heldTriggerHealPercent = 0f;
        [LegacyField, HideInInspector, SerializeField] private bool heldConsumedOnTrigger = true;

        [LegacyField, HideInInspector, SerializeField] private AbilityData.ConditionalStatData[] heldStatMultipliers = new AbilityData.ConditionalStatData[0];
        [LegacyField, HideInInspector, SerializeField, Tooltip("Solo puede repetir el primer movimiento que usa hasta retirarse (objetos Elección).")] private bool heldChoiceLock;
        [LegacyField, HideInInspector, SerializeField, Range(0f, 100f), Tooltip("% de PS que pierde cada vez que hace daño (Vidasfera: 10).")] private float heldAttackRecoilPercent;
        [LegacyField, HideInInspector, SerializeField, Tooltip("Con PS al máximo aguanta un golpe con 1 PS y se gasta (Banda Focus).")] private bool heldSurviveFromFullHp;
        [LegacyField, HideInInspector, SerializeField, Range(0f, 100f), Tooltip("% de PS que pierde quien le golpea con contacto (Casco Dentado: 16,67).")] private float heldContactDamagePercent;
        [LegacyField, HideInInspector, SerializeField] private AbilityData.OnHitStatData[] heldOnHitStats = new AbilityData.OnHitStatData[0];
        [LegacyField, HideInInspector, SerializeField, Tooltip("Se gasta al activarse lo anterior.")] private bool heldOnHitConsumed;
        [LegacyField, HideInInspector, SerializeField, Tooltip("Inmune a Tierra hasta que le golpean (Globo Helio).")] private bool heldAirBalloon;
        [LegacyField, HideInInspector, SerializeField, Tooltip("No puede usar movimientos de estado (Chaleco Asalto).")] private bool heldBlocksStatusMoves;
        [LegacyField, HideInInspector, SerializeField, Range(0, 3), Tooltip("+ índice de crítico (Periscopio: 1).")] private int heldCritStageBonus;
        [LegacyField, HideInInspector, SerializeField, Range(0.5f, 2f), Tooltip("Precisión de sus movimientos × (Lupa: 1,1).")] private float heldAccuracyMultiplier = 1f;
        [LegacyField, HideInInspector, SerializeField, Range(0.5f, 2f), Tooltip("Precisión de quien le ataca × (Polvo Brillo: 0,9).")] private float heldEvasionMultiplier = 1f;
        [LegacyField, HideInInspector, SerializeField, Tooltip("Baya de resistencia: tipo cuyo golpe muy eficaz reduce a la mitad (fire, water...). Se gasta.")] private string heldResistBerryType = "";
        [LegacyField, HideInInspector, SerializeField, Tooltip("Se cura de cualquier estado y se gasta (Baya Ziuela).")] private bool heldCuresAnyStatus;
        [LegacyField, HideInInspector, StatusIdReference, SerializeField, Tooltip("Estado que se pone él mismo al final del turno (Llamasfera: burn).")] private string heldSelfStatusEndOfTurn = "";
        [LegacyField, HideInInspector, SerializeField, Range(0f, 100f), Tooltip("% de hacer retroceder con sus ataques (Roca del Rey: 10).")] private float heldFlinchChance;
        [LegacyField, HideInInspector, SerializeField, Range(0f, 100f), Tooltip("% del daño hecho que recupera (Campana Concha: 12,5).")] private float heldHealOnDamagePercent;
        [LegacyField, HideInInspector, SerializeField, Range(0, 10), Tooltip("Turnos extra del clima que pone (rocas de clima: 3).")] private int heldWeatherTurnsBonus;
        [LegacyField, HideInInspector, SerializeField, Range(0, 10), Tooltip("Turnos extra de sus pantallas (Refleluz: 3).")] private int heldScreenTurnsBonus;
        [LegacyField, HideInInspector, SerializeField, Tooltip("Veneno: cura 1/16; los demás pierden 1/8 (Lodo Negro).")] private bool heldBlackSludge;
        [LegacyField, HideInInspector, SerializeField, Range(0f, 100f), Tooltip("% de actuar el primero en su prioridad (Garra Rápida: 20).")] private float heldQuickClawChance;
        [LegacyField, HideInInspector, SerializeField, Range(1f, 2f), Tooltip("Daño de sus golpes muy eficaces × (Cinta Experto: 1,2).")] private float heldSuperEffectiveBoost = 1f;

        public AbilityData.ConditionalStatData[] HeldStatMultipliers => heldStatMultipliers;
        public AbilityData.OnHitStatData[] HeldOnHitStats => heldOnHitStats;
        public bool HeldChoiceLock => heldChoiceLock; public float HeldAttackRecoilPercent => heldAttackRecoilPercent;
        public bool HeldSurviveFromFullHp => heldSurviveFromFullHp; public float HeldContactDamagePercent => heldContactDamagePercent;
        public bool HeldOnHitConsumed => heldOnHitConsumed; public bool HeldAirBalloon => heldAirBalloon; public bool HeldBlocksStatusMoves => heldBlocksStatusMoves;
        public int HeldCritStageBonus => heldCritStageBonus; public float HeldAccuracyMultiplier => heldAccuracyMultiplier;
        public float HeldEvasionMultiplier => heldEvasionMultiplier; public string HeldResistBerryType => heldResistBerryType;
        public bool HeldCuresAnyStatus => heldCuresAnyStatus; public string HeldSelfStatusEndOfTurn => heldSelfStatusEndOfTurn;
        public float HeldFlinchChance => heldFlinchChance; public float HeldHealOnDamagePercent => heldHealOnDamagePercent;
        public int HeldWeatherTurnsBonus => heldWeatherTurnsBonus; public int HeldScreenTurnsBonus => heldScreenTurnsBonus;
        public bool HeldBlackSludge => heldBlackSludge; public float HeldQuickClawChance => heldQuickClawChance;
        public float HeldSuperEffectiveBoost => heldSuperEffectiveBoost;

        public bool IsBerry => isBerry;
        public EffectBlockData[] Effects => effects;

        /// <summary>¿Le quedan datos en los campos ANTIGUOS? (hay que convertirlos a efectos).</summary>
        public bool HasLegacyEffects => healHp != 0 || healPercent != 0 || curesAllStatus || !string.IsNullOrWhiteSpace(curesStatusId) || revives
            || restorePp != 0 || friendshipChange != 0 || catchMultiplier != 0 || (!string.IsNullOrWhiteSpace(battleStatId) && battleStages != 0)
            || (heldPowerModifiers?.Length ?? 0) > 0 || heldEndOfTurnHealPercent != 0 || heldTriggerHpPercent != 0
            || (heldStatMultipliers?.Length ?? 0) > 0 || heldChoiceLock || heldAttackRecoilPercent != 0 || heldSurviveFromFullHp
            || heldContactDamagePercent != 0 || (heldOnHitStats?.Length ?? 0) > 0 || heldAirBalloon || heldBlocksStatusMoves
            || heldCritStageBonus != 0 || (heldAccuracyMultiplier != 1f && heldAccuracyMultiplier > 0) || (heldEvasionMultiplier != 1f && heldEvasionMultiplier > 0)
            || !string.IsNullOrWhiteSpace(heldResistBerryType) || heldCuresAnyStatus || !string.IsNullOrWhiteSpace(heldSelfStatusEndOfTurn)
            || heldFlinchChance != 0 || heldHealOnDamagePercent != 0 || heldWeatherTurnsBonus != 0 || heldScreenTurnsBonus != 0
            || heldBlackSludge || heldQuickClawChance != 0 || heldSuperEffectiveBoost > 1f;

        public string Id => id;
        public string DisplayName => displayName;
        public string EnglishName => englishName;
        public string Description => description;
        public ItemCategory Category => category;
        public Sprite Icon => icon;
        public int Price => price;
        public bool UsableInBattle => usableInBattle;
        public bool UsableOutsideBattle => usableOutsideBattle;
        public bool Consumable => consumable;
        public int HealHp => healHp;
        public float HealPercent => healPercent;
        public bool CuresAllStatus => curesAllStatus;
        public string CuresStatusId => curesStatusId;
        public bool Revives => revives;
        public float ReviveHpPercent => reviveHpPercent;
        public int RestorePp => restorePp;
        public bool RestorePpAllMoves => restorePpAllMoves;
        public int FriendshipChange => friendshipChange;
        public float CatchMultiplier => catchMultiplier;
        public string BattleStatId => battleStatId;
        public int BattleStages => battleStages;
        public PowerModifierData[] HeldPowerModifiers => heldPowerModifiers;
        public float HeldEndOfTurnHealPercent => heldEndOfTurnHealPercent;
        public float HeldTriggerHpPercent => heldTriggerHpPercent;
        public int HeldTriggerHealHp => heldTriggerHealHp;
        public float HeldTriggerHealPercent => heldTriggerHealPercent;
        public bool HeldConsumedOnTrigger => heldConsumedOnTrigger;
    }
}
