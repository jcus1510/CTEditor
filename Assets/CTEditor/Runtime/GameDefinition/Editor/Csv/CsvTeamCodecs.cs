using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CTEditor.GameDefinition.Editor.Csv
{
    /// <summary>
    /// Formatos de Excel para EQUIPOS (entrenadores y equipos prearmados) y ZONAS salvajes. Todo en una
    /// sola celda, separado por | :
    ///
    ///   Equipo:  especie@nivel[mov1/mov2]{objeto}~naturaleza#iv"mote"   (todo tras el nivel es opcional)
    ///            pidgey@5 | rattata@4[tackle/quick_attack]{oran_berry} | onix@14#31"Rocoso"
    ///   Zona:    especie@min-max:frecuencia
    ///            pidgey@2-5:50 | rattata@2-4:50 | pikachu@3:5
    /// </summary>
    public static class CsvTeamCodecs
    {
        public sealed class ParsedMember
        {
            public string Species, Held = "", Nature = "", Nickname = "";
            public int Level, Iv = -1;
            public List<string> Moves = new List<string>();
        }

        private static readonly Regex MemberRx = new Regex(
            @"^(?<sp>[^@\[\]{}~#""]+)@(?<lvl>\d+)(?:\[(?<moves>[^\]]*)\])?(?:\{(?<held>[^}]*)\})?(?:~(?<nat>[^#""]+))?(?:#(?<iv>\d+))?(?:""(?<nick>[^""]*)"")?$");

        public static List<ParsedMember> ParseTeam(string cell)
        {
            var list = new List<ParsedMember>();
            foreach (var item in CsvCodecs.SplitList(cell))
            {
                var m = MemberRx.Match(item.Trim());
                if (!m.Success)
                    throw new CsvCellException($"'{item}' no es un miembro válido. Usa especie@nivel, p. ej. pidgey@5 o rattata@4[tackle/quick_attack]{{oran_berry}}.");
                var p = new ParsedMember
                {
                    Species = m.Groups["sp"].Value.Trim(),
                    Level = int.Parse(m.Groups["lvl"].Value),
                    Held = m.Groups["held"].Success ? m.Groups["held"].Value.Trim() : "",
                    Nature = m.Groups["nat"].Success ? m.Groups["nat"].Value.Trim() : "",
                    Iv = m.Groups["iv"].Success ? int.Parse(m.Groups["iv"].Value) : -1,
                    Nickname = m.Groups["nick"].Success ? m.Groups["nick"].Value : "",
                };
                if (p.Level < 1) throw new CsvCellException($"'{item}': el nivel debe ser al menos 1.");
                if (m.Groups["moves"].Success)
                    p.Moves = m.Groups["moves"].Value.Split('/').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                list.Add(p);
            }
            return list;
        }

        public static string FormatTeam(IEnumerable<ParsedMember> members)
            => CsvCodecs.JoinList(members.Select(FormatMember));

        public static string FormatMember(ParsedMember p)
        {
            var sb = new StringBuilder();
            sb.Append(p.Species).Append('@').Append(p.Level);
            if (p.Moves.Count > 0) sb.Append('[').Append(string.Join("/", p.Moves)).Append(']');
            if (!string.IsNullOrEmpty(p.Held)) sb.Append('{').Append(p.Held).Append('}');
            if (!string.IsNullOrEmpty(p.Nature)) sb.Append('~').Append(p.Nature);
            if (p.Iv >= 0) sb.Append('#').Append(p.Iv);
            if (!string.IsNullOrEmpty(p.Nickname)) sb.Append('"').Append(p.Nickname).Append('"');
            return sb.ToString();
        }

        // ---------------- Zonas ----------------

        private static readonly Regex ZoneRx = new Regex(@"^(?<sp>[^@]+)@(?<min>\d+)(?:-(?<max>\d+))?(?::(?<w>\d+))?$");

        public static List<(string species, int min, int max, int weight)> ParseZone(string cell)
        {
            var list = new List<(string, int, int, int)>();
            foreach (var item in CsvCodecs.SplitList(cell))
            {
                var m = ZoneRx.Match(item.Trim());
                if (!m.Success) throw new CsvCellException($"'{item}' no es válido. Usa especie@min-max:frecuencia, p. ej. pidgey@2-5:50.");
                int min = int.Parse(m.Groups["min"].Value);
                int max = m.Groups["max"].Success ? int.Parse(m.Groups["max"].Value) : min;
                int w = m.Groups["w"].Success ? int.Parse(m.Groups["w"].Value) : 10;
                list.Add((m.Groups["sp"].Value.Trim(), min, max, w));
            }
            return list;
        }

        public static string FormatZone(IEnumerable<(string species, int min, int max, int weight)> entries)
            => CsvCodecs.JoinList(entries.Select(e => $"{e.species}@{e.min}{(e.max != e.min ? "-" + e.max : "")}:{e.weight}"));
    }
}
