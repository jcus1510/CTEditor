using UnityEngine;

namespace CTEditor.GameDefinition.Infrastructure.ScriptableObjects
{
    /// <summary>
    /// La ficha que rellena el AUTOR para crear una HABILIDAD (Lote 1). El ACL (AbilityMapper) la
    /// traduce a AbilityDefinition (dominio puro). Cubre: modificadores pasivos de stat, inmunidad a
    /// estados e inmunidad a tipos.
    /// </summary>
    [CreateAssetMenu(menuName = "CTEditor/Habilidad", fileName = "NuevaHabilidad")]
    public sealed class AbilityData : ScriptableObject, IContentAsset
    {
        [Tooltip("Id estable de la habilidad (clave de texto, p.ej. \"levitate\").")]
        [SerializeField] private string id;

        [SerializeField] private string displayName;

        [Tooltip("Multiplica estadísticas mientras el portador está en combate (p.ej. ataque ×2). Se combina en EffectiveStat.")]
        [SerializeField] private PassiveStatModifierData[] passiveModifiers;

        [Tooltip("Estados que esta habilidad IMPIDE (no se pueden infligir al portador).")]
        [StatusIdReference, SerializeField] private string[] statusImmunities;

        [Tooltip("Tipos a los que el portador es INMUNE (los movimientos de ese tipo no le afectan).")]
        [SerializeField] private ElementTypeData[] typeImmunities;

        [Tooltip("Si es inmune a un tipo, % de PS máx que CURA al recibirlo (Absorbe Agua). 0 = solo anula.")]
        [Range(0f, 100f)] [SerializeField] private float absorbImmuneHealPercent = 0f;

        [Tooltip("Estado a infligir al ATACANTE cuando le pega un golpe de CONTACTO (Estática, Cuerpo Llama). Vacío = ninguno.")]
        [StatusIdReference, SerializeField] private string contactReactionStatus;

        [Tooltip("Probabilidad de la reacción por contacto.")]
        [Range(0f, 100f)] [SerializeField] private float contactReactionChance = 0f;

        [Tooltip("Al ENTRAR al campo, cambia una etapa de estadística (Intimidación: estadística \"attack\", -1, al rival). Vacío = nada.")]
        [StatIdReference, SerializeField] private string onEntryStatId;

        [Range(-6, 6)] [SerializeField] private int onEntryStages = 0;

        [Tooltip("Si está marcado, el cambio al entrar es sobre uno mismo; si no, sobre el rival.")]
        [SerializeField] private bool onEntryTargetsSelf = false;

        [Header("Condicionales")]
        [Tooltip("Stat que se multiplica SOLO si el portador tiene un estado (Agallas: \"attack\"; Pies Rápidos: \"speed\"). Vacío = nada.")]
        [StatIdReference, SerializeField] private string statusStatBoostStatId;

        [SerializeField] private float statusStatBoostMultiplier = 1f;

        [Tooltip("Tipo de movimiento potenciado al atacar con poca vida (Espesura: fuego). Vacío = nada.")]
        [SerializeField] private ElementTypeData lowHpBoostType;

        [Tooltip("Umbral de PS (% del máx) bajo el cual se activa el refuerzo a poca vida.")]
        [Range(0f, 100f)] [SerializeField] private float lowHpThresholdPercent = 33f;

        [SerializeField] private float lowHpBoostMultiplier = 1f;

        [Tooltip("Daño ENTRANTE por tipo: multiplicadores (Sebo: Fuego x0.5 y Hielo x0.5).")]
        [SerializeField] private IncomingTypeMultiplierData[] incomingTypeMultipliers;

        [Header("Prioridad / fin de turno / al cambiar / prevención")]
        [Tooltip("Multiplicador STAB explícito (Adaptable = 2). 0 = clásico (1.5).")]
        [SerializeField] private float stabMultiplierOverride = 0f;

        [Tooltip("Bonus de prioridad para movimientos de ESTADO (Vista Lince = 1).")]
        [SerializeField] private int statusMovePriorityBonus = 0;

