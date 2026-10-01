using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Conditions;
using CTEditor.GameDefinition.Text;

namespace CTEditor.GameDefinition.Editor.Csv
{
    /// <summary>Error en una celda: se muestra al autor tal cual, junto a la fila y la columna.</summary>
    public sealed class CsvCellException : Exception
    {
        public CsvCellException(string message) : base(message) { }
    }

    /// <summary>
    /// Traducción entre las CELDAS de Excel y los datos (sin Unity: se testea con NUnit puro).
    /// Formatos pensados para escribirse a mano:
    ///   Lista:        valor|valor|valor
    ///   Learnset:     1:tackle|7:ember            (nivel:movimiento)
    ///   Evoluciones:  ivysaur@16                  (especie@nivel)
    ///   EVs / stats:  attack:1|speed:2            (stat:cantidad)
    ///   Efectos:      estado:burn@10 | estado_propio:sleep | drenar:50 | retroceso:33 | retroceso_ps:25
    ///                 curar:50 | curar_rival:50 | curar_estado | curar_estado:burn | curar_estado_rival
    ///                 clima:rain | clima:rain:8 | stat:attack:-1@100 | stat_propio:attack:+2 | amedrentar@30
    ///                 (@N = probabilidad en %, por defecto 100)
    ///                 Condiciones al final entre corchetes: stat_propio:attack:+2 [si propio.vida<=50 & clima=sun]
    ///                 Un '&' delante = usa el MISMO dado que el efecto anterior: stat_propio:attack:+1@10|&stat_propio:defense:+1
    /// </summary>
    public static class CsvCodecs
    {
        public static List<string> SplitList(string cell)
            => (cell ?? "").Split('|').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

        public static string JoinList(IEnumerable<string> items) => string.Join("|", items.Where(s => !string.IsNullOrEmpty(s)));

        // ---------------- Pares clave:número ----------------

        public static List<(string key, int value)> ParsePairs(string cell, string what)
        {
            var list = new List<(string, int)>();
            foreach (var item in SplitList(cell))
            {
                var parts = item.Split(':');
                if (parts.Length != 2 || parts[0].Trim().Length == 0 || !CsvTable.TryInt(parts[1], out int v))
                    throw new CsvCellException($"'{item}' no es válido en {what}: usa clave:número (ej. attack:1).");
                list.Add((parts[0].Trim(), v));
            }
            return list;
        }

        public static string FormatPairs(IEnumerable<(string key, int value)> pairs)
            => JoinList(pairs.Select(p => $"{p.key}:{p.value}"));

        // ---------------- Learnset: nivel:movimiento ----------------

        public static List<(int level, string move)> ParseLearnset(string cell)
        {
            var list = new List<(int, string)>();
            foreach (var item in SplitList(cell))
            {
                int colon = item.IndexOf(':');
                if (colon <= 0 || !CsvTable.TryInt(item.Substring(0, colon), out int level) || colon == item.Length - 1)
                    throw new CsvCellException($"'{item}' no es válido en el learnset: usa nivel:movimiento (ej. 7:ember).");
                list.Add((level, item.Substring(colon + 1).Trim()));
            }
            return list;
        }

        public static string FormatLearnset(IEnumerable<(int level, string move)> entries)
            => JoinList(entries.Select(e => $"{e.level}:{e.move}"));

        // ---------------- Evoluciones ----------------
        //   ivysaur@16                      por nivel
        //   raichu@objeto:thunder_stone     con un objeto
        //   crobat@amistad  |  crobat@amistad:200   por amistad (por defecto 220) al subir de nivel
        //   alakazam@intercambio  |  steelix@intercambio:metal_coat   por intercambio (llevando un objeto)
        //   sylveon@subir                   al subir de nivel si se cumplen sus condiciones
        // CONDICIONES EXTRA (todas a la vez), cada una con "+":  espeon@amistad+hora:dia
        //   hitmonlee@20+stats:atq>def · weavile@subir+lleva:razor_claw+hora:noche · sylveon@subir+amistad:160+sabe_tipo:fairy
        //   nivel:N  amistad:N  lleva:objeto  sabe:movimiento  sabe_tipo:tipo  hora:dia|noche|manana|atardecer  lugar:id
        //   clima:id  stats:atq>def|atq<def|atq=def  equipo_especie:id  equipo_tipo:id  naturaleza:id  azar:N  marca:id  genero:macho|hembra
        //   desde:N (nivel mínimo del método, si no es "por nivel").  "!" delante = al revés: +!lleva:everstone

