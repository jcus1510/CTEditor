using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CTEditor.Content
{
    // ── Evolutions («evoluciona» column) ────────────────────────────────────────────────────────────
    //   ivysaur@16 · raichu@objeto:thunder_stone · crobat@amistad(:200) · steelix@intercambio(:metal_coat) · sylveon@subir
    //   extra conditions with «+»: espeon@amistad+hora:dia · weavile@subir+lleva:razor_claw+hora:noche · +!lleva:everstone

    /// <summary>An extra condition of an evolution: «hora: noche», «lleva: razor_claw», «no: lleva everstone»...</summary>
    public sealed class EvoCondition
    {
        public bool Not;
        public string Key = "";
        public string Value = "";

        public override string ToString() => (Not ? "!" : "") + Key + (Value.Length > 0 ? ":" + Value : "");
    }

    /// <summary>One evolution: the species it becomes, how (method + value) and its extra conditions.</summary>
    public sealed class Evolution
    {
        public string Target = "";
        /// <summary>nivel, objeto, amistad, intercambio, subir or otro (anything the windows do not know, kept as it is).</summary>
        public string Method = "nivel";
        public string Value = "";
        public List<EvoCondition> Conditions = new List<EvoCondition>();

        public string Condition(string key) => Conditions.FirstOrDefault(c => !c.Not && c.Key == key)?.Value;
    }

    /// <summary>What a kind of value is, so the windows show the right control (a number, a list of items, of moves...).</summary>
    public enum EvoValueKind { None, Number, Item, Move, Type, Species, Weather, Nature, Text, Choice }

    public static class Evolutions
    {
        public static readonly (string key, string label)[] Methods =
        {
            ("nivel", "Al llegar al nivel"), ("objeto", "Con un objeto"), ("amistad", "Con amistad"), ("intercambio", "Al intercambiarlo"),
            ("subir", "Al subir de nivel"), ("otro", "Otra condición"),
        };

        /// <summary>The extra conditions a window can add (key, label, kind of value, choices).</summary>
        public static readonly (string key, string label, EvoValueKind kind, string[] options)[] ConditionKinds =
        {
            ("hora", "Hora del día", EvoValueKind.Choice, new[] { "dia", "noche", "manana", "atardecer" }),
            ("lleva", "Llevando un objeto", EvoValueKind.Item, null),
            ("sabe", "Sabiendo un movimiento", EvoValueKind.Move, null),
            ("sabe_tipo", "Sabiendo un movimiento de tipo", EvoValueKind.Type, null),
            ("lugar", "En un lugar", EvoValueKind.Text, null),
            ("genero", "Género", EvoValueKind.Choice, new[] { "macho", "hembra" }),
            ("stats", "Ataque frente a Defensa", EvoValueKind.Choice, new[] { "atq>def", "atq<def", "atq=def" }),
            ("amistad", "Amistad mínima", EvoValueKind.Number, null),
            ("nivel", "Nivel mínimo", EvoValueKind.Number, null),
            ("desde", "Desde el nivel", EvoValueKind.Number, null),
            ("clima", "Con un clima en el mapa", EvoValueKind.Weather, null),
            ("equipo_especie", "Con una especie en el equipo", EvoValueKind.Species, null),
            ("equipo_tipo", "Con un tipo en el equipo", EvoValueKind.Type, null),
            ("naturaleza", "Con una naturaleza", EvoValueKind.Nature, null),
            ("azar", "Al azar (%)", EvoValueKind.Number, null),
            ("marca", "Con una marca de la historia", EvoValueKind.Text, null),
        };

        private static readonly Dictionary<string, string> ChoiceLabels = new Dictionary<string, string>
        {
            ["dia"] = "De día", ["noche"] = "De noche", ["manana"] = "Por la mañana", ["atardecer"] = "Al atardecer",
            ["macho"] = "Macho", ["hembra"] = "Hembra", ["atq>def"] = "Ataque mayor", ["atq<def"] = "Defensa mayor", ["atq=def"] = "Iguales",
        };

        public static string ChoiceLabel(string value) => ChoiceLabels.TryGetValue(value ?? "", out var l) ? l : value;

        public static string MethodLabel(string method) => Methods.FirstOrDefault(m => m.key == method).label ?? method;

        public static string ConditionLabel(string key) => ConditionKinds.FirstOrDefault(k => k.key == key).label ?? key;

        public static List<Evolution> Parse(string cell)
        {
            var list = new List<Evolution>();
            foreach (var raw in (cell ?? "").Split('|'))
            {
                var e = raw.Trim();
                if (e.Length == 0) continue;
                var parts = e.Split('+');
                var head = parts[0].Trim();
                int at = head.IndexOf('@');
                var evo = new Evolution { Target = (at >= 0 ? head.Substring(0, at) : head).Trim() };
                var how = at >= 0 ? head.Substring(at + 1).Trim() : "";
                string key = how, arg = "";
                int colon = how.IndexOf(':');
                if (colon >= 0) { key = how.Substring(0, colon).Trim(); arg = how.Substring(colon + 1).Trim(); }
                if (int.TryParse(how, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) { evo.Method = "nivel"; evo.Value = how; }
                else if (how.Length == 0) { evo.Method = "nivel"; }
                else if (key == "objeto" || key == "amistad" || key == "intercambio") { evo.Method = key; evo.Value = arg; }
                else if (how == "subir") evo.Method = "subir";
                else { evo.Method = "otro"; evo.Value = how; }
                for (int i = 1; i < parts.Length; i++)
                {
                    var c = parts[i].Trim();
                    if (c.Length == 0) continue;
                    var cond = new EvoCondition { Not = c.StartsWith("!") };
                    if (cond.Not) c = c.Substring(1).Trim();
                    int cc = c.IndexOf(':');
                    cond.Key = (cc >= 0 ? c.Substring(0, cc) : c).Trim();
                    cond.Value = cc >= 0 ? c.Substring(cc + 1).Trim() : "";
                    evo.Conditions.Add(cond);
                }
                list.Add(evo);
            }
            return list;
        }

        public static string Write(IEnumerable<Evolution> list) => string.Join(" | ", list.Where(e => e.Target.Trim().Length > 0).Select(e =>
        {
            string how = e.Method switch
            {
                "nivel" => e.Value,
                "objeto" => "objeto:" + e.Value,
                "amistad" => e.Value.Length > 0 ? "amistad:" + e.Value : "amistad",
                "intercambio" => e.Value.Length > 0 ? "intercambio:" + e.Value : "intercambio",
                "subir" => "subir",
                _ => e.Value,
            };
            var s = how.Length > 0 ? $"{e.Target}@{how}" : e.Target;
            foreach (var c in e.Conditions) s += "+" + c;
            return s;
        }));

        /// <summary>A short text for an arrow of the tree: «Nv. 16», «Piedra Fuego · de noche», «Intercambio + Revestimiento metálico».</summary>
        public static string Describe(Evolution e, Func<string, string, string> nameOf)
        {
            string N(string category, string id) => string.IsNullOrEmpty(id) ? "" : nameOf?.Invoke(category, id) ?? id;
            string main = e.Method switch
            {
                "nivel" => e.Value.Length > 0 ? "Nv. " + e.Value : "Por nivel",
                "objeto" => N(ContentSchemas.Items, e.Value),
                "amistad" => "Amistad" + (e.Value.Length > 0 ? " " + e.Value : ""),
                "intercambio" => "Intercambio" + (e.Value.Length > 0 ? " + " + N(ContentSchemas.Items, e.Value) : ""),
                "subir" => "Al subir de nivel",
                _ => e.Value,
            };
            var extra = e.Conditions.Select(c => (c.Not ? "sin " : "") + DescribeCondition(c, N));
            return string.Join(" · ", new[] { main }.Concat(extra).Where(x => x.Length > 0));
        }

        private static string DescribeCondition(EvoCondition c, Func<string, string, string> n) => c.Key switch
        {
            "hora" or "genero" or "stats" => ChoiceLabel(c.Value).ToLowerInvariant(),
            "lleva" => "llevando " + n(ContentSchemas.Items, c.Value),
            "sabe" => "sabiendo " + n(ContentSchemas.Moves, c.Value),
            "sabe_tipo" => "con un mov. " + n(ContentSchemas.Types, c.Value),
            "lugar" => "en " + c.Value.Replace('_', ' '),
            "amistad" => "amistad " + c.Value,
            "nivel" or "desde" => "desde nv. " + c.Value,
            "clima" => "con " + n(ContentSchemas.Weathers, c.Value),
            "equipo_especie" => "con " + n(ContentSchemas.Species, c.Value) + " en el equipo",
            "equipo_tipo" => "con un " + n(ContentSchemas.Types, c.Value) + " en el equipo",
            "naturaleza" => n(ContentSchemas.Natures, c.Value),
            "azar" => c.Value + " %",
            _ => c.ToString(),
        };
    }

    // ── Battle forms («formas») and form changes («cambios_forma») ──────────────────────────────────
    //   formas: id;nombre;tipo1/tipo2;atq/def/atq_esp/def_esp/vel;habilidad;vuelve   (separated by |; empty = like the species)
    //   cambios_forma: desde>hasta:disparador[:valor][;con=habilidad][;sabe=movimiento][;despues]   («*» desde = any form)

    /// <summary>A form that appears in the middle of a battle (Mega-Charizard X, Modo Daruma...).</summary>
    public sealed class BattleForm
    {
        public string Id = "", Name = "", Type1 = "", Type2 = "", Ability = "";
        /// <summary>Attack, Defense, Sp. Atk, Sp. Def, Speed (HP never changes); all 0 = like the species.</summary>
        public int[] Stats = new int[5];
        public bool Reverts;

        public bool HasStats => Stats.Any(s => s > 0);
    }

    /// <summary>What makes a species change form in battle: from → to, its trigger and its value.</summary>
    public sealed class FormChange
    {
        public string From = "", To = "";
        /// <summary>mega, objeto, movimiento, ataque, ps_bajo, ps_desde or clima.</summary>
        public string Trigger = "mega";
        public string Value = "", Ability = "", Knows = "";
        public bool After;
        /// <summary>Options this window does not know (kept as they are).</summary>
        public List<string> Others = new List<string>();
    }

    public static class SpeciesForms
    {
        public const string AnyForm = "*";

        public static readonly (string key, string label, EvoValueKind kind)[] Triggers =
        {
            ("mega", "Megaevolución (con su megapiedra)", EvoValueKind.Item),
            ("objeto", "Llevando un objeto", EvoValueKind.Item),
            ("movimiento", "Al usar un movimiento", EvoValueKind.Move),
            ("ataque", "Al usar cualquier ataque", EvoValueKind.None),
            ("ps_bajo", "Con los PS por debajo de (%)", EvoValueKind.Number),
            ("ps_desde", "Con los PS desde (%)", EvoValueKind.Number),
            ("clima", "Con un clima", EvoValueKind.Weather),
        };

        public static string TriggerLabel(string key) => Triggers.FirstOrDefault(t => t.key == key).label ?? key;

        public static List<BattleForm> ParseForms(string cell)
        {
            var list = new List<BattleForm>();
            foreach (var raw in Split(cell, '|'))
            {
                var p = raw.Split(';').Select(x => x.Trim()).ToList();
                while (p.Count < 6) p.Add("");
                var f = new BattleForm { Id = p[0], Name = p[1], Ability = p[4] };
                var types = Split(p[2], '/');
                f.Type1 = types.Count > 0 ? types[0] : "";
                f.Type2 = types.Count > 1 ? types[1] : "";
                var s = p[3].Split('/');
                for (int i = 0; i < 5 && i < s.Length; i++) int.TryParse(s[i].Trim(), out f.Stats[i]);
                var back = p[5].ToLowerInvariant();
                f.Reverts = back == "vuelve" || back == "si" || back == "sí";
                list.Add(f);
            }
            return list;
        }

        public static string WriteForms(IEnumerable<BattleForm> forms) => string.Join("|", forms.Where(f => f.Id.Trim().Length > 0).Select(f =>
        {
            var types = string.Join("/", new[] { f.Type1, f.Type2 }.Where(t => !string.IsNullOrWhiteSpace(t)));
            var stats = f.HasStats ? string.Join("/", f.Stats) : "";
            var parts = new List<string> { f.Id, f.Name ?? "", types, stats, f.Ability ?? "", f.Reverts ? "vuelve" : "" };
            while (parts.Count > 2 && parts[parts.Count - 1].Length == 0) parts.RemoveAt(parts.Count - 1);
            return string.Join(";", parts);
        }));

        public static List<FormChange> ParseChanges(string cell)
        {
            var list = new List<FormChange>();
            foreach (var raw in Split(cell, '|'))
            {
                var parts = raw.Split(';').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                var main = parts[0];
                int gt = main.IndexOf('>');
                var c = new FormChange { From = gt >= 0 ? main.Substring(0, gt).Trim() : "" };
                var rest = (gt >= 0 ? main.Substring(gt + 1) : main).Split(':').Select(x => x.Trim()).ToList();
                c.To = rest[0];
                c.Trigger = rest.Count > 1 ? rest[1].ToLowerInvariant() : "";
                c.Value = rest.Count > 2 ? string.Join(":", rest.Skip(2)) : "";
                foreach (var opt in parts.Skip(1))
                {
                    var o = opt.ToLowerInvariant();
                    if (o.StartsWith("con=")) c.Ability = opt.Substring(4).Trim();
                    else if (o.StartsWith("sabe=")) c.Knows = opt.Substring(5).Trim();
                    else if (o == "despues" || o == "después") c.After = true;
                    else c.Others.Add(opt);
                }
                list.Add(c);
            }
            return list;
        }

        public static string WriteChanges(IEnumerable<FormChange> changes) => string.Join("|", changes.Select(c =>
        {
            var s = $"{c.From}>{c.To}:{c.Trigger}" + (c.Value.Length > 0 ? ":" + c.Value : "");
            if (c.Knows.Length > 0) s += ";sabe=" + c.Knows;
            if (c.Ability.Length > 0) s += ";con=" + c.Ability;
            if (c.After) s += ";despues";
            foreach (var o in c.Others) s += ";" + o;
            return s;
        }));

        /// <summary>The changes that lead INTO a form (how it appears): «Megaevolución · Charizardita X».</summary>
        public static IEnumerable<FormChange> Into(IEnumerable<FormChange> changes, string formId) =>
            changes.Where(c => string.Equals(c.To, formId, StringComparison.OrdinalIgnoreCase));

        private static List<string> Split(string cell, char sep) => (cell ?? "").Split(sep).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
    }

    // ── The family graph (cached per version of the data) ───────────────────────────────────────────

    /// <summary>
    /// Who evolves from whom and which species are variants of which («forma_de»), computed once per change of the data
    /// (with ~900 species, searching per card was slow). Roots are species nobody evolves into; a chain is a root and all
    /// it can become.
    /// </summary>
    public sealed class FamilyGraph
    {
        private static FamilyGraph _last;
        private readonly Dictionary<string, string> _parent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<Evolution>> _evos = new Dictionary<string, List<Evolution>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> _variants = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _baseOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly ContentDatabase _db;
        private readonly int _version;

        public IReadOnlyList<string> Species { get; }

        public static FamilyGraph For(ContentDatabase db)
        {
            var last = _last;
            if (last != null && last._db == db && last._version == db.Version) return last;
            return _last = new FamilyGraph(db);
        }

        private FamilyGraph(ContentDatabase db)
        {
            _db = db;
            _version = db.Version;
            var t = db.Table(ContentSchemas.Species);
            var ids = new List<string>();
            foreach (var r in t.Records)
            {
                var id = t.IdOf(r);
                if (id.Length == 0) continue;
                ids.Add(id);
                var evos = Evolutions.Parse(r["evoluciona"]);
                _evos[id] = evos;
                foreach (var e in evos) if (e.Target.Length > 0 && !_parent.ContainsKey(e.Target)) _parent[e.Target] = id;
                var of = r["forma_de"].Trim();
                if (of.Length > 0 && !string.Equals(of, id, StringComparison.OrdinalIgnoreCase))
                {
                    _baseOf[id] = of;
                    if (!_variants.TryGetValue(of, out var l)) _variants[of] = l = new List<string>();
                    l.Add(id);
                }
            }
            Species = ids;
        }

        public string ParentOf(string id) => _parent.TryGetValue(id ?? "", out var p) ? p : null;
        public string BaseOf(string id) => _baseOf.TryGetValue(id ?? "", out var b) ? b : null;
        public IReadOnlyList<Evolution> EvolutionsOf(string id) => _evos.TryGetValue(id ?? "", out var l) ? l : (IReadOnlyList<Evolution>)new Evolution[0];
        public IReadOnlyList<string> VariantsOf(string id) => _variants.TryGetValue(id ?? "", out var l) ? l : (IReadOnlyList<string>)new string[0];

        /// <summary>The first stage of the family (a variant counts in the family of its base).</summary>
        public string RootOf(string id)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (id != null && seen.Add(id))
            {
                var p = ParentOf(id);
                if (p == null && BaseOf(id) is string b && !_parent.ContainsKey(id)) p = b;
                if (p == null) break;
                id = p;
            }
            return id;
        }

        /// <summary>Every species of the chain that starts at a root (evolutions and variants), each once.</summary>
        public List<string> Members(string root)
        {
            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Walk(string id)
            {
                if (id == null || !seen.Add(id)) return;
                list.Add(id);
                foreach (var e in EvolutionsOf(id)) Walk(e.Target);
                foreach (var v in VariantsOf(id)) Walk(v);
            }
            Walk(root);
            return list;
        }

        /// <summary>The roots of the game: species nobody evolves into (and are not a variant). «singles» = also lone species.</summary>
        public List<string> Roots(bool singles) => Species.Where(id => ParentOf(id) == null && BaseOf(id) == null
            && (singles || EvolutionsOf(id).Count > 0 || VariantsOf(id).Count > 0)).ToList();

        /// <summary>Problems of a chain: evolutions into a species that does not exist, loops, levels that go back.</summary>
        public IEnumerable<string> Warnings(string root, Func<string, bool> exists)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var path = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var found = new List<string>();
            void Walk(string id, int level)
            {
                if (!path.Add(id)) { found.Add($"Hay un ciclo: {id} vuelve a evolucionar en sí mismo."); return; }
                if (seen.Add(id))
                    foreach (var e in EvolutionsOf(id))
                    {
                        if (!exists(e.Target)) { found.Add($"{id} evoluciona en «{e.Target}», que no existe."); continue; }
                        int l = e.Method == "nivel" && int.TryParse(e.Value, out var n) ? n : 0;
                        if (l > 0 && l < level) found.Add($"{e.Target} evoluciona al nivel {l}, antes que su forma anterior ({level}).");
                        Walk(e.Target, Math.Max(level, l));
                    }
                path.Remove(id);
            }
            Walk(root, 0);
            return found;
        }
    }
}
