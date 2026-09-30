using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.Abstractions;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Encounters;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Trainers;
using CTEditor.Party.Domain;
using System.Linq;
using CTEditor.GameDefinition.Domain.Items;
using CTEditor.GameDefinition.Domain.Weather;
using SpeciesDef = CTEditor.GameDefinition.Domain.Species.Species;

namespace CTEditor.Adventure.Domain
{
    /// <summary>
    /// La FÁBRICA de equipos: convierte las recetas del autor (TeamMemberSpec) en monstruos reales.
    /// La MISMA lógica arma el equipo del jugador (equipo prearmado o inicial), el de un entrenador
    /// rival y un salvaje. Así, lo que funciona para el jugador funciona igual para los NPC.
    ///
    /// No lanza por contenido roto: si una especie no existe, ese miembro se salta y se anota en
    /// 'problems' (la interfaz puede mostrarlo). El azar (IVs, naturaleza, niveles) se inyecta.
    /// </summary>
    public static class TeamBuilder
    {
        /// <summary>Crea UN monstruo a partir de su receta. Null si la especie no existe.</summary>
        public static MonsterInstance Build(TeamMemberSpec spec, Id<MonsterInstance> id, GameData data, IRng rng,
            List<string> problems = null, MovesetStyle autoMoves = MovesetStyle.Classic, AiProfile profile = null)
        {
            if (spec == null || data == null) return null;
            if (!data.Species.TryGet(spec.Species, out var species))
            {
                problems?.Add($"La especie '{spec.Species.Value}' no existe: se omitió ese miembro.");
                return null;
            }

            // Movimientos elegidos (solo los que existen). Vacío = los de su nivel.
            List<Id<Move>> moves = null;
            if (spec.Moves.Count > 0)
            {
                moves = new List<Id<Move>>();
                foreach (var m in spec.Moves)
                {
                    if (data.Moves.Contains(m) && !moves.Contains(m)) moves.Add(m);
                    else if (!data.Moves.Contains(m)) problems?.Add($"El movimiento '{m.Value}' no existe (en {species.DisplayName}).");
                }
                if (moves.Count == 0) moves = null;
            }
            // Sin movimientos escritos: los elige el planificador (los entrenadores listos y expertos).
            if (moves == null && autoMoves != MovesetStyle.Classic && autoMoves != MovesetStyle.ByAi)
                moves = PlanMoves(species, spec.Level, data, autoMoves, profile);

            // Naturaleza: la fijada o una al azar de las que existan (ninguna = neutra).
            Nature nature = null;
            if (spec.Nature.HasValue && !data.Natures.TryGet(spec.Nature.Value, out nature))
                problems?.Add($"La naturaleza '{spec.Nature.Value.Value}' no existe (en {species.DisplayName}).");
            if (nature == null && !spec.Nature.HasValue) nature = RandomNature(data, rng);

            var mon = MonsterFactory.Create(id, species, spec.Level, data.Ruleset, data.Growth, moves,
                data.CurveFor(species), rng, nature, spec.FixedIv);
            if (!string.IsNullOrWhiteSpace(spec.HeldItem)) mon.SetHeldItem(spec.HeldItem);
            else if (profile != null && profile.AutoHeldItems) mon.SetHeldItem(PickHeldItem(species, mon, data));
            if (!string.IsNullOrWhiteSpace(spec.Nickname)) mon.SetNickname(spec.Nickname);
            return mon;
        }

        /// <summary>Crea un monstruo de una especie a un nivel (salvaje, regalo, inicial).</summary>
        public static MonsterInstance Build(Id<SpeciesDef> species, int level, Id<MonsterInstance> id, GameData data, IRng rng,
            List<string> problems = null)
            => Build(new TeamMemberSpec(species, level), id, data, rng, problems);

        // ---------------- Partidas nuevas ----------------