        public sealed class ParsedCondition
        {
            public CTEditor.GameDefinition.Domain.Species.EvolutionConditionKind Kind;
            public int Value;
            public string Id = "";
            public CTEditor.GameDefinition.Domain.Species.DayTime Time;
            public CTEditor.GameDefinition.Domain.Species.StatRelation Relation;
            public bool Negate;
        }

        public sealed class ParsedEvolution
        {
            public string Species;
            public CTEditor.GameDefinition.Domain.Species.EvolutionMethod Method;
            public int Level;
            public string ItemId = "";
            public int Friendship;
            public List<ParsedCondition> Conditions = new List<ParsedCondition>();
        }

        private static readonly (string key, CTEditor.GameDefinition.Domain.Species.EvolutionConditionKind kind)[] ConditionKeys =
        {
            ("nivel", CTEditor.GameDefinition.Domain.Species.EvolutionConditionKind.MinLevel),
            ("amistad", CTEditor.GameDefinition.Domain.Species.EvolutionConditionKind.MinFriendship),
            ("lleva", CTEditor.GameDefinition.Domain.Species.EvolutionConditionKind.HoldsItem),
            ("sabe", CTEditor.GameDefinition.Domain.Species.EvolutionConditionKind.KnowsMove),
            ("sabe_tipo", CTEditor.GameDefinition.Domain.Species.EvolutionConditionKind.KnowsMoveOfType),
            ("hora", CTEditor.GameDefinition.Domain.Species.EvolutionConditionKind.TimeOfDay),
            ("lugar", CTEditor.GameDefinition.Domain.Species.EvolutionConditionKind.AtLocation),
            ("clima", CTEditor.GameDefinition.Domain.Species.EvolutionConditionKind.MapWeather),
            ("stats", CTEditor.GameDefinition.Domain.Species.EvolutionConditionKind.StatRelation),
            ("equipo_especie", CTEditor.GameDefinition.Domain.Species.EvolutionConditionKind.PartyHasSpecies),
            ("equipo_tipo", CTEditor.GameDefinition.Domain.Species.EvolutionConditionKind.PartyHasType),
            ("naturaleza", CTEditor.GameDefinition.Domain.Species.EvolutionConditionKind.Nature),
            ("azar", CTEditor.GameDefinition.Domain.Species.EvolutionConditionKind.Chance),
            ("marca", CTEditor.GameDefinition.Domain.Species.EvolutionConditionKind.GameFlag),
            ("genero", CTEditor.GameDefinition.Domain.Species.EvolutionConditionKind.Gender),
        };

        private static readonly (string key, CTEditor.GameDefinition.Domain.Species.DayTime time)[] TimeKeys =
        {
            ("dia", CTEditor.GameDefinition.Domain.Species.DayTime.Day), ("noche", CTEditor.GameDefinition.Domain.Species.DayTime.Night),
            ("manana", CTEditor.GameDefinition.Domain.Species.DayTime.Morning), ("atardecer", CTEditor.GameDefinition.Domain.Species.DayTime.Evening),
        };

        private static readonly (string key, CTEditor.GameDefinition.Domain.Species.StatRelation rel)[] RelationKeys =
        {
            ("atq>def", CTEditor.GameDefinition.Domain.Species.StatRelation.AttackHigher),
            ("atq<def", CTEditor.GameDefinition.Domain.Species.StatRelation.DefenseHigher),
            ("atq=def", CTEditor.GameDefinition.Domain.Species.StatRelation.AttackEqualsDefense),
        };

