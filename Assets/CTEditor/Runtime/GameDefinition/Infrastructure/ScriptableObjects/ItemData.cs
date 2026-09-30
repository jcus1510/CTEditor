using UnityEngine;
using CTEditor.GameDefinition.Domain.Items;

namespace CTEditor.GameDefinition.Infrastructure.ScriptableObjects
{
    /// <summary>
    /// La ficha de un OBJETO que rellena el autor. Un objeto puede combinar efectos: curar, curar estados,
    /// revivir, recuperar PP, capturar, subir stats en combate, cambiar la amistad, hacer evolucionar (se
    /// indica en la evolución de la especie) y efectos al llevarlo EQUIPADO (potencia, curación por turno,
    /// bayas que se activan con poca vida). Lo traduce ItemMapper al dominio.
    /// </summary>
    [CreateAssetMenu(menuName = "CTEditor/Objeto", fileName = "NuevoObjeto")]
    public sealed class ItemData : ScriptableObject, IContentAsset
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
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

        [Header("Al usarlo sobre un monstruo")]
        [SerializeField, Min(0)] private int healHp = 0;
        [SerializeField, Range(0f, 100f)] private float healPercent = 0f;
        [SerializeField] private bool curesAllStatus = false;
        [Tooltip("Estado que cura (su id). Varios separados por | (ej. poison|toxic). Para curar todos, marca la casilla de arriba.")]
        [SerializeField] private string curesStatusId = "";
        [SerializeField] private bool revives = false;
        [SerializeField, Range(0f, 100f)] private float reviveHpPercent = 50f;
        [SerializeField, Min(0)] private int restorePp = 0;
        [SerializeField] private bool restorePpAllMoves = false;
        [SerializeField] private int friendshipChange = 0;

        [Header("En combate")]
        [Tooltip("0 = no es una bola. 1 = Poké Ball, 1,5 = Super Ball, 2 = Ultra Ball, 255 = Master Ball (siempre captura).")]
        [SerializeField, Min(0f)] private float catchMultiplier = 0f;
        [StatIdReference, SerializeField] private string battleStatId = "";
        [SerializeField, Range(-6, 6)] private int battleStages = 0;

        [Header("Equipado (lo lleva un monstruo)")]
        [SerializeField] private PowerModifierData[] heldPowerModifiers = new PowerModifierData[0];
        [SerializeField, Range(0f, 100f)] private float heldEndOfTurnHealPercent = 0f;
        [SerializeField, Range(0f, 100f)] private float heldTriggerHpPercent = 0f;
        [SerializeField, Min(0)] private int heldTriggerHealHp = 0;
        [SerializeField, Range(0f, 100f)] private float heldTriggerHealPercent = 0f;
        [SerializeField] private bool heldConsumedOnTrigger = true;

        [Header("Equipado: competición (5.ª y 6.ª gen.)")]
        [Tooltip("Estadísticas multiplicadas con condiciones (Cinta Elección: attack ×1,5). Se edita en Excel: stats_condicionales.")]
        [SerializeField] private AbilityData.ConditionalStatData[] heldStatMultipliers = new AbilityData.ConditionalStatData[0];
        [SerializeField, Tooltip("Solo puede repetir el primer movimiento que usa hasta retirarse (objetos Elección).")] private bool heldChoiceLock;
        [SerializeField, Range(0f, 100f), Tooltip("% de PS que pierde cada vez que hace daño (Vidasfera: 10).")] private float heldAttackRecoilPercent;
        [SerializeField, Tooltip("Con PS al máximo aguanta un golpe con 1 PS y se gasta (Banda Focus).")] private bool heldSurviveFromFullHp;
        [SerializeField, Range(0f, 100f), Tooltip("% de PS que pierde quien le golpea con contacto (Casco Dentado: 16,67).")] private float heldContactDamagePercent;
        [Tooltip("Cambios de etapa al recibir un golpe (Seguro Debilidad: attack +2 y sp_attack +2 si es muy eficaz). En Excel: al_recibir_golpe.")]
        [SerializeField] private AbilityData.OnHitStatData[] heldOnHitStats = new AbilityData.OnHitStatData[0];
        [SerializeField, Tooltip("Se gasta al activarse lo anterior.")] private bool heldOnHitConsumed;
        [SerializeField, Tooltip("Inmune a Tierra hasta que le golpean (Globo Helio).")] private bool heldAirBalloon;
        [SerializeField, Tooltip("No puede usar movimientos de estado (Chaleco Asalto).")] private bool heldBlocksStatusMoves;
        [SerializeField, Range(0, 3), Tooltip("+ índice de crítico (Periscopio: 1).")] private int heldCritStageBonus;
        [SerializeField, Range(0.5f, 2f), Tooltip("Precisión de sus movimientos × (Lupa: 1,1).")] private float heldAccuracyMultiplier = 1f;
        [SerializeField, Range(0.5f, 2f), Tooltip("Precisión de quien le ataca × (Polvo Brillo: 0,9).")] private float heldEvasionMultiplier = 1f;
        [SerializeField, Tooltip("Baya de resistencia: tipo cuyo golpe muy eficaz reduce a la mitad (fire, water...). Se gasta.")] private string heldResistBerryType = "";
        [SerializeField, Tooltip("Se cura de cualquier estado y se gasta (Baya Ziuela).")] private bool heldCuresAnyStatus;
        [StatusIdReference, SerializeField, Tooltip("Estado que se pone él mismo al final del turno (Llamasfera: burn).")] private string heldSelfStatusEndOfTurn = "";
        [SerializeField, Range(0f, 100f), Tooltip("% de hacer retroceder con sus ataques (Roca del Rey: 10).")] private float heldFlinchChance;
        [SerializeField, Range(0f, 100f), Tooltip("% del daño hecho que recupera (Campana Concha: 12,5).")] private float heldHealOnDamagePercent;
        [SerializeField, Range(0, 10), Tooltip("Turnos extra del clima que pone (rocas de clima: 3).")] private int heldWeatherTurnsBonus;
        [SerializeField, Range(0, 10), Tooltip("Turnos extra de sus pantallas (Refleluz: 3).")] private int heldScreenTurnsBonus;
        [SerializeField, Tooltip("Veneno: cura 1/16; los demás pierden 1/8 (Lodo Negro).")] private bool heldBlackSludge;
        [SerializeField, Range(0f, 100f), Tooltip("% de actuar el primero en su prioridad (Garra Rápida: 20).")] private float heldQuickClawChance;
        [SerializeField, Range(1f, 2f), Tooltip("Daño de sus golpes muy eficaces × (Cinta Experto: 1,2).")] private float heldSuperEffectiveBoost = 1f;

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

        public string Id => id;
        public string DisplayName => displayName;
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