        /// <summary>Partida NUEVA con un equipo prearmado: miembros, dinero y mochila del equipo.</summary>
        public static PlayerSave NewGame(TeamPreset preset, GameData data, IRng rng, string playerName = "Jugador",
            List<string> problems = null)
        {
            if (preset == null) throw new ArgumentNullException(nameof(preset));
            var save = new PlayerSave(data.Ruleset, playerName, preset.Money >= 0 ? preset.Money : (int?)null);
            foreach (var spec in preset.Members)
            {
                var mon = Build(spec, save.NewMonsterId(), data, rng, problems);
                if (mon == null) continue;
                if (save.Receive(mon, data.Rules.SendToBoxWhenFull) != StoredIn.Party)
                    problems?.Add($"{data.NameOf(mon)} no cabía en el equipo: fue al PC.");
            }
            foreach (var (itemId, qty) in preset.Items)
            {
                if (!data.TryGetItem(itemId, out _)) { problems?.Add($"El objeto '{itemId}' no existe: no se añadió a la mochila."); continue; }
                save.Bag.Add(itemId, qty);
            }
            return save;
        }

        /// <summary>
        /// Partida DESDE CERO: solo un monstruo inicial (y la mochila que quieras). Lo demás, a capturarlo.
        /// </summary>
        public static PlayerSave NewGameWithStarter(Id<SpeciesDef> starter, int level, GameData data, IRng rng,
            string playerName = "Jugador", IReadOnlyList<(string itemId, int quantity)> items = null, List<string> problems = null)
        {
            var save = new PlayerSave(data.Ruleset, playerName);
            var mon = Build(starter, level, save.NewMonsterId(), data, rng, problems);
            if (mon != null) save.Receive(mon);
            if (items != null)
                foreach (var (itemId, qty) in items)
                    if (data.TryGetItem(itemId, out _)) save.Bag.Add(itemId, qty);
                    else problems?.Add($"El objeto '{itemId}' no existe: no se añadió a la mochila.");
            return save;
        }

        // ---------------- Rivales ----------------

        /// <summary>El equipo de un ENTRENADOR (monstruos efímeros: existen solo durante el combate).</summary>
        public static List<MonsterInstance> TrainerTeam(TrainerDefinition trainer, GameData data, IRng rng, List<string> problems = null)
        {
            var team = new List<MonsterInstance>();
            if (trainer == null) return team;
            // Su NIVEL de IA (1-5) decide cómo arma los movimientos y a qué fuentes llega (MT, tutor, huevo).
            var profile = data.AiProfileFor(trainer);
            var style = trainer.AiSettings.Moveset != MovesetStyle.ByAi ? trainer.AiSettings.Moveset : profile.Moveset;
            for (int i = 0; i < trainer.Team.Count && team.Count < data.Ruleset.MaxPartySize; i++)
            {
                var mon = Build(trainer.Team[i], new Id<MonsterInstance>($"t:{trainer.Id}:{i + 1}"), data, rng, problems, style, profile);
                if (mon != null) team.Add(mon);
            }
            return team;
        }

        // Objeto equipado automático (Élite y Campeón): lo más útil que exista en el juego.
        //   1) un objeto que potencie su tipo principal (Carbón, Agua Mística...);
        //   2) si no, Restos; 3) si no, una baya que cure (Zidra, Aranja).
        private static string PickHeldItem(SpeciesDef species, MonsterInstance mon, GameData data)
        {
            ItemDefinition typeItem = null, leftovers = null, berry = null;
            foreach (var it in data.Items.All)
            {
                if (it == null || !it.HasHeldEffect) continue;
                foreach (var pm in it.HeldPowerModifiers)
                    foreach (var c in pm.Conditions)
                        if (c.Kind == CTEditor.GameDefinition.Domain.Conditions.ConditionKind.MoveType && species.Types.Count > 0 &&
                            c.Text == species.Types[0].Value && pm.Multiplier > 1f && typeItem == null) typeItem = it;
                if (it.HeldEndOfTurnHealPercent > 0f && leftovers == null) leftovers = it;
                if (it.HeldTriggerHpPercent > 0f && (berry == null || it.HeldTriggerHealPercent > berry.HeldTriggerHealPercent)) berry = it;
            }
            // Un atacante con buen ataque de su tipo prefiere el potenciador; los demás, aguantar.
            var pick = typeItem != null && mon.Moves.Any(m => data.Moves.TryGet(m, out var mv) && mv.Type == species.Types[0] && mv.Power >= 60)
                ? typeItem : leftovers ?? berry ?? typeItem;
            return pick?.Id;
        }

