using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.Abstractions;
using CTEditor.SharedKernel.Events;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Items;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Status;
using CTEditor.GameDefinition.Domain.Trainers;
using CTEditor.Party.Domain;
using CTEditor.Battle.Domain;
using CTEditor.Battle.Domain.Actions;
using CTEditor.Battle.Domain.AI;
using CTEditor.Battle.Domain.Events;
using CTEditor.Battle.Domain.Formulas;
using CTEditor.Battle.Domain.Turn;
using BattleAggregate = CTEditor.Battle.Domain.Battle;
using SpeciesDef = CTEditor.GameDefinition.Domain.Species.Species;

namespace CTEditor.Adventure.Domain
{
    /// <summary>
    /// UNA SESIÓN DE COMBATE con las reglas de un Pokémon de verdad, de principio a fin:
    ///
    ///   1. Se arma desde la PARTIDA del jugador (su equipo tal como está: PS, estados, PP, niveles) y
    ///      el rival (un salvaje o el equipo de un entrenador). Sale el primero que puede luchar.
    ///   2. Cada turno el jugador ELIGE (Luchar / Mochila / Equipo / Huir). La sesión VALIDA con las
    ///      reglas ("no puedes huir de un entrenador", "esa poción no tendría efecto", "no quedan PP")
    ///      sin gastar el turno, y si vale, el motor resuelve el turno.
    ///   3. La experiencia se aplica AL MOMENTO: sube de nivel en mitad del combate (con sus nuevas
    ///      estadísticas en el campo), aprende movimientos (o pregunta cuál olvidar).
    ///   4. Si cae un monstruo: el entrenador saca el siguiente; el jugador elige quién sale.
    ///   5. Al terminar, TODO vuelve a la partida: PS, estados, PP, objetos, EVs; el capturado entra
    ///      al equipo o al PC; el premio en dinero; o la derrota (pierde dinero y vuelve al Centro);
    ///      y por último las evoluciones pendientes (que el jugador puede cancelar).
    ///
    /// Lógica PURA: la escena de Unity solo muestra los eventos y pasa las decisiones del jugador.
    /// El combate (Battle) sigue sin conocer al equipo: recibe fotos y devuelve resultados.
    /// </summary>
    public sealed class BattleSession
    {
        public GameData Data { get; }
        public PlayerSave Save { get; }
        public BattleKind Kind { get; }
        /// <summary>El entrenador rival (null en combates salvajes).</summary>
        public TrainerDefinition Trainer { get; }
        public BattleAggregate Battle { get; }
        public TurnResolver Resolver { get; }
        public SessionPhase Phase { get; private set; }
        public BattleOutcome Outcome => Battle.Outcome;

        /// <summary>Contenido roto detectado al armar los equipos (para mostrarlo; el combate sigue).</summary>
        public IReadOnlyList<string> Problems => _problems;

        private readonly IRng _rng;
        private readonly IBattleAI _ai;          // salvajes: al azar
        private readonly TrainerBrain _brain;     // entrenadores: objetos, cambios y movimientos según su IA
        private readonly Dictionary<string, MonsterInstance> _playerMons = new Dictionary<string, MonsterInstance>();
        private readonly Dictionary<string, MonsterInstance> _enemyMons = new Dictionary<string, MonsterInstance>();
        private readonly Queue<(Id<BattleParticipant> who, Id<Move> move)> _learnQueue = new Queue<(Id<BattleParticipant>, Id<Move>)>();
        private readonly Queue<(MonsterInstance mon, SpeciesDef target)> _evoQueue = new Queue<(MonsterInstance, SpeciesDef)>();
        private readonly HashSet<string> _leveledUp = new HashSet<string>();
        private readonly List<string> _problems = new List<string>();
        private (MonsterInstance mon, SpeciesDef target)? _pendingEvo;
        private bool _started, _finished;

        // ---------------- Crear ----------------

        /// <summary>Combate contra un SALVAJE (ya generado: TeamBuilder.Wild o Build).</summary>
        public static BattleSession Wild(GameData data, PlayerSave save, MonsterInstance wild, IRng rng = null)
        {
            if (wild == null) throw new ArgumentNullException(nameof(wild));
            return new BattleSession(data, save, BattleKind.Wild, null, new List<MonsterInstance> { wild }, rng ?? new DefaultRng(), null);
        }

