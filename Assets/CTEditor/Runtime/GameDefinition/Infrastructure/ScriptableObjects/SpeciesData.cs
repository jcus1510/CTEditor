using System;
using UnityEngine;

namespace CTEditor.GameDefinition.Infrastructure.ScriptableObjects
{
    /// <summary>
    /// La ficha del autor para una criatura. Es la más rica del motor, así que reúne varias técnicas
    /// de autoría a la vez. La clave conceptual: aquí el autor disfruta de COMODIDAD (campos con
    /// nombre, listas arrastrables), y el mapper la colapsa luego al dominio UNIFORME y puro.
    /// </summary>
    [CreateAssetMenu(menuName = "CTEditor/Especie", fileName = "NuevaEspecie")]
    public sealed class SpeciesData : ScriptableObject, IContentAsset
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [Tooltip("Nombre en INGLÉS (el de Showdown): sirve para importar y exportar equipos. Vacío = se deduce del id.")]
        [SerializeField] private string englishName = "";

        // Lista de tipos (arrastrables). El '[]' es un arreglo serializable: Unity lo muestra como
        // una lista editable. Soporta mono, doble o más tipos: la libertad de la que hablamos.
        [SerializeField] private ElementTypeData[] types;

        [Tooltip("Id de la habilidad de la especie (debe existir como AbilityData). Vacío = ninguna.")]
        [ContentIdReference(typeof(AbilityData)), SerializeField] private string abilityId;

        [Tooltip("Segunda habilidad posible (vacío = solo tiene una). Cada individuo sale con la 1.ª o la 2.ª.")]
        [ContentIdReference(typeof(AbilityData)), SerializeField] private string secondAbilityId;

        [Tooltip("Habilidad OCULTA (vacío = ninguna). Solo la tienen los individuos que tú elijas.")]
        [ContentIdReference(typeof(AbilityData)), SerializeField] private string hiddenAbilityId;

        [Tooltip("Id de la curva de XP (debe existir como GrowthCurveData en GameContent/Curves). Vacío = por defecto.")]
        [ContentIdReference(typeof(GrowthCurveData)), SerializeField] private string growthCurveId;

        [Tooltip("Rendimiento base de XP: cuánta experiencia 'vale' derrotar a esta especie (clásico: 50-300).")]
        [SerializeField, Min(1)] private int baseExpYield = 64;

        [Tooltip("EVs que otorga derrotarla: id de la estadística (hp, attack, defense, sp_attack, sp_defense, speed) + puntos (clásico: 1-3).")]
        [SerializeField] private EvYieldEntryData[] evYield;

        [Tooltip("Amistad con la que empieza un individuo recién obtenido (0-255). Clásico: 70. La usan las condiciones de los movimientos (p. ej. Retribución).")]
        [SerializeField, Range(0, 255)] private int baseFriendship = 70;

        [Tooltip("Ratio de captura (1-255): cuanto más alto, más fácil de capturar. Caterpie 255, Pikachu 190, iniciales 45, legendarios 3.")]
        [SerializeField, Range(1, 255)] private int catchRate = 45;

        [Header("Pokédex")]
        [Tooltip("Número en la Pokédex (0 = sin número). Ordena la lista de especies.")]
        [SerializeField, Min(0)] private int dexNumber = 0;
        [Tooltip("Categoría: «Semilla» → se ve «Pokémon Semilla».")]
        [SerializeField] private string category = "";
        [Tooltip("Altura en metros (Bulbasaur: 0,7).")]
        [SerializeField, Min(0f)] private float heightM = 0f;
        [Tooltip("Peso en kilos (Bulbasaur: 6,9).")]
        [SerializeField, Min(0f)] private float weightKg = 0f;
        [Tooltip("Color principal para buscar en la Pokédex: verde, rojo, azul...")]
        [SerializeField] private string dexColor = "";
        [Tooltip("Descripción de la Pokédex.")]
        [SerializeField, TextArea(2, 5)] private string dexDescription = "";
        [Tooltip("% de hembras (0-100). -1 = sin género (Magnemite, legendarios...).")]
        [SerializeField, Range(-1f, 100f)] private float femalePercent = 50f;
        [Tooltip("Legendaria o singular.")]
        [SerializeField] private bool legendary = false;

        // --- STATS BASE ---
        // Aquí está la idea importante: en el dominio los stats son UNIFORMES por clave (StatBlock).
        // Pero pedirle al autor que escriba "hp", "attack"... a mano sería horrible. Así que en la
        // FICHA los 6 clásicos son campos con nombre (cómodos), y los inventados van en una lista.
        // El mapper junta ambos en el StatBlock único. Misma idea que los accesores .Attack del
        // dominio: una comodidad encima del almacenamiento uniforme, sin dos caminos de verdad.
        [Header("Estadísticas base — clásicas")]
        [SerializeField, Min(0)] private int hp = 1;
        [SerializeField, Min(0)] private int attack = 1;
        [SerializeField, Min(0)] private int defense = 1;
        [SerializeField, Min(0)] private int spAttack = 1;
        [SerializeField, Min(0)] private int spDefense = 1;
        [SerializeField, Min(0)] private int speed = 1;

