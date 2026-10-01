using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CTEditor.Content
{
    /// <summary>One member of a team as written in the sheets.</summary>
    public sealed class TeamMember
    {
        public string Species = "", Item = "", Nature = "", Ability = "", Gender = "", Evs = "", Ivs = "", Nickname = "";
        public int Level = 5, Iv = -1;
        public List<string> Moves = new List<string>();
        /// <summary>A member that could not be read: kept exactly as it was.</summary>
        public string Raw;

        public TeamMember Clone()
        {
            var c = (TeamMember)MemberwiseClone();
            c.Moves = Moves.ToList();
            return c;
        }
    }

    /// <summary>
    /// The team format of the sheets (the same as Unity's CsvTeamCodecs):
    /// «especie@nivel%g[mov1/mov2]{objeto}~naturaleza!habilidad(EVs)#iv(IVs)"mote"», members separated by «|».
    /// Everything after the level is optional; members that cannot be read are kept untouched.
    /// </summary>
    public static class TeamFormat
    {
        private static readonly Regex MemberRx = new Regex(
            @"^(?<sp>[^@\[\]{}~#""!()]+)@(?<lvl>\d+)(?:%(?<g>[mhMH]))?(?:\[(?<moves>[^\]]*)\])?(?:\{(?<held>[^}]*)\})?(?:~(?<nat>[^#""!(]+))?" +
            @"(?:!(?<ab>[^#""(]+))?(?:\((?<ev>[^)]*)\))?(?:#(?<iv>\d+)?(?:\((?<ivs>[^)]*)\))?)?(?:""(?<nick>[^""]*)"")?$", RegexOptions.Compiled);

        public static TeamMember ParseMember(string text)
        {
            var m = MemberRx.Match((text ?? "").Trim());
            if (!m.Success) return new TeamMember { Raw = text ?? "" };
            return new TeamMember
            {
                Species = m.Groups["sp"].Value.Trim(),
                Level = int.Parse(m.Groups["lvl"].Value),
                Gender = m.Groups["g"].Success ? m.Groups["g"].Value.ToLowerInvariant() : "",
                Moves = m.Groups["moves"].Success ? m.Groups["moves"].Value.Split('/').Select(s => s.Trim()).Where(s => s.Length > 0).ToList() : new List<string>(),
                Item = m.Groups["held"].Success ? m.Groups["held"].Value.Trim() : "",
                Nature = m.Groups["nat"].Success ? m.Groups["nat"].Value.Trim() : "",
                Ability = m.Groups["ab"].Success ? m.Groups["ab"].Value.Trim() : "",
                Evs = m.Groups["ev"].Success ? m.Groups["ev"].Value.Trim() : "",
                Iv = m.Groups["iv"].Success ? int.Parse(m.Groups["iv"].Value) : -1,
                Ivs = m.Groups["ivs"].Success ? m.Groups["ivs"].Value.Trim() : "",
                Nickname = m.Groups["nick"].Success ? m.Groups["nick"].Value : "",
            };
        }

        public static List<TeamMember> Parse(string cell) =>
            string.IsNullOrWhiteSpace(cell) ? new List<TeamMember>() : cell.Split('|').Where(x => x.Trim().Length > 0).Select(ParseMember).ToList();

        public static string FormatMember(TeamMember p)
        {
            if (p.Raw != null) return p.Raw.Trim();
            var sb = new StringBuilder();
            sb.Append(p.Species).Append('@').Append(p.Level);
            if (p.Gender == "m" || p.Gender == "h") sb.Append('%').Append(p.Gender);
            if (p.Moves.Count > 0) sb.Append('[').Append(string.Join("/", p.Moves)).Append(']');
            if (!string.IsNullOrEmpty(p.Item)) sb.Append('{').Append(p.Item).Append('}');
            if (!string.IsNullOrEmpty(p.Nature)) sb.Append('~').Append(p.Nature);
            if (!string.IsNullOrEmpty(p.Ability)) sb.Append('!').Append(p.Ability);
            if (!string.IsNullOrEmpty(p.Evs)) sb.Append('(').Append(p.Evs).Append(')');
            if (p.Iv >= 0 || !string.IsNullOrEmpty(p.Ivs)) sb.Append('#');
            if (p.Iv >= 0) sb.Append(p.Iv);
            if (!string.IsNullOrEmpty(p.Ivs)) sb.Append('(').Append(p.Ivs).Append(')');
            if (!string.IsNullOrEmpty(p.Nickname)) sb.Append('"').Append(p.Nickname).Append('"');
            return sb.ToString();
        }

        public static string Format(IEnumerable<TeamMember> members) => string.Join(" | ", members.Select(FormatMember));

        /// <summary>The ids a member uses, by the position of the column's targets: species, moves, item, nature, ability.</summary>
        public static IEnumerable<(int slot, string id)> Ids(TeamMember m)
        {
            if (m.Raw != null) yield break;
            if (m.Species.Length > 0) yield return (0, m.Species);
            foreach (var mv in m.Moves) yield return (1, mv);
            if (m.Item.Length > 0) yield return (2, m.Item);
            if (m.Nature.Length > 0) yield return (3, m.Nature);
            if (m.Ability.Length > 0) yield return (4, m.Ability);
        }
    }
}
