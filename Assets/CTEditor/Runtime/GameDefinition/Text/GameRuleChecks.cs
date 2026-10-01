using System;
using System.Collections.Generic;
using System.Linq;
using CTEditor.Content;
using CTEditor.GameDefinition.Domain.Abilities;
using CTEditor.GameDefinition.Domain.Effects;
using CTEditor.GameDefinition.Domain.Moves;

namespace CTEditor.GameDefinition.Text
{
    /// <summary>
    /// The checks of the Unity content validator, over the sheets: effects that cannot be read or point to things that do
    /// not exist, moves that would do nothing, evolutions without their item, egg groups, repeated Pokédex numbers, teams
    /// beyond the rules (members, level, moves, EVs), bags with items that cannot be used in battle, weathers, hazards and
    /// side effects that do nothing... Errors stop things working; warnings only say «look at this».
    /// </summary>
    public static class GameRuleChecks
    {
        private static bool _registered;

        /// <summary>Adds these checks to ContentChecks (the Problems window and the data windows).</summary>
        public static void Register()
        {
            if (_registered) return;
            _registered = true;
            ContentChecks.Register(Check);
        }

        private static ContentIssue E(ContentTable t, ContentRecord r, int row, string col, string text) =>
            new ContentIssue(ContentIssueLevel.Error, t.Schema.Key, t.IdOf(r), col, row, $"{t.Schema.File}, fila {row}: {text}");
        private static ContentIssue W(ContentTable t, ContentRecord r, int row, string col, string text) =>
            new ContentIssue(ContentIssueLevel.Warning, t.Schema.Key, t.IdOf(r), col, row, $"{t.Schema.File}, fila {row}: {text}");

        public static IEnumerable<ContentIssue> Check(ContentDatabase db, ContentTable t)
        {
            var list = new List<ContentIssue>();
            var rules = db.Table(ContentSchemas.Rules).Records.FirstOrDefault();
            int Rule(string col, int fallback) => rules != null && int.TryParse(rules[col], out var v) && v > 0 ? v : fallback;
            for (int i = 0; i < t.Records.Count; i++)
            {
                var r = t.Records[i];
                int row = i + 2;
                string name = t.NameOf(r);
                switch (t.Schema.Key)
                {
                    case ContentSchemas.Species: Species(db, t, r, row, name, list); break;
                    case ContentSchemas.Moves: Move(db, t, r, row, name, list); break;
                    case ContentSchemas.Items: Blocks(db, t, r, row, name, false, list); break;
                    case ContentSchemas.Abilities: Blocks(db, t, r, row, name, true, list); break;
                    case ContentSchemas.Natures:
                        if (ContentChecks.TryNumber(r["porcentaje"], out var pct) && pct >= 100) list.Add(W(t, r, row, "porcentaje", $"«{name}» tiene {pct} %: la estadística perjudicada quedará en 0."));
                        break;
                    case ContentSchemas.Trainers:
                    case ContentSchemas.Teams:
                        Team(db, t, r, row, name, Rule("equipo_max", 6), Rule("nivel_max", 100), Rule("movimientos_max", 4), Rule("ev_max_stat", 252), Rule("ev_max_total", 510), list);
                        break;
                    case ContentSchemas.Weathers:
                        if (r["potencia_por_tipo"].Trim().Length == 0 && !ContentChecks.TryNumber(r["daño_por_turno"], out var dmg)) list.Add(W(t, r, row, null, $"El clima «{name}» no hace nada (ni potencia tipos ni daña)."));
                        break;
                    case ContentSchemas.Hazards:
                        if (r["daño_por_capa"].Trim().Length == 0 && r["estado_por_capa"].Trim().Length == 0 && r["estadistica"].Trim().Length == 0)
                            list.Add(W(t, r, row, null, $"La trampa «{name}» no hace nada (sin daño, estado ni estadística)."));
                        break;
                    case ContentSchemas.SideEffects:
                        bool any = new[] { "mult_fisico", "mult_especial", "mult_velocidad" }.Any(c => ContentChecks.TryNumber(r[c], out var m) && Math.Abs(m - 1) > 1e-6)
                                   || ContentChecks.IsYes(r["sin_bajadas"]) || ContentChecks.IsYes(r["sin_estados"]);
                        if (!any) list.Add(W(t, r, row, null, $"El efecto de lado «{name}» no hace nada."));
                        break;
                    case ContentSchemas.Rules:
                        if (int.TryParse(r["ev_max_total"], out var tot) && int.TryParse(r["ev_max_stat"], out var per) && tot < per)
                            list.Add(W(t, r, row, "ev_max_total", $"El tope TOTAL de EVs ({tot}) es menor que el tope por estadística ({per})."));
                        if (r["forcejeo"].Trim().Length > 0 && !db.Has(ContentSchemas.Moves, r["forcejeo"].Trim()))
                            list.Add(W(t, r, row, "forcejeo", $"Forcejeo usa «{r["forcejeo"]}», que no existe: sin PP se perderá el turno."));
                        break;
                }
            }
            if (t.Schema.Key == ContentSchemas.Species)
                foreach (var g in t.Records.Select((r, i) => (r, row: i + 2)).Where(x => x.r["forma_de"].Trim().Length == 0 && int.TryParse(x.r["numero"], out var n) && n > 0)
                             .GroupBy(x => x.r["numero"].Trim()).Where(g => g.Count() > 1))
                    list.Add(W(t, g.First().r, g.First().row, "numero", $"El número de Pokédex {g.Key} lo tienen {string.Join(", ", g.Select(x => t.NameOf(x.r)))}."));
            return list;
        }