        [Tooltip("Impide que el RIVAL le baje etapas de estadística (Cuerpo Puro).")]
        [SerializeField] private bool preventsStatReduction = false;

        [Tooltip("Al cambiarse (salir), cura su estado alterado (Cura Natural).")]
        [SerializeField] private bool curesStatusOnSwitchOut = false;

        [Tooltip("Al cambiarse, recupera este % de PS máx (Regeneración = 33).")]
        [Range(0f, 100f)] [SerializeField] private float healPercentOnSwitchOut = 0f;

        [Tooltip("Al fin de turno, cambia una etapa de estadística propia (Impulso: \"speed\"). Vacío = nada.")]
        [StatIdReference, SerializeField] private string endOfTurnStatId;

        [Range(-6, 6)] [SerializeField] private int endOfTurnStages = 0;

        [Tooltip("Al fin de turno, recupera este % de PS máx (Cura Veneno = 12.5).")]
        [Range(0f, 100f)] [SerializeField] private float endOfTurnHealPercent = 0f;

        [Tooltip("Si está marcado, esa curación solo ocurre si el portador TIENE estado (Cura Veneno).")]
        [SerializeField] private bool endOfTurnHealRequiresStatus = false;

        [Tooltip("Al fin de turno, probabilidad de curarse el estado (Mudar = 30).")]
        [Range(0f, 100f)] [SerializeField] private float endOfTurnCureStatusChance = 0f;

        [Tooltip("Anula el daño residual de estados (Cura Veneno / Guardia Mágica).")]
        [SerializeField] private bool negatesStatusDamage = false;

        [Header("Potencia con condiciones")]
        [Tooltip("AL ATACAR: multiplica la potencia de SUS movimientos si se cumplen las condiciones. Técnico: ×1,5 si potencia ≤ 60. Puño Férreo: ×1,2 si el movimiento tiene la etiqueta 'puño'. 'Propio' = el portador.")]
        [SerializeField] private PowerModifierData[] offensivePowerModifiers = new PowerModifierData[0];
        [Tooltip("AL RECIBIR: multiplica la potencia de los golpes que RECIBE. Peluche: ×0,5 si hace contacto. 'Propio' = el portador; 'rival' = quien ataca.")]
        [SerializeField] private PowerModifierData[] defensivePowerModifiers = new PowerModifierData[0];


        // ================= 3.ª y 4.ª generación (y habilidades ocultas) =================
        [Header("Clima")]
        [Tooltip("Al entrar pone este clima (Llovizna: rain; Sequía: sun; Chorro Arena: sandstorm; Nevada: hail).")]
        [SerializeField] private string onEntryWeather = "";
        [Tooltip("Turnos del clima al entrar. 0 = hasta que otro lo cambie (3.ª-5.ª gen.).")]
        [SerializeField, Min(0)] private int onEntryWeatherTurns = 0;
        [Tooltip("Mientras está en el campo el clima no hace nada (Aclimatación, Bucle Aire).")]
        [SerializeField] private bool suppressesWeather;
        [Tooltip("PS al final del turno con cada clima: positivo cura, negativo daña. Ej.: rain:6,25 (Cura Lluvia); sun:-12,5|rain:12,5 (Piel Seca).")]
        [SerializeField] private WeatherHpData[] weatherHpChanges = new WeatherHpData[0];
        [Tooltip("Climas que no le dañan. * = todos (Velo Arena: sandstorm; Funda: *).")]
        [SerializeField] private string[] weatherImmunities = new string[0];
        [Tooltip("Con este clima se cura su estado al final del turno (Hidratación: rain).")]
        [SerializeField] private string curesStatusInWeather = "";
        [Tooltip("Con este clima no le pueden poner estados (Defensa Hoja: sun).")]
        [SerializeField] private string statusImmuneInWeather = "";
        [Tooltip("Su tipo según el clima (Predicción). Ej.: rain:water|sun:fire|hail:ice.")]
        [SerializeField] private WeatherTypeData[] typeByWeather = new WeatherTypeData[0];

