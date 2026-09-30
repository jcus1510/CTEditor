using UnityEngine;

namespace CTEditor.GameDefinition.Infrastructure.ScriptableObjects
{
    /// <summary>
    /// Ficha de un SET DE COMPETICIÓN (de Smogon o tuyo): con qué movimientos, objeto, habilidad, naturaleza y EVs juega una
    /// especie en un formato. Los entrenadores cuya IA usa sets (Campeón, Maestro, Injusto) los eligen al azar dando más peso
    /// a los de más puntuación. Las alternativas van separadas por coma y los huecos de movimientos por «/».
    /// La traduce CompetitiveSetMapper al dominio (CompetitiveSet).
    /// </summary>
    [CreateAssetMenu(menuName = "CTEditor/Set de competición", fileName = "Set")]
    public sealed class CompetitiveSetData : ScriptableObject, IContentAsset
    {
        [SerializeField] private string id;
        [Tooltip("Nombre del set (el del análisis: «Choice Scarf», «Dragon Dance»...).")]
        [SerializeField] private string displayName;
        [Tooltip("Especie del set.")]
        [ContentIdReference(typeof(SpeciesData)), SerializeField] private string speciesId = "";
        [Tooltip("Formato: ou, ubers, uu, ru, nu, pu, lc... (o el tuyo). Cada entrenador puede limitar de qué formatos coge sets.")]
        [SerializeField] private string setFormat = "ou";
        [Tooltip("0-100: cuánto se usa de verdad (movimientos, objeto y habilidad en las estadísticas). La IA elige con más peso los altos.")]
        [SerializeField, Range(0, 100)] private int setScore = 50;
        [Tooltip("Objetos posibles separados por coma (el primero, el recomendado). Vacío = el que le ponga su IA.")]
        [SerializeField] private string itemOptions = "";
        [Tooltip("Habilidades posibles separadas por coma. Vacío = la que le toque.")]
        [SerializeField] private string abilityOptions = "";
        [Tooltip("Naturalezas posibles separadas por coma.")]
        [SerializeField] private string natureOptions = "";
        [Tooltip("EVs, p. ej. «252 Atq / 4 DefE / 252 Vel».")]
        [SerializeField] private string evs = "";
        [Tooltip("IVs distintos de 31, p. ej. «0 Atq».")]
        [SerializeField] private string ivs = "";
        [Tooltip("Movimientos: 4 huecos separados por «/»; en cada hueco, alternativas separadas por coma. " +
                 "Ej.: swords_dance / earthquake / outrage, dragon_claw / stone_edge, fire_fang")]
        [SerializeField, TextArea(2, 4)] private string moveSlots = "";

        public string Id => id;
        public string DisplayName => displayName;
        public string SpeciesId => speciesId;
        public string Format => setFormat;
        public int Score => setScore;
        public string Items => itemOptions;
        public string Abilities => abilityOptions;
        public string Natures => natureOptions;
        public string Evs => evs;
        public string Ivs => ivs;
        public string Moves => moveSlots;
    }
}
