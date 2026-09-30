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
            // ENTRENAMIENTO DE COMPETICIÓN (Maestro, Injusto): naturaleza que sube su mejor ataque y baja el que no usa.
            bool training = profile != null && profile.CompetitiveTraining;
            if (training && !spec.Nature.HasValue) nature = CompetitiveNature(species, data) ?? nature;
            if (nature == null && !spec.Nature.HasValue) nature = RandomNature(data, rng);

            var mon = MonsterFactory.Create(id, species, spec.Level, data.Ruleset, data.Growth, moves,
                data.CurveFor(species), rng, nature, spec.FixedIv ?? (training ? 31 : (int?)null), spec.Ivs);
            // EVs: los escritos por el autor mandan; si no, los de su entrenamiento de competición.
            if (!spec.Evs.IsEmpty) ApplyEvs(mon, species, data, spec.Evs, problems);
            else if (training) ApplyCompetitiveEvs(mon, species, data);
            // Habilidad elegida: una de las de su especie (1.ª, 2.ª u oculta).
            if (spec.Ability.HasValue)
            {
                int slot = AbilitySlotOf(species, spec.Ability.Value);
                if (slot >= 0) mon.SetAbilitySlot(slot);
                else problems?.Add($"{species.DisplayName} no puede tener la habilidad '{spec.Ability.Value.Value}': se queda con la suya.");
            }
            // Reglas de generación: sin objetos equipados (1.ª gen.) no se le pone ninguno.
            bool items = data.Ruleset.Generation.HeldItems;
            if (items && !string.IsNullOrWhiteSpace(spec.HeldItem)) mon.SetHeldItem(spec.HeldItem);
            else if (items && profile != null && profile.HeldItems == HeldItemStyle.Competitive)
                mon.SetHeldItem(PickCompetitiveItem(species, mon, data) ?? PickHeldItem(species, mon, data));
            else if (items && profile != null && profile.AutoHeldItems) mon.SetHeldItem(PickHeldItem(species, mon, data));
            if (!string.IsNullOrWhiteSpace(spec.Nickname)) mon.SetNickname(spec.Nickname);
            // Género fijado por el autor (si la especie tiene género).
            if (data.Ruleset.Generation.Genders && spec.Gender.HasValue && !species.Dex.IsGenderless && spec.Gender.Value != CTEditor.GameDefinition.Domain.Species.Gender.Genderless)
                mon.SetGender(spec.Gender.Value);
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
            // Su NIVEL de IA (1-7) decide cómo arma los movimientos y a qué fuentes llega (MT, tutor, huevo).
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

        // ---------------- Competición (Maestro, Injusto) ----------------

        private static bool IsPhysical(SpeciesDef s) => s.BaseStats.Attack >= s.BaseStats.SpAttack;

        /// <summary>Naturaleza de competición: sube su mejor ataque y baja el que no usa (Firme / Modesta...).</summary>
        public static Nature CompetitiveNature(SpeciesDef species, GameData data)
        {
            var up = IsPhysical(species) ? StatId.Attack : StatId.SpAttack;
            var down = IsPhysical(species) ? StatId.SpAttack : StatId.Attack;
            // Los muy rápidos prefieren Velocidad (Alegre / Miedosa).
            if (species.BaseStats.Speed >= 90 && species.BaseStats.Speed >= Math.Max(species.BaseStats.Attack, species.BaseStats.SpAttack) - 10)
                up = StatId.Speed;
            foreach (var n in data.Natures.All)
                if (n != null && n.BoostedStat.HasValue && n.HinderedStat.HasValue && n.BoostedStat.Value == up && n.HinderedStat.Value == down) return n;
            return null;
        }

        /// <summary>EVs de competición: 252 en su mejor ataque, 252 en Velocidad (o PS si es lento) y 4 en PS/Defensa.</summary>
        /// <summary>Hueco de habilidad (0 = 1.ª, 1 = 2.ª, 2 = oculta) de una habilidad de la especie; -1 si no la tiene.</summary>
        public static int AbilitySlotOf(SpeciesDef species, CTEditor.GameDefinition.Domain.Abilities.AbilityId ability)
        {
            if (species.Ability.HasValue && species.Ability.Value == ability) return 0;
            if (species.SecondAbility.HasValue && species.SecondAbility.Value == ability) return 1;
            if (species.HiddenAbility.HasValue && species.HiddenAbility.Value == ability) return 2;
            return -1;
        }

        /// <summary>Pone los EVs escritos (recortados a los topes de las reglas) y recalcula sus estadísticas.</summary>
        public static void ApplyEvs(MonsterInstance mon, SpeciesDef species, GameData data, StatSpread evs, List<string> problems = null)
        {
            if (data.Ruleset.MaxEvPerStat <= 0) return;   // juego sin EVs
            int total = 0;
            foreach (var kv in evs.Values)
            {
                int given = mon.AddEffort(kv.Key, kv.Value);
                total += given;
                if (given < kv.Value)
                    problems?.Add($"{species.DisplayName}: {kv.Value} EVs de {kv.Key.Value} pasan del tope de las reglas; se quedan en {given}.");
            }
            if (total == 0) return;
            mon.RecomputeStats(species.BaseStats, data.Growth);
            mon.Heal(mon.MaxHp);
        }

        public static void ApplyCompetitiveEvs(MonsterInstance mon, SpeciesDef species, GameData data)
        {
            var atk = IsPhysical(species) ? StatId.Attack : StatId.SpAttack;
            bool slow = species.BaseStats.Speed < 60;
            mon.AddEffort(atk, 252);
            mon.AddEffort(slow ? StatId.Hp : StatId.Speed, 252);
            mon.AddEffort(slow ? StatId.Defense : StatId.Hp, 4);
            mon.RecomputeStats(species.BaseStats, data.Growth);
            mon.Heal(mon.MaxHp);
        }

        /// <summary>
        /// Objeto de COMPETICIÓN según su papel (solo los que existan en el juego):
        ///   puede evolucionar → Mineral Evolutivo · frágil y fuerte → Banda Focus · solo ataques y rápido → Pañuelo Elección ·
        ///   solo ataques → Cinta / Gafas Elección · especial resistente → Chaleco Asalto · con mejoras → Vidasfera ·
        ///   resistente → Restos (o Lodo Negro si es Veneno) · y si no, Casco Dentado / Cinturón Experto.
        /// </summary>
        public static string PickCompetitiveItem(SpeciesDef species, MonsterInstance mon, GameData data)
        {
            bool Has(string id) => data.TryGetItem(id, out _);
            string First(params string[] ids) { foreach (var id in ids) if (Has(id)) return id; return null; }
            var b = species.BaseStats;
            bool physical = IsPhysical(species);
            var moves = mon.Moves.Select(m => data.Moves.TryGet(m, out var mv) ? mv : null).Where(m => m != null).ToList();
            bool allAttacks = moves.Count > 0 && moves.All(m => m.DealsDirectDamage);
            bool boosts = moves.Any(m => m.Category == MoveCategory.Status && m.SecondaryEffects.Any(e =>
                e.Kind == MoveEffectKind.ChangeStatStage && e.Target == EffectTarget.Self && e.Stages > 0));
            int bulk = b.Hp + b.Defense + b.SpDefense;
            if (species.Evolutions.Count > 0) return First("eviolite", "leftovers");
            if (bulk < 200 && Math.Max(b.Attack, b.SpAttack) >= 90) return First("focus_sash", "life_orb");
            if (allAttacks && b.Speed >= 80 && b.Speed < 110) return First("choice_scarf", physical ? "choice_band" : "choice_specs");
            if (allAttacks && Math.Max(b.Attack, b.SpAttack) >= 100) return physical ? First("choice_band", "life_orb") : First("choice_specs", "life_orb");
            if (allAttacks && b.SpDefense >= 80) return First("assault_vest", "leftovers");
            if (boosts) return First("life_orb", "leftovers");
            if (bulk >= 280) return species.Types.Any(t => t.Value == "poison") ? First("black_sludge", "leftovers") : First("leftovers", "rocky_helmet");
            return First("expert_belt", "life_orb", "rocky_helmet", "leftovers");
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

        // ---------------- Sugerencias para el EDITOR ----------------

        /// <summary>Lo que el juego le pondría a un miembro: movimientos, naturaleza (vacía = al azar) y objeto (vacío = ninguno).</summary>
        public sealed class MemberSuggestion
        {
            public IReadOnlyList<Id<Move>> Moves = Array.Empty<Id<Move>>();
            public string NatureId = "";
            public string HeldItem = "";
            /// <summary>EVs que le pondría su entrenamiento de competición (vacío si su nivel de IA no entrena).</summary>
            public StatSpread Evs = StatSpread.Empty;
        }

        /// <summary>
        /// SUGERENCIA para el editor: arma el miembro con el MISMO código que el juego en Play (Build) pero sin los
        /// movimientos, objeto y naturaleza escritos, y devuelve lo que el juego elegiría. La naturaleza solo se sugiere si
        /// el nivel de IA entrena como en competición (si no, en el juego sale al azar). Null si la especie no existe.
        /// </summary>
        public static MemberSuggestion Suggest(TeamMemberSpec spec, GameData data, AiProfile profile, MovesetStyle style)
        {
            if (spec == null || data == null) return null;
            if (style == MovesetStyle.ByAi) style = profile?.Moveset ?? MovesetStyle.Classic;
            var blank = new TeamMemberSpec(spec.Species, spec.Level, null, "", null, spec.FixedIv, spec.Nickname, spec.Gender);
            var mon = Build(blank, new Id<MonsterInstance>("sugerencia"), data, new FirstRng(), null, style, profile);
            if (mon == null) return null;
            bool training = profile != null && profile.CompetitiveTraining;
            return new MemberSuggestion
            {
                Moves = mon.Moves.ToList(),
                NatureId = training && mon.Nature != null ? mon.Nature.Id.Value : "",
                HeldItem = mon.HeldItem ?? "",
                Evs = training ? new StatSpread(mon.Stats.Stats.Where(st => mon.EvOf(st) > 0).Select(st => new KeyValuePair<StatId, int>(st, mon.EvOf(st))))
                               : StatSpread.Empty,
            };
        }

        // Azar fijo (siempre el primero): una sugerencia debe ser la misma cada vez que se pide.
        private sealed class FirstRng : IRng
        {
            public int Next(int minInclusive, int maxExclusive) => minInclusive;
            public float NextFloat() => 0f;
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
