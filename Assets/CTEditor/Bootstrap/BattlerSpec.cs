using System.Collections.Generic;
using UnityEngine;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Infrastructure.Acl;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.Bootstrap
{
    /// <summary>
    /// Ficha para AUTORAR un combatiente concreto: especie + nivel + un moveset EXPLÍCITO.
    ///
    /// Clave de tu observación: los movimientos NO se deducen del learnset ("los últimos 4 aprendidos").
    /// Un entrenador o un salvaje puede llevar los ataques que el autor quiera. Si dejas la lista vacía,
    /// se cae al learnset por defecto (cómodo para pruebas). Esta ficha es la unidad con la que se
    /// arman tanto la party del jugador como los rivales (entrenadores/encuentros).
    /// </summary>
    [System.Serializable]
    public sealed class BattlerSpec
    {
        [SerializeField] private SpeciesData species;
        [SerializeField] private int level = 5;

        [Tooltip("Movimientos EXPLÍCITOS de este combatiente. Vacío = usa el learnset por defecto.")]
        [SerializeField] private MoveData[] moves;

        [Header("Genética (opcional)")]
        [Tooltip("Naturaleza fija. Vacío = una al azar de GameContent/Natures (o neutra si no hay ninguna).")]
        [SerializeField] private NatureData nature;

        [Tooltip("IVs fijos para TODAS las estadísticas (p. ej. 31 = perfectos). -1 = al azar, como al nacer.")]
        [SerializeField] private int fixedIvs = -1;

        [Header("Objeto (opcional)")]
        [Tooltip("Objeto que lleva EQUIPADO (Restos, Carbón, una baya...). Vacío = nada.")]
        [ContentIdReference(typeof(ItemData)), SerializeField] private string heldItem = "";

        /// <summary>Id del objeto equipado (vacío = ninguno).</summary>
        public string HeldItem => heldItem;

        public SpeciesData Species => species;
        public int Level => level;

        /// <summary>IV fijo elegido por el autor, o null para tirarlos al azar.</summary>
        public int? FixedIvs => fixedIvs >= 0 ? fixedIvs : (int?)null;

        /// <summary>La naturaleza fijada por el autor ya traducida al dominio, o null (= elegir al azar).</summary>
        public Nature FixedNature() => nature != null ? NatureMapper.ToDomain(nature) : null;

        /// <summary>Ids de los movimientos elegidos, o null si no se especificaron (usar learnset).</summary>
        public IReadOnlyList<Id<Move>> MoveIds()
        {
            if (moves == null || moves.Length == 0) return null;
            var list = new List<Id<Move>>();
            foreach (var m in moves)
                if (m != null) list.Add(new Id<Move>(m.Id));
            return list.Count == 0 ? null : list;
        }
    }
}
