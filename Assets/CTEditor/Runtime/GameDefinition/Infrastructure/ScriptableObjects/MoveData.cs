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
    }
}