        private static void Species(ContentDatabase db, ContentTable t, ContentRecord r, int row, string name, List<ContentIssue> list)
        {
            var groups = r["grupos_huevo"].Split('|', ',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            if (groups.Count > 2) list.Add(W(t, r, row, "grupos_huevo", $"«{name}» tiene {groups.Count} grupos huevo: en los juegos son como mucho 2."));
            if (groups.Count != groups.Distinct().Count()) list.Add(W(t, r, row, "grupos_huevo", $"«{name}» repite un grupo huevo."));
            foreach (var e in r["evoluciona"].Split('|').Where(x => x.Trim().Length > 0))
            {
                int at = e.IndexOf('@');
                var target = (at >= 0 ? e.Substring(0, at) : e).Trim();
                var cond = at >= 0 ? e.Substring(at + 1).Trim() : "";
                if (cond == "objeto" || cond == "objeto:") list.Add(E(t, r, row, "evoluciona", $"«{name}» evoluciona a «{target}» con un objeto, pero no dice cuál."));
                if (int.TryParse(cond.Split('+')[0], out var lvl) && lvl < 1) list.Add(W(t, r, row, "evoluciona", $"«{name}» evoluciona a «{target}» con nivel {lvl} (debe ser 1 o más)."));
                if (cond == "subir") list.Add(W(t, r, row, "evoluciona", $"«{name}» evoluciona a «{target}» al subir de nivel sin condiciones: lo hará al subir cualquier nivel."));
                if (cond.Contains("hora:dia") && cond.Contains("hora:noche")) list.Add(W(t, r, row, "evoluciona", $"«{name}» → «{target}» pide de día y de noche a la vez."));
            }
            if (r["aprende"].Trim().Length == 0 && r["forma_de"].Trim().Length == 0)
                list.Add(W(t, r, row, "aprende", $"«{name}» no aprende ningún movimiento por nivel: en los combates no tendría ataques."));
        }

        private static void Move(ContentDatabase db, ContentTable t, ContentRecord r, int row, string name, List<ContentIssue> list)
        {
            var cat = r["categoria"].Trim().ToLowerInvariant();
            bool special = r["daño_especial"].Trim().Length > 0;
            bool power = int.TryParse(r["potencia"], out var pw) && pw > 0;
            if (cat == "estado" && special) list.Add(W(t, r, row, "daño_especial", $"«{name}» tiene daño especial pero es de estado: no hará daño (ponlo físico o especial)."));
            if ((cat == "fisico" || cat == "especial") && !power && !special && r["formula_potencia"].Trim().Length == 0)
                list.Add(W(t, r, row, "potencia", $"«{name}» es de daño pero tiene potencia 0: no hará nada."));
            var hits = r["golpes"].Split('-');
            if (hits.Length == 2 && int.TryParse(hits[0], out var lo) && int.TryParse(hits[1], out var hi) && hi < lo)
                list.Add(W(t, r, row, "golpes", $"«{name}» golpea {lo}-{hi} veces: el máximo es menor que el mínimo."));
            foreach (var col in new[] { "efectos", "efecto_z" })
            {
                if (r[col].Trim().Length == 0) continue;
                List<ParsedEffect> effects;
                try { effects = MoveEffectText.Parse(r[col]); }
                catch (FormatException e) { list.Add(E(t, r, row, col, $"«{name}»: {e.Message}")); continue; }
                foreach (var e in effects)
                {
                    void Need(string category, string id, string what, bool required)
                    {
                        if (string.IsNullOrWhiteSpace(id)) { if (required) list.Add(W(t, r, row, col, $"«{name}» tiene un efecto «{MoveEffectText.Info(e.Kind).Label}» sin {what} (se ignorará).")); return; }
                        if (!db.Has(category, id)) list.Add(E(t, r, row, col, $"«{name}» usa {what} «{id}», que no existe."));
                    }
                    switch (e.Kind)
                    {
                        case MoveEffectKind.InflictStatus: Need(ContentSchemas.Statuses, e.Status, "el estado", true); break;
                        case MoveEffectKind.CureStatus: case MoveEffectKind.Rampage: Need(ContentSchemas.Statuses, e.Status, "el estado", false); break;
                        case MoveEffectKind.SetWeather: Need(ContentSchemas.Weathers, e.Weather, "el clima", true); break;
                        case MoveEffectKind.SetHazard: Need(ContentSchemas.Hazards, e.Hazard, "la trampa", true); break;
                        case MoveEffectKind.ClearHazards: Need(ContentSchemas.Hazards, e.Hazard, "la trampa", false); break;
                        case MoveEffectKind.SetSideCondition: Need(ContentSchemas.SideEffects, e.Side, "el efecto de lado", true); break;
                        case MoveEffectKind.ChangeType: case MoveEffectKind.AddType: Need(ContentSchemas.Types, e.TypeId, "el tipo", e.Kind == MoveEffectKind.AddType); break;
                        case MoveEffectKind.SetAbility: Need(ContentSchemas.Abilities, e.Text, "la habilidad", true); break;
                        case MoveEffectKind.CallMove: Need(ContentSchemas.Moves, e.Text, "el movimiento", true); break;
                        case MoveEffectKind.ChangeStatStage:
                            if (e.Stages == 0) list.Add(W(t, r, row, col, $"«{name}» cambia {e.Stat} en 0 etapas (sin efecto)."));
                            break;
                    }
                }
            }
        }

        private static void Blocks(ContentDatabase db, ContentTable t, ContentRecord r, int row, string name, bool ability, List<ContentIssue> list)
        {
            if (r["efectos"].Trim().Length == 0) return;
            List<EffectBlock> blocks;
            try { blocks = EffectText.Parse(r["efectos"]); }
            catch (FormatException e) { list.Add(E(t, r, row, "efectos", $"«{name}» tiene un efecto que no se puede leer: {e.Message}")); return; }
            foreach (var b in blocks)
            {
                if (!(ability ? AbilityEffects.IsSupported(b) : EffectRules.IsSupported(b)))
                    list.Add(W(t, r, row, "efectos", $"«{name}»: «{EffectText.Label(b.Action)}» con «{EffectText.Label(b.Trigger, ability)}» se guarda, pero el motor aún no lo aplica."));
                string category = EffectText.RefOf(b.Action) switch
                {
                    EffectRefKind.Type or EffectRefKind.TypeList => ContentSchemas.Types,
                    EffectRefKind.Status or EffectRefKind.StatusList => ContentSchemas.Statuses,
                    EffectRefKind.Weather => ContentSchemas.Weathers,
                    EffectRefKind.WeatherList => ContentSchemas.Weathers,
                    EffectRefKind.Move => ContentSchemas.Moves,
                    EffectRefKind.SideCondition => ContentSchemas.SideEffects,
                    _ => null,
                };
                if (category == null) continue;
                if (EffectText.RefOf(b.Action) is EffectRefKind.Type or EffectRefKind.Status or EffectRefKind.Weather or EffectRefKind.Move or EffectRefKind.SideCondition
                    && b.Ref.Length == 0 && b.Action != EffectAction.ExtendWeather)
                    list.Add(W(t, r, row, "efectos", $"«{name}»: «{EffectText.Label(b.Action)}» no dice cuál (falta elegirlo)."));
                foreach (var id in b.RefList.Where(x => x != "*"))
                    if (!db.Has(category, id)) list.Add(E(t, r, row, "efectos", $"«{name}» usa «{id}», que no existe."));
            }
        }

        private static void Team(ContentDatabase db, ContentTable t, ContentRecord r, int row, string name, int maxParty, int levelCap, int maxMoves, int evStat, int evTotal, List<ContentIssue> list)
        {
            var members = TeamFormat.Parse(r["equipo"]);
            var species = db.Table(ContentSchemas.Species);
            if (members.Count == 0) list.Add(E(t, r, row, "equipo", $"«{name}» no tiene ningún miembro."));
            if (members.Count > maxParty) list.Add(W(t, r, row, "equipo", $"«{name}» tiene {members.Count} miembros; las reglas permiten {maxParty} (los demás no combaten)."));
            foreach (var m in members)
            {
                if (m.Raw != null) { list.Add(E(t, r, row, "equipo", $"«{name}»: «{m.Raw.Trim()}» no es un miembro válido (especie@nivel...).")); continue; }
                var sp = species.Find(m.Species);
                string mon = sp != null ? species.NameOf(sp) : m.Species;
                if (m.Level > levelCap) list.Add(W(t, r, row, "equipo", $"«{name}»: {mon} está a nivel {m.Level}, por encima del máximo ({levelCap})."));
                if (m.Moves.Count > maxMoves) list.Add(W(t, r, row, "equipo", $"«{name}»: {mon} tiene {m.Moves.Count} movimientos; solo se usan los {maxMoves} primeros."));
                if (sp != null && m.Moves.Count == 0)
                {
                    bool learns = sp["aprende"].Split('|').Any(e => int.TryParse(e.Split(':')[0], out var l) && l <= m.Level);
                    if (!learns) list.Add(E(t, r, row, "equipo", $"«{name}»: {mon} no tiene movimientos elegidos ni aprende ninguno hasta el nivel {m.Level}: no podría luchar."));
                }
                if (m.Evs.Length > 0)
                {
                    var ev = StatNamesOf(m.Evs);
                    if (ev.Max() > evStat || ev.Sum() > evTotal) list.Add(W(t, r, row, "equipo", $"«{name}»: los EVs de {mon} pasan de los topes ({evStat} por estadística, {evTotal} en total): se recortan."));
                }
            }
            foreach (var (item, _) in r["mochila"].Split('|').Select(x => x.Split(':')).Where(x => x[0].Trim().Length > 0).Select(x => (x[0].Trim(), x.Length > 1 ? x[1] : "1")))
            {
                var it = db.Table(ContentSchemas.Items).Find(item);
                if (it != null && it["en_combate"].Trim().Length > 0 && !ContentChecks.IsYes(it["en_combate"]))
                    list.Add(W(t, r, row, "mochila", $"«{name}» lleva «{db.Table(ContentSchemas.Items).NameOf(it)}», que no se puede usar en combate: nunca lo usará."));
            }
            if (t.Schema.Key == ContentSchemas.Trainers && int.TryParse(r["dinero_base"], out var money) && money == 0)
                list.Add(W(t, r, row, "dinero_base", $"«{name}» no da dinero al perder (dinero base 0)."));
        }

        private static int[] StatNamesOf(string spread) => StatNames.ParseSpread(spread);
    }
}
