using UnityEngine;
using CTEditor.GameDefinition.Domain.Moves;

namespace CTEditor.GameDefinition.Infrastructure.ScriptableObjects
{
    /// <summary>
    /// La ficha que rellena el autor para un movimiento. Igual que ElementTypeData, vive en
    /// Infrastructure y la traduce su mapper. Aquí aparecen dos cosas nuevas que vale la pena ver.
    /// </summary>
    [CreateAssetMenu(menuName = "CTEditor/Move", fileName = "NewMove")]
    public sealed class MoveData : ScriptableObject
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

        [Header("Presentacion (lo lee solo la UI; el dominio lo ignora)")]
        [Tooltip("Segundos que la UI espera para la animacion de este movimiento (0 = sin pausa de animacion).")]
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
        public float AnimationSeconds => animationSeconds;

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
            public string statStatId;                       // si kind == ChangeStatStage (id de la stat: ej. "attack")
            [Range(-6, 6)] public int statStages;           // si kind == ChangeStatStage (delta de etapas, p.ej. +2 o -1)
        }
    }
}
