using UnityEngine;

namespace CTEditor.GameDefinition.Infrastructure.ScriptableObjects
{
    /// <summary>
    /// La ficha del autor para un Ruleset. El autor crea uno "Clásico" y, si quiere, copias con otras
    /// perillas. Mínima y plana, igual que el Ruleset del dominio (tu decisión). La fórmula se nombra
    /// por texto ("classic"), coherente con FormulaId: la ficha tampoco conoce la clase de la fórmula.
    /// </summary>
    [CreateAssetMenu(menuName = "CTEditor/Reglas del juego", fileName = "NuevasReglas")]
    public sealed class RulesetData : ScriptableObject, IContentAsset
    {
        [Tooltip("Id del conjunto de reglas (p. ej. 'classic'). El juego usa el PRIMERO de la carpeta Rulesets: ten solo uno.")]
        [SerializeField] private string id = "classic";
        [SerializeField] private string displayName = "Clásico";

        [Header("Equipo y combate")]
        [SerializeField, Min(1)] private int maxPartySize = 6;
        [SerializeField, Min(1)] private int maxMovesPerMonster = 4;
        [SerializeField, Min(1)] private int levelCap = 100;
        [SerializeField] private string damageFormulaId = "classic";

        [Header("Genética (0 = apagado)")]
        [Tooltip("Valor máximo de un IV. Clásico: 31. Pon 0 para un juego sin IVs.")]
        [SerializeField, Min(0)] private int maxIv = 31;
        [Tooltip("Tope de EVs por estadística. Clásico: 252. Pon 0 para un juego sin EVs.")]
        [SerializeField, Min(0)] private int maxEvPerStat = 252;
        [Tooltip("Tope de EVs totales sumando todas las estadísticas. Clásico: 510.")]
        [SerializeField, Min(0)] private int maxEvTotal = 510;

        [Header("PP (puntos de poder)")]
        [Tooltip("¿Los movimientos gastan PP al usarse? Desmárcalo para usos ilimitados.")]
        [SerializeField] private bool usePp = true;
        [Tooltip("Movimiento que se usa cuando no quedan PP en ninguno (Forcejeo). Vacío = el turno se pierde.")]
        [ContentIdReference(typeof(MoveData)), SerializeField] private string struggleMoveId = "struggle";

        [Header("Golpes críticos")]
        [Tooltip("Probabilidad de crítico por etapa del movimiento, como '1 entre N'. Moderno: 24, 8, 2, 1 (etapa 3 = siempre). 0 = nunca.")]
        [SerializeField] private int[] critDenominators = { 24, 8, 2, 1 };
        [Tooltip("Cuánto multiplica un crítico. Moderno: 1,5. En 2ª-5ª gen.: 2.")]
        [SerializeField, Min(0.1f)] private float critMultiplier = 1.5f;

        [Header("Estadísticas que usa el daño según la categoría")]
        [StatIdReference, SerializeField] private string physicalAttackStat = "attack";
        [StatIdReference, SerializeField] private string physicalDefenseStat = "defense";
        [StatIdReference, SerializeField] private string specialAttackStat = "sp_attack";
        [StatIdReference, SerializeField] private string specialDefenseStat = "sp_defense";

        [Header("Reglas de generación")]
        [Tooltip("Generación de referencia (1-9; 0 = personalizada). Solo informa: las perillas de abajo son las que mandan. " +
                 "Usa las plantillas «Reglas de la 1.ª gen.»... del editor de reglas para ponerlas todas de golpe.")]
        [SerializeField, Range(0, 9)] private int generation = 0;
        [Tooltip("1.ª-3.ª gen.: la categoría Físico/Especial la decide el TIPO del movimiento (los de estado siguen siendo de estado).")]
        [SerializeField] private bool categoryByType = false;
        [Tooltip("Con «categoría por tipo»: los tipos cuyos movimientos son ESPECIALES. El resto, físicos.")]
        [ContentIdReference(typeof(ElementTypeData)), SerializeField]
        private string[] specialTypes = { "fire", "water", "grass", "electric", "ice", "psychic", "dragon", "dark" };
        [Tooltip("1.ª gen.: una sola estadística «Especial». Lo que sube o baja el Ataque Especial también mueve la Defensa Especial.")]
        [SerializeField] private bool singleSpecialStat = false;
        [Tooltip("¿Hay habilidades? (desde 3.ª gen.). Desmarcado: ninguna habilidad hace nada en combate.")]
        [SerializeField] private bool abilitiesEnabled = true;
        [Tooltip("¿Se pueden equipar objetos? (desde 2.ª gen.). Desmarcado: nadie lleva objetos.")]
        [SerializeField] private bool heldItemsEnabled = true;
        [Tooltip("¿Hay naturalezas? (desde 3.ª gen.). Desmarcado: todos neutros.")]
        [SerializeField] private bool naturesEnabled = true;
        [Tooltip("¿Hay géneros? (desde 2.ª gen.). Desmarcado: todos sin género.")]
        [SerializeField] private bool gendersEnabled = true;

