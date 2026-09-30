using UnityEngine;
using CTEditor.GameDefinition.Domain.Trainers;

namespace CTEditor.GameDefinition.Infrastructure.ScriptableObjects
{
    /// <summary>
    /// La ficha de un ENTRENADOR rival: nombre, clase, equipo, cómo piensa, cuánto paga y qué dice.
    /// La traduce TrainerMapper al dominio.
    /// </summary>
    [CreateAssetMenu(menuName = "CTEditor/Entrenador", fileName = "NuevoEntrenador")]
    public sealed class TrainerData : ScriptableObject, IContentAsset
    {
        [SerializeField] private string id;
        [Tooltip("Nombre del entrenador (Joaquín, Brock...).")]
        [SerializeField] private string displayName;
        [Tooltip("Clase del entrenador (Joven, Cazabichos, Líder de gimnasio...). Se muestra delante del nombre.")]
        [SerializeField] private string trainerClass = "Entrenador";
        [Tooltip("Retrato para la pantalla de combate (opcional).")]
        [SerializeField] private Sprite portrait;

        [Header("Equipo")]
        [SerializeField] private TeamMemberData[] team;

        [Header("Cómo combate")]
        [Tooltip("NIVEL DE IA (1-7): 1 Novato, 2 Aficionado, 3 Veterano, 4 Élite, 5 Campeón, 6 Maestro, 7 Injusto. Cada nivel se ajusta en CTEditor → Entrenadores → Niveles de IA. " +
                 "0 = el de la IA antigua (novato 1, listo 2, experto 4).")]
        [SerializeField, Range(0, 7)] private int aiLevel = 0;
        [Tooltip("IA PERSONALIZADA (opcional): id de una ficha de «Niveles de IA» marcada como personalizada (ia_brock...). " +
                 "Si existe, manda sobre el nivel. Vacío = la IA de su nivel.")]
        [ContentIdReference(typeof(AiLevelData))]
        [SerializeField] private string aiProfileId = "";
        [Tooltip("IA antigua (solo se usa si «Nivel de IA» está en 0).")]
        [SerializeField] private TrainerAi ai = TrainerAi.Smart;
        [Tooltip("¿Usa los objetos de su mochila en combate (pociones, curas de estado, Ataque X...)?")]
        [SerializeField] private bool useItems = true;
        [Tooltip("Su mochila para el combate: qué objetos lleva y cuántos. Se gastan al usarlos (solo en ese combate).")]
        [SerializeField] private BagEntryData[] items;
        [Tooltip("Se cura cuando su monstruo tiene este % de PS o menos. Clásico: 25.")]
        [SerializeField, Range(1, 100)] private int healBelowPercent = 25;
        [Tooltip("¿Puede retirar a su monstruo para sacar otro que gane el duelo? (solo lo hace la IA Experta).")]
        [SerializeField] private bool canSwitch = true;
        [Tooltip("Movimientos de los miembros SIN movimientos escritos. Según su IA: novato = los 4 últimos que aprende, " +
                 "listo = equilibrado (mejor ataque + cobertura + apoyo), experto = fuerte (los que más daño hacen).")]
        [SerializeField] private MovesetStyle movesetStyle = MovesetStyle.ByAi;
        [Tooltip("Dinero base. Premio al ganarle = este valor × nivel de su ÚLTIMO monstruo. Clásico: Joven 16, Cazabichos 12, Líder 100.")]
        [SerializeField, Min(0)] private int baseMoney = 20;

        [Header("Frases")]
        [Tooltip("Lo que dice al empezar el combate.")]
        [SerializeField, TextArea(2, 3)] private string introLine = "";
        [Tooltip("Lo que dice al perder.")]
        [SerializeField, TextArea(2, 3)] private string defeatLine = "";
        [Tooltip("Lo que dice si gana él.")]
        [SerializeField, TextArea(2, 3)] private string victoryLine = "";

        public string Id => id;
        public string DisplayName => displayName;
        public string TrainerClass => trainerClass;
        public Sprite Portrait => portrait;
        public TeamMemberData[] Team => team;
        public TrainerAi Ai => ai;
        /// <summary>Nivel de IA 1-7 (0 = sacarlo de la IA antigua).</summary>
        public int AiLevel => aiLevel;
        /// <summary>Id de su IA personalizada (vacío = la de su nivel).</summary>
        public string AiProfileId => aiProfileId;
        /// <summary>El nivel efectivo (1-7).</summary>
        public int EffectiveAiLevel => aiLevel > 0 ? aiLevel : AiProfile.LevelFromLegacy(ai);
        public bool UseItems => useItems;
        public BagEntryData[] Items => items;
        public int HealBelowPercent => healBelowPercent;
        public bool CanSwitch => canSwitch;
        public MovesetStyle MovesetStyle => movesetStyle;
        public int BaseMoney => baseMoney;
        public string IntroLine => introLine;
        public string DefeatLine => defeatLine;
        public string VictoryLine => victoryLine;

        // Guarda los ids junto a las referencias (si una especie se vuelve a crear, el equipo no se pierde).
        private void OnValidate() => TeamMemberData.SyncIds(team);
    }
}