        /// <summary>Combate contra un ENTRENADOR (su equipo se arma con TeamBuilder, igual que el del jugador).</summary>
        public static BattleSession Against(GameData data, PlayerSave save, TrainerDefinition trainer, IRng rng = null)
        {
            if (trainer == null) throw new ArgumentNullException(nameof(trainer));
            rng = rng ?? new DefaultRng();
            var problems = new List<string>();
            var team = TeamBuilder.TrainerTeam(trainer, data, rng, problems);
            return new BattleSession(data, save, BattleKind.Trainer, trainer, team, rng, problems);
        }

        private BattleSession(GameData data, PlayerSave save, BattleKind kind, TrainerDefinition trainer,
            List<MonsterInstance> enemies, IRng rng, List<string> problems)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            Save = save ?? throw new ArgumentNullException(nameof(save));
            if (!save.CanBattle) throw new InvalidOperationException("No tienes ningún monstruo en condiciones de luchar.");
            if (enemies == null || enemies.Count == 0) throw new InvalidOperationException("El rival no tiene ningún monstruo válido.");
            if (problems != null) _problems.AddRange(problems);

            Kind = kind;
            Trainer = trainer;
            _rng = rng;

            var playerSnaps = new List<BattleParticipant>();
            foreach (var m in save.Party.Members)
            {
                playerSnaps.Add(data.Snapshot(m));
                _playerMons[m.Id.Value] = m;
            }
            var enemySnaps = new List<BattleParticipant>();
            foreach (var m in enemies)
            {
                enemySnaps.Add(data.Snapshot(m));
                _enemyMons[m.Id.Value] = m;
            }

            Battle = new BattleAggregate(playerSnaps, enemySnaps, kind == BattleKind.Trainer);
            Resolver = data.CreateResolver(rng);
            _ai = new SimpleBattleAI(rng) { CanUse = Resolver.CanChooseMove };
            if (kind == BattleKind.Trainer) _brain = new TrainerBrain(data, Battle, Resolver, trainer, rng);
            Phase = SessionPhase.ChooseAction;
        }

        /// <summary>
        /// Arranque: presentación ("¡Un X salvaje apareció!" / "¡El entrenador te desafía!"), ambos
        /// sacan a su monstruo y se aplican las habilidades al entrar. Llamar UNA vez.
        /// </summary>
        public IReadOnlyList<IDomainEvent> Begin()
        {
            var output = new List<IDomainEvent>();
            if (_started) return output;
            _started = true;

            output.Add(new BattleIntroEvent(Kind, Battle.Enemy.Id, Trainer?.FullName, Trainer?.IntroLine));
            if (Kind == BattleKind.Trainer) output.Add(new SentOutEvent(Battle.Enemy.Id, false, Trainer.FullName));
            output.Add(new SentOutEvent(Battle.Player.Id, true));
            output.AddRange(Resolver.ResolveBattleStart(Battle));
            return output;
        }

        // ---------------- Consultas para la interfaz ----------------

        public bool IsPlayerSide(Id<BattleParticipant> id) => _playerMons.ContainsKey(id.Value);

        /// <summary>El monstruo real detrás de un combatiente (del jugador o del rival).</summary>
        public MonsterInstance MonsterOf(Id<BattleParticipant> id)
            => _playerMons.TryGetValue(id.Value, out var m) ? m : _enemyMons.TryGetValue(id.Value, out m) ? m : null;

        /// <summary>Nombre visible de un combatiente (mote o especie).</summary>
        public string NameOf(Id<BattleParticipant> id)
        {
            var m = MonsterOf(id);
            return m != null ? Data.NameOf(m) : id.Value;
        }

        /// <summary>
        /// ¿El activo del jugador está obligado a seguir (cargando un movimiento o recargando)? Entonces
        /// no hay menú: la interfaz envía Fight(0) y el motor hace lo que toca.
        /// </summary>
        public bool PlayerIsLocked => Battle.Player.IsLockedIntoMove;