        [Header("Mecánicas especiales")]
        [Tooltip("Las fichas de mecánica ACTIVAS en tu juego (Megaevolución...). Se pueden combinar. Vacío = ninguna.")]
        [ContentIdReference(typeof(MechanicData)), SerializeField] private string[] mechanicIds = new string[0];

        [Header("Huir")]
        [Tooltip("Desmarcado (clásico): huir de un salvaje depende de la velocidad y de los intentos. Marcado: siempre se huye.")]
        [SerializeField] private bool fleeAlwaysWorks = false;
        [Tooltip("¿Se puede huir de un combate contra un entrenador? Clásico: no.")]
        [SerializeField] private bool canFleeTrainerBattles = false;

        [Header("Captura")]
        [Tooltip("¿Se pueden capturar los monstruos de un entrenador? Clásico: no.")]
        [SerializeField] private bool canCatchTrainerMonsters = false;
        [Tooltip("Facilidad de captura global (1 = clásico, 2 = el doble de fácil, 0,5 = la mitad).")]
        [SerializeField, Min(0.1f)] private float catchRateMultiplier = 1f;
        [Tooltip("Si el equipo está lleno, el capturado va al PC. Desmárcalo para que se libere.")]
        [SerializeField] private bool sendToBoxWhenFull = true;

        [Header("Experiencia, movimientos y amistad")]
        [Tooltip("Repartir Experiencia para todo el equipo (desde 6ª gen.). Clásico: desmarcado (solo quien lucha).")]
        [SerializeField] private bool expShareAll = false;
        [Tooltip("Con Repartir Experiencia: % de la experiencia que reciben los que NO lucharon.")]
        [SerializeField, Range(0f, 100f)] private float expShareOthersPercent = 50f;
        [Tooltip("Al subir de nivel, aprende los movimientos de su lista de aprendizaje.")]
        [SerializeField] private bool learnMovesOnLevelUp = true;
        [Tooltip("Al terminar el combate, evoluciona si cumple la condición (el jugador puede cancelarlo).")]
        [SerializeField] private bool evolveAfterBattle = true;
        [Tooltip("Amistad que gana al subir un nivel.")]
        [SerializeField, Min(0)] private int friendshipPerLevelUp = 3;
        [Tooltip("Amistad que pierde al debilitarse.")]
        [SerializeField, Min(0)] private int friendshipLostOnFaint = 1;

        [Header("Dinero y derrota")]
        [Tooltip("Dinero con el que empieza una partida nueva.")]
        [SerializeField, Min(0)] private int startingMoney = 3000;
        [Tooltip("% del dinero que se pierde al quedarse sin monstruos en pie. Clásico: 50.")]
        [SerializeField, Range(0f, 100f)] private float moneyLostOnBlackoutPercent = 50f;
        [Tooltip("Al perder, el equipo aparece curado (como al volver al Centro).")]
        [SerializeField] private bool healOnBlackout = true;

        public string Id => id;
        public string DisplayName => displayName;
        public int MaxPartySize => maxPartySize;
        public int MaxMovesPerMonster => maxMovesPerMonster;
        public int LevelCap => levelCap;
        public string DamageFormulaId => damageFormulaId;
        public int MaxIv => maxIv;
        public int MaxEvPerStat => maxEvPerStat;
        public int MaxEvTotal => maxEvTotal;
        public bool UsePp => usePp;
        public string StruggleMoveId => struggleMoveId;
        public int[] CritDenominators => critDenominators;
        public float CritMultiplier => critMultiplier;
        public string PhysicalAttackStat => physicalAttackStat;
        public string PhysicalDefenseStat => physicalDefenseStat;
        public string SpecialAttackStat => specialAttackStat;
        public string SpecialDefenseStat => specialDefenseStat;
        public int Generation => generation;
        public bool CategoryByType => categoryByType;
        public string[] SpecialTypes => specialTypes;
        public bool SingleSpecialStat => singleSpecialStat;
        public bool Abilities => abilitiesEnabled;
        public bool HeldItems => heldItemsEnabled;
        public bool Natures => naturesEnabled;
        public bool Genders => gendersEnabled;
        public string[] MechanicIds => mechanicIds;
        public bool FleeAlwaysWorks => fleeAlwaysWorks;
        public bool CanFleeTrainerBattles => canFleeTrainerBattles;
        public bool CanCatchTrainerMonsters => canCatchTrainerMonsters;
        public float CatchRateMultiplier => catchRateMultiplier;
        public bool SendToBoxWhenFull => sendToBoxWhenFull;
        public bool ExpShareAll => expShareAll;
        public float ExpShareOthersPercent => expShareOthersPercent;
        public bool LearnMovesOnLevelUp => learnMovesOnLevelUp;
        public bool EvolveAfterBattle => evolveAfterBattle;
        public int FriendshipPerLevelUp => friendshipPerLevelUp;
        public int FriendshipLostOnFaint => friendshipLostOnFaint;
        public int StartingMoney => startingMoney;
        public float MoneyLostOnBlackoutPercent => moneyLostOnBlackoutPercent;
        public bool HealOnBlackout => healOnBlackout;
    }
}
