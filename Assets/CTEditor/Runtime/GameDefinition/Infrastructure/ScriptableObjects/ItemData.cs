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