        /// <summary>
        /// ¿El jugador está eligiendo quién ENTRA tras su Ida y Vuelta / Relevo? (fase ChooseReplacement con el
        /// turno en pausa: el que se va sigue en pie). La interfaz puede decir "¿quién entra?" en vez de "¿quién sale?".
        /// </summary>
        public bool IsSelfSwitchPending => Resolver.IsTurnSuspended;

        /// <summary>PP actuales y máximos del movimiento 'index' del activo del jugador.</summary>
        public (int current, int max) PpOf(int index) => Resolver.PpOf(Battle.Player, index);

        /// <summary>Probabilidad (0-1) de huir ahora.</summary>
        public double FleeChance => Kind == BattleKind.Trainer && !Resolver.Rules.CanFleeTrainerBattles ? 0 : Resolver.FleeChance(Battle);

        /// <summary>Probabilidad (0-1) de capturar al rival con esa bola ahora mismo.</summary>
        public double CatchChance(string ballItemId)
        {
            if (Kind == BattleKind.Trainer && !Resolver.Rules.CanCatchTrainerMonsters) return 0;
            if (!Data.TryGetItem(ballItemId, out var ball) || !ball.IsBall) return 0;
            return ClassicCatchFormula.Probability(Resolver.CatchContextFor(Battle.Enemy, ball.CatchMultiplier));
        }

        /// <summary>El movimiento que quiere aprender ahora (fase LearnMove) y quién.</summary>
        public (Id<BattleParticipant> who, Id<Move> move)? PendingLearn => _learnQueue.Count > 0 ? _learnQueue.Peek() : ((Id<BattleParticipant>, Id<Move>)?)null;

        /// <summary>La evolución pendiente (fase Evolution).</summary>
        public (MonsterInstance mon, SpeciesDef target)? PendingEvolution => _pendingEvo;

        /// <summary>¿Por qué NO se puede elegir esto ahora? Null = sí se puede. No cambia nada.</summary>
        public string WhyNot(PlayerChoice choice) => TryTranslate(choice, out _, out _, out var reason) ? null : reason;

        // ---------------- Decisiones del jugador ----------------

        /// <summary>El jugador elige su acción del turno. Si las reglas no lo permiten, no se gasta el turno.</summary>
        public SessionStep Submit(PlayerChoice choice)
        {
            if (Phase != SessionPhase.ChooseAction) return SessionStep.Rejected("Ahora no toca elegir una acción.");
            if (!TryTranslate(choice, out var action, out var spendItem, out var reason)) return SessionStep.Rejected(reason);
            if (spendItem != null) Save.Bag.Remove(spendItem);

            BattleAction enemyAction;
            string enemyItem = null;
            if (_brain != null) (enemyAction, enemyItem) = _brain.Decide();
            else enemyAction = _ai.ChooseAction(Battle.Enemy, Battle.Player);
            string playerItem = choice is ItemChoice ic && action is UseItemAction ? ic.ItemId : null;

            var output = new List<IDomainEvent>();
            Process(WithItemNames(Resolver.ResolveTurn(Battle, action, enemyAction), playerItem, enemyItem), output);
            Continue(output);
            return SessionStep.Done(output);
        }

        /// <summary>Lo que le queda en la mochila al entrenador rival (vacío en combates salvajes).</summary>
        public IReadOnlyDictionary<string, int> TrainerBag => _brain != null ? _brain.Bag : (IReadOnlyDictionary<string, int>)new Dictionary<string, int>();

        // Pone delante de cada "usó un objeto" del motor QUIÉN lo usó y QUÉ objeto era (para narrarlo bien).
        private IReadOnlyList<IDomainEvent> WithItemNames(IReadOnlyList<IDomainEvent> events, string playerItem, string enemyItem)
        {
            if (playerItem == null && enemyItem == null) return events;
            var list = new List<IDomainEvent>(events.Count + 2);
            foreach (var e in events)
            {
                if (e is ItemUsedInBattleEvent iu)
                {
                    bool mine = IsPlayerSide(iu.Target);
                    if (mine && playerItem != null) list.Add(new BagItemUsedEvent(true, Save.PlayerName, playerItem, iu.Target));
                    else if (!mine && enemyItem != null) list.Add(new BagItemUsedEvent(false, Trainer?.FullName ?? "", enemyItem, iu.Target));
                }
                list.Add(e);
            }
            return list;
        }