        [Header("Estadísticas base — inventadas (opcional)")]
        [SerializeField] private CustomStatValue[] customStats;

        [Header("Aprendizaje y evolución")]
        [SerializeField] private LearnableMoveEntry[] learnset;
        [SerializeField] private EvolutionEntry[] evolutions;

        [Header("Otros movimientos (MT, tutor y huevo)")]
        [Tooltip("Movimientos que puede aprender por MT/MO.")]
        [SerializeField] private MoveData[] machineMoves = new MoveData[0];
        [Tooltip("Movimientos que le puede enseñar un tutor.")]
        [SerializeField] private MoveData[] tutorMoves = new MoveData[0];
        [Tooltip("Movimientos HUEVO: los hereda al nacer (crianza futura) y los usan los entrenadores Élite y Campeón.")]
        [SerializeField] private MoveData[] eggMoves = new MoveData[0];

        [Header("Crianza")]
        [Tooltip("Sus grupos huevo (hasta 2). Dos especies pueden criar si comparten alguno.")]
        [SerializeField] private EggGroupData[] eggGroups = new EggGroupData[0];

        // --- Getters de solo lectura ---
        [Header("Formas y variantes")]
        [Tooltip("VARIANTE: esta especie es forma de otra (Rotom Lavado es forma de Rotom). Vacío = no es variante.")]
        [ContentIdReference(typeof(SpeciesData)), SerializeField] private string formOf = "";
        [Tooltip("Con «Es forma de»: objeto que, usado fuera del combate, cambia a esta variante (y la devuelve a la base). " +
                 "Vacío = solo cambia con un personaje del mapa.")]
        [ContentIdReference(typeof(ItemData)), SerializeField] private string variantItem = "";
        [Tooltip("Formas de COMBATE: cambian tipos, estadísticas o habilidad en mitad del combate y al acabar vuelven a la normal.")]
        [SerializeField] private FormEntry[] forms = new FormEntry[0];
        [Tooltip("Qué provoca cada cambio de forma en combate (objeto, movimiento, PS, clima, megaevolución).")]
        [SerializeField] private FormChangeEntry[] formChanges = new FormChangeEntry[0];

        public string Id => id;
        public string DisplayName => displayName;
        public string EnglishName => englishName;
        public ElementTypeData[] Types => types;
        public string AbilityId => abilityId;
        public string SecondAbilityId => secondAbilityId;
        public string HiddenAbilityId => hiddenAbilityId;
        public MoveData[] MachineMoves => machineMoves;
        public MoveData[] TutorMoves => tutorMoves;
        public MoveData[] EggMoves => eggMoves;
        public EggGroupData[] EggGroups => eggGroups;
        public string GrowthCurveId => growthCurveId;
        public int BaseExpYield => baseExpYield;
        public EvYieldEntryData[] EvYield => evYield;
        public int BaseFriendship => baseFriendship;
        public int CatchRate => catchRate;
        public int DexNumber => dexNumber;
        public string Category => category;
        public float HeightM => heightM;
        public float WeightKg => weightKg;
        public string DexColor => dexColor;
        public string DexDescription => dexDescription;
        public float FemalePercent => femalePercent;
        public bool Legendary => legendary;
        public int Hp => hp;
        public int Attack => attack;
        public int Defense => defense;
        public int SpAttack => spAttack;
        public int SpDefense => spDefense;
        public int Speed => speed;
        public CustomStatValue[] CustomStats => customStats;
        public LearnableMoveEntry[] Learnset => learnset;
        public EvolutionEntry[] Evolutions => evolutions;
        public string FormOf => formOf;
        public string VariantItem => variantItem;
        public FormEntry[] Forms => forms;
        public FormChangeEntry[] FormChanges => formChanges;

        /// <summary>
        /// Una forma de combate: lo que no se rellena (tipos vacíos, estadística 0, habilidad vacía) se queda como en la
        /// especie. Los PS no cambian en combate.
        /// </summary>
        [Serializable]
        public sealed class FormEntry
        {
            [Tooltip("Id de la forma dentro de la especie (zen, blade, mega...).")]
            public string id = "";
            public string displayName = "";
            [ContentIdReference(typeof(ElementTypeData))] public string type1 = "";
            [ContentIdReference(typeof(ElementTypeData))] public string type2 = "";
            [Tooltip("0 = igual que la especie.")] public int attack, defense, spAttack, spDefense, speed;
            [ContentIdReference(typeof(AbilityData))] public string ability = "";
            [Tooltip("Al retirarse vuelve a la forma normal (Modo Daruma, Aegislash). Desmarcado: se queda hasta el final.")]
            public bool revertsOnSwitch;
        }

