using System.Collections.Generic;
using CTEditor.GameDefinition.Domain.Stats;

namespace CTEditor.Party.Domain
{
    /// <summary>
    /// Los PUNTOS DE ESFUERZO (EVs) de un individuo: el "entrenamiento" que acumula derrotando
    /// rivales. Es estado MUTABLE con DOS invariantes que este objeto custodia (un mini "portero",
    /// A.8): tope por stat y tope total, ambos del Ruleset (0 = juego sin EVs).
    ///
    /// Clásico (Gen III+): 252 por stat, 510 en total. Cuánto "vale" cada EV en la stat final NO se
    /// decide aquí sino en la fórmula (la clásica suma EV/4). Así el contenedor y la regla quedan
    /// separados: puedes cambiar la fórmula sin tocar cómo se guardan los EVs.
    /// </summary>
    public sealed class EffortValues
    {
        private readonly Dictionary<StatId, int> _values = new Dictionary<StatId, int>();

        public int PerStatCap { get; }
        public int TotalCap { get; }

        public EffortValues(int perStatCap, int totalCap)
        {
            PerStatCap = perStatCap < 0 ? 0 : perStatCap;
            TotalCap = totalCap < 0 ? 0 : totalCap;
        }

        /// <summary>EVs actuales de una stat (0 si nunca ganó).</summary>
        public int Of(StatId stat) => _values.TryGetValue(stat, out var v) ? v : 0;

        /// <summary>Suma de todos los EVs.</summary>
        public int Total
        {
            get
            {
                int total = 0;
                foreach (var v in _values.Values) total += v;
                return total;
            }
        }

        /// <summary>Stats que tienen algún EV (para mostrar/guardar).</summary>
        public IEnumerable<StatId> Stats => _values.Keys;

        /// <summary>
        /// Intenta añadir EVs a una stat respetando AMBOS topes. Devuelve cuántos entraron de verdad
        /// (0 si la stat o el total ya estaban llenos). Jamás deja el objeto en un estado inválido.
        /// </summary>
        public int Add(StatId stat, int amount)
        {
            if (amount <= 0) return 0;

            int current = Of(stat);
            int roomInStat = PerStatCap - current;
            int roomInTotal = TotalCap - Total;
            int room = roomInStat < roomInTotal ? roomInStat : roomInTotal;
            if (room <= 0) return 0;

            int applied = amount < room ? amount : room;
            _values[stat] = current + applied;
            return applied;
        }
    }
}
