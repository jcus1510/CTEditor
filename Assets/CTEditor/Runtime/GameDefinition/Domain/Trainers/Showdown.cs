using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CTEditor.GameDefinition.Domain.Stats;

namespace CTEditor.GameDefinition.Domain.Trainers
{
    /// <summary>
    /// Un miembro en formato SHOWDOWN (el estándar para compartir equipos), con los NOMBRES tal cual (en inglés):
    ///
    ///     Chompy (Garchomp) (F) @ Choice Scarf
    ///     Ability: Rough Skin
    ///     Level: 62
    ///     EVs: 252 Atk / 4 SpD / 252 Spe
    ///     Jolly Nature
    ///     IVs: 0 SpA
    ///     - Earthquake
    ///     - Dragon Claw
    ///
    /// Los nombres se traducen a ids del juego con <see cref="ShowdownNames"/>.
    /// </summary>
    public sealed class ShowdownSet
    {
        public string Species = "", Nickname = "", Gender = "", Item = "", Ability = "", Nature = "";
        /// <summary>Nivel (null = no se escribió; Showdown entiende 100).</summary>
        public int? Level;
        public StatSpread Evs = StatSpread.Empty, Ivs = StatSpread.Empty;
        public List<string> Moves = new List<string>();
        public bool Shiny;
    }

    /// <summary>Leer y escribir equipos en formato Showdown.</summary>
    public static class ShowdownFormat
    {
        /// <summary>
        /// Lee un equipo (miembros separados por una línea en blanco). Lo que no entiende (Tera Type, Happiness...) lo
        /// ignora; los errores de un miembro van a 'problems' y ese miembro se salta si no tiene especie.
        /// </summary>
        public static List<ShowdownSet> Parse(string text, List<string> problems = null)
        {
            var list = new List<ShowdownSet>();
            var block = new List<string>();
            foreach (var raw in (text ?? "").Replace("\r", "").Split('\n').Concat(new[] { "" }))
            {
                var line = raw.Trim();
                if (line.Length == 0)
                {
                    if (block.Count > 0)
                    {
                        var set = ParseBlock(block, problems);
                        if (set != null) list.Add(set);
                        block.Clear();
                    }
                    continue;
                }
                if (line.StartsWith("===")) continue;   // cabeceras de equipo de Showdown («=== [gen6ou] Mi equipo ===»)
                block.Add(line);
            }
            return list;
        }