        /// <summary>Su monstruo cayó: el jugador elige quién sale (índice en el equipo).</summary>
        public SessionStep ChooseReplacement(int teamIndex)
        {
            if (Phase != SessionPhase.ChooseReplacement) return SessionStep.Rejected("Ahora no toca elegir un relevo.");
            var members = Battle.PlayerTeam.Members;
            if (teamIndex < 0 || teamIndex >= members.Count) return SessionStep.Rejected("No hay nadie en esa posición.");
            var m = members[teamIndex];
            if (m.IsFainted) return SessionStep.Rejected($"¡{NameOf(m.Id)} no tiene fuerzas para luchar!");

            // Ida y Vuelta / Relevo: entra el elegido y el turno sigue donde se quedó.
            if (Resolver.IsTurnSuspended)
            {
                if (ReferenceEquals(m, Battle.Player)) return SessionStep.Rejected($"¡{NameOf(m.Id)} ya está luchando!");
                var resumed = new List<IDomainEvent>();
                Process(Resolver.ResumeTurn(Battle, m.Id), resumed);
                if (Resolver.IsTurnSuspended) return SessionStep.Rejected("No puede entrar ahora.");
                Phase = SessionPhase.ChooseAction;
                Continue(resumed);
                return SessionStep.Done(resumed);
            }
            if (!Battle.SendReplacement(true, m.Id)) return SessionStep.Rejected("No puede salir ahora.");

            var output = new List<IDomainEvent> { new SentOutEvent(m.Id, true) };
            Process(Resolver.ResolveReplacementEntry(Battle, true), output);
            Phase = SessionPhase.ChooseAction;
            Continue(output);
            return SessionStep.Done(output);
        }

        /// <summary>
        /// Quiere aprender un movimiento y no hay hueco: 'forgetSlot' = el hueco a olvidar (0-3), o -1
        /// para NO aprenderlo.
        /// </summary>
        public SessionStep AnswerLearnMove(int forgetSlot)
        {
            if (Phase != SessionPhase.LearnMove || _learnQueue.Count == 0) return SessionStep.Rejected("No hay ningún movimiento esperando.");
            var (who, move) = _learnQueue.Peek();
            var mon = MonsterOf(who);
            var output = new List<IDomainEvent>();

            if (forgetSlot >= 0 && mon != null)
            {
                if (forgetSlot >= mon.Moves.Count) return SessionStep.Rejected("Ese hueco está vacío.");
                var old = mon.Moves[forgetSlot];
                Teach(who, mon, forgetSlot, move);
                output.Add(new MoveLearnedEvent(who, move, old));
            }
            else output.Add(new MoveNotLearnedEvent(who, move));

            _learnQueue.Dequeue();
            Phase = SessionPhase.ChooseAction;
            Continue(output);
            return SessionStep.Done(output);
        }

        /// <summary>Está evolucionando: true = dejarlo; false = cancelarlo (como pulsar B).</summary>
        public SessionStep AnswerEvolution(bool accept)
        {
            if (Phase != SessionPhase.Evolution || !_pendingEvo.HasValue) return SessionStep.Rejected("No hay ninguna evolución esperando.");
            var (mon, target) = _pendingEvo.Value;
            _pendingEvo = null;
            var from = mon.SpeciesId;
            var output = new List<IDomainEvent>();

            if (accept)
            {
                mon.Evolve(target.Id, target.BaseStats, Data.Growth);
                output.Add(new EvolutionResultEvent(mon.Id, from, target.Id, true));
                // Movimientos que la especie nueva aprende justo a su nivel actual.
                if (Data.Rules.LearnMovesOnLevelUp)
                    foreach (var lm in target.Learnset)
                        if (lm.Level == mon.Level.Value) TryLearn(new Id<BattleParticipant>(mon.Id.Value), mon, lm.Move, output);
            }
            else output.Add(new EvolutionResultEvent(mon.Id, from, target.Id, false));

            Phase = SessionPhase.ChooseAction;
            Continue(output);
            return SessionStep.Done(output);
        }