        [Header("Estadísticas y precisión con condiciones")]
        [Tooltip("Estadística × multiplicador si se cumplen las condiciones. Nado Rápido: speed ×2 si clima=rain.")]
        [SerializeField] private ConditionalStatData[] conditionalStats = new ConditionalStatData[0];
        [Tooltip("Precisión de SUS movimientos (Ojo Compuesto ×1,3).")]
        [SerializeField] private PowerModifierData[] accuracyModifiers = new PowerModifierData[0];
        [Tooltip("Precisión de lo que LE lanzan (Velo Arena ×0,8 si hay tormenta de arena).")]
        [SerializeField] private PowerModifierData[] evasionModifiers = new PowerModifierData[0];

        [Header("Daño y golpes")]
        [SerializeField, Tooltip("No sufre retroceso (Cabeza Roca).")] private bool noRecoil;
        [SerializeField, Tooltip("Solo le dañan los ataques (Muro Mágico).")] private bool noIndirectDamage;
        [SerializeField, Tooltip("No recibe críticos (Armadura Batalla).")] private bool critImmune;
        [SerializeField, Tooltip("+ índice de crítico (Afortunado: 1).")] private int critStageBonus;
        [SerializeField, Tooltip("Multiplica el daño de sus críticos (Francotirador: 1,5).")] private float critDamageMultiplier = 1f;
        [SerializeField, Tooltip("Estadísticas que el rival no le puede bajar (Vista Lince: accuracy).")] private string[] preventedStatDrops = new string[0];
        [SerializeField, Tooltip("No retrocede (Foco Interno).")] private bool flinchImmune;
        [SerializeField, Tooltip("Al retroceder sube esta estadística (Impasible: speed).")] private string onFlinchStat = "";
        [SerializeField, Range(-6, 6)] private int onFlinchStages;
        [SerializeField, Tooltip("Multiplica la probabilidad de sus efectos secundarios (Dicha: 2).")] private float secondaryChanceMultiplier = 1f;
        [SerializeField, Tooltip("Los efectos secundarios que le lanzan no le afectan (Polvo Escudo).")] private bool blocksIncomingSecondaries;
        [SerializeField, Tooltip("Sus movimientos pierden los efectos secundarios (Potencia Bruta).")] private bool removesOwnSecondaries;
        [SerializeField, Range(0f, 100f), Tooltip("% de hacer retroceder con sus ataques (Hedor: 10).")] private float flinchChanceOnAttack;

        [Header("Cambios y huida")]
        [SerializeField, Tooltip("No se le puede obligar a cambiar (Ventosas).")] private bool forcedSwitchImmune;
        [SerializeField, Tooltip("El rival no puede cambiarse ni huir (Sombratrampa).")] private bool trapsOpponent;
        [SerializeField, Tooltip("Solo atrapa a estos tipos (Imán: steel).")] private ElementTypeData[] trapOnlyTypes = new ElementTypeData[0];
        [SerializeField, Tooltip("Solo atrapa a los que pisan el suelo (Trampa Arena).")] private bool trapOnlyGrounded;
        [SerializeField, Tooltip("Siempre escapa de los salvajes (Fuga).")] private bool alwaysEscapes;

