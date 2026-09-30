using UnityEngine;

namespace CTEditor.GameDefinition.Infrastructure.ScriptableObjects
{
    /// <summary>
    /// La ficha que rellena el AUTOR para un estado alterado. "Quemado", "Veneno", o cualquier estado
    /// que invente. El combate no codifica ninguno: ejecuta lo que diga esta ficha (E.4). Su mapper la
    /// traduce a StatusConditionDefinition (dominio puro).
    /// </summary>
    [CreateAssetMenu(menuName = "CTEditor/Estado alterado", fileName = "NuevoEstado")]
    public sealed class StatusConditionData : ScriptableObject, IContentAsset
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;

        [Header("Tipo de estado")]
        [Tooltip("PRINCIPAL (desmarcado): quemado, parálisis, sueño... solo uno a la vez. VOLÁTIL (marcado): confusión, atrapado, drenadoras... se suma al principal y a otros volátiles, y se va al retirarse.")]
        [SerializeField] private bool isVolatile = false;

        [Tooltip("Daño por turno como % de los PS máximos. 0 = no hace daño (p.ej. parálisis).")]
        [SerializeField, Range(0f, 100f)] private float residualDamagePercent = 0f;

        [Tooltip("Si está marcado, el daño residual escala cada turno (envenenamiento grave / tóxico).")]
        [SerializeField] private bool progressiveResidual = false;

        [Tooltip("Si está marcado, el residual CURA en vez de dañar cada turno (regeneración).")]
        [SerializeField] private bool residualHeals = false;

        [Tooltip("Probabilidad por turno de no poder actuar. 0 = nunca impide (p.ej. quemado).")]
        [SerializeField, Range(0f, 100f)] private float actionPreventionChance = 0f;

        [Tooltip("Si está marcado, el estado se quita al cambiar de monstruo (estados volátiles).")]
        [SerializeField] private bool clearedOnSwitch = false;

        [Tooltip("Duración en turnos. 0 = permanente (hasta curar). >0 = se quita solo tras esos turnos (p.ej. sueño).")]
        [SerializeField, Min(0)] private int durationTurns = 0;

        [Tooltip("Probabilidad por turno de recuperarse solo (dormido/confuso despiertan al azar). 0 = solo por duración.")]
        [SerializeField, Range(0f, 100f)] private float recoveryChancePerTurn = 0f;

        [Tooltip("Si impide la acción, daño que el portador se hace a sí mismo (% PS máx). Confusión > 0; dormido = 0.")]
        [SerializeField, Range(0f, 100f)] private float selfDamageOnPreventedPercent = 0f;

        [Tooltip("Al terminar, se convierte en este estado en vez de curarse (somnoliento -> dormido). Vacío = se cura.")]
        [StatusIdReference, SerializeField] private string transformsToStatus;

        [Tooltip("Modificadores pasivos de estadísticas mientras dura (p.ej. Ataque ×0,5 para quemado).")]
        [SerializeField] private PassiveStatModifierData[] passiveModifiers;

        [Tooltip("Tipos que NO pueden sufrir este estado (Fuego no se quema, Eléctrico no se paraliza...). Vacío = cualquiera.")]
        [SerializeField] private ElementTypeData[] immuneTypes;

        [Header("Comportamientos especiales")]
        [Tooltip("Si es mayor que 'Duration Turns', la duración se sortea entre ambos (Atadura: 4 a 5 turnos).")]
        [SerializeField, Min(0)] private int durationMaxTurns = 0;
        [Tooltip("El portador no puede cambiarse ni huir (Atadura, Mal de Ojo).")]
        [SerializeField] private bool preventsSwitch = false;
        [Tooltip("Los movimientos del rival contra el portador fallan (Protección). Úsalo con duración 1.")]
        [SerializeField] private bool blocksIncomingMoves = false;
        [Tooltip("Un golpe que lo debilitaría lo deja con 1 PS (Aguante). Úsalo con duración 1.")]
        [SerializeField] private bool survivesLethalHit = false;
        [Tooltip("El daño por turno CURA al rival que está en el campo (Drenadoras).")]
        [SerializeField] private bool residualHealsOpponent = false;
        [Tooltip("Si se usa turnos seguidos, cada vez es más difícil que salga (Protección: 1/3, 1/9...).")]
        [SerializeField] private bool harderWhenRepeated = false;