        // ---------------- Traducir la elección a una acción del motor ----------------

        private bool TryTranslate(PlayerChoice choice, out BattleAction action, out string spendItem, out string reason)
        {
            action = null; spendItem = null; reason = null;
            var active = Battle.Player;

            if (PlayerIsLocked)
            {
                // Cargando o recargando: el motor ignora la elección y hace lo que toca.
                action = new UseMove(active.ChargingMove ?? (active.Moves.Count > 0 ? active.Moves[0] : default));
                return true;
            }

            switch (choice)
            {
                case FightChoice f:
                {
                    if (active.Moves.Count == 0) { reason = $"{NameOf(active.Id)} no sabe ningún movimiento."; return false; }
                    if (f.MoveIndex < 0 || f.MoveIndex >= active.Moves.Count) { reason = "Ese movimiento no existe."; return false; }
                    if (Resolver.UsesPp)
                    {
                        var (pp, _) = Resolver.PpOf(active, f.MoveIndex);
                        if (pp <= 0 && active.HasAnyPp) { reason = "¡No quedan PP para este movimiento!"; return false; }
                    }
                    var picked = active.Moves[f.MoveIndex];
                    // Otra Vez: solo vale el movimiento que tiene que repetir.
                    if (active.EncoreMove.HasValue && active.EncoreMove.Value != picked && active.IndexOfMove(active.EncoreMove.Value) >= 0)
                    { reason = $"¡{NameOf(active.Id)} tiene que repetir {Data.MoveName(active.EncoreMove.Value)}!"; return false; }
                    if (active.IsDisabled(picked)) { reason = $"¡{Data.MoveName(picked)} está anulado!"; return false; }
                    action = new UseMove(picked);
                    return true;
                }

                case SwitchChoice s:
                {
                    var members = Battle.PlayerTeam.Members;
                    if (s.TeamIndex < 0 || s.TeamIndex >= members.Count) { reason = "No hay nadie en esa posición."; return false; }
                    var m = members[s.TeamIndex];
                    if (m.IsFainted) { reason = $"¡{NameOf(m.Id)} no tiene fuerzas para luchar!"; return false; }
                    if (ReferenceEquals(m, active)) { reason = $"¡{NameOf(m.Id)} ya está luchando!"; return false; }
                    if (IsTrapped(active)) { reason = $"¡{NameOf(active.Id)} no puede retirarse!"; return false; }
                    action = new SwitchMonster(m.Id);
                    return true;
                }

                case ItemChoice it:
                {
                    if (!Data.TryGetItem(it.ItemId, out var item)) { reason = "Ese objeto no existe."; return false; }
                    if (!Save.Bag.Has(item.Id)) { reason = $"No te quedan {item.DisplayName}."; return false; }
                    if (item.IsBall) return TryTranslate(new BallChoice(item.Id), out action, out spendItem, out reason);
                    if (!item.UsableInBattle) { reason = "Ese objeto no se puede usar en combate."; return false; }
                    var members = Battle.PlayerTeam.Members;
                    if (it.TeamIndex < 0 || it.TeamIndex >= members.Count) { reason = "No hay nadie en esa posición."; return false; }
                    var target = members[it.TeamIndex];
                    if (!WouldHaveEffect(item, target)) { reason = "No tendría ningún efecto."; return false; }
                    action = new UseItemAction(target.Id, BattleItemEffect.From(item));
                    spendItem = item.Consumable ? item.Id : null;
                    return true;
                }

                case BallChoice b:
                {
                    if (!Data.TryGetItem(b.ItemId, out var ball) || !ball.IsBall) { reason = "Eso no es una bola."; return false; }
                    if (!Save.Bag.Has(ball.Id)) { reason = $"No te quedan {ball.DisplayName}."; return false; }
                    if (Kind == BattleKind.Trainer && !Resolver.Rules.CanCatchTrainerMonsters)
                    { reason = "¡No puedes capturar el monstruo de otro entrenador!"; return false; }
                    action = new Capturar(ball.CatchMultiplier);
                    spendItem = ball.Id;
                    return true;
                }

                case RunChoice _:
                {
                    if (Kind == BattleKind.Trainer && !Resolver.Rules.CanFleeTrainerBattles)
                    { reason = "¡No puedes huir de un combate contra un entrenador!"; return false; }
                    if (IsTrapped(active)) { reason = $"¡{NameOf(active.Id)} no puede escapar!"; return false; }
                    action = new Flee();
                    return true;
                }
            }
            reason = "Elección desconocida.";
            return false;
        }

