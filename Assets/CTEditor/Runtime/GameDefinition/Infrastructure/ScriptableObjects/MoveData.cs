using UnityEngine;
using CTEditor.GameDefinition.Domain.Moves;

namespace CTEditor.GameDefinition.Infrastructure.ScriptableObjects
{
    /// <summary>
    /// La ficha que rellena el autor para un movimiento. Igual que ElementTypeData, vive en
    /// Infrastructure y la traduce su mapper. Aquí aparecen dos cosas nuevas que vale la pena ver.
    /// </summary>
    [CreateAssetMenu(menuName = "CTEditor/Movimiento", fileName = "NuevoMovimiento")]
    public sealed class MoveData : ScriptableObject, IContentAsset
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;

        // NOVEDAD 1 — referencia a OTRA ficha. El autor arrastra aquí el asset del tipo (Fuego, etc.).
        // En el Inspector esto es cómodo (drag & drop) y Unity mantiene la referencia aunque renombres
        // o muevas el archivo (la rastrea por su GUID interno, no por nombre). PERO el dominio y los
        // guardados NO usarán esta referencia de objeto: el mapper leerá el id ESTABLE de la ficha
        // referenciada (type.Id) y trabajará con ese string (M.4). Así juntamos lo mejor de ambos
        // mundos: autoría cómoda arriba, ids estables abajo.
        [SerializeField] private ElementTypeData type;

        // NOVEDAD 2 — un enum del DOMINIO usado directo en la ficha. Unity lo dibuja como desplegable
        // en el Inspector. Se puede porque un enum es un valor simple, sin nada de Unity dentro;
        // Infrastructure conoce el dominio, así que puede reutilizar sus enums tal cual.
        [SerializeField] private MoveCategory category = MoveCategory.Physical;

        [SerializeField] private int power = 0;

        // La precisión en el dominio era 'Percentage?' (nullable; null = nunca falla). Un nullable es
        // incómodo en el Inspector, así que aquí lo partimos en dos campos amigables: un check
        // "nunca falla" y un número 0–100. El mapper los recombina en el Percentage? del dominio.
        [SerializeField] private bool neverMisses = false;
        [SerializeField, Range(0f, 100f)] private float accuracy = 100f;

        [SerializeField] private int maxPp = 5;
        [SerializeField] private int priority = 0;
        [SerializeField] private MoveTarget target = MoveTarget.SingleEnemy;

        // Efectos secundarios autorables: cada uno = probabilidad + estado a infligir. Un movimiento
        // de daño con un efecto al 10% es un "efecto secundario"; un movimiento de Estado (Power 0,
        // categoría Status) con un efecto al 100% es algo como Fuego Fatuo.
        [SerializeField] private MoveEffectData[] secondaryEffects;

        [Tooltip("Golpe múltiple: mínimo y máximo de impactos por turno. 1 y 1 = normal; 2 y 5 = multi-golpe clásico.")]
        [SerializeField, Min(1)] private int minHits = 1;
        [SerializeField, Min(1)] private int maxHits = 1;

        [Tooltip("Nivel de crítico: 0 normal, mayor = más probabilidad (0=1/16, 1=1/8, 2=1/4, 3=1/3, 4+=1/2).")]
        [SerializeField, Min(0)] private int critStage = 0;

        [Tooltip("Dos turnos: None normal; Charge carga y golpea al siguiente; Recharge golpea y recarga.")]
        [SerializeField] private TwoTurnKind twoTurn = TwoTurnKind.None;

        [Tooltip("¿Hace contacto físico? Lo usan habilidades como Estática o Cuerpo Llama.")]
        [SerializeField] private bool makesContact = false;

        [Header("Daño especial (ignora potencia y estadísticas)")]
        [Tooltip("None = daño normal. Fixed = siempre la cantidad de abajo (Bomba Sónica). UserLevel = el nivel del usuario (Sísmico). HalfTargetHp = mitad de los PS actuales (Superdiente). OneHitKo = KO directo si el objetivo no tiene más nivel (Guillotina).")]
        [SerializeField] private FixedDamageKind fixedDamage = FixedDamageKind.None;
        [Tooltip("PS exactos que quita si el daño especial es Fixed.")]
        [SerializeField, Min(0)] private int fixedDamageAmount = 0;

        [Tooltip("Solo movimientos de ESTADO: si se marca, no afecta a quien es inmune por tipo (Onda Trueno no afecta a Tierra).")]
        [SerializeField] private bool respectsTypeImmunity = false;

        [Header("Potencia avanzada (opcional: vacío = potencia normal)")]
        [Tooltip("Multiplicadores de potencia con condiciones. Ej.: ×2 si propio tiene estado (Fachada). Sin condiciones = siempre.")]
        [SerializeField] private PowerModifierData[] powerModifiers = new PowerModifierData[0];
        [Tooltip("Fórmula que REEMPLAZA la potencia (vacío = usar 'Power'). Variables: potencia, nivel, nivel_rival, vida, vida_rival (0-100), amistad, velocidad, velocidad_rival. Ej.: 150 * vida / 100")]
        [SerializeField] private string powerFormula = "";