        [Header("Estados")]
        [SerializeField, Tooltip("El sueño se le pasa el doble de rápido (Madrugar).")] private bool sleepAgesTwice;
        [SerializeField, Tooltip("Devuelve quemadura, veneno y parálisis a quien se la pone (Sincronía).")] private bool synchronizeStatus;
        [StatusIdReference, SerializeField, Tooltip("Estados alternativos por contacto, uno al azar (Efecto Espora: poison|paralysis|sleep).")] private string[] contactReactionStatuses = new string[0];
        [SerializeField, Range(0f, 100f), Tooltip("% de PS máx. que pierde quien le golpea con contacto (Piel Tosca: 6,25).")] private float contactDamagePercent;
        [StatusIdReference, SerializeField, Tooltip("Al golpear con contacto puede poner este estado (Toque Tóxico: poison).")] private string offensiveContactStatus = "";
        [SerializeField, Range(0f, 100f)] private float offensiveContactChance;
        [SerializeField, Range(0f, 100f), Tooltip("% de PS que pierde quien lo debilita con contacto (Resquicio: 25).")] private float aftermathPercent;
        [SerializeField, Range(0f, 100f), Tooltip("% de anular el movimiento que le golpea (Cuerpo Maldito: 30).")] private float disableOnHitChance;
        [SerializeField, Tooltip("Cambios de etapa al recibir un golpe que cumpla las condiciones (Justiciero: attack +1 si mov.tipo=dark).")] private OnHitStatData[] onHitStats = new OnHitStatData[0];
        [SerializeField, Tooltip("Un crítico le sube el Ataque al máximo (Irascible).")] private bool critMaxesAttack;

        [Header("Inmunidades")]
        [SerializeField, Tooltip("Inmune a los KO de un golpe (Robustez).")] private bool ohkoImmune;
        [SerializeField, Tooltip("Con los PS al máximo aguanta con 1 PS (Robustez).")] private bool survivesFromFullHp;
        [SerializeField, Tooltip("Inmune a movimientos con estas etiquetas (Insonorizar: sonido).")] private string[] immuneToMoveTags = new string[0];
        [SerializeField, Tooltip("Nadie puede usar movimientos con estas etiquetas (Humedad: explosión).")] private string[] blocksMoveTagsForAll = new string[0];
        [SerializeField, Tooltip("Solo le afectan los golpes muy eficaces (Superguarda).")] private bool onlySuperEffectiveHits;
        [SerializeField, Tooltip("Al anular un golpe por inmunidad, sube esta estadística (Electromotor: speed).")] private string onImmuneStat = "";
        [SerializeField, Range(-6, 6)] private int onImmuneStages;
        [SerializeField, Tooltip("Tras absorber un golpe, sus movimientos de ese tipo ×1,5 (Absorbe Fuego).")] private bool boostsAbsorbedType;

        [Header("Al entrar")]
        [SerializeField, Tooltip("Copia la habilidad del rival (Rastro).")] private bool traceOnEntry;
        [SerializeField, Tooltip("Se transforma en el rival (Impostor).")] private bool transformOnEntry;
        [SerializeField, Tooltip("Sube Ataque o Atq. Esp. según la defensa más baja del rival (Descarga).")] private bool downloadOnEntry;
        [SerializeField, Tooltip("Avisa al entrar: objeto (Cacheo), peligro (Anticipación), movimiento (Alerta).")] private string announceOnEntry = "";
        [SerializeField, Tooltip("Las demás habilidades no funcionan mientras está (Gas Reactivo).")] private bool neutralizingGas;
        [SerializeField, Tooltip("El rival no puede comer bayas (Nerviosismo).")] private bool unnerve;