        // ¿Usar este objeto sobre este miembro haría algo? (Una Poción con la vida llena no se gasta.)
        private bool WouldHaveEffect(ItemDefinition item, Combatant target)
        {
            if (target.IsFainted) return item.Revives;
            bool useful = false;
            if (item.HealsHp && target.CurrentHp < target.MaxHp) useful = true;
            if (item.CuresStatus && target.Status.HasValue && item.CuresThis(target.Status.Value.Value)) useful = true;
            if (!string.IsNullOrEmpty(item.BattleStatId) && item.BattleStages != 0 && ReferenceEquals(target, Battle.Player)) useful = true;
            if (item.RestorePp > 0)
                for (int i = 0; i < target.Moves.Count; i++)
                {
                    var (pp, max) = Resolver.PpOf(target, i);
                    if (pp < max) { useful = true; break; }
                }
            return useful;
        }

        private bool IsTrapped(Combatant c)
        {
            foreach (var v in c.Volatiles)
                if (Data.Statuses.TryGet(new Id<StatusConditionDefinition>(v.Id.Value), out var d) && d.PreventsSwitch) return true;
            if (c.Status.HasValue && Data.Statuses.TryGet(new Id<StatusConditionDefinition>(c.Status.Value.Value), out var p) && p.PreventsSwitch)
                return true;
            return false;
        }

        // ---------------- Después de cada turno ----------------

        // Copia los eventos del motor y, justo después de cada "ganó experiencia", aplica la
        // experiencia AL MOMENTO (subidas de nivel y movimientos nuevos en mitad del combate).
        private void Process(IReadOnlyList<IDomainEvent> raw, List<IDomainEvent> output)
        {
            foreach (var e in raw)
            {
                output.Add(e);
                switch (e)
                {
                    case ExperienceAwardedEvent xp:
                        ApplyExperience(xp.Recipient, xp.Amount, output);
                        break;
                    case MonsterFaintedEvent f when _playerMons.TryGetValue(f.Combatant.Value, out var fallen):
                        fallen.ChangeFriendship(-Data.Rules.FriendshipLostOnFaint);
                        break;
                }
            }
        }

        private void ApplyExperience(Id<BattleParticipant> who, int amount, List<IDomainEvent> output)
        {
            if (!_playerMons.TryGetValue(who.Value, out var mon) || amount <= 0) return;
            var species = Data.SpeciesOf(mon);
            if (species == null) return;
            var curve = Data.CurveFor(species);
            int cap = Math.Min(Data.Ruleset.LevelCap, curve.MaxLevel);

            // Nivel a nivel: un mensaje y sus movimientos por CADA nivel subido, como en los juegos.
            while (amount > 0 && mon.Level.Value < cap)
            {
                int current = Math.Max(mon.Experience.Value, curve.XpToReachLevel(mon.Level.Value));
                int toNext = curve.XpToReachLevel(mon.Level.Value + 1) - current;
                int piece = Math.Min(amount, Math.Max(1, toNext));
                amount -= piece;

                var before = mon.Stats;
                int oldLevel = mon.Level.Value;
                var res = mon.AddExperience(piece, curve, species.BaseStats, Data.Growth);
                if (!res.LeveledUp) continue;

                _leveledUp.Add(mon.Id.Value);
                mon.ChangeFriendship(Data.Rules.FriendshipPerLevelUp * res.LevelsGained);
                var gains = new Dictionary<StatId, int>();
                foreach (var stat in mon.Stats.Stats) gains[stat] = mon.Stats.Of(stat) - (before.Has(stat) ? before.Of(stat) : 0);
                Battle.ApplyLevelUp(who, res.NewLevel, mon.Stats);
                output.Add(new LevelUpEvent(who, res.NewLevel, gains));

                if (!Data.Rules.LearnMovesOnLevelUp) continue;
                for (int lvl = oldLevel + 1; lvl <= res.NewLevel; lvl++)
                    foreach (var lm in species.Learnset)
                        if (lm.Level == lvl) TryLearn(who, mon, lm.Move, output);
            }
        }