        [Header("Estadísticas del daño (vacío = las de su categoría)")]
        [Tooltip("Stat con la que ATACA. Vacío = Ataque (físico) o Atq. Esp. (especial).")]
        [StatIdReference, SerializeField] private string attackStat = "";
        [Tooltip("Stat con la que se DEFIENDE el rival. Vacío = Defensa (físico) o Def. Esp. (especial). Psicocarga: 'defense'.")]
        [StatIdReference, SerializeField] private string defenseStat = "";
        [Tooltip("Ataca con la estadística del RIVAL en vez de la propia (Juego Sucio).")]
        [SerializeField] private bool attackStatFromTarget = false;

        [Tooltip("Etiquetas libres para agrupar movimientos: puño, sonido, mordisco, polvo... Las habilidades pueden potenciarlas.")]
        [SerializeField] private string[] tags = new string[0];

        [Header("Requisitos (vacío = siempre funciona)")]
        [Tooltip("Solo funciona si se cumplen TODAS. Si no: «¡Pero falló!». Comesueños: el rival está dormido (rival · tiene el estado · sleep).")]
        [SerializeField] private ConditionData[] requirements = new ConditionData[0];

        [Header("Tipo según el clima (vacío = siempre el suyo)")]
        [Tooltip("Meteorobola: rain → Agua, sun → Fuego, hail → Hielo, sandstorm → Roca.")]
        [SerializeField] private WeatherTypeEntry[] typeByWeather = new WeatherTypeEntry[0];

        [System.Serializable]
        public sealed class WeatherTypeEntry
        {
            [ContentIdReference(typeof(WeatherData))] public string weatherId = "rain";
            public ElementTypeData type;
        }

        [Header("Presentación (solo la usa la interfaz; no afecta al combate)")]
        [Tooltip("Segundos que la interfaz espera para la animación de este movimiento (0 = sin pausa de animación).")]
        [SerializeField] private float animationSeconds = 0f;

        public string Id => id;
        public string DisplayName => displayName;
        public ElementTypeData Type => type;
        public MoveCategory Category => category;
        public int Power => power;
        public bool NeverMisses => neverMisses;
        public float Accuracy => accuracy;
        public int MaxPp => maxPp;
        public int Priority => priority;
        public MoveTarget Target => target;
        public MoveEffectData[] SecondaryEffects => secondaryEffects;
        public int MinHits => minHits;
        public int MaxHits => maxHits;
        public int CritStage => critStage;
        public TwoTurnKind TwoTurn => twoTurn;
        public bool MakesContact => makesContact;
        public FixedDamageKind FixedDamage => fixedDamage;
        public int FixedDamageAmount => fixedDamageAmount;
        public bool RespectsTypeImmunity => respectsTypeImmunity;
        public PowerModifierData[] PowerModifiers => powerModifiers;
        public string PowerFormula => powerFormula;
        public string AttackStat => attackStat;
        public string DefenseStat => defenseStat;
        public bool AttackStatFromTarget => attackStatFromTarget;
        public string[] Tags => tags;
        public ConditionData[] Requirements => requirements ?? new ConditionData[0];
        public float AnimationSeconds => animationSeconds;
        public WeatherTypeEntry[] TypeByWeather => typeByWeather ?? new WeatherTypeEntry[0];

        /// <summary>
        /// Sub-ficha de un efecto secundario, editable en el Inspector (Unity sabe dibujar clases
        /// marcadas [Serializable]). El mapper la traduce a un MoveEffect del dominio.
        /// </summary>
        [System.Serializable]
        public sealed class MoveEffectData
        {
            [Range(0f, 100f)] public float chancePercent = 100f;
            public MoveEffectKind kind = MoveEffectKind.InflictStatus;
            public EffectTarget target = EffectTarget.Opponent;
            [StatusIdReference] public string statusId;     // si kind == InflictStatus
            [Range(0f, 100f)] public float amountPercent;   // si kind == Drain/Recoil/HealSelf (% del daño o de PS máx)
            [StatIdReference] public string statStatId;                       // si kind == ChangeStatStage (id de la stat: ej. "attack")
            [Range(-6, 6)] public int statStages;           // si kind == ChangeStatStage (delta de etapas, p.ej. +2 o -1)
            [ContentIdReference(typeof(WeatherData))] public string weatherId = ""; // si kind == SetWeather (id del clima)
            [Min(0)] public int weatherTurns = 0;           // si kind == SetWeather (0 = los de la ficha del clima)
            [ContentIdReference(typeof(HazardData))] public string hazardId = ""; // si kind == SetHazard / ClearHazards (vacío al quitar = todas)
            [ContentIdReference(typeof(SideConditionData))] public string sideConditionId = ""; // si kind == SetSideCondition (Reflejo, Pantalla de Luz...)
            [Tooltip("Turnos: Anulación (4), Otra Vez (3), máximo de Saña (3). 0 = los clásicos.")]
            [Min(0)] public int turns = 0;
            [Tooltip("Cambiar tipo: el tipo nuevo. Vacío = el de su primer movimiento que no tenga ya (Conversión).")]
            [ContentIdReference(typeof(ElementTypeData))] public string typeId = "";
            [Tooltip("Texto según el efecto: habilidad (Abatidoras: insomnia), movimiento (Adaptación: tri_attack), estadísticas (Cambia Fuerza: attack,sp_attack)...")]
            public string text = "";
            [Tooltip("Usa el MISMO dado que el efecto anterior: o salen los dos o ninguno (Poder Pasado).")]
            public bool sharesPreviousRoll = false;
            [Tooltip("El efecto solo ocurre si se cumplen TODAS. Vacío = siempre.")]
            public ConditionData[] conditions = new ConditionData[0];
        }
    }
}
