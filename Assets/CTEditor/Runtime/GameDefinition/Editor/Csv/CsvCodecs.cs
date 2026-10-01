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

    /// <summary>Un efecto de movimiento leído de una celda (independiente de Unity, para poder testearlo).</summary>
    public sealed class ParsedEffect
    {
        public MoveEffectKind Kind;
        public EffectTarget Target = EffectTarget.Opponent;
        public float Chance = 100;
        public string Status;
        public float Amount;
        public string Stat;
        public int Stages;
        public List<Condition> Conditions = new List<Condition>();
        public bool Shared;
        public string Weather;
        public int WeatherTurns;
        public string Hazard;
        public int Turns;
        public string Side;
        public string TypeId;
        public string Text;
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
            var list = new List<ParsedEffect>();
            foreach (var raw in SplitList(cell))
            {
                string item = raw.Trim();
                // Condiciones: "[si ...]" al final.
                var conditions = new List<Condition>();
                int br = item.IndexOf('[');
                if (br >= 0)
                {
                    int close = item.LastIndexOf(']');
                    if (close < br) throw new CsvCellException($"'{raw}': falta cerrar el corchete ].");
                    try { conditions = ConditionText.ParseAll(item.Substring(br + 1, close - br - 1)); }
                    catch (FormatException fe) { throw new CsvCellException(fe.Message); }
                    item = item.Substring(0, br).Trim();
                }
                // Dado compartido: "&" delante.
                bool shared = item.StartsWith("&");
                if (shared) item = item.Substring(1).Trim();
                float chance = 100;
                int at = item.LastIndexOf('@');
                if (at >= 0)
                {
                    if (!CsvTable.TryNumber(item.Substring(at + 1), out chance) || chance < 0 || chance > 100)
                        throw new CsvCellException($"'{raw}': la probabilidad tras '@' debe ser un número de 0 a 100.");
                    item = item.Substring(0, at);
                }
                var p = item.Split(':').Select(s => s.Trim()).ToArray();
                var e = new ParsedEffect { Chance = chance, Conditions = conditions, Shared = shared };
                switch (p[0].ToLowerInvariant())
                {
                    case "estado": case "estado_propio":
                        if (p.Length != 2 || p[1].Length == 0) throw new CsvCellException($"'{raw}': usa estado:idDelEstado (ej. estado:burn@10).");
                        e.Kind = MoveEffectKind.InflictStatus; e.Status = p[1];
                        e.Target = p[0].EndsWith("propio") ? EffectTarget.Self : EffectTarget.Opponent;
                        break;
                    case "drenar": e.Kind = MoveEffectKind.Drain; e.Target = EffectTarget.Self; e.Amount = Amount(p, raw); break;
                    case "retroceso": e.Kind = MoveEffectKind.Recoil; e.Target = EffectTarget.Self; e.Amount = Amount(p, raw); break;
                    case "retroceso_ps": e.Kind = MoveEffectKind.RecoilMaxHp; e.Target = EffectTarget.Self; e.Amount = Amount(p, raw); break;
                    case "curar": e.Kind = MoveEffectKind.Heal; e.Target = EffectTarget.Self; e.Amount = Amount(p, raw); break;
                    case "curar_rival": e.Kind = MoveEffectKind.Heal; e.Target = EffectTarget.Opponent; e.Amount = Amount(p, raw); break;
                    case "curar_estado": case "curar_estado_rival":
                        e.Kind = MoveEffectKind.CureStatus;
                        e.Target = p[0].EndsWith("rival") ? EffectTarget.Opponent : EffectTarget.Self;
                        e.Status = p.Length > 1 ? p[1] : "";
                        break;
                    case "clima":
                        if (p.Length < 2 || p[1].Length == 0) throw new CsvCellException($"'{raw}': usa clima:idDelClima (ej. clima:rain o clima:rain:8).");
                        e.Kind = MoveEffectKind.SetWeather; e.Target = EffectTarget.Self; e.Weather = p[1];
                        if (p.Length > 2 && (!CsvTable.TryInt(p[2], out e.WeatherTurns) || e.WeatherTurns < 0))
                            throw new CsvCellException($"'{raw}': los turnos del clima deben ser un número (ej. clima:rain:8).");
                        break;
                    case "amedrentar": e.Kind = MoveEffectKind.Flinch; break;
                    case "trampa":
                        if (p.Length != 2 || p[1].Length == 0) throw new CsvCellException($"'{raw}': usa trampa:idDeLaTrampa (ej. trampa:spikes).");
                        e.Kind = MoveEffectKind.SetHazard; e.Target = EffectTarget.Opponent; e.Hazard = p[1];
                        break;
                    case "quitar_trampas": case "quitar_trampas_rival":
                        e.Kind = MoveEffectKind.ClearHazards;
                        e.Target = p[0].EndsWith("rival") ? EffectTarget.Opponent : EffectTarget.Self;
                        e.Hazard = p.Length > 1 ? p[1] : "";
                        break;
                    case "forzar_cambio": e.Kind = MoveEffectKind.ForceSwitch; break;
                    // --- Cierre del combate ---
                    case "lado": case "lado_rival":
                        if (p.Length != 2 || p[1].Length == 0) throw new CsvCellException($"'{raw}': usa lado:idDelEfecto (ej. lado:reflect).");
                        e.Kind = MoveEffectKind.SetSideCondition; e.Side = p[1];
                        e.Target = p[0].EndsWith("rival") ? EffectTarget.Opponent : EffectTarget.Self;
                        break;
                    case "reiniciar_etapas": case "reiniciar_etapas_rival":
                        e.Kind = MoveEffectKind.ResetStages; e.Target = p[0].EndsWith("rival") ? EffectTarget.Opponent : EffectTarget.Self; break;
                    case "foco":
                        e.Kind = MoveEffectKind.CritBoost; e.Target = EffectTarget.Self; e.Stages = OptInt(p, 1, 2, raw); break;
                    case "sustituto":
                        e.Kind = MoveEffectKind.Substitute; e.Target = EffectTarget.Self; e.Amount = p.Length > 1 ? Amount(p, raw) : 25f; break;
                    case "anular": e.Kind = MoveEffectKind.DisableMove; e.Turns = OptInt(p, 1, 0, raw); break;
                    case "otra_vez": e.Kind = MoveEffectKind.Encore; e.Turns = OptInt(p, 1, 0, raw); break;
                    case "desenfreno":
                        e.Kind = MoveEffectKind.Rampage; e.Target = EffectTarget.Self; e.Turns = OptInt(p, 1, 0, raw);
                        e.Status = p.Length > 2 ? p[2] : "";
                        break;
                    case "furia":
                        e.Kind = MoveEffectKind.Rage; e.Target = EffectTarget.Self;
                        e.Stat = p.Length > 1 ? p[1] : "attack";
                        e.Stages = p.Length > 2 ? OptInt(new[] { p[0], p[2].Replace("+", "") }, 1, 1, raw) : 1;
                        break;
                    case "metronomo": e.Kind = MoveEffectKind.CallRandomMove; e.Target = EffectTarget.Self; break;
                    case "espejo": e.Kind = MoveEffectKind.CallLastMove; break;
                    case "mimetico": e.Kind = MoveEffectKind.CopyLastMove; break;
                    case "transformarse": e.Kind = MoveEffectKind.Transform; break;
                    case "cambiar_tipo": e.Kind = MoveEffectKind.ChangeType; e.Target = EffectTarget.Self; e.TypeId = p.Length > 1 ? p[1] : ""; break;
                    case "cambio_propio": e.Kind = MoveEffectKind.SwitchSelf; e.Target = EffectTarget.Self; break;
                    case "relevo": e.Kind = MoveEffectKind.SwitchSelf; e.Target = EffectTarget.Self; e.Stages = 1; break;
                    case "teletransporte": e.Kind = MoveEffectKind.Teleport; e.Target = EffectTarget.Self; break;
                    case "stat": case "stat_propio":
                        if (p.Length != 3 || p[1].Length == 0 || !CsvTable.TryInt(p[2].Replace("+", ""), out int stages))
                            throw new CsvCellException($"'{raw}': usa stat:idDeStat:etapas (ej. stat:attack:-1 o stat_propio:speed:+2).");
                        e.Kind = MoveEffectKind.ChangeStatStage; e.Stat = p[1]; e.Stages = stages;
                        e.Target = p[0].EndsWith("propio") ? EffectTarget.Self : EffectTarget.Opponent;
                        break;
                    // --- 3.ª y 4.ª generación ---
                    case "quitar_lado": e.Kind = MoveEffectKind.ClearSideConditions; e.Text = p.Length > 1 ? string.Join("|", p.Skip(1)) : ""; break;
                    case "quitar_objeto": e.Kind = MoveEffectKind.RemoveItem; break;
                    case "robar_objeto": e.Kind = MoveEffectKind.StealItem; break;
                    case "cambiar_objetos": e.Kind = MoveEffectKind.SwapItems; break;
                    case "comer_baya": e.Kind = MoveEffectKind.ConsumeTargetBerry; break;
                    case "reciclar": e.Kind = MoveEffectKind.RestoreItem; e.Target = EffectTarget.Self; break;
                    case "copiar_habilidad": e.Kind = MoveEffectKind.CopyAbility; break;
                    case "cambiar_habilidades": e.Kind = MoveEffectKind.SwapAbility; break;
                    case "poner_habilidad":
                        if (p.Length != 2 || p[1].Length == 0) throw new CsvCellException($"'{raw}': usa poner_habilidad:idDeLaHabilidad (ej. poner_habilidad:insomnia).");
                        e.Kind = MoveEffectKind.SetAbility; e.Text = p[1]; break;
                    case "deseo": e.Kind = MoveEffectKind.DelayedHeal; e.Target = EffectTarget.Self; e.Turns = OptInt(p, 1, 1, raw); e.Amount = p.Length > 2 && CsvTable.TryNumber(p[2], out var dh) ? dh : 50f; break;
                    case "premonicion": e.Kind = MoveEffectKind.DelayedDamage; e.Turns = OptInt(p, 1, 2, raw); break;
                    case "reserva": e.Kind = MoveEffectKind.Stockpile; e.Target = EffectTarget.Self; break;
                    case "usar_reserva": e.Kind = MoveEffectKind.UseStockpile; e.Target = EffectTarget.Self; e.Amount = p.Length > 1 && p[1].StartsWith("cur") ? 1f : 0f; break;
                    case "pasar_estado": e.Kind = MoveEffectKind.TransferStatus; break;
                    case "stat_al_azar": e.Kind = MoveEffectKind.RaiseRandomStat; e.Target = EffectTarget.Self; e.Stages = OptInt(p, 1, 2, raw); break;
                    case "cambiar_stats": e.Kind = MoveEffectKind.SwapOwnStats; e.Target = EffectTarget.Self; e.Text = p.Length > 1 ? p[1] : "attack,defense"; break;
                    case "intercambiar_etapas": e.Kind = MoveEffectKind.SwapStages; e.Text = p.Length > 1 ? p[1] : ""; break;
                    case "copiar_etapas": e.Kind = MoveEffectKind.CopyStages; break;
                    case "debilitarse": e.Kind = MoveEffectKind.SelfFaint; e.Target = EffectTarget.Self; break;
                    case "deseo_cura": e.Kind = MoveEffectKind.HealNextSwitchIn; e.Target = EffectTarget.Self; break;
                    case "dividir_dolor": e.Kind = MoveEffectKind.PainSplit; break;
                    case "ayuda": e.Kind = MoveEffectKind.CallTeamMove; e.Target = EffectTarget.Self; break;
                    case "usar":
                        if (p.Length != 2 || p[1].Length == 0) throw new CsvCellException($"'{raw}': usa usar:idDelMovimiento (ej. usar:tri_attack).");
                        e.Kind = MoveEffectKind.CallMove; e.Text = p[1]; break;
                    case "yo_primero": e.Kind = MoveEffectKind.CallTargetMove; break;
                    case "curar_equipo": e.Kind = MoveEffectKind.TeamCureStatus; e.Target = EffectTarget.Self; break;
                    case "sonambulo": e.Kind = MoveEffectKind.CallOwnMove; e.Target = EffectTarget.Self; break;
                    // --- 5.ª y 6.ª generación ---
                    case "dar_habilidad": e.Kind = MoveEffectKind.GiveAbility; break;
                    case "copiar_tipos": e.Kind = MoveEffectKind.CopyTypes; break;
                    case "dar_objeto": e.Kind = MoveEffectKind.GiveItem; break;
                    case "anadir_tipo": case "añadir_tipo":
                        if (p.Length != 2 || p[1].Length == 0) throw new CsvCellException($"'{raw}': usa añadir_tipo:idDelTipo (ej. añadir_tipo:ghost).");
                        e.Kind = MoveEffectKind.AddType; e.TypeId = p[1]; break;
                    case "invertir_etapas": e.Kind = MoveEffectKind.InvertStages; break;
                    case "cambiar_tipo_rival": e.Kind = MoveEffectKind.ChangeType; e.Target = EffectTarget.Opponent; e.TypeId = p.Length > 1 ? p[1] : ""; break;
                    default:
                        throw new CsvCellException($"'{raw}': efecto desconocido '{p[0]}'. Usa: estado, estado_propio, drenar, retroceso, retroceso_ps, curar, curar_rival, curar_estado, curar_estado_rival, clima, stat, stat_propio, amedrentar, trampa, quitar_trampas, quitar_trampas_rival, forzar_cambio, lado, lado_rival, reiniciar_etapas, reiniciar_etapas_rival, foco, sustituto, anular, otra_vez, desenfreno, furia, metronomo, espejo, mimetico, transformarse, cambiar_tipo, cambio_propio, relevo, teletransporte, quitar_lado, quitar_objeto, robar_objeto, cambiar_objetos, comer_baya, reciclar, copiar_habilidad, cambiar_habilidades, poner_habilidad, deseo, premonicion, reserva, usar_reserva, pasar_estado, stat_al_azar, cambiar_stats, intercambiar_etapas, copiar_etapas, debilitarse, deseo_cura, dividir_dolor, ayuda, usar, yo_primero, curar_equipo.");
                }
                list.Add(e);
            }
            return list;
        }

        // Número opcional en la posición 'i' (si falta, 'fallback').
        private static int OptInt(string[] p, int i, int fallback, string raw)
        {
            if (p.Length <= i || p[i].Length == 0) return fallback;
            if (!CsvTable.TryInt(p[i].Replace("+", ""), out int v)) throw new CsvCellException($"'{raw}': '{p[i]}' debería ser un número.");
            return v;
        }

        private static float Amount(string[] p, string raw)
        {
            if (p.Length != 2 || !CsvTable.TryNumber(p[1], out float v))
                throw new CsvCellException($"'{raw}': falta el porcentaje (ej. {p[0]}:50).");
            return v;
        }

        public static string FormatEffects(IEnumerable<ParsedEffect> effects)
        {
            string C(float c) => c >= 100f ? "" : "@" + c.ToString("0.##", CultureInfo.InvariantCulture);
            string N(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
            return JoinList(effects.Select(e =>
            {
                string text = FormatOne(e);
                if (text.Length == 0) return text;
                if (e.Shared) text = "&" + text;
                if (e.Conditions != null && e.Conditions.Count > 0) text += " [si " + ConditionText.FormatAll(e.Conditions) + "]";
                return text;
            }));

            string FormatOne(ParsedEffect e)
            {
                bool self = e.Target == EffectTarget.Self;
                switch (e.Kind)
                {
                    case MoveEffectKind.InflictStatus: return $"{(self ? "estado_propio" : "estado")}:{e.Status}{C(e.Chance)}";
                    case MoveEffectKind.Drain: return $"drenar:{N(e.Amount)}{C(e.Chance)}";
                    case MoveEffectKind.Recoil: return $"retroceso:{N(e.Amount)}{C(e.Chance)}";
                    case MoveEffectKind.RecoilMaxHp: return $"retroceso_ps:{N(e.Amount)}{C(e.Chance)}";
                    case MoveEffectKind.HealSelf: return $"curar:{N(e.Amount)}{C(e.Chance)}";
                    case MoveEffectKind.Heal: return $"{(self ? "curar" : "curar_rival")}:{N(e.Amount)}{C(e.Chance)}";
                    case MoveEffectKind.CureStatus:
                        return $"{(self ? "curar_estado" : "curar_estado_rival")}{(string.IsNullOrEmpty(e.Status) ? "" : ":" + e.Status)}{C(e.Chance)}";
                    case MoveEffectKind.SetWeather: return $"clima:{e.Weather}{(e.WeatherTurns > 0 ? ":" + e.WeatherTurns : "")}{C(e.Chance)}";
                    case MoveEffectKind.Flinch: return $"amedrentar{C(e.Chance)}";
                    case MoveEffectKind.SetHazard: return $"trampa:{e.Hazard}{C(e.Chance)}";
                    case MoveEffectKind.ClearHazards: return $"{(self ? "quitar_trampas" : "quitar_trampas_rival")}{(string.IsNullOrEmpty(e.Hazard) ? "" : ":" + e.Hazard)}{C(e.Chance)}";
                    case MoveEffectKind.ForceSwitch: return $"forzar_cambio{C(e.Chance)}";
                    case MoveEffectKind.ChangeStatStage: return $"{(self ? "stat_propio" : "stat")}:{e.Stat}:{(e.Stages > 0 ? "+" : "")}{e.Stages}{C(e.Chance)}";
                    case MoveEffectKind.SetSideCondition: return $"{(self ? "lado" : "lado_rival")}:{e.Side}{C(e.Chance)}";
                    case MoveEffectKind.ResetStages: return $"{(self ? "reiniciar_etapas" : "reiniciar_etapas_rival")}{C(e.Chance)}";
                    case MoveEffectKind.CritBoost: return $"foco{(e.Stages > 0 && e.Stages != 2 ? ":" + e.Stages : "")}{C(e.Chance)}";
                    case MoveEffectKind.Substitute: return $"sustituto{(e.Amount > 0 && e.Amount != 25f ? ":" + N(e.Amount) : "")}{C(e.Chance)}";
                    case MoveEffectKind.DisableMove: return $"anular{(e.Turns > 0 ? ":" + e.Turns : "")}{C(e.Chance)}";
                    case MoveEffectKind.Encore: return $"otra_vez{(e.Turns > 0 ? ":" + e.Turns : "")}{C(e.Chance)}";
                    case MoveEffectKind.Rampage:
                        return $"desenfreno{(e.Turns > 0 || !string.IsNullOrEmpty(e.Status) ? ":" + e.Turns : "")}{(string.IsNullOrEmpty(e.Status) ? "" : ":" + e.Status)}{C(e.Chance)}";
                    case MoveEffectKind.Rage:
                        return $"furia{(string.IsNullOrEmpty(e.Stat) || e.Stat == "attack" && e.Stages == 1 ? "" : $":{e.Stat}:{(e.Stages > 0 ? "+" : "")}{e.Stages}")}{C(e.Chance)}";
                    case MoveEffectKind.CallRandomMove: return $"metronomo{C(e.Chance)}";
                    case MoveEffectKind.CallLastMove: return $"espejo{C(e.Chance)}";
                    case MoveEffectKind.CopyLastMove: return $"mimetico{C(e.Chance)}";
                    case MoveEffectKind.Transform: return $"transformarse{C(e.Chance)}";
                    case MoveEffectKind.ChangeType: return $"{(e.Target == EffectTarget.Opponent ? "cambiar_tipo_rival" : "cambiar_tipo")}{(string.IsNullOrEmpty(e.TypeId) ? "" : ":" + e.TypeId)}{C(e.Chance)}";
                    case MoveEffectKind.SwitchSelf: return $"{(e.Stages > 0 ? "relevo" : "cambio_propio")}{C(e.Chance)}";
                    case MoveEffectKind.Teleport: return $"teletransporte{C(e.Chance)}";
                    case MoveEffectKind.ClearSideConditions: return $"quitar_lado{(string.IsNullOrEmpty(e.Text) ? "" : ":" + e.Text.Replace("|", ":"))}{C(e.Chance)}";
                    case MoveEffectKind.RemoveItem: return $"quitar_objeto{C(e.Chance)}";
                    case MoveEffectKind.StealItem: return $"robar_objeto{C(e.Chance)}";
                    case MoveEffectKind.SwapItems: return $"cambiar_objetos{C(e.Chance)}";
                    case MoveEffectKind.ConsumeTargetBerry: return $"comer_baya{C(e.Chance)}";
                    case MoveEffectKind.RestoreItem: return $"reciclar{C(e.Chance)}";
                    case MoveEffectKind.CopyAbility: return $"copiar_habilidad{C(e.Chance)}";
                    case MoveEffectKind.SwapAbility: return $"cambiar_habilidades{C(e.Chance)}";
                    case MoveEffectKind.SetAbility: return $"poner_habilidad:{e.Text}{C(e.Chance)}";
                    case MoveEffectKind.DelayedHeal: return $"deseo:{(e.Turns > 0 ? e.Turns : 1)}:{N(e.Amount > 0 ? e.Amount : 50)}{C(e.Chance)}";
                    case MoveEffectKind.DelayedDamage: return $"premonicion:{(e.Turns > 0 ? e.Turns : 2)}{C(e.Chance)}";
                    case MoveEffectKind.Stockpile: return $"reserva{C(e.Chance)}";
                    case MoveEffectKind.UseStockpile: return $"usar_reserva{(e.Amount > 0 ? ":curar" : "")}{C(e.Chance)}";
                    case MoveEffectKind.TransferStatus: return $"pasar_estado{C(e.Chance)}";
                    case MoveEffectKind.RaiseRandomStat: return $"stat_al_azar:{(e.Stages != 0 ? e.Stages : 2)}{C(e.Chance)}";
                    case MoveEffectKind.SwapOwnStats: return $"cambiar_stats:{(string.IsNullOrEmpty(e.Text) ? "attack,defense" : e.Text)}{C(e.Chance)}";
                    case MoveEffectKind.SwapStages: return $"intercambiar_etapas{(string.IsNullOrEmpty(e.Text) ? "" : ":" + e.Text)}{C(e.Chance)}";
                    case MoveEffectKind.CopyStages: return $"copiar_etapas{C(e.Chance)}";
                    case MoveEffectKind.SelfFaint: return $"debilitarse{C(e.Chance)}";
                    case MoveEffectKind.HealNextSwitchIn: return $"deseo_cura{C(e.Chance)}";
                    case MoveEffectKind.PainSplit: return $"dividir_dolor{C(e.Chance)}";
                    case MoveEffectKind.CallTeamMove: return $"ayuda{C(e.Chance)}";
                    case MoveEffectKind.CallMove: return $"usar:{e.Text}{C(e.Chance)}";
                    case MoveEffectKind.CallTargetMove: return $"yo_primero{C(e.Chance)}";
                    case MoveEffectKind.TeamCureStatus: return $"curar_equipo{C(e.Chance)}";
                    case MoveEffectKind.CallOwnMove: return $"sonambulo{C(e.Chance)}";
                    case MoveEffectKind.GiveAbility: return $"dar_habilidad{C(e.Chance)}";
                    case MoveEffectKind.CopyTypes: return $"copiar_tipos{C(e.Chance)}";
                    case MoveEffectKind.GiveItem: return $"dar_objeto{C(e.Chance)}";
                    case MoveEffectKind.AddType: return $"añadir_tipo:{e.TypeId}{C(e.Chance)}";
                    case MoveEffectKind.InvertStages: return $"invertir_etapas{C(e.Chance)}";
                    default: return "";
                }
            }
        }

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