        // Aprende en un hueco libre; si no hay, lo deja en cola para preguntar al jugador.
        private void TryLearn(Id<BattleParticipant> who, MonsterInstance mon, Id<Move> move, List<IDomainEvent> output)
        {
            if (!Data.Moves.Contains(move) || mon.KnowsMove(move)) return;
            foreach (var q in _learnQueue) if (q.who == who && q.move == move) return;
            if (mon.Moves.Count < Data.Ruleset.MaxMovesPerMonster)
            {
                Teach(who, mon, mon.Moves.Count, move);
                output.Add(new MoveLearnedEvent(who, move));
            }
            else _learnQueue.Enqueue((who, move));
        }

        private void Teach(Id<BattleParticipant> who, MonsterInstance mon, int slot, Id<Move> move)
        {
            int maxPp = Data.MaxPpOf(move);
            var c = Battle.PlayerTeam.Find(who);
            if (c != null && c.Moves.Count > 0) Resolver.PpOf(c, 0); // asegura que los PP del combate estén cargados
            mon.LearnMove(slot, move, maxPp);
            Battle.TeachMove(who, slot, move, maxPp);
        }

        // Decide qué toca ahora: preguntar un movimiento, relevos, fin del combate o siguiente turno.
        // Es un bucle porque un relevo puede caer al entrar (trampas) y hacer falta otro.
        private void Continue(List<IDomainEvent> output)
        {
            for (int guard = 0; guard < 20; guard++)
            {
                // Turno en pausa por Ida y Vuelta / Relevo: el jugador elige quién entra.
                if (Resolver.IsTurnSuspended) { Phase = SessionPhase.ChooseReplacement; return; }

                if (_learnQueue.Count > 0)
                {
                    var (who, move) = _learnQueue.Peek();
                    Phase = SessionPhase.LearnMove;
                    output.Add(new MoveLearnPromptEvent(who, move));
                    return;
                }

                if (Battle.IsOver)
                {
                    if (!_finished) Finish(output);
                    else NextEvolution(output);
                    return;
                }

                // El rival saca al siguiente (el entrenador, en el orden de su equipo).
                if (Battle.NeedsReplacement(false))
                {
                    var next = Battle.EnemyTeam.Reserves();
                    if (next.Count > 0 && Battle.SendReplacement(false, next[0].Id))
                    {
                        output.Add(new SentOutEvent(next[0].Id, false, Trainer?.FullName));
                        Process(Resolver.ResolveReplacementEntry(Battle, false), output);
                        continue; // puede haber caído por las trampas: se vuelve a mirar
                    }
                }

                // El jugador elige quién sale.
                if (Battle.NeedsReplacement(true)) { Phase = SessionPhase.ChooseReplacement; return; }
                Phase = SessionPhase.ChooseAction;
                return;
            }
            Phase = SessionPhase.ChooseAction;
        }

        // ---------------- Fin: todo vuelve a la partida ----------------