        /// <summary>
        /// Un SALVAJE de una zona: sortea la especie según los pesos y el nivel entre su mínimo y máximo.
        /// Recibe el id que tendrá si se captura (normalmente save.NewMonsterId()).
        /// </summary>
        public static MonsterInstance Wild(EncounterZone zone, Id<MonsterInstance> id, GameData data, IRng rng, List<string> problems = null)
        {
            if (zone == null || zone.Entries.Count == 0) { problems?.Add("La zona no tiene especies."); return null; }
            // Solo entre las especies que existen (una zona con una especie borrada sigue funcionando).
            var valid = new List<EncounterEntry>();
            foreach (var e in zone.Entries)
                if (data.Species.Contains(e.Species)) valid.Add(e);
                else problems?.Add($"La especie '{e.Species.Value}' de la zona no existe.");
            if (valid.Count == 0) return null;

            var usable = new EncounterZone(zone.Id, zone.DisplayName, valid);
            var pick = usable.Pick(rng.Next(0, usable.TotalWeight)).Value;
            int level = rng.Next(pick.MinLevel, pick.MaxLevel + 1);
            return Build(pick.Species, level, id, data, rng, problems);
        }

        /// <summary>Los movimientos que elegiría el planificador para esa especie a ese nivel (null = clásico).</summary>
        public static List<Id<Move>> PlanMoves(SpeciesDef species, int level, GameData data, MovesetStyle style, AiProfile profile = null)
        {
            var learn = new List<(Move, int)>();
            foreach (var lm in species.Learnset)
                if (data.Moves.TryGet(lm.Move, out var mv)) learn.Add((mv, lm.Level));
            return MovesetPlanner.Plan(learn, species.Types, species.BaseStats.Attack, species.BaseStats.SpAttack, level, style,
                Math.Max(1, data.Ruleset.MaxMovesPerMonster), OptionsFor(species, data, profile));
        }

        /// <summary>Opciones del planificador según el nivel de IA: MT (≥2), tutor (≥3), huevo (≥4) y sinergias.</summary>
        public static PlanOptions OptionsFor(SpeciesDef species, GameData data, AiProfile profile)
        {
            if (profile == null) return null;
            var extra = new List<Move>();
            void Add(IReadOnlyList<Id<Move>> ids) { foreach (var id in ids) if (data.Moves.TryGet(id, out var m)) extra.Add(m); }
            if (profile.UseMachineMoves) Add(species.MachineMoves);
            if (profile.UseTutorMoves) Add(species.TutorMoves);
            if (profile.UseEggMoves) Add(species.EggMoves);
            return new PlanOptions
            {
                ExtraMoves = extra,
                Synergies = profile.Synergies,
                WeatherBoost = (w, t) => data.Weathers.TryGet(new Id<WeatherDefinition>(w), out var wd) ? wd.MultiplierFor(t) : 1f,
            };
        }

        private static Nature RandomNature(GameData data, IRng rng)
        {
            if (rng == null) return null;
            var all = data.Natures.All;
            if (all == null || all.Count == 0) return null;
            int pick = rng.Next(0, all.Count), i = 0;
            foreach (var n in all) if (i++ == pick) return n;
            return null;
        }
    }
}
