using UnityEngine;

namespace CTEditor.GameDefinition.Infrastructure.ScriptableObjects
{
    /// <summary>
    /// Un EQUIPO PREARMADO del jugador: para empezar una partida (o probar combates) con un equipo
    /// listo, su dinero y su mochila. La escena de pruebas deja elegirlo.
    /// </summary>
    [CreateAssetMenu(menuName = "CTEditor/Equipo prearmado", fileName = "NuevoEquipo")]
    public sealed class TeamPresetData : ScriptableObject, IContentAsset
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [Tooltip("Para qué sirve este equipo (se ve al elegirlo).")]
        [SerializeField, TextArea(2, 3)] private string description = "";

        [Header("Equipo")]
        [SerializeField] private TeamMemberData[] members;

        [Header("Dinero y mochila")]
        [Tooltip("Dinero inicial. -1 = el de las reglas del juego.")]
        [SerializeField] private int money = -1;
        [SerializeField] private BagEntryData[] items =
        {
            new BagEntryData { itemId = "potion", quantity = 5 },
            new BagEntryData { itemId = "poke_ball", quantity = 10 },
        };

        public string Id => id;
        public string DisplayName => displayName;
        public string Description => description;
        public TeamMemberData[] Members => members;
        public int Money => money;
        public BagEntryData[] Items => items;
    }

    /// <summary>Una línea de la mochila: qué objeto y cuántos.</summary>
    [System.Serializable]
    public sealed class BagEntryData
    {
        [ContentIdReference(typeof(ItemData))] public string itemId;
        [Min(1)] public int quantity = 1;
    }
}
