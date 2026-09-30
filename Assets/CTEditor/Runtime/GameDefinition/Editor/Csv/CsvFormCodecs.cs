using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CTEditor.GameDefinition.Domain.Species;
using FormEntry = CTEditor.GameDefinition.Infrastructure.ScriptableObjects.SpeciesData.FormEntry;
using FormChangeEntry = CTEditor.GameDefinition.Infrastructure.ScriptableObjects.SpeciesData.FormChangeEntry;

namespace CTEditor.GameDefinition.Editor.Csv
{
    /// <summary>
    /// Celdas de FORMAS del Excel de especies (independiente de Unity, para poder testearlo).
    ///
    /// formas (separadas por |):  id;nombre;tipo1/tipo2;atq/def/atq_esp/def_esp/vel;habilidad;vuelve
    ///   zen;Modo Daruma;fire/psychic;30/105/140/105/55;zen_mode;vuelve
    ///   (vacío o 0 = igual que la especie; «vuelve» = vuelve a la normal al retirarse)
    ///
    /// cambios_forma (separados por |):  desde>hasta:disparador[:valor][;con=habilidad][;despues]
    ///   >zen:ps_bajo:50;con=zen_mode          (de la normal a zen con menos del 50 % de PS)
    ///   zen>:ps_desde:50;con=zen_mode         (de zen a la normal con el 50 % o más)
    ///   >blade:ataque;con=stance_change       (al usar cualquier ataque, antes de golpear)
    ///   >pirouette:movimiento:relic_song;despues
    ///   >origin:objeto:griseous_orb     >sun:clima:sun     >mega_x:mega:charizardite_x
    ///   Disparadores: objeto, movimiento, ataque, ps_bajo, ps_desde, clima, mega. «*» en desde = cualquier forma.
    /// </summary>
    public static class CsvFormCodecs
    {
        private static readonly (FormTrigger trigger, string word)[] Words =
        {
            (FormTrigger.HeldItem, "objeto"), (FormTrigger.UseMove, "movimiento"), (FormTrigger.DamagingMove, "ataque"),
            (FormTrigger.HpBelow, "ps_bajo"), (FormTrigger.HpAtLeast, "ps_desde"), (FormTrigger.Weather, "clima"),
            (FormTrigger.MegaEvolution, "mega"),
        };

        public static string WordOf(FormTrigger t) => Words.First(w => w.trigger == t).word;

        // ---------------- formas ----------------

        public static string FormatForms(IEnumerable<FormEntry> forms)
            => string.Join("|", (forms ?? Enumerable.Empty<FormEntry>()).Where(f => f != null && !string.IsNullOrWhiteSpace(f.id)).Select(f =>
            {
                string types = string.Join("/", new[] { f.type1, f.type2 }.Where(t => !string.IsNullOrWhiteSpace(t)));
                bool anyStat = f.attack > 0 || f.defense > 0 || f.spAttack > 0 || f.spDefense > 0 || f.speed > 0;
                string stats = anyStat ? $"{f.attack}/{f.defense}/{f.spAttack}/{f.spDefense}/{f.speed}" : "";
                var parts = new List<string> { f.id, f.displayName ?? "", types, stats, f.ability ?? "", f.revertsOnSwitch ? "vuelve" : "" };
                while (parts.Count > 2 && parts[parts.Count - 1].Length == 0) parts.RemoveAt(parts.Count - 1);
                return string.Join(";", parts);
            }));

        public static List<FormEntry> ParseForms(string cell)
        {
            var list = new List<FormEntry>();
            foreach (var raw in Split(cell, '|'))
            {
                var p = raw.Split(';').Select(x => x.Trim()).ToList();
                while (p.Count < 6) p.Add("");
                if (p[0].Length == 0) throw new CsvCellException($"La forma '{raw}' no tiene id (id;nombre;tipos;estadísticas;habilidad;vuelve).");
                var f = new FormEntry { id = p[0], displayName = p[1], ability = p[4] };
                var types = Split(p[2], '/');
                if (types.Count > 2) throw new CsvCellException($"La forma '{p[0]}' tiene más de 2 tipos.");
                f.type1 = types.Count > 0 ? types[0] : "";
                f.type2 = types.Count > 1 ? types[1] : "";
                if (p[3].Length > 0)
                {
                    var s = p[3].Split('/').Select(x => x.Trim()).ToList();
                    if (s.Count != 5)
                        throw new CsvCellException($"La forma '{p[0]}': las estadísticas son 5 números atq/def/atq_esp/def_esp/vel (los PS no cambian).");
                    var n = s.Select(x => Int(x, p[0])).ToList();
                    f.attack = n[0]; f.defense = n[1]; f.spAttack = n[2]; f.spDefense = n[3]; f.speed = n[4];
                }
                string back = p[5].ToLowerInvariant();
                if (back.Length > 0 && back != "vuelve" && back != "si" && back != "sí" && back != "no")
                    throw new CsvCellException($"La forma '{p[0]}': el último campo es «vuelve» (o vacío), no '{p[5]}'.");
                f.revertsOnSwitch = back == "vuelve" || back == "si" || back == "sí";
                if (list.Any(x => string.Equals(x.id, f.id, StringComparison.OrdinalIgnoreCase)))
                    throw new CsvCellException($"La forma '{f.id}' está repetida.");
                list.Add(f);
            }
            return list;
        }