        [Header("Otros")]
        [SerializeField, Tooltip("Su tipo pasa a ser el del ataque que le golpea (Cambio Color).")] private bool colorChange;
        [SerializeField, Tooltip("Su tipo pasa a ser el del movimiento que usa (Mutatipo).")] private bool protean;
        [SerializeField, Tooltip("El rival gasta 1 PP más (Presión).")] private bool pressure;
        [SerializeField, Tooltip("Actúa un turno sí y otro no (Ausente).")] private bool truant;
        [SerializeField, Tooltip("Ignora las etapas del rival (Ignorante).")] private bool ignoresStages;
        [SerializeField, Min(1), Tooltip("Multiplica sus cambios de etapa (Simple: 2).")] private int stageMultiplier = 1;
        [SerializeField, Tooltip("Sus cambios de etapa se invierten (Respondón).")] private bool invertsStages;
        [SerializeField, Tooltip("Quien le drena PS los pierde (Lodo Líquido).")] private bool liquidOoze;
        [SerializeField, Tooltip("Todos sus movimientos son de tipo Normal (Normalidad).")] private bool normalizeMoves;
        [SerializeField, Tooltip("Sus movimientos de estos tipos alcanzan a los inmunes (Intrépido: normal, fighting).")] private ElementTypeData[] ignoresImmunityFor = new ElementTypeData[0];
        [SerializeField, Tooltip("Ignora las habilidades del rival al atacar (Rompemoldes).")] private bool moldBreaker;
        [SerializeField, Tooltip("Sus golpes múltiples siempre dan el máximo (Encadenado).")] private bool skillLink;
        [SerializeField, Range(0f, 100f), Tooltip("% de PS que pierde el rival DORMIDO cada turno (Mal Sueño: 12,5).")] private float badDreamsPercent;
        [SerializeField, Tooltip("Sus movimientos y los que le lanzan nunca fallan (Indefenso).")] private bool noGuard;
        [SerializeField, Tooltip("Actúa el último (Rezagado).")] private bool movesLast;
        [SerializeField, Tooltip("Su objeto equipado no hace nada (Zoquete).")] private bool klutz;
        [SerializeField, Range(0, 100), Tooltip("Come las bayas con este % de PS (Gula: 50). 0 = el de la baya.")] private int berryThresholdPercent;
        [SerializeField, Tooltip("No le pueden quitar el objeto (Viscosidad).")] private bool stickyHold;
        [SerializeField, Tooltip("Roba el objeto de quien le golpea con contacto (Hurto).")] private bool pickpocket;
        [SerializeField, Tooltip("Al debilitar a un rival sube esta estadística (Autoestima: attack).")] private string onKoStat = "";
        [SerializeField, Range(-6, 6)] private int onKoStages;
        [SerializeField, Tooltip("Si el rival le baja una estadística, sube esta (Competitivo: attack; Tenacidad: sp_attack).")] private string onStatDroppedStat = "";
        [SerializeField, Range(-6, 6)] private int onStatDroppedStages;
        [SerializeField, Range(0f, 100f), Tooltip("% de recuperar su baya al final del turno (Cosecha: 50; con sol, siempre).")] private float harvestChance;
        [SerializeField, Tooltip("Cada turno sube mucho una estadística al azar y baja otra (Veleta).")] private bool moody;
        [SerializeField, Tooltip("Atraviesa pantallas y sustitutos (Allanamiento).")] private bool infiltrator;
        [SerializeField, Tooltip("Devuelve los movimientos de estado (Espejo Mágico).")] private bool magicBounce;
        [SerializeField, Range(0.1f, 4f), Tooltip("Multiplica su peso (Metal Pesado: 2).")] private float weightMultiplier = 1f;

        [Header("5.ª y 6.ª gen.")]
        [SerializeField, Tooltip("Tipo cuyos movimientos ganan prioridad (Alas Vendaval: flying).")] private string priorityType = "";
        [SerializeField, Range(0, 3)] private int priorityTypeBonus;
        [SerializeField, Tooltip("Quien le golpea con contacto pierde etapas de esta estadística (Baba: speed).")] private string contactStatDrop = "";
        [SerializeField, Range(-6, 0)] private int contactStatDropStages;
        [SerializeField, Tooltip("Al golpear roba el objeto del rival si no lleva nada (Prestidigitador).")] private bool stealOnHit;
        [SerializeField, Tooltip("Quien le golpea con contacto pasa a tener esta habilidad (Momia).")] private bool spreadsAbilityOnContact;
        [SerializeField, Tooltip("Sus movimientos Normales pasan a ser de este tipo (Piel Feérica: fairy; Piel Helada: ice).")] private string convertNormalTo = "";
        [SerializeField, Range(1f, 2f), Tooltip("Y hacen más daño (1,3).")] private float convertBoost = 1f;
        [SerializeField, Range(0f, 100f), Tooltip("Al comer una baya recupera además este % de PS (Carrillo: 33).")] private float berryBonusHealPercent;
        public string PriorityType => priorityType; public int PriorityTypeBonus => priorityTypeBonus;
        public string ContactStatDrop => contactStatDrop; public int ContactStatDropStages => contactStatDropStages;
        public bool StealOnHit => stealOnHit; public bool SpreadsAbilityOnContact => spreadsAbilityOnContact;
        public string ConvertNormalTo => convertNormalTo; public float ConvertBoost => convertBoost; public float BerryBonusHealPercent => berryBonusHealPercent;