        private static ShowdownSet ParseBlock(List<string> lines, List<string> problems)
        {
            var set = new ShowdownSet();
            ParseHeader(lines[0], set);
            if (set.Species.Length == 0) { problems?.Add($"«{lines[0]}»: no se entiende la especie."); return null; }
            foreach (var line in lines.Skip(1))
            {
                if (line.StartsWith("-"))
                {
                    var move = line.TrimStart('-', ' ').Trim();
                    int br = move.IndexOf('[');                    // Hidden Power [Fire] -> Hidden Power
                    if (br > 0) move = move.Substring(0, br).Trim();
                    if (move.Length > 0) set.Moves.Add(move);
                }
                else if (Starts(line, "Ability:", out var ab)) set.Ability = ab;
                else if (Starts(line, "Level:", out var lv))
                {
                    if (int.TryParse(lv, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n > 0) set.Level = n;
                    else problems?.Add($"{set.Species}: el nivel «{lv}» no es un número.");
                }
                else if (Starts(line, "EVs:", out var ev)) set.Evs = Spread(ev, set.Species, "EVs", problems);
                else if (Starts(line, "IVs:", out var iv)) set.Ivs = Spread(iv, set.Species, "IVs", problems);
                else if (Starts(line, "Shiny:", out var sh)) set.Shiny = sh.StartsWith("y", StringComparison.OrdinalIgnoreCase);
                else if (line.EndsWith(" Nature", StringComparison.OrdinalIgnoreCase)) set.Nature = line.Substring(0, line.Length - 7).Trim();
                // Tera Type, Happiness, Gigantamax, Pokeball, Dynamax Level...: no se usan en este juego.
            }
            return set;
        }

        // «Apodo (Especie) (F) @ Objeto», «Especie (M)», «Especie @ Objeto».
        private static void ParseHeader(string line, ShowdownSet set)
        {
            int at = line.LastIndexOf(" @ ", StringComparison.Ordinal);
            if (at >= 0) { set.Item = line.Substring(at + 3).Trim(); line = line.Substring(0, at).Trim(); }
            if (line.EndsWith("(M)") || line.EndsWith("(F)"))
            {
                set.Gender = line.Substring(line.Length - 2, 1);
                line = line.Substring(0, line.Length - 3).Trim();
            }
            if (line.EndsWith(")"))
            {
                int open = line.LastIndexOf(" (", StringComparison.Ordinal);
                if (open > 0)
                {
                    set.Nickname = line.Substring(0, open).Trim();
                    set.Species = line.Substring(open + 2, line.Length - open - 3).Trim();
                    return;
                }
            }
            set.Species = line.Trim();
        }

        private static bool Starts(string line, string key, out string rest)
        {
            rest = null;
            if (!line.StartsWith(key, StringComparison.OrdinalIgnoreCase)) return false;
            rest = line.Substring(key.Length).Trim();
            return true;
        }

        private static StatSpread Spread(string text, string species, string what, List<string> problems)
        {
            if (StatSpread.TryParse(text, out var s, out var error)) return s;
            problems?.Add($"{species}: {what} no válidos ({error}).");
            return StatSpread.Empty;
        }

        /// <summary>Escribe el equipo en formato Showdown (miembros separados por una línea en blanco).</summary>
        public static string Format(IEnumerable<ShowdownSet> sets)
        {
            var sb = new StringBuilder();
            foreach (var s in sets ?? Enumerable.Empty<ShowdownSet>())
            {
                if (s == null || string.IsNullOrWhiteSpace(s.Species)) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(string.IsNullOrWhiteSpace(s.Nickname) || s.Nickname == s.Species ? s.Species : $"{s.Nickname} ({s.Species})");
                if (s.Gender == "M" || s.Gender == "F") sb.Append($" ({s.Gender})");
                if (!string.IsNullOrWhiteSpace(s.Item)) sb.Append(" @ ").Append(s.Item);
                sb.Append('\n');
                if (!string.IsNullOrWhiteSpace(s.Ability)) sb.Append("Ability: ").Append(s.Ability).Append('\n');
                if (s.Level.HasValue && s.Level.Value != 100) sb.Append("Level: ").Append(s.Level.Value).Append('\n');
                if (s.Shiny) sb.Append("Shiny: Yes\n");
                if (!s.Evs.IsEmpty) sb.Append("EVs: ").Append(s.Evs.FormatShowdown()).Append('\n');
                if (!string.IsNullOrWhiteSpace(s.Nature)) sb.Append(s.Nature).Append(" Nature\n");
                if (!s.Ivs.IsEmpty) sb.Append("IVs: ").Append(s.Ivs.FormatShowdown()).Append('\n');
                foreach (var m in s.Moves) sb.Append("- ").Append(m).Append('\n');
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Traduce NOMBRES de Showdown (inglés) ↔ ids del juego, para una categoría (especies, movimientos...). Compara sin
    /// mayúsculas, espacios ni signos: «U-turn» = «u_turn», «King's Shield» = «kings_shield», «Rotom-Wash» = «rotom_wash».
    /// Así casi todo se reconoce aunque la ficha no tenga nombre en inglés; el nombre en inglés (y el visible) ayudan con lo
    /// inventado o renombrado.
    /// </summary>
    public sealed class ShowdownNames
    {
        private readonly Dictionary<string, string> _byName = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _english = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly bool _speciesStyle;

        /// <param name="speciesStyle">Especies: al deducir el nombre del id, las partes se unen con «-» (rotom_wash → Rotom-Wash).</param>
        public ShowdownNames(bool speciesStyle = false) => _speciesStyle = speciesStyle;

        /// <summary>Registra una ficha: su id y los nombres por los que se la puede reconocer (el primero, su nombre en inglés).</summary>
        public void Add(string id, string englishName = null, string displayName = null)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            id = id.Trim();
            if (!string.IsNullOrWhiteSpace(englishName)) _english[id] = englishName.Trim();
            // El id y el nombre en inglés mandan sobre el nombre visible (que puede coincidir con otro en inglés).
            foreach (var n in new[] { displayName, id, englishName })
                if (!string.IsNullOrWhiteSpace(n)) _byName[Normalize(n)] = id;
        }

        /// <summary>El id de un nombre de Showdown (null si no hay ninguna ficha con ese nombre).</summary>
        public string IdOf(string name)
            => string.IsNullOrWhiteSpace(name) ? null : _byName.TryGetValue(Normalize(name), out var id) ? id : null;

        /// <summary>El nombre en inglés de un id: el de su ficha o, si no tiene, deducido del id (rough_skin → Rough Skin).</summary>
        public string NameOf(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return "";
            if (_english.TryGetValue(id.Trim(), out var en)) return en;
            return FromId(id, _speciesStyle);
        }

        public static string FromId(string id, bool speciesStyle = false)
        {
            var parts = (id ?? "").Trim().Split(new[] { '_', '-' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p.Substring(1));
            return string.Join(speciesStyle ? "-" : " ", parts);
        }

        /// <summary>Solo letras y números, en minúsculas (sin acentos): la clave con la que se comparan los nombres.</summary>
        public static string Normalize(string name)
        {
            var d = (name ?? "").Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(d.Length);
            foreach (var c in d)
                if (char.IsLetterOrDigit(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }
    }
}

namespace CTEditor.GameDefinition.Domain.Trainers
{
    /// <summary>Un miembro con IDS del juego (lo que se guarda en una ficha de equipo). Textos vacíos = automático.</summary>
    public sealed class ShowdownMember
    {
        public string SpeciesId = "", Nickname = "", Gender = "", ItemId = "", AbilityId = "", NatureId = "";
        public int Level = 100;
        public StatSpread Evs = StatSpread.Empty, Ivs = StatSpread.Empty;
        public List<string> MoveIds = new List<string>();
    }

    /// <summary>
    /// Convierte entre Showdown (nombres en inglés) y miembros con ids del juego. Lo que no reconoce lo avisa en
    /// 'problems' y lo deja vacío (automático); un miembro con especie desconocida se salta.
    /// </summary>
    public sealed class ShowdownConverter
    {
        public ShowdownNames Species { get; } = new ShowdownNames(speciesStyle: true);
        public ShowdownNames Moves { get; } = new ShowdownNames();
        public ShowdownNames Abilities { get; } = new ShowdownNames();
        public ShowdownNames Items { get; } = new ShowdownNames();
        public ShowdownNames Natures { get; } = new ShowdownNames();

        /// <summary>Lee un texto de Showdown y lo traduce a miembros. 'defaultLevel' = el nivel si no se escribe (Showdown: 100).</summary>
        public List<ShowdownMember> Import(string text, int defaultLevel = 100, List<string> problems = null)
        {
            var list = new List<ShowdownMember>();
            foreach (var set in ShowdownFormat.Parse(text, problems))
            {
                var m = Resolve(set, defaultLevel, problems);
                if (m != null) list.Add(m);
            }
            return list;
        }

        public ShowdownMember Resolve(ShowdownSet set, int defaultLevel = 100, List<string> problems = null)
        {
            string sp = Species.IdOf(set.Species);
            if (sp == null) { problems?.Add($"No hay ninguna especie «{set.Species}»: se salta ese miembro."); return null; }
            var m = new ShowdownMember
            {
                SpeciesId = sp,
                Nickname = set.Nickname ?? "",
                Gender = set.Gender ?? "",
                Level = Math.Max(1, set.Level ?? defaultLevel),
                Evs = set.Evs ?? StatSpread.Empty,
                Ivs = set.Ivs ?? StatSpread.Empty,
            };
            m.ItemId = Find(Items, set.Item, "el objeto", set.Species, problems);
            m.AbilityId = Find(Abilities, set.Ability, "la habilidad", set.Species, problems);
            m.NatureId = Find(Natures, set.Nature, "la naturaleza", set.Species, problems);
            foreach (var mv in set.Moves)
            {
                var id = Find(Moves, mv, "el movimiento", set.Species, problems);
                if (id.Length > 0 && !m.MoveIds.Contains(id)) m.MoveIds.Add(id);
            }
            return m;
        }

        private static string Find(ShowdownNames names, string name, string what, string species, List<string> problems)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            var id = names.IdOf(name);
            if (id == null) { problems?.Add($"{species}: no existe {what} «{name}» en tu juego (se deja automático)."); return ""; }
            return id;
        }

        /// <summary>Un miembro con ids → su texto de Showdown (nombres en inglés).</summary>
        public ShowdownSet ToSet(ShowdownMember m)
        {
            var nick = (m.Nickname ?? "").Trim();
            return new ShowdownSet
            {
                Species = Species.NameOf(m.SpeciesId),
                Nickname = nick,
                Gender = m.Gender ?? "",
                Item = string.IsNullOrWhiteSpace(m.ItemId) ? "" : Items.NameOf(m.ItemId),
                Ability = string.IsNullOrWhiteSpace(m.AbilityId) ? "" : Abilities.NameOf(m.AbilityId),
                Nature = string.IsNullOrWhiteSpace(m.NatureId) ? "" : Natures.NameOf(m.NatureId),
                Level = m.Level,
                Evs = m.Evs ?? StatSpread.Empty,
                Ivs = m.Ivs ?? StatSpread.Empty,
                Moves = m.MoveIds.Where(x => !string.IsNullOrWhiteSpace(x)).Select(Moves.NameOf).ToList(),
            };
        }

        public string Export(IEnumerable<ShowdownMember> members) => ShowdownFormat.Format(members.Select(ToSet));
    }
}