        private void Finish(List<IDomainEvent> output)
        {
            _finished = true;
            var result = Battle.ToResult();

            // 1) EVs ganados (antes que los PS: al recalcular, los PS máximos pueden subir un poco).
            var touched = new HashSet<string>();
            foreach (var ev in result.EvAwards)
                if (ev.Amount > 0 && _playerMons.TryGetValue(ev.ParticipantId.Value, out var m) && m.AddEffort(ev.Stat, ev.Amount) > 0)
                    touched.Add(m.Id.Value);
            foreach (var id in touched)
            {
                var m = _playerMons[id];
                var sp = Data.SpeciesOf(m);
                if (sp != null) m.RecomputeStats(sp.BaseStats, Data.Growth);
            }

            // 2) PS, estado, PP y objeto de cada miembro del equipo, tal como acabaron.
            foreach (var pr in result.Participants)
                if (_playerMons.TryGetValue(pr.ParticipantId.Value, out var m)) ApplyFinalState(m, pr);

            // 3) Según cómo acabó.
            switch (Battle.Outcome)
            {
                case BattleOutcome.Caught:
                {
                    var caughtId = Battle.Enemy.Id;
                    if (_enemyMons.TryGetValue(caughtId.Value, out var caught))
                    {
                        foreach (var pr in result.Participants)
                            if (pr.ParticipantId == caughtId) ApplyFinalState(caught, pr);
                        var where = Save.Receive(caught, Data.Rules.SendToBoxWhenFull);
                        output.Add(new CaptureStoredEvent(caught.Id, Data.NameOf(caught), where));
                    }
                    break;
                }
                case BattleOutcome.PlayerWon when Kind == BattleKind.Trainer:
                {
                    if (!string.IsNullOrWhiteSpace(Trainer.DefeatLine)) output.Add(new TrainerSaysEvent(Trainer.FullName, Trainer.DefeatLine));
                    int prize = PrizeMoney();
                    if (prize > 0) { Save.AddMoney(prize); output.Add(new MoneyWonEvent(prize)); }
                    Save.MarkDefeated(Trainer.Id);
                    break;
                }
                case BattleOutcome.PlayerLost:
                {
                    if (Kind == BattleKind.Trainer && !string.IsNullOrWhiteSpace(Trainer.VictoryLine))
                        output.Add(new TrainerSaysEvent(Trainer.FullName, Trainer.VictoryLine));
                    int lost = Save.LosePercent(Data.Rules.MoneyLostOnBlackoutPercent);
                    bool heal = Data.Rules.HealOnBlackout;
                    if (heal) Save.HealAll();
                    output.Add(new BlackoutEvent(lost, heal));
                    break;
                }
            }

            // 4) Evoluciones de los que subieron de nivel (no tras una derrota, ni si están debilitados).
            if (Battle.Outcome != BattleOutcome.PlayerLost && Data.Rules.EvolveAfterBattle)
                foreach (var m in Save.Party.Members)
                {
                    if (!_leveledUp.Contains(m.Id.Value) || m.IsFainted) continue;
                    var sp = Data.SpeciesOf(m);
                    var evo = sp != null ? EvolutionRules.Find(m, sp, EvolutionTrigger.LevelUp, null, Data.EvolutionContextFor(Save)) : null;
                    if (evo != null && Data.Species.TryGet(evo.Target, out var target)) _evoQueue.Enqueue((m, target));
                }

            NextEvolution(output);
        }

        private void NextEvolution(List<IDomainEvent> output)
        {
            if (_evoQueue.Count > 0)
            {
                _pendingEvo = _evoQueue.Dequeue();
                Phase = SessionPhase.Evolution;
                output.Add(new EvolutionPromptEvent(_pendingEvo.Value.mon.Id, _pendingEvo.Value.mon.SpeciesId, _pendingEvo.Value.target.Id));
                return;
            }
            Phase = SessionPhase.Finished;
        }

        /// <summary>Premio clásico: dinero base del entrenador × nivel de su ÚLTIMO monstruo.</summary>
        public int PrizeMoney()
        {
            if (Trainer == null || Battle.EnemyTeam.Members.Count == 0) return 0;
            var last = Battle.EnemyTeam.Members[Battle.EnemyTeam.Members.Count - 1];
            return Trainer.BaseMoney * last.Level;
        }

        private static void ApplyFinalState(MonsterInstance m, ParticipantResult pr)
        {
            int target = pr.Fainted ? 0 : Math.Min(pr.FinalHp, m.MaxHp);
            if (m.IsFainted && target > 0) m.Revive(target);
            int delta = target - m.CurrentHp;
            if (delta < 0) m.TakeDamage(-delta); else if (delta > 0) m.Heal(delta);

            if (pr.Fainted || !pr.FinalStatus.HasValue) m.ClearStatus();
            else m.SetStatus(pr.FinalStatus.Value);
            if (pr.FinalPp != null) m.SetCurrentPp(pr.FinalPp);
            m.SetHeldItem(pr.FinalHeldItem);
        }
    }
}