        [System.Serializable] public sealed class WeatherHpData { public string weatherId = "rain"; public float percent = 6.25f; }
        [System.Serializable] public sealed class WeatherTypeData { public string weatherId = "rain"; public ElementTypeData type; }
        [System.Serializable] public sealed class ConditionalStatData { [StatIdReference] public string statId = "speed"; public float multiplier = 2f; public ConditionData[] conditions = new ConditionData[0]; }
        [System.Serializable] public sealed class OnHitStatData { [StatIdReference] public string statId = "attack"; [Range(-6, 6)] public int stages = 1; public ConditionData[] conditions = new ConditionData[0]; }

        public string OnEntryWeather => onEntryWeather; public int OnEntryWeatherTurns => onEntryWeatherTurns; public bool SuppressesWeather => suppressesWeather;
        public WeatherHpData[] WeatherHpChanges => weatherHpChanges; public string[] WeatherImmunities => weatherImmunities;
        public string CuresStatusInWeather => curesStatusInWeather; public string StatusImmuneInWeather => statusImmuneInWeather; public WeatherTypeData[] TypeByWeather => typeByWeather;
        public ConditionalStatData[] ConditionalStats => conditionalStats; public PowerModifierData[] AccuracyModifiers => accuracyModifiers; public PowerModifierData[] EvasionModifiers => evasionModifiers;
        public bool NoRecoil => noRecoil; public bool NoIndirectDamage => noIndirectDamage; public bool CritImmune => critImmune; public int CritStageBonus => critStageBonus;
        public float CritDamageMultiplier => critDamageMultiplier; public string[] PreventedStatDrops => preventedStatDrops; public bool FlinchImmune => flinchImmune;
        public string OnFlinchStat => onFlinchStat; public int OnFlinchStages => onFlinchStages; public float SecondaryChanceMultiplier => secondaryChanceMultiplier;
        public bool BlocksIncomingSecondaries => blocksIncomingSecondaries; public bool RemovesOwnSecondaries => removesOwnSecondaries; public float FlinchChanceOnAttack => flinchChanceOnAttack;
        public bool ForcedSwitchImmune => forcedSwitchImmune; public bool TrapsOpponent => trapsOpponent; public ElementTypeData[] TrapOnlyTypes => trapOnlyTypes;
        public bool TrapOnlyGrounded => trapOnlyGrounded; public bool AlwaysEscapes => alwaysEscapes; public bool SleepAgesTwice => sleepAgesTwice; public bool SynchronizeStatus => synchronizeStatus;
        public string[] ContactReactionStatuses => contactReactionStatuses; public float ContactDamagePercent => contactDamagePercent; public string OffensiveContactStatus => offensiveContactStatus;
        public float OffensiveContactChance => offensiveContactChance; public float AftermathPercent => aftermathPercent; public float DisableOnHitChance => disableOnHitChance;
        public OnHitStatData[] OnHitStats => onHitStats; public bool CritMaxesAttack => critMaxesAttack; public bool OhkoImmune => ohkoImmune; public bool SurvivesFromFullHp => survivesFromFullHp;
        public string[] ImmuneToMoveTags => immuneToMoveTags; public string[] BlocksMoveTagsForAll => blocksMoveTagsForAll; public bool OnlySuperEffectiveHits => onlySuperEffectiveHits;
        public string OnImmuneStat => onImmuneStat; public int OnImmuneStages => onImmuneStages; public bool BoostsAbsorbedType => boostsAbsorbedType;
        public bool TraceOnEntry => traceOnEntry; public bool TransformOnEntry => transformOnEntry; public bool DownloadOnEntry => downloadOnEntry; public string AnnounceOnEntry => announceOnEntry;
        public bool NeutralizingGas => neutralizingGas; public bool Unnerve => unnerve; public bool ColorChange => colorChange; public bool Protean => protean; public bool Pressure => pressure;
        public bool Truant => truant; public bool IgnoresStages => ignoresStages; public int StageMultiplier => stageMultiplier; public bool InvertsStages => invertsStages;
        public bool LiquidOoze => liquidOoze; public bool NormalizeMoves => normalizeMoves; public ElementTypeData[] IgnoresImmunityFor => ignoresImmunityFor; public bool MoldBreaker => moldBreaker;
        public bool SkillLink => skillLink; public float BadDreamsPercent => badDreamsPercent; public bool NoGuard => noGuard; public bool MovesLast => movesLast; public bool Klutz => klutz;
        public int BerryThresholdPercent => berryThresholdPercent; public bool StickyHold => stickyHold; public bool Pickpocket => pickpocket; public string OnKoStat => onKoStat; public int OnKoStages => onKoStages;
        public string OnStatDroppedStat => onStatDroppedStat; public int OnStatDroppedStages => onStatDroppedStages; public float HarvestChance => harvestChance; public bool Moody => moody;
        public bool Infiltrator => infiltrator; public bool MagicBounce => magicBounce; public float WeightMultiplier => weightMultiplier;