        [Header("Captura")]
        [Tooltip("Cuánto facilita capturar a un monstruo con este estado (1 = nada). Clásico: dormido y congelado 2,5; paralizado, envenenado y quemado 1,5.")]
        [SerializeField, Min(1f)] private float statusCatchMultiplier = 1f;

        [Header("Más comportamientos (3.ª y 4.ª gen.)")]
        [Tooltip("No puede usar movimientos de estado (Mofa).")]
        [SerializeField] private bool blocksStatusMoves;
        [Tooltip("No puede repetir el mismo movimiento dos turnos seguidos (Tormento).")]
        [SerializeField] private bool blocksRepeatedMove;
        [Tooltip("No puede curarse (Anticura).")]
        [SerializeField] private bool blocksHealing;
        [Tooltip("No puede usar objetos (Embargo).")]
        [SerializeField] private bool blocksItems;
        [Tooltip("Su habilidad deja de funcionar (Bilis).")]
        [SerializeField] private bool suppressesAbility;
        [Tooltip("El daño por turno solo ocurre si además tiene este estado (Pesadilla: sleep). Si no lo tiene, el volátil se va.")]
        [StatusIdReference, SerializeField] private string residualRequiresStatus;
        [Tooltip("Al acabarse, el portador se debilita (Canto Mortal).")]
        [SerializeField] private bool faintsWhenEnds;
        [Tooltip("Si un ataque lo debilita mientras dura, el atacante también cae (Mismo Destino).")]
        [SerializeField] private bool destinyBond;
        [Tooltip("Si un ataque lo debilita, ese movimiento se queda sin PP (Rabia).")]
        [SerializeField] private bool grudge;
        [Tooltip("Devuelve los movimientos de estado que le lanzan (Capa Mágica).")]
        [SerializeField] private bool reflectsStatusMoves;
        [Tooltip("Se queda las mejoras propias que use el rival (Robo).")]
        [SerializeField] private bool stealsBoostMoves;
        [Tooltip("Su próximo movimiento de este tipo hace más daño (Carga: Eléctrico).")]
        [SerializeField] private ElementTypeData boostedType;
        [SerializeField, Min(0f)] private float boostMultiplier = 1f;
        [Tooltip("El refuerzo se gasta al usar ese tipo.")]
        [SerializeField] private bool boostConsumed;
        [Tooltip("Tipos a los que es inmune mientras dura (Levitón: Tierra).")]
        [SerializeField] private ElementTypeData[] extraTypeImmunities = new ElementTypeData[0];
        [Tooltip("Pierde este tipo mientras dura (Respiro: Volador).")]
        [SerializeField] private ElementTypeData suppressedType;
        [Tooltip("Identificado: se ignora su evasión y le alcanzan los tipos de abajo aunque fuera inmune (Profecía, Gran Ojo).")]
        [SerializeField] private bool identified;
        [SerializeField] private ElementTypeData[] hittableByTypes = new ElementTypeData[0];
        [Tooltip("Su próximo movimiento no falla (Fijar Blanco). Se pone en quien lo usa.")]
        [SerializeField] private bool sureHit;
        [Tooltip("El rival no puede usar los movimientos que el portador conoce (Cerca).")]
        [SerializeField] private bool imprisons;
        [Tooltip("En el suelo: le afecta Tierra aunque vuele o levite (Arraigo).")]
        [SerializeField] private bool grounded;
        [Tooltip("Solo afecta a un rival del género OPUESTO al de quien lo causa (Atracción, Gran Encanto). Nunca a los sin género.")]
        [SerializeField] private bool requiresOppositeGender;
        [Header("Protecciones de la 6.ª gen.")]
        [Tooltip("La protección solo para los movimientos que hacen daño (Escudo Real, Escudo Tatami).")]
        [SerializeField] private bool protectOnlyDamaging;
        [Tooltip("Si le golpean con contacto mientras se protege, el atacante pierde etapas de esta estadística (Escudo Real: attack).")]
        [StatIdReference, SerializeField] private string protectContactStat = "";
        [SerializeField, Range(-6, 0)] private int protectContactStages;
        [Tooltip("Si le golpean con contacto mientras se protege, el atacante pierde este % de PS (Barrera Espinosa: 12,5).")]
        [SerializeField, Range(0f, 100f)] private float protectContactDamagePercent;

