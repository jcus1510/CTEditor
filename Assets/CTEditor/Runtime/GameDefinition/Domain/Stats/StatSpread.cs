using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CTEditor.GameDefinition.Domain.Stats
{
    /// <summary>
    /// Un REPARTO de puntos por estadística (EVs o IVs de un miembro de equipo): «252 Atq / 4 PS / 252 Vel». Solo guarda
    /// las estadísticas escritas; las demás quedan como decida quien lo use (EVs a 0, IVs al azar o fijos).
    ///
    /// Se escribe en español (PS, Atq, Def, AtqE, DefE, Vel) y también acepta lo de Showdown (HP, Atk, Def, SpA, SpD,
    /// Spe) y las estadísticas inventadas por su id («30 suerte»). Inmutable.
    /// </summary>
    public sealed class StatSpread
    {
        private readonly Dictionary<StatId, int> _values;

        public IReadOnlyDictionary<StatId, int> Values => _values;
        public bool IsEmpty => _values.Count == 0;
        public int Total => _values.Values.Sum();

        public static readonly StatSpread Empty = new StatSpread(null);

        public StatSpread(IEnumerable<KeyValuePair<StatId, int>> values)
        {
            _values = new Dictionary<StatId, int>();
            if (values != null)
                foreach (var kv in values) _values[kv.Key] = Math.Max(0, kv.Value);
        }

        /// <summary>El valor de una estadística (null = no escrito).</summary>
        public int? Of(StatId stat) => _values.TryGetValue(stat, out var v) ? v : (int?)null;

        // ---------------- Nombres ----------------

        private static readonly (StatId stat, string es, string sd)[] Classic =
        {
            (StatId.Hp, "PS", "HP"), (StatId.Attack, "Atq", "Atk"), (StatId.Defense, "Def", "Def"),
            (StatId.SpAttack, "AtqE", "SpA"), (StatId.SpDefense, "DefE", "SpD"), (StatId.Speed, "Vel", "Spe"),
        };

        private static readonly Dictionary<string, StatId> Aliases = new Dictionary<string, StatId>(StringComparer.OrdinalIgnoreCase)
        {
            ["ps"] = StatId.Hp, ["hp"] = StatId.Hp, ["vida"] = StatId.Hp,
            ["atq"] = StatId.Attack, ["ataque"] = StatId.Attack, ["atk"] = StatId.Attack, ["attack"] = StatId.Attack,
            ["def"] = StatId.Defense, ["defensa"] = StatId.Defense, ["defense"] = StatId.Defense,
            ["atqe"] = StatId.SpAttack, ["atqesp"] = StatId.SpAttack, ["ataqueespecial"] = StatId.SpAttack, ["spa"] = StatId.SpAttack,
            ["spatk"] = StatId.SpAttack, ["spattack"] = StatId.SpAttack,
            ["defe"] = StatId.SpDefense, ["defesp"] = StatId.SpDefense, ["defensaespecial"] = StatId.SpDefense, ["spd"] = StatId.SpDefense,
            ["spdef"] = StatId.SpDefense, ["spdefense"] = StatId.SpDefense,
            ["vel"] = StatId.Speed, ["velocidad"] = StatId.Speed, ["spe"] = StatId.Speed, ["speed"] = StatId.Speed,
        };

        /// <summary>La estadística de un nombre (español, Showdown o id). Null si está vacío.</summary>
        public static StatId? StatOf(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            string key = new string(name.Where(c => char.IsLetterOrDigit(c)).ToArray());
            if (Aliases.TryGetValue(key, out var s)) return s;
            return new StatId(name.Trim().ToLowerInvariant());   // estadística inventada: su id
        }

        // ---------------- Texto ----------------

        /// <summary>
        /// Lee «252 Atq / 4 PS / 252 Vel» (separado por / o ,; también «Atq 252»). Vacío = reparto vacío. Devuelve false
        /// con el motivo si algo no se entiende.
        /// </summary>
        public static bool TryParse(string text, out StatSpread spread, out string error)
        {
            spread = Empty; error = null;
            if (string.IsNullOrWhiteSpace(text)) return true;
            var values = new Dictionary<StatId, int>();
            foreach (var raw in text.Split('/', ','))
            {
                var part = raw.Trim();
                if (part.Length == 0) continue;
                var tokens = part.Split(new[] { ' ', ':', '=' }, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length != 2) { error = $"«{part}» no es «número estadística» (ej. 252 Atq)."; return false; }
                bool firstIsNumber = int.TryParse(tokens[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n);
                if (!firstIsNumber && !int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                { error = $"«{part}» no tiene un número."; return false; }
                if (n < 0) { error = $"«{part}»: no puede ser negativo."; return false; }
                var stat = StatOf(firstIsNumber ? tokens[1] : tokens[0]);
                if (!stat.HasValue) { error = $"«{part}» no dice la estadística."; return false; }
                if (values.ContainsKey(stat.Value)) { error = $"La estadística de «{part}» está repetida."; return false; }
                values[stat.Value] = n;
            }
            spread = new StatSpread(values);
            return true;
        }

        public static StatSpread Parse(string text)
            => TryParse(text, out var s, out var e) ? s : throw new FormatException(e);

        /// <summary>En español: «252 Atq/4 PS/252 Vel» (en el orden clásico; las inventadas al final).</summary>
        public string Format(string separator = "/") => Join(separator, false);

        /// <summary>Como Showdown: «252 Atk / 4 HP / 252 Spe».</summary>
        public string FormatShowdown() => Join(" / ", true);

        private string Join(string separator, bool showdown)
        {
            var parts = new List<string>();
            foreach (var (stat, es, sd) in Classic)
                if (_values.TryGetValue(stat, out var v)) parts.Add($"{v} {(showdown ? sd : es)}");
            foreach (var kv in _values.Where(kv => !Classic.Any(c => c.stat == kv.Key)).OrderBy(kv => kv.Key.Value))
                parts.Add($"{kv.Value} {kv.Key.Value}");
            return string.Join(separator, parts);
        }

        public override string ToString() => Format(" / ");
    }
}