        public string Id => id;
        public string DisplayName => displayName;
        public PassiveStatModifierData[] PassiveModifiers => passiveModifiers;
        public string[] StatusImmunities => statusImmunities;
        public ElementTypeData[] TypeImmunities => typeImmunities;
        public float AbsorbImmuneHealPercent => absorbImmuneHealPercent;
        public string ContactReactionStatus => contactReactionStatus;
        public float ContactReactionChance => contactReactionChance;
        public string OnEntryStatId => onEntryStatId;
        public int OnEntryStages => onEntryStages;
        public bool OnEntryTargetsSelf => onEntryTargetsSelf;
        public string StatusStatBoostStatId => statusStatBoostStatId;
        public float StatusStatBoostMultiplier => statusStatBoostMultiplier;
        public ElementTypeData LowHpBoostType => lowHpBoostType;
        public float LowHpThresholdPercent => lowHpThresholdPercent;
        public float LowHpBoostMultiplier => lowHpBoostMultiplier;
        public IncomingTypeMultiplierData[] IncomingTypeMultipliers => incomingTypeMultipliers;
        public float StabMultiplierOverride => stabMultiplierOverride;
        public int StatusMovePriorityBonus => statusMovePriorityBonus;
        public bool PreventsStatReduction => preventsStatReduction;
        public bool CuresStatusOnSwitchOut => curesStatusOnSwitchOut;
        public float HealPercentOnSwitchOut => healPercentOnSwitchOut;
        public string EndOfTurnStatId => endOfTurnStatId;
        public int EndOfTurnStages => endOfTurnStages;
        public float EndOfTurnHealPercent => endOfTurnHealPercent;
        public bool EndOfTurnHealRequiresStatus => endOfTurnHealRequiresStatus;
        public float EndOfTurnCureStatusChance => endOfTurnCureStatusChance;
        public bool NegatesStatusDamage => negatesStatusDamage;
        public PowerModifierData[] OffensivePowerModifiers => offensivePowerModifiers;
        public PowerModifierData[] DefensivePowerModifiers => defensivePowerModifiers;

        [System.Serializable]
        public sealed class PassiveStatModifierData
        {
            [StatIdReference] public string statId;        // "attack", "speed"... (id de stat)
            public float multiplier = 1f;
        }

        [System.Serializable]
        public sealed class IncomingTypeMultiplierData
        {
            public ElementTypeData type;
            public float multiplier = 1f;
        }
    }
}
