using UnityEngine;
using CTEditor.GameDefinition.Domain.Items;

namespace CTEditor.GameDefinition.Infrastructure.ScriptableObjects
{
    /// <summary>
    /// The ITEM asset the author fills in: identity, where it can be used and a list of EFFECT BLOCKS
    /// («when / if / then»). ItemMapper turns it into an ItemDefinition.
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

        public bool IsBerry => isBerry;
        public EffectBlockData[] Effects => effects;

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
    }
}