        public bool BlocksStatusMoves => blocksStatusMoves;
        public bool BlocksRepeatedMove => blocksRepeatedMove;
        public bool BlocksHealing => blocksHealing;
        public bool BlocksItems => blocksItems;
        public bool SuppressesAbility => suppressesAbility;
        public string ResidualRequiresStatus => residualRequiresStatus;
        public bool FaintsWhenEnds => faintsWhenEnds;
        public bool DestinyBond => destinyBond;
        public bool Grudge => grudge;
        public bool ReflectsStatusMoves => reflectsStatusMoves;
        public bool StealsBoostMoves => stealsBoostMoves;
        public ElementTypeData BoostedType => boostedType;
        public float BoostMultiplier => boostMultiplier;
        public bool BoostConsumed => boostConsumed;
        public ElementTypeData[] ExtraTypeImmunities => extraTypeImmunities;
        public ElementTypeData SuppressedType => suppressedType;
        public bool Identified => identified;
        public ElementTypeData[] HittableByTypes => hittableByTypes;
        public bool SureHit => sureHit;
        public bool Imprisons => imprisons;
        public bool Grounded => grounded;
        public bool RequiresOppositeGender => requiresOppositeGender;
        public bool ProtectOnlyDamaging => protectOnlyDamaging;
        public string ProtectContactStat => protectContactStat;
        public int ProtectContactStages => protectContactStages;
        public float ProtectContactDamagePercent => protectContactDamagePercent;

        public string Id => id;
        public string DisplayName => displayName;
        public float ResidualDamagePercent => residualDamagePercent;
        public bool ProgressiveResidual => progressiveResidual;
        public bool ResidualHeals => residualHeals;
        public float ActionPreventionChance => actionPreventionChance;
        public bool ClearedOnSwitch => clearedOnSwitch;
        public int DurationTurns => durationTurns;
        public float RecoveryChancePerTurn => recoveryChancePerTurn;
        public float SelfDamageOnPreventedPercent => selfDamageOnPreventedPercent;
        public string TransformsToStatus => transformsToStatus;
        public PassiveStatModifierData[] PassiveModifiers => passiveModifiers;
        public ElementTypeData[] ImmuneTypes => immuneTypes;
        public bool IsVolatile => isVolatile;
        public int DurationMaxTurns => durationMaxTurns;
        public bool PreventsSwitch => preventsSwitch;
        public bool BlocksIncomingMoves => blocksIncomingMoves;
        public bool SurvivesLethalHit => survivesLethalHit;
        public bool ResidualHealsOpponent => residualHealsOpponent;
        public bool HarderWhenRepeated => harderWhenRepeated;
        public float CatchMultiplier => statusCatchMultiplier;

        /// <summary>Sub-ficha de un modificador pasivo: qué stat y por cuánto la multiplica (0.5 = la mitad).</summary>
        [System.Serializable]
        public sealed class PassiveStatModifierData
        {
            [StatIdReference] public string statId;        // "attack", "speed"... (id de stat)
            public float multiplier = 1f;
        }
    }
}