        public static List<ParsedEvolution> ParseEvolutions(string cell)
        {
            var list = new List<ParsedEvolution>();
            foreach (var raw in SplitList(cell))
            {
                var parts = raw.Split('+');
                string item = parts[0].Trim();
                int at = item.IndexOf('@');
                string err = $"'{raw}' no es válido en evoluciones: usa especie@16, especie@objeto:id, especie@amistad, especie@intercambio o especie@subir (y condiciones con +).";
                if (at <= 0 || at == item.Length - 1) throw new CsvCellException(err);
                var e = new ParsedEvolution { Species = item.Substring(0, at).Trim() };
                string how = item.Substring(at + 1).Trim();
                string key = how, arg = "";
                int colon = how.IndexOf(':');
                if (colon >= 0) { key = how.Substring(0, colon).Trim(); arg = how.Substring(colon + 1).Trim(); }
                switch (key.ToLowerInvariant())
                {
                    case "objeto": case "item":
                        if (arg.Length == 0) throw new CsvCellException(err);
                        e.Method = CTEditor.GameDefinition.Domain.Species.EvolutionMethod.Item; e.ItemId = arg; break;
                    case "amistad": case "friendship":
                        e.Method = CTEditor.GameDefinition.Domain.Species.EvolutionMethod.Friendship;
                        if (arg.Length > 0 && !CsvTable.TryInt(arg, out e.Friendship)) throw new CsvCellException(err);
                        break;
                    case "intercambio": case "trade":
                        e.Method = CTEditor.GameDefinition.Domain.Species.EvolutionMethod.Trade; e.ItemId = arg; break;
                    case "subir": case "levelup":
                        e.Method = CTEditor.GameDefinition.Domain.Species.EvolutionMethod.LevelUp; break;
                    default:
                        if (!CsvTable.TryInt(how, out e.Level) || e.Level < 1) throw new CsvCellException(err);
                        e.Method = CTEditor.GameDefinition.Domain.Species.EvolutionMethod.Level; break;
                }
                for (int i = 1; i < parts.Length; i++) ParseCondition(parts[i].Trim(), raw, e);
                list.Add(e);
            }
            return list;
        }

        private static void ParseCondition(string text, string raw, ParsedEvolution e)
        {
            bool negate = text.StartsWith("!");
            if (negate) text = text.Substring(1).Trim();
            int colon = text.IndexOf(':');
            if (colon <= 0 || colon == text.Length - 1)
                throw new CsvCellException($"'{raw}': la condición '{text}' debe ser clave:valor (ej. hora:noche, lleva:razor_claw).");
            string key = text.Substring(0, colon).Trim().ToLowerInvariant(), arg = text.Substring(colon + 1).Trim();

            if (key == "desde")
            {
                if (!CsvTable.TryInt(arg, out e.Level)) throw new CsvCellException($"'{raw}': 'desde' necesita un número.");
                return;
            }
            var found = ConditionKeys.FirstOrDefault(k => k.key == key);
            if (found.key == null)
                throw new CsvCellException($"'{raw}': condición desconocida '{key}'. Usa: " + string.Join(", ", ConditionKeys.Select(k => k.key)) + ", desde.");

            var c = new ParsedCondition { Kind = found.kind, Negate = negate };
            switch (found.kind)
            {
                case CTEditor.GameDefinition.Domain.Species.EvolutionConditionKind.MinLevel:
                case CTEditor.GameDefinition.Domain.Species.EvolutionConditionKind.MinFriendship:
                case CTEditor.GameDefinition.Domain.Species.EvolutionConditionKind.Chance:
                    if (!CsvTable.TryInt(arg, out c.Value)) throw new CsvCellException($"'{raw}': '{key}' necesita un número.");
                    break;
                // Género: genero:macho / genero:hembra (1 = macho, 2 = hembra).
                case CTEditor.GameDefinition.Domain.Species.EvolutionConditionKind.Gender:
                {
                    string g = arg.ToLowerInvariant().Replace("é", "e");
                    if (g.StartsWith("h") || g.StartsWith("f") || g == "♀") c.Value = 2;
                    else if (g.StartsWith("m") || g == "♂") c.Value = 1;
                    else throw new CsvCellException($"'{raw}': género '{arg}' no válido. Usa macho o hembra.");
                    break;
                }
                case CTEditor.GameDefinition.Domain.Species.EvolutionConditionKind.TimeOfDay:
                {
                    string norm = arg.ToLowerInvariant().Replace("í", "i").Replace("ñ", "n").Replace("día", "dia");
                    var t = TimeKeys.FirstOrDefault(k => k.key == norm);
                    if (t.key == null) throw new CsvCellException($"'{raw}': hora '{arg}' no válida. Usa dia, noche, manana o atardecer.");
                    c.Time = t.time;
                    break;
                }
                case CTEditor.GameDefinition.Domain.Species.EvolutionConditionKind.StatRelation:
                {
                    var r = RelationKeys.FirstOrDefault(k => k.key == arg.ToLowerInvariant().Replace(" ", ""));
                    if (r.key == null) throw new CsvCellException($"'{raw}': stats '{arg}' no válido. Usa atq>def, atq<def o atq=def.");
                    c.Relation = r.rel;
                    break;
                }
                default: c.Id = arg; break;
            }
            e.Conditions.Add(c);
        }