        // ---------------- cambios_forma ----------------

        public static string FormatChanges(IEnumerable<FormChangeEntry> changes)
            => string.Join("|", (changes ?? Enumerable.Empty<FormChangeEntry>()).Where(c => c != null).Select(c =>
            {
                string value;
                switch (c.trigger)
                {
                    case FormTrigger.HeldItem: case FormTrigger.MegaEvolution: value = c.item; break;
                    case FormTrigger.UseMove: value = c.move; break;
                    case FormTrigger.HpBelow: case FormTrigger.HpAtLeast: value = c.hpPercent.ToString(CultureInfo.InvariantCulture); break;
                    case FormTrigger.Weather: value = c.weather; break;
                    default: value = ""; break;
                }
                string s = $"{c.from}>{c.to}:{WordOf(c.trigger)}" + (string.IsNullOrEmpty(value) ? "" : ":" + value);
                if (!string.IsNullOrWhiteSpace(c.requiredAbility)) s += ";con=" + c.requiredAbility;
                if (c.afterMove) s += ";despues";
                return s;
            }));

        public static List<FormChangeEntry> ParseChanges(string cell)
        {
            var list = new List<FormChangeEntry>();
            foreach (var raw in Split(cell, '|'))
            {
                var parts = raw.Split(';').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                var main = parts[0];
                int gt = main.IndexOf('>');
                if (gt < 0) throw new CsvCellException($"El cambio '{raw}' debe empezar por desde>hasta (ej. >zen:ps_bajo:50).");
                var c = new FormChangeEntry { from = main.Substring(0, gt).Trim() };
                var rest = main.Substring(gt + 1).Split(':').Select(x => x.Trim()).ToList();
                if (rest.Count < 2) throw new CsvCellException($"El cambio '{raw}' no dice qué lo provoca (ej. >zen:ps_bajo:50).");
                c.to = rest[0];
                string word = rest[1].ToLowerInvariant(), value = rest.Count > 2 ? string.Join(":", rest.Skip(2)) : "";
                var w = Words.FirstOrDefault(x => x.word == word);
                if (w.word == null)
                    throw new CsvCellException($"El cambio '{raw}': «{rest[1]}» no es un disparador ({string.Join(", ", Words.Select(x => x.word))}).");
                c.trigger = w.trigger;
                switch (c.trigger)
                {
                    case FormTrigger.HeldItem:
                    case FormTrigger.MegaEvolution:
                        if (value.Length == 0) throw new CsvCellException($"El cambio '{raw}' necesita el objeto (ej. :objeto:griseous_orb).");
                        c.item = value; break;
                    case FormTrigger.UseMove:
                        if (value.Length == 0) throw new CsvCellException($"El cambio '{raw}' necesita el movimiento (ej. :movimiento:relic_song).");
                        c.move = value; break;
                    case FormTrigger.HpBelow:
                    case FormTrigger.HpAtLeast:
                        c.hpPercent = value.Length == 0 ? 50 : Int(value, raw);
                        if (c.hpPercent < 0 || c.hpPercent > 100) throw new CsvCellException($"El cambio '{raw}': el % de PS va de 0 a 100.");
                        break;
                    case FormTrigger.Weather:
                        c.weather = value; break;
                }
                foreach (var opt in parts.Skip(1))
                {
                    var o = opt.ToLowerInvariant();
                    if (o.StartsWith("con=")) c.requiredAbility = opt.Substring(4).Trim();
                    else if (o == "despues" || o == "después") c.afterMove = true;
                    else if (o == "antes") c.afterMove = false;
                    else throw new CsvCellException($"El cambio '{raw}': opción desconocida «{opt}» (con=habilidad, despues).");
                }
                list.Add(c);
            }
            return list;
        }

        /// <summary>Ids de forma que usan los cambios pero no existen (ni la normal "" ni "*").</summary>
        public static IEnumerable<string> UnknownForms(IEnumerable<FormEntry> forms, IEnumerable<FormChangeEntry> changes)
        {
            var ids = new HashSet<string>((forms ?? Enumerable.Empty<FormEntry>()).Where(f => f != null).Select(f => f.id ?? ""), StringComparer.OrdinalIgnoreCase);
            foreach (var c in changes ?? Enumerable.Empty<FormChangeEntry>())
            {
                if (c == null) continue;
                if (!string.IsNullOrEmpty(c.from) && c.from != FormChange.AnyForm && !ids.Contains(c.from)) yield return c.from;
                if (!string.IsNullOrEmpty(c.to) && !ids.Contains(c.to)) yield return c.to;
            }
        }

        private static List<string> Split(string cell, char sep)
            => (cell ?? "").Split(sep).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();

        private static int Int(string s, string where)
        {
            if (s.Length == 0) return 0;
            if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) || v < 0)
                throw new CsvCellException($"'{s}' no es un número válido (en {where}).");
            return v;
        }
    }
}