        /// <summary>Una regla de cambio de forma: de 'from' a 'to' ("" = la forma normal, "*" = desde cualquiera) cuando...</summary>
        [Serializable]
        public sealed class FormChangeEntry
        {
            [Tooltip("Desde qué forma (vacío = la normal, * = cualquiera).")] public string from = "";
            [Tooltip("A qué forma (vacío = vuelve a la normal).")] public string to = "";
            public CTEditor.GameDefinition.Domain.Species.FormTrigger trigger;
            [ContentIdReference(typeof(ItemData))] public string item = "";
            [ContentIdReference(typeof(MoveData))] public string move = "";
            [Range(0, 100)] public int hpPercent = 50;
            [ContentIdReference(typeof(WeatherData))] public string weather = "";
            [ContentIdReference(typeof(AbilityData))] public string requiredAbility = "";
            [Tooltip("Con movimientos: cambia DESPUÉS de usarlo (Meloetta). Desmarcado: antes (Aegislash).")]
            public bool afterMove;
        }

        // --- Structs anidados y serializables ---
        // '[Serializable]' le dice a Unity "sabes dibujar esto en el Inspector". Sin él, estas
        // estructuras no aparecerían como campos editables. Usamos campos públicos porque es la
        // convención de Unity para estos contenedores de datos planos.

        /// <summary>Un stat inventado: su id de texto y su valor base.</summary>
        [Serializable]
        public struct CustomStatValue
        {
            public string statId;
            public int value;
        }

        /// <summary>Una entrada de rendimiento de EVs: id de la stat y cuántos puntos otorga.</summary>
        [Serializable]
        public struct EvYieldEntryData
        {
            [StatIdReference] public string statId;
            public int amount;
        }

        /// <summary>Una entrada del learnset: el movimiento (arrastrado) y a qué nivel se aprende.</summary>
        [Serializable]
        public struct LearnableMoveEntry
        {
            public MoveData move;
            public int level;
        }

        /// <summary>
        /// Una evolución: la especie destino (arrastrada), CÓMO evoluciona (nivel, objeto, amistad o
        /// intercambio) y sus requisitos. Con "nivel" se usa el nivel; con los demás, el nivel es un
        /// mínimo opcional (0 = cualquiera).
        /// </summary>
        [Serializable]
        public struct EvolutionEntry
        {
            public SpeciesData target;
            public int requiredLevel;
            public CTEditor.GameDefinition.Domain.Species.EvolutionMethod method;
            [ContentIdReference(typeof(ItemData))] public string itemId;
            [Range(0, 255)] public int minFriendship;
            [Tooltip("Condiciones EXTRA: todas deben cumplirse a la vez (de día, llevando un objeto, sabiendo un movimiento...).")]
            public EvolutionConditionData[] conditions;
        }

        /// <summary>
        /// Una condición extra de evolución. Solo se usan los campos que pide su "Qué comprueba" (el
        /// Inspector esconde el resto). "Al revés" la invierte: "y SIN llevar la Piedra Eterna".
        /// </summary>
        [Serializable]
        public sealed class EvolutionConditionData
        {
            public CTEditor.GameDefinition.Domain.Species.EvolutionConditionKind check;
            [Tooltip("Número: nivel, amistad (0-255) o porcentaje (1-99) según lo que compruebe.")]
            public int value;
            [ContentIdReference(typeof(ItemData))] public string itemId;
            public MoveData move;
            public ElementTypeData type;
            public SpeciesData species;
            public NatureData nature;
            [ContentIdReference(typeof(WeatherData))] public string weatherId;
            [Tooltip("Lugar del mapa o marca de la partida (texto libre, ej. monte_plateado o vencio_alto_mando).")]
            public string text;
            public CTEditor.GameDefinition.Domain.Species.DayTime time;
            public CTEditor.GameDefinition.Domain.Species.StatRelation relation;
            [Tooltip("Al revés: se cumple cuando NO pasa.")]
            public bool negate;

            /// <summary>El id que usa el dominio según lo que compruebe.</summary>
            public string IdFor()
            {
                switch (check)
                {
                    case CTEditor.GameDefinition.Domain.Species.EvolutionConditionKind.HoldsItem: return itemId ?? "";
                    case CTEditor.GameDefinition.Domain.Species.EvolutionConditionKind.KnowsMove: return move != null ? move.Id : "";
                    case CTEditor.GameDefinition.Domain.Species.EvolutionConditionKind.KnowsMoveOfType:
                    case CTEditor.GameDefinition.Domain.Species.EvolutionConditionKind.PartyHasType: return type != null ? type.Id : "";
                    case CTEditor.GameDefinition.Domain.Species.EvolutionConditionKind.PartyHasSpecies: return species != null ? species.Id : "";
                    case CTEditor.GameDefinition.Domain.Species.EvolutionConditionKind.Nature: return nature != null ? nature.Id : "";
                    case CTEditor.GameDefinition.Domain.Species.EvolutionConditionKind.MapWeather: return weatherId ?? "";
                    case CTEditor.GameDefinition.Domain.Species.EvolutionConditionKind.AtLocation:
                    case CTEditor.GameDefinition.Domain.Species.EvolutionConditionKind.GameFlag: return text ?? "";
                    default: return "";
                }
            }
        }
    }
}