        public static string FormatEvolutions(IEnumerable<ParsedEvolution> entries)
            => JoinList(entries.Select(e =>
            {
                string head;
                switch (e.Method)
                {
                    case CTEditor.GameDefinition.Domain.Species.EvolutionMethod.Item: head = $"{e.Species}@objeto:{e.ItemId}"; break;
                    case CTEditor.GameDefinition.Domain.Species.EvolutionMethod.Friendship: head = $"{e.Species}@amistad" + (e.Friendship > 0 && e.Friendship != 220 ? $":{e.Friendship}" : ""); break;
                    case CTEditor.GameDefinition.Domain.Species.EvolutionMethod.Trade: head = $"{e.Species}@intercambio" + (string.IsNullOrEmpty(e.ItemId) ? "" : $":{e.ItemId}"); break;
                    case CTEditor.GameDefinition.Domain.Species.EvolutionMethod.LevelUp: head = $"{e.Species}@subir"; break;
                    default: head = $"{e.Species}@{e.Level}"; break;
                }
                if (e.Method != CTEditor.GameDefinition.Domain.Species.EvolutionMethod.Level && e.Level > 0) head += $"+desde:{e.Level}";
                foreach (var c in e.Conditions ?? new List<ParsedCondition>())
                {
                    string key = ConditionKeys.First(k => k.kind == c.Kind).key, val;
                    switch (c.Kind)
                    {
                        case CTEditor.GameDefinition.Domain.Species.EvolutionConditionKind.MinLevel:
                        case CTEditor.GameDefinition.Domain.Species.EvolutionConditionKind.MinFriendship:
                        case CTEditor.GameDefinition.Domain.Species.EvolutionConditionKind.Chance: val = c.Value.ToString(); break;
                        case CTEditor.GameDefinition.Domain.Species.EvolutionConditionKind.Gender: val = c.Value == 2 ? "hembra" : "macho"; break;
                        case CTEditor.GameDefinition.Domain.Species.EvolutionConditionKind.TimeOfDay: val = TimeKeys.First(k => k.time == c.Time).key; break;
                        case CTEditor.GameDefinition.Domain.Species.EvolutionConditionKind.StatRelation: val = RelationKeys.First(k => k.rel == c.Relation).key; break;
                        default: val = c.Id; break;
                    }
                    head += $"+{(c.Negate ? "!" : "")}{key}:{val}";
                }
                return head;
            }));

        // ---------------- Efectos de movimiento ----------------

        public static List<ParsedEffect> ParseEffects(string cell)
        {
            try { return MoveEffectText.Parse(cell); }
            catch (FormatException e) { throw new CsvCellException(e.Message); }
        }

        public static string FormatEffects(IEnumerable<ParsedEffect> effects) => MoveEffectText.Format(effects);

        // ---------------- Enumeraciones en español ----------------

