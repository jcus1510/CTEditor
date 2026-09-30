using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Types;

namespace CTEditor.GameDefinition.Domain.Weather
{
    /// <summary>
    /// Un CLIMA (lluvia, sol, tormenta de arena...) definido por el autor. Mientras está activo:
    ///   - multiplica la potencia de ciertos tipos (lluvia: Agua ×1,5, Fuego ×0,5);
    ///   - puede dañar cada turno a todos salvo a los tipos inmunes (arena: Roca, Tierra y Acero no);
    ///   - dura unos turnos (0 = hasta que otro clima lo reemplace).
    /// Las condiciones de los movimientos y habilidades pueden preguntar "si hace este clima".
    /// </summary>
    public sealed class WeatherDefinition
    {
        public string Id { get; }
        public string DisplayName { get; }
        public int DefaultTurns { get; }
        public IReadOnlyDictionary<Id<ElementType>, float> TypePowerMultipliers { get; }
        public Percentage ResidualDamagePercent { get; }
        public IReadOnlyList<Id<ElementType>> ImmuneTypes { get; }

        public WeatherDefinition(string id, string displayName, int defaultTurns = 5,
            IReadOnlyDictionary<Id<ElementType>, float> typePowerMultipliers = null,
            Percentage residualDamagePercent = default, IReadOnlyList<Id<ElementType>> immuneTypes = null)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("El clima necesita un id.", nameof(id));
            Id = id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName;
            DefaultTurns = defaultTurns < 0 ? 0 : defaultTurns;
            TypePowerMultipliers = typePowerMultipliers == null
                ? new Dictionary<Id<ElementType>, float>()
                : new Dictionary<Id<ElementType>, float>(typePowerMultipliers);
            ResidualDamagePercent = residualDamagePercent;
            ImmuneTypes = immuneTypes == null ? Array.Empty<Id<ElementType>>() : new List<Id<ElementType>>(immuneTypes);
        }

        public float MultiplierFor(Id<ElementType> type) => TypePowerMultipliers.TryGetValue(type, out var m) ? m : 1f;
    }
}