        public static MoveCategory ParseCategory(string s)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "fisico": case "físico": case "physical": return MoveCategory.Physical;
                case "especial": case "special": return MoveCategory.Special;
                case "estado": case "status": return MoveCategory.Status;
                default: throw new CsvCellException($"Categoría '{s}' desconocida: usa fisico, especial o estado.");
            }
        }

        public static string FormatCategory(MoveCategory c)
            => c == MoveCategory.Physical ? "fisico" : c == MoveCategory.Special ? "especial" : "estado";

        public static TwoTurnKind ParseTwoTurn(string s)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "": case "no": case "none": return TwoTurnKind.None;
                case "carga": case "charge": return TwoTurnKind.Charge;
                case "recarga": case "recharge": return TwoTurnKind.Recharge;
                default: throw new CsvCellException($"'{s}' no válido en dos_turnos: usa no, carga o recarga.");
            }
        }

        public static string FormatTwoTurn(TwoTurnKind k)
            => k == TwoTurnKind.Charge ? "carga" : k == TwoTurnKind.Recharge ? "recarga" : "no";

        public static MoveTarget ParseTarget(string s)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "": case "rival": case "enemigo": return MoveTarget.SingleEnemy;
                case "propio": case "usuario": case "self": return MoveTarget.Self;
                case "rivales": case "todos_rivales": return MoveTarget.AllEnemies;
                case "aliado": return MoveTarget.Ally;
                case "todos": case "campo": return MoveTarget.Everyone;
                default: throw new CsvCellException($"'{s}' no válido en objetivo: usa rival, propio, rivales, aliado o todos.");
            }
        }

        public static string FormatTarget(MoveTarget t)
        {
            switch (t)
            {
                case MoveTarget.Self: return "propio";
                case MoveTarget.AllEnemies: return "rivales";
                case MoveTarget.Ally: return "aliado";
                case MoveTarget.Everyone: return "todos";
                default: return "rival";
            }
        }

        /// <summary>Daño especial: "" (normal) | fijo:40 | nivel | mitad | ko | devolver_fisico(:200) | devolver_especial(:200) | venganza(:200).</summary>
        public static (FixedDamageKind kind, int amount) ParseSpecialDamage(string s)
        {
            var v = (s ?? "").Trim().ToLowerInvariant();
            if (v.Length == 0 || v == "no" || v == "normal") return (FixedDamageKind.None, 0);
            if (v == "nivel") return (FixedDamageKind.UserLevel, 0);
            if (v == "mitad") return (FixedDamageKind.HalfTargetHp, 0);
            if (v == "ko") return (FixedDamageKind.OneHitKo, 0);
            if (v == "esfuerzo") return (FixedDamageKind.Endeavor, 0);
            if (v == "ps_propios") return (FixedDamageKind.UserHp, 0);
            if (v == "devolver") return (FixedDamageKind.ReturnAny, 0);
            if (v.StartsWith("devolver:") && CsvTable.TryInt(v.Substring(9), out int pa) && pa >= 0) return (FixedDamageKind.ReturnAny, pa);
            if (v.StartsWith("fijo:") && CsvTable.TryInt(v.Substring(5), out int n) && n >= 0) return (FixedDamageKind.Fixed, n);
            foreach (var (key, kind) in new[] { ("devolver_fisico", FixedDamageKind.ReturnPhysical), ("devolver_especial", FixedDamageKind.ReturnSpecial), ("venganza", FixedDamageKind.Bide) })
            {
                if (v == key) return (kind, 0);
                if (v.StartsWith(key + ":") && CsvTable.TryInt(v.Substring(key.Length + 1), out int pct) && pct >= 0) return (kind, pct);
            }
            throw new CsvCellException($"'{s}' no válido en daño_especial: usa fijo:40, nivel, mitad, ko, devolver_fisico, devolver_especial, venganza esfuerzo, devolver (Represión Metal) o déjalo vacío.");
        }

        public static string FormatSpecialDamage(FixedDamageKind kind, int amount)
        {
            switch (kind)
            {
                case FixedDamageKind.Fixed: return "fijo:" + amount;
                case FixedDamageKind.UserLevel: return "nivel";
                case FixedDamageKind.HalfTargetHp: return "mitad";
                case FixedDamageKind.OneHitKo: return "ko";
                case FixedDamageKind.Endeavor: return "esfuerzo";
                case FixedDamageKind.UserHp: return "ps_propios";
                case FixedDamageKind.ReturnAny: return "devolver" + (amount > 0 && amount != 150 ? ":" + amount : "");
                case FixedDamageKind.ReturnPhysical: return "devolver_fisico" + (amount > 0 && amount != 200 ? ":" + amount : "");
                case FixedDamageKind.ReturnSpecial: return "devolver_especial" + (amount > 0 && amount != 200 ? ":" + amount : "");
                case FixedDamageKind.Bide: return "venganza" + (amount > 0 && amount != 200 ? ":" + amount : "");
                default: return "";
            }
        }

        /// <summary>"2-5" o "2" o "" → (mín, máx).</summary>
        public static (int min, int max) ParseHits(string s)
        {
            s = (s ?? "").Trim();
            if (s.Length == 0) return (1, 1);
            var parts = s.Split('-');
            if (parts.Length == 1 && CsvTable.TryInt(parts[0], out int n) && n >= 1) return (n, n);
            if (parts.Length == 2 && CsvTable.TryInt(parts[0], out int a) && CsvTable.TryInt(parts[1], out int b) && a >= 1 && b >= a) return (a, b);
            throw new CsvCellException($"'{s}' no válido en golpes: usa un número (2) o un rango (2-5).");
        }

        public static string FormatHits(int min, int max) => min == max ? min.ToString() : $"{min}-{max}";
    }
}
