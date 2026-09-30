using System;
using System.Collections.Generic;
using System.Linq;
using CTEditor.SharedKernel.Abstractions;
using CTEditor.SharedKernel.Events;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Catalog;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Species;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Status;
using CTEditor.GameDefinition.Domain.Abilities;
using CTEditor.GameDefinition.Domain.Conditions;
using CTEditor.GameDefinition.Domain.Weather;
using CTEditor.GameDefinition.Domain.Effects;
using CTEditor.GameDefinition.Domain.Items;
using CTEditor.GameDefinition.Domain.Hazards;
using CTEditor.GameDefinition.Domain.Battlefield;
using CTEditor.Battle.Domain.Actions;
using CTEditor.Battle.Domain.Events;
using CTEditor.Battle.Domain.Formulas;

namespace CTEditor.Battle.Domain.Turn
{
    /// <summary>
    /// El DOMAIN SERVICE que resuelve un turno: el pipeline de fases de M.1 (orden -> resolución ->
    /// chequeo de desenlace), versión 1v1. No guarda estado de partida; recibe el Battle y dos
    /// acciones, muta los combatientes y DEVUELVE una línea de tiempo de eventos (M.2). El dominio
    /// resuelve al instante; la presentación reproducirá esos eventos a su ritmo.
    ///
    /// Sus dependencias son TODAS abstracciones o contenido (catálogo de movimientos, tabla de tipos,
    /// fórmula de daño, azar). Se inyectan: por eso el resolvedor es puro y, con un IRng de semilla
    /// fija, totalmente determinista y testeable.
    /// </summary>
    public sealed partial class TurnResolver
    {
        private readonly ICatalog<Move> _moves;
        private readonly TypeChart _typeChart;
        private readonly IDamageFormula _damageFormula;
        private readonly IRng _rng;
        private readonly ICatalog<StatusConditionDefinition> _statuses; // opcional: null = sin estados
        private readonly ICatchFormula _catchFormula; // opcional: null = sin captura
        private readonly ICatalog<AbilityDefinition> _abilities; // opcional: null = sin habilidades
        private readonly IXpFormula _xpFormula; // opcional: null = sin ganancia de XP
        private readonly bool _awardEffortValues; // ¿se reparten EVs al caer/capturar un rival?
        private readonly bool _usePp;              // ¿los movimientos gastan PP? (regla configurable)
        private readonly Id<Move>? _struggleMove;  // movimiento de emergencia sin PP (Forcejeo)
        private readonly ICatalog<WeatherDefinition> _weathers; // opcional: null = climas sin efectos propios
        private readonly BattleRules _rules;       // críticos y stats del daño (del Ruleset)
        private readonly ICatalog<ItemDefinition> _items; // opcional: null = los objetos equipados no hacen nada
        private readonly ICatalog<HazardDefinition> _hazards; // opcional: null = las trampas de campo no hacen nada
        private readonly ICatalog<SideConditionDefinition> _sideConditions; // opcional: null = sin Reflejo, Pantalla de Luz...

        // TURNO EN PAUSA: Ida y Vuelta / Relevo del JUGADOR esperan a que elija quién entra.
        private List<(bool isPlayer, BattleAction action)> _pendingPlays;
        private int _pendingIndex;
        private bool _pendingPass;
        // Petición de cambio propio hecha por el movimiento que se acaba de resolver.
        private (bool playerSide, bool pass)? _selfSwitchRequest;
        // Profundidad de movimientos llamados (Metrónomo → Espejo → ...): tope para no encadenar sin fin.
        private int _callDepth;
        // Aplicando un movimiento rebotado o robado (Capa Mágica, Robo): no rebota otra vez.
        private bool _reflecting;
        private float _lastEffectiveness = 1f;
        private bool _quickClawPlayer, _quickClawEnemy;   // Garra Rápida: ¿le tocó actuar el primero este turno?   // eficacia del último golpe (Seguro Debilidad)

        // Los que entraron al campo EXPULSANDO a otro este turno (Rugido): no actúan hasta el turno siguiente.
        private readonly HashSet<string> _forcedInThisTurn = new HashSet<string>();

        // El combate que se está resolviendo AHORA (para el clima y las condiciones). Se fija al entrar
        // en ResolveTurn / ResolveBattleStart; el resolvedor no guarda nada más entre llamadas.
        private Battle _battle;

        public TurnResolver(
            ICatalog<Move> moves,
            TypeChart typeChart,
            IDamageFormula damageFormula,
            IRng rng,
            ICatalog<StatusConditionDefinition> statuses = null,
            ICatchFormula catchFormula = null,
            ICatalog<AbilityDefinition> abilities = null,
            IXpFormula xpFormula = null,
            bool awardEffortValues = true,
            bool usePp = true,
            Id<Move>? struggleMove = null,
            ICatalog<WeatherDefinition> weathers = null,
            BattleRules rules = null,
            ICatalog<ItemDefinition> items = null,
            ICatalog<HazardDefinition> hazards = null,
            ICatalog<SideConditionDefinition> sideConditions = null)
        {
            _sideConditions = sideConditions;
            _items = items;
            _hazards = hazards;
            _weathers = weathers;
            _rules = rules ?? BattleRules.Default;
            _moves = moves ?? throw new ArgumentNullException(nameof(moves));
            _typeChart = typeChart ?? throw new ArgumentNullException(nameof(typeChart));
            _damageFormula = damageFormula ?? throw new ArgumentNullException(nameof(damageFormula));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _statuses = statuses; // si es null, las mecánicas de estado quedan inertes (demos/tests viejos)
            _catchFormula = catchFormula; // si es null, los intentos de captura fallan
            _abilities = abilities; // si es null, las habilidades quedan inertes
            _xpFormula = xpFormula; // si es null, no se otorga XP (demos/tests viejos)
            _awardEffortValues = awardEffortValues;
            _usePp = usePp;
            _struggleMove = struggleMove;
        }

        /// <summary>Las reglas con las que resuelve (huida, captura, críticos...).</summary>
        public BattleRules Rules => _rules;

        /// <summary>El catálogo de movimientos (para la interfaz y la capa de partida).</summary>
        public ICatalog<Move> Moves => _moves;

        /// <summary>Resuelve un turno completo y devuelve los eventos en orden.</summary>
        public IReadOnlyList<IDomainEvent> ResolveTurn(Battle battle, BattleAction playerAction, BattleAction enemyAction)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));
            var events = new List<IDomainEvent>();
            if (battle.IsOver) return events;
            _battle = battle;
            ApplyGenerationRules(battle);
            _forcedInThisTurn.Clear();
            _pendingPlays = null;
            _chosenPlayer = playerAction;
            _chosenEnemy = enemyAction;
            battle.Player.ClearActed();
            battle.Enemy.ClearActed();
            foreach (var m in battle.PlayerTeam.Members) m.ResetTurnDamage();
            foreach (var m in battle.EnemyTeam.Members) m.ResetTurnDamage();

            // --- FASE: ORDEN ---
            // Cada "jugada" guarda solo el BANDO y la acción; el actor y el objetivo se calculan en
            // VIVO durante la resolución, para que un cambio de monstruo recoloque el objetivo del rival.
            // --- FASE: MEGAEVOLUCIÓN (antes de ordenar: cuenta ya la nueva Velocidad) ---
            ApplyMegaEvolutions(battle, playerAction, enemyAction, events);

            var plays = new List<(bool isPlayer, BattleAction action)>
            {
                (true, playerAction),
                (false, enemyAction)
            };
            OrderPlays(battle, plays);

            // --- FASE: RESOLUCIÓN (cada jugada, en orden) ---
            if (RunPlays(battle, plays, 0, events)) return events; // en pausa: el jugador elige quién entra
            FinishTurn(battle, events);
            return events;
        }

        /// <summary>¿El turno está en pausa esperando a que el jugador elija quién entra (Ida y Vuelta, Relevo)?</summary>
        public bool IsTurnSuspended => _pendingPlays != null;

        /// <summary>
        /// Continúa el turno en pausa: entra 'incoming' en lugar del que usó Ida y Vuelta / Relevo y se
        /// resuelve lo que quedaba del turno (la otra jugada y el fin de turno).
        /// </summary>
        public IReadOnlyList<IDomainEvent> ResumeTurn(Battle battle, Id<BattleParticipant> incoming)
        {
            var events = new List<IDomainEvent>();
            if (battle == null || _pendingPlays == null) return events;
            _battle = battle;
            ApplyGenerationRules(battle);
            var plays = _pendingPlays;
            int next = _pendingIndex + 1;
            bool pass = _pendingPass;
            _pendingPlays = null;
            if (!PerformSelfSwitch(battle, true, incoming, pass, events))
            {
                // Elección no válida: el turno sigue en pausa.
                _pendingPlays = plays; _pendingIndex = next - 1; _pendingPass = pass;
                return events;
            }
            if (RunPlays(battle, plays, next, events)) return events;
            FinishTurn(battle, events);
            return events;
        }

        // Resuelve las jugadas desde 'start'. Devuelve true si el turno quedó EN PAUSA.
        private bool RunPlays(Battle battle, List<(bool isPlayer, BattleAction action)> plays, int start, List<IDomainEvent> events)
        {
            for (int i = start; i < plays.Count; i++)
            {
                var play = plays[i];
                if (battle.IsOver) break;
                var actor = play.isPlayer ? battle.Player : battle.Enemy;
                var target = play.isPlayer ? battle.Enemy : battle.Player;

                if (actor.IsFainted) continue;       // un debilitado no actúa (esperará relevo)
                if (_forcedInThisTurn.Contains(actor.Id.Value)) continue; // entró por un Rugido: no le toca
                actor.MarkActed();                   // para condiciones como "si el rival ya se movió"
                _selfSwitchRequest = null;

                ResolvePlay(battle, play, actor, target, events);

                // Bayas: se comen en cuanto los PS bajan del umbral (tras cualquier acción).
                CheckHeldTrigger(battle.Player, events);
                CheckHeldTrigger(battle.Enemy, events);

                // CAMBIO PROPIO (Ida y Vuelta, Relevo, Teletransporte contra entrenador).
                if (_selfSwitchRequest.HasValue && !battle.IsOver)
                {
                    var (side, pass) = _selfSwitchRequest.Value;
                    _selfSwitchRequest = null;
                    var mover = side ? battle.Player : battle.Enemy;
                    var team = side ? battle.PlayerTeam : battle.EnemyTeam;
                    if (!mover.IsFainted && team.Reserves().Count > 0)
                    {
                        if (side)
                        {
                            // El JUGADOR elige: el turno se pausa aquí.
                            _pendingPlays = plays; _pendingIndex = i; _pendingPass = pass;
                            events.Add(new SelfSwitchRequiredEvent(mover.Id, pass));
                            return true;
                        }
                        var reserves = team.Reserves();
                        PerformSelfSwitch(battle, false, reserves[_rng.Next(0, reserves.Count)].Id, pass, events);
                    }
                }
            }
            return false;
        }

        // Una jugada: cargas y recargas, movimientos encadenados, estados que impiden actuar y la acción elegida.
        private void ResolvePlay(Battle battle, (bool isPlayer, BattleAction action) play, Combatant actor, Combatant target, List<IDomainEvent> events)
        {
            // RECARGA: tras un movimiento de recarga, este turno se pierde por completo.
            if (actor.MustRecharge)
            {
                events.Add(new RechargingEvent(actor.Id));
                actor.ClearRecharge();
                return;
            }

            // CARGA COMPROMETIDA: si venía cargando, lanza ESE movimiento e ignora la acción elegida.
            // Simplificación deliberada: una carga ya iniciada se libera (no la interrumpe estado/flinch).
            if (actor.ChargingMove.HasValue)
            {
                var release = new UseMove(actor.ChargingMove.Value);
                actor.ClearChargingMove();
                ResolveMove(actor, target, release, events, isChargedRelease: true);
                return;
            }

            // VENGANZA: aguanta los turnos que le quedan y luego desata el daño.
            if (actor.BideMove.HasValue)
            {
                var bide = actor.BideMove.Value;
                if (!actor.AdvanceBide()) { events.Add(new BideStoringEvent(actor.Id, false)); return; }
                ResolveMove(actor, target, new UseMove(bide), events, isChargedRelease: true, payPp: false, bideRelease: true);
                actor.ClearBide();
                return;
            }

            // Los estados (sueño, parálisis, confusión) y el retroceso solo impiden USAR MOVIMIENTOS:
            // cambiarse, huir o usar un objeto siempre se puede (como en los juegos).
            bool usesMove = play.action is UseMove || actor.RampageMove.HasValue;
            // AUSENTE: un turno actúa y el siguiente holgazanea.
            if (usesMove && X(actor).Truant)
            {
                bool loaf = actor.Loafing;
                actor.ToggleLoafing();
                if (loaf) { events.Add(new LoafingEvent(actor.Id)); return; }
            }
            // RONQUIDO / SONÁMBULO (etiqueta «dormido»): se pueden usar dormido; el estado principal no lo impide.
            bool asleepMove = play.action is UseMove chosenUm && _moves.TryGet(chosenUm.Move, out var chosenMv) && chosenMv.HasTag("dormido");
            if (usesMove && IsPreventedByStatus(actor, events, asleepMove)) { actor.ClearRampage(); return; } // p.ej. paralizado/dormido
            if (usesMove && actor.Flinched && !X(actor).FlinchImmune)      // retrocedió: pierde el turno (lo provocó quien actuó antes)
            {
                events.Add(new FlinchedEvent(actor.Id));
                actor.ClearRampage();
                // Impasible: al retroceder, sube su Velocidad.
                var fx = X(actor);
                if (fx.OnFlinchStat.HasValue && fx.OnFlinchStages != 0) ChangeStageFrom(actor, actor, fx.OnFlinchStat.Value, fx.OnFlinchStages, events);
                RunHeld(actor, EffectTrigger.OnFlinch, OpponentOf(actor), null, events);
                return;
            }

            // SAÑA / DANZA PÉTALO: sigue con el mismo movimiento (sin gastar PP) hasta que se acabe.
            if (actor.RampageMove.HasValue)
            {
                var status = actor.RampageEndStatus;
                ResolveMove(actor, target, new UseMove(actor.RampageMove.Value), events, isChargedRelease: true, payPp: false);
                if (actor.AdvanceRampage())
                {
                    events.Add(new RampageEndedEvent(actor.Id));
                    if (status.HasValue && !actor.IsFainted) TryInflictStatus(actor, status.Value, events);
                    actor.ClearRampage();
                }
                return;
            }

            var action = play.action;
            if (action is UseMove chosen)
            {
                // OTRA VEZ: tiene que repetir su último movimiento (si aún puede).
                if (actor.EncoreMove.HasValue)
                {
                    if (actor.IndexOfMove(actor.EncoreMove.Value) >= 0 && actor.CanUse(actor.EncoreMove.Value))
                        action = chosen = new UseMove(actor.EncoreMove.Value);
                    else { actor.ClearEncore(); events.Add(new EncoreEndedEvent(actor.Id)); }
                }
                // ANULACIÓN: ese movimiento no se puede usar.
                if (actor.IsDisabled(chosen.Move))
                {
                    events.Add(new DisabledMoveTriedEvent(actor.Id, chosen.Move));
                    return;
                }
                // MOFA, TORMENTO, CERCA, ANTICURA, HUMEDAD.
                var restriction = RestrictionFor(actor, chosen.Move);
                if (restriction != null)
                {
                    events.Add(new MoveRestrictedEvent(actor.Id, chosen.Move, restriction));
                    return;
                }
            }

            switch (action)
            {
                case Flee _:
                    ResolveFlee(battle, play.isPlayer, actor, target, events);
                    break;
                case SwitchMonster sw:
                    if (IsTrapped(actor, out var trapSw)) { events.Add(new SwitchPreventedEvent(actor.Id, trapSw)); break; }
                    ResolveSwitch(battle, play.isPlayer, sw, events);
                    break;
                case UseItemAction item:
                    ResolveItem(battle, play.isPlayer, item, events);
                    break;
                case Capturar cap:
                    ResolveCapture(battle, play.isPlayer, cap, events);
                    break;
                case UseMove useMove:
                    ResolveMove(actor, target, useMove, events);
                    break;
            }
        }

        // Fin de turno: estados, habilidades, clima, objetos, efectos de lado, anulaciones, experiencia y desenlace.
        private void FinishTurn(Battle battle, List<IDomainEvent> events)
        {
            // --- FASE: FIN DE TURNO (estados: daño residual, progresión, expiración por duración) ---
            if (!battle.IsOver)
            {
                ApplyEndOfTurnStatus(battle.Player, events);
                ApplyEndOfTurnStatus(battle.Enemy, events);
                ApplyEndOfTurnAbilities(battle.Player, events);
                ApplyEndOfTurnAbilities(battle.Enemy, events);
                ApplyEndOfTurnWeather(battle, events);
                ApplyEndOfTurnHeldItem(battle.Player, events);
                ApplyEndOfTurnHeldItem(battle.Enemy, events);
                ApplyEndOfTurnBattlefield(battle, events);
                ApplyGen4EndOfTurn(battle, events);
                ApplyGen6EndOfTurn(battle, events);
                CheckEndOfTurnForms(battle.Player, events);
                CheckEndOfTurnForms(battle.Enemy, events);
            }

            // Protección: si este turno NO se protegió, el contador de "seguidas" vuelve a 0.
            battle.Player.EndTurnGuards();
            battle.Enemy.EndTurnGuards();

            // El retroceso (flinch) solo dura este turno: se limpia siempre.
            battle.Player.ClearFlinch();
            battle.Enemy.ClearFlinch();

            // --- FASE: EXPERIENCIA --- (antes del desenlace, para que "ganó XP" preceda a "ganaste")
            AwardExperience(battle, events);

            // --- FASE: DESENLACE ---
            CheckOutcome(battle, events);
        }

        // Efectos de lado, Anulación y Otra Vez: pierden un turno y avisan al acabar.
        private void ApplyEndOfTurnBattlefield(Battle battle, List<IDomainEvent> events)
        {
            foreach (var id in battle.PlayerTeam.TickSideConditions()) events.Add(new SideConditionEndedEvent(true, id));
            foreach (var id in battle.EnemyTeam.TickSideConditions()) events.Add(new SideConditionEndedEvent(false, id));
            foreach (var c in new[] { battle.Player, battle.Enemy })
            {
                if (c.IsFainted) continue;
                if (c.TickDisable()) events.Add(new DisableEndedEvent(c.Id));
                if (c.TickEncore()) events.Add(new EncoreEndedEvent(c.Id));
            }
        }

        // ¿Terminó el combate? Si no, pide relevos para los activos caídos que aún tienen reservas.
        private static void CheckOutcome(Battle battle, List<IDomainEvent> events)
        {
            if (battle.IsOver) return;
            if (battle.EnemyTeam.IsWipedOut)
            {
                battle.SetOutcome(BattleOutcome.PlayerWon);
                events.Add(new BattleEndedEvent(BattleOutcome.PlayerWon));
            }
            else if (battle.PlayerTeam.IsWipedOut)
            {
                battle.SetOutcome(BattleOutcome.PlayerLost);
                events.Add(new BattleEndedEvent(BattleOutcome.PlayerLost));
            }
            else
            {
                // Algún activo cayó pero su equipo aún tiene reservas: se PIDE relevo y el combate se pausa.
                if (battle.NeedsReplacement(false)) events.Add(new ReplacementRequiredEvent(false));
                if (battle.NeedsReplacement(true)) events.Add(new ReplacementRequiredEvent(true));
            }
        }

        // Procesa un cambio de monstruo (acción de turno). Si el cambio es válido, narra retirada y
        // entrada; si no (id inválido, ya activo o debilitado), se ignora en silencio.
        private void ResolveSwitch(Battle battle, bool isPlayer, SwitchMonster sw, List<IDomainEvent> events)
            => PerformSwitch(battle, isPlayer, sw.Target, events);

        // Cambio de monstruo: el que sale (habilidad al retirarse, pierde volátiles y etapas), el que entra
        // (trampas de su lado, y luego su habilidad al entrar). Lo usan el cambio normal y el forzado.
        private bool PerformSwitch(Battle battle, bool isPlayer, Id<BattleParticipant> targetId, List<IDomainEvent> events)
        {
            var leaving = isPlayer ? battle.Player : battle.Enemy;
            if (!battle.SwitchActive(isPlayer, targetId)) return false;
            var entering = isPlayer ? battle.Player : battle.Enemy;
            events.Add(new MonsterWithdrawnEvent(leaving.Id));

            // Habilidad AL CAMBIARSE (Cura Natural, Regeneración) del que se retira.
            ApplyOnSwitchOut(leaving, events);

            // Al retirarse: se van los volátiles y las etapas; el principal solo si su ficha lo dice.
            leaving.OnSwitchedOut();
            if (leaving.Status.HasValue && TryGetStatus(leaving.Status.Value, out var leavingDef) && leavingDef.ClearedOnSwitch)
                leaving.ClearStatus();

            events.Add(new MonsterSentEvent(entering.Id));
            EnterField(entering, isPlayer, events);
            return true;
        }

        // Lo que pasa al PISAR el campo: primero las trampas de su lado; si sigue en pie, su habilidad al entrar.
        private void EnterField(Combatant entering, bool isPlayer, List<IDomainEvent> events)
        {
            ApplyHazardsOnEntry(entering, isPlayer, events);
            if (entering.IsFainted) return;
            CheckEntryForms(entering, events);
            var opponent = isPlayer ? _battle.Enemy : _battle.Player;
            ApplyOnEntry(entering, opponent, events);
            ApplyGen4OnEntry(entering, opponent, events);
        }

        // ---------------- Cambio propio (Ida y Vuelta, Relevo) ----------------

        // Se retira el activo de 'isPlayer' y entra 'incoming'. Con 'pass' (Relevo) el que entra hereda
        // las etapas, el sustituto y el crítico extra. false si 'incoming' no puede entrar.
        private bool PerformSelfSwitch(Battle battle, bool isPlayer, Id<BattleParticipant> incoming, bool pass, List<IDomainEvent> events)
        {
            var leaving = isPlayer ? battle.Player : battle.Enemy;
            var team = isPlayer ? battle.PlayerTeam : battle.EnemyTeam;
            var inc = team.Find(incoming);
            if (inc == null || inc.IsFainted || ReferenceEquals(inc, leaving)) return false;
            var stages = pass ? leaving.StagesSnapshot() : null;
            int sub = pass ? leaving.SubstituteHp : 0;
            int crit = pass ? leaving.CritBonus : 0;
            events.Add(new SelfSwitchedEvent(leaving.Id, pass));
            if (!PerformSwitch(battle, isPlayer, incoming, events)) return false;
            if (pass)
            {
                var entering = isPlayer ? battle.Player : battle.Enemy;
                if (!entering.IsFainted)
                {
                    entering.SetStages(stages);
                    if (sub > 0) entering.SetSubstitute(sub);
                    if (crit > 0) entering.AddCritBonus(crit);
                }
            }
            return true;
        }

        // ---------------- Efectos de lado (Reflejo, Pantalla de Luz, Neblina...) ----------------

        private BattleTeam SideOf(Combatant c)
        {
            if (_battle == null || c == null) return null;
            if (_battle.PlayerTeam.Find(c.Id) != null) return _battle.PlayerTeam;
            if (_battle.EnemyTeam.Find(c.Id) != null) return _battle.EnemyTeam;
            return null;
        }

        // Las fichas de los efectos de lado activos en el lado de 'c'.
        private IEnumerable<SideConditionDefinition> SideConditionsOf(Combatant c)
        {
            var team = SideOf(c);
            if (team == null || _sideConditions == null || team.SideConditions.Count == 0) yield break;
            foreach (var id in team.SideConditions.Keys)
                if (_sideConditions.TryGet(new Id<SideConditionDefinition>(id), out var def)) yield return def;
        }

        // ¿Algún efecto del lado de 'c' cumple 'flag'? Devuelve cuál.
        private bool SideBlocks(Combatant c, Func<SideConditionDefinition, bool> flag, out string conditionId)
        {
            conditionId = null;
            foreach (var d in SideConditionsOf(c))
                if (flag(d)) { conditionId = d.Id; return true; }
            return false;
        }

        // Multiplicador del daño recibido por 'target' según su lado (Reflejo / Pantalla de Luz).
        private float ScreenMultiplier(Combatant target, MoveCategory category)
        {
            float m = 1f;
            foreach (var d in SideConditionsOf(target))
                m *= category == MoveCategory.Physical ? d.PhysicalDamageMultiplier
                   : category == MoveCategory.Special ? d.SpecialDamageMultiplier : 1f;
            return m;
        }

        // REGLAS DE GENERACIÓN sobre los combatientes: con «Especial único» (1.ª gen.) las etapas de Atq. Esp. y
        // Def. Esp. se mueven juntas. Idempotente: se llama al entrar en cada fase del combate.
        private void ApplyGenerationRules(Battle battle)
        {
            (StatId, StatId)? link = _rules.Generation.SingleSpecialStat ? (_rules.SpecialAttack, _rules.SpecialDefense) : ((StatId, StatId)?)null;
            foreach (var m in battle.PlayerTeam.Members) m.LinkStages(link);
            foreach (var m in battle.EnemyTeam.Members) m.LinkStages(link);
        }

        // Categoría con la que hace daño según las reglas (1.ª-3.ª gen.: la decide el tipo real del movimiento).
        private MoveCategory CategoryOf(Combatant actor, Move move) => _rules.CategoryOf(move, MoveTypeOf(actor, move).Value);

        private void SetSideCondition(Combatant actor, Combatant sideOf, string id, List<IDomainEvent> events)
        {
            var team = SideOf(sideOf);
            if (team == null || _sideConditions == null || !_sideConditions.TryGet(new Id<SideConditionDefinition>(id), out var def))
            { events.Add(new MoveFailedEvent(actor.Id)); return; }
            // CAMPOS (grupo): el nuevo quita a los demás del mismo grupo, estén en el lado que estén.
            if (!string.IsNullOrEmpty(def.Group) && !team.HasSideCondition(def.Id)) ClearGroup(def.Group, def.Id, team, events);
            // Refleluz: las pantallas (Reflejo, Pantalla de Luz, Velo Aurora) duran más.
            int turns = def.Turns;
            bool isScreen = def.PhysicalDamageMultiplier < 1f || def.SpecialDamageMultiplier < 1f;
            if (isScreen && turns > 0 && actor != null) turns += (int)HeldSum(actor, EffectAction.ExtendScreens, OpponentOf(actor), null);
            if (!team.AddSideCondition(def.Id, turns))
            { events.Add(new MoveFailedEvent(actor.Id)); return; }
            events.Add(new SideConditionStartedEvent(ReferenceEquals(team, _battle.PlayerTeam), def.Id));
        }

        // ---------------- Trampas de campo ----------------

        private bool IsOnPlayerSide(Combatant c) => _battle != null && _battle.PlayerTeam.Find(c.Id) != null;

        private void ApplyHazardsOnEntry(Combatant entering, bool isPlayer, List<IDomainEvent> events)
        {
            if (_hazards == null || _battle == null || entering == null || entering.IsFainted) return;
            var team = isPlayer ? _battle.PlayerTeam : _battle.EnemyTeam;
            if (team.Hazards.Count == 0) return;

            foreach (var kv in new List<KeyValuePair<string, int>>(team.Hazards))
            {
                if (!_hazards.TryGet(new Id<HazardDefinition>(kv.Key), out var def)) continue;

                // Un tipo que la ABSORBE la retira de su lado (Veneno con Púas Tóxicas) y no le afecta.
                if (HasAnyType(entering, def.AbsorbedByTypes))
                {
                    team.ClearHazards(kv.Key);
                    events.Add(new HazardAbsorbedEvent(entering.Id, kv.Key));
                    continue;
                }
                // Inmunes: por tipo (Volador) o por habilidad (Levitación = inmune a Tierra).
                if (HasAnyType(entering, def.ImmuneTypes)) continue;
                if (def.ImmuneIfAbilityBlocksType.HasValue && TryGetAbility(entering, out var ab) && ab.IsImmuneToType(def.ImmuneIfAbilityBlocksType.Value))
                    continue;

                int layers = kv.Value;
                float pct = def.DamagePercentFor(layers);
                if (pct > 0f && !X(entering).NoIndirectDamage)
                {
                    float mult = def.DamageScalesWithType.HasValue
                        ? _typeChart.Effectiveness(def.DamageScalesWithType.Value, entering.Types).Multiplier : 1f;
                    if (mult > 0f)
                    {
                        int dmg = Math.Max(1, (int)(entering.MaxHp * pct / 100f * mult));
                        entering.TakeDamage(dmg);
                        events.Add(new HazardDamageEvent(entering.Id, kv.Key, dmg));
                        if (entering.IsFainted) { events.Add(new MonsterFaintedEvent(entering.Id)); return; }
                    }
                }

                var status = def.StatusFor(layers);
                if (status != null && _statuses != null) TryInflictStatus(entering, new StatusId(status), events);

                if (def.Stat.HasValue && def.Stages != 0)
                {
                    int applied = entering.ChangeStage(def.Stat.Value, def.Stages);
                    if (applied != 0) events.Add(new StatStageChangedEvent(entering.Id, def.Stat.Value, applied));
                }
            }
        }

        private static bool HasAnyType(Combatant c, IReadOnlyList<Id<ElementType>> types)
        {
            foreach (var t in types)
                foreach (var own in c.Types)
                    if (own == t) return true;
            return false;
        }

        private void SetHazard(Combatant actor, Combatant sideOf, string hazardId, List<IDomainEvent> events)
        {
            if (_battle == null || _hazards == null || sideOf == null ||
                !_hazards.TryGet(new Id<HazardDefinition>(hazardId), out var def)) { events.Add(new MoveFailedEvent(actor.Id)); return; }
            bool playerSide = IsOnPlayerSide(sideOf);
            var team = playerSide ? _battle.PlayerTeam : _battle.EnemyTeam;
            int layers = team.AddHazardLayer(def.Id, def.MaxLayers);
            if (layers == 0) { events.Add(new MoveFailedEvent(actor.Id)); return; } // ya al máximo
            events.Add(new HazardSetEvent(playerSide, def.Id, layers));
        }

        private void ClearHazards(Combatant sideOf, string hazardId, List<IDomainEvent> events)
        {
            if (_battle == null || sideOf == null) return;
            bool playerSide = IsOnPlayerSide(sideOf);
            var removed = (playerSide ? _battle.PlayerTeam : _battle.EnemyTeam).ClearHazards(hazardId);
            if (removed.Count > 0) events.Add(new HazardsClearedEvent(playerSide, removed));
        }

        // RUGIDO / REMOLINO: en un combate salvaje, el combate termina. Contra un entrenador, el objetivo
        // se retira y entra otro de su equipo AL AZAR (que no actúa este turno). Sin reservas: falla.
        private void ForceSwitch(Combatant actor, Combatant target, List<IDomainEvent> events)
        {
            if (_battle == null || target == null || target.IsFainted || ReferenceEquals(target, actor)) return;
            // Ventosas y Arraigo: no se le puede sacar.
            if (X(target).ForcedSwitchImmune || HasVolatileFlag(target, d => d.Extras.Grounded && d.PreventsSwitch))
            { events.Add(new MoveFailedEvent(actor.Id)); return; }
            if (!_battle.IsTrainerBattle)
            {
                events.Add(new ForcedOutEvent(target.Id, true));
                _battle.SetOutcome(BattleOutcome.Fled);
                events.Add(new BattleEndedEvent(BattleOutcome.Fled));
                return;
            }
            bool playerSide = IsOnPlayerSide(target);
            var reserves = (playerSide ? _battle.PlayerTeam : _battle.EnemyTeam).Reserves();
            if (reserves.Count == 0) { events.Add(new MoveFailedEvent(actor.Id)); return; }
            var next = reserves[_rng.Next(0, reserves.Count)];
            events.Add(new ForcedOutEvent(target.Id, false));
            if (PerformSwitch(_battle, playerSide, next.Id, events)) _forcedInThisTurn.Add(next.Id.Value);
        }

        // El que se RETIRA puede curarse el estado (Cura Natural) y/o recuperar PS (Regeneración).
        private void ApplyOnSwitchOut(Combatant leaving, List<IDomainEvent> events)
        {
            if (leaving == null || leaving.IsFainted) return;
            RunHeld(leaving, EffectTrigger.OnSwitchOut, OpponentOf(leaving), null, events);
            if (!TryGetAbility(leaving, out var ability)) return;

            if (ability.CuresStatusOnSwitchOut && leaving.Status.HasValue)
            {
                var was = leaving.Status.Value;
                leaving.ClearStatus();
                events.Add(new StatusFadedEvent(leaving.Id, was));
            }

            float healFrac = ability.HealPercentOnSwitchOut.AsFraction;
            if (healFrac > 0f)
            {
                int heal = (int)(leaving.MaxHp * healFrac);
                if (heal > 0)
                {
                    leaving.HealHp(heal);
                    events.Add(new HpRestoredEvent(leaving.Id, heal));
                }
            }
        }

        // Aplica el efecto "al entrar" de la habilidad del combatiente que acaba de pisar el campo.
        private void ApplyOnEntry(Combatant entering, Combatant opponent, List<IDomainEvent> events)
        {
            ApplyEntryHeldItem(entering, opponent, events);
            if (!TryGetAbility(entering, out var ability)) return;
            if (!ability.OnEntryStat.HasValue || ability.OnEntryStages == 0) return;

            var affected = ability.OnEntryTargetsSelf ? entering : opponent;
            if (affected.IsFainted) return;

            // Cuerpo Puro: si el efecto al entrar le bajaría una etapa al RIVAL, lo impide.
            if (!ability.OnEntryTargetsSelf && ability.OnEntryStages < 0
                && TryGetAbility(affected, out var cbAb) && cbAb.PreventsStatReduction)
                return;

            int applied = affected.ChangeStage(ability.OnEntryStat.Value, ability.OnEntryStages);
            if (applied != 0)
                events.Add(new StatStageChangedEvent(affected.Id, ability.OnEntryStat.Value, applied));
        }

        /// <summary>
        /// Aplica las habilidades "al entrar" de AMBOS activos al comenzar el combate (Intimidación...).
        /// El orquestador lo llama UNA vez, tras construir el Battle y antes del primer turno.
        /// </summary>
        public IReadOnlyList<IDomainEvent> ResolveBattleStart(Battle battle)
        {
            var events = new List<IDomainEvent>();
            if (battle == null) return events;
            _battle = battle;
            ApplyGenerationRules(battle);
            CheckEntryForms(battle.Player, events);
            CheckEntryForms(battle.Enemy, events);
            ApplyOnEntry(battle.Player, battle.Enemy, events);
            ApplyOnEntry(battle.Enemy, battle.Player, events);
            ApplyGen4OnEntry(battle.Player, battle.Enemy, events);
            ApplyGen4OnEntry(battle.Enemy, battle.Player, events);
            return events;
        }

        /// <summary>Aplica la habilidad "al entrar" del nuevo activo de un bando tras un relevo forzado.</summary>
        public IReadOnlyList<IDomainEvent> ResolveReplacementEntry(Battle battle, bool playerSide)
        {
            var events = new List<IDomainEvent>();
            if (battle == null) return events;
            _battle = battle;
            ApplyGenerationRules(battle);
            var entering = playerSide ? battle.Player : battle.Enemy;
            EnterField(entering, playerSide, events);
            // Si las trampas lo debilitan al entrar: experiencia y, quizá, fin del combate.
            if (entering.IsFainted)
            {
                AwardExperience(battle, events);
                CheckOutcome(battle, events);
            }
            return events;
        }

        // Usa un objeto sobre un miembro del PROPIO equipo (curar / revivir / quitar estado). El
        // 'qué hace' viene ya traducido en BattleItemEffect: Battle no conoce las fichas de objetos.
        private void ResolveItem(Battle battle, bool isPlayer, UseItemAction action, List<IDomainEvent> events)
        {
            var team = isPlayer ? battle.PlayerTeam : battle.EnemyTeam;
            var target = team.Find(action.Target);
            if (target == null) return; // objetivo inválido: se ignora

            var fx = action.Effect;
            events.Add(new ItemUsedInBattleEvent(target.Id));

            if (fx.Revives && target.IsFainted)
            {
                int reviveHp = fx.HealAmount > 0 ? fx.HealAmount : (int)(target.MaxHp * fx.ReviveHpPercent / 100f);
                target.Revive(Math.Max(1, reviveHp));
                events.Add(new HpRestoredEvent(target.Id, target.CurrentHp));
            }
            else if (fx.HealAmount > 0 && !target.IsFainted)
            {
                int before = target.CurrentHp;
                target.HealHp(fx.HealAmount);
                int healed = target.CurrentHp - before;
                if (healed > 0) events.Add(new HpRestoredEvent(target.Id, healed));
            }

            if (fx.HealPercent > 0f && !target.IsFainted)
            {
                int before = target.CurrentHp;
                target.HealHp((int)(target.MaxHp * fx.HealPercent / 100f));
                if (target.CurrentHp > before) events.Add(new HpRestoredEvent(target.Id, target.CurrentHp - before));
            }

            if (fx.CuresStatus && target.Status.HasValue &&
                (fx.CuresStatusId.Length == 0 || Array.IndexOf(fx.CuresStatusId.Split('|'), target.Status.Value.Value) >= 0))
            {
                var cleared = target.Status.Value;
                target.ClearStatus();
                events.Add(new StatusFadedEvent(target.Id, cleared));
            }

            // Objetos de combate (Ataque X): cambian una etapa del objetivo (solo si está en el campo).
            if (fx.Stat.HasValue && fx.Stages != 0 && !target.IsFainted)
            {
                int applied = target.ChangeStage(fx.Stat.Value, fx.Stages);
                if (applied != 0) events.Add(new StatStageChangedEvent(target.Id, fx.Stat.Value, applied));
            }

            // Éter / Elixir.
            if (fx.RestorePp > 0) RestorePp(target, fx.RestorePp, fx.RestorePpAllMoves);
        }

        // HUIR, con las reglas clásicas:
        //   - atrapado (Atadura...) → no puede.
        //   - contra un ENTRENADOR → no se puede (salvo que las reglas lo permitan).
        //   - contra un salvaje: si eres igual o más rápido, escapas; si no, fórmula de 3ª/4ª gen.:
        //       F = (tuVelocidad × 128 / suVelocidad) + 30 × intentos;  escapas si un azar 0-255 < F.
        //     Cada intento fallido facilita el siguiente. (Regla "huir siempre funciona": escapa siempre.)
        private void ResolveFlee(Battle battle, bool isPlayer, Combatant actor, Combatant opponent, List<IDomainEvent> events)
        {
            if (!X(actor).AlwaysEscapes && IsTrapped(actor, out var trapFlee)) { events.Add(new SwitchPreventedEvent(actor.Id, trapFlee)); return; }
            if (battle.IsTrainerBattle && !_rules.CanFleeTrainerBattles) { events.Add(new FleeBlockedEvent(actor.Id)); return; }

            bool escaped = true;
            if (isPlayer && !_rules.FleeAlwaysWorks && !X(actor).AlwaysEscapes)
            {
                battle.NoteFleeAttempt();
                int mine = EffectiveStat(actor, StatId.Speed);
                int theirs = opponent == null || opponent.IsFainted ? 0 : EffectiveStat(opponent, StatId.Speed);
                if (mine < theirs)
                {
                    int f = mine * 128 / Math.Max(1, theirs) + 30 * battle.FleeAttempts;
                    escaped = f > 255 || _rng.Next(0, 256) < f;
                }
            }

            if (!escaped) { events.Add(new FleeFailedEvent(actor.Id)); return; }
            battle.SetOutcome(BattleOutcome.Fled);
            events.Add(new BattleEndedEvent(BattleOutcome.Fled));
        }

        /// <summary>
        /// Probabilidad (0-1) de que el JUGADOR escape ahora (para mostrarla en la interfaz). No tira
        /// dados ni cambia nada.
        /// </summary>
        public double FleeChance(Battle battle)
        {
            if (battle == null || battle.IsOver) return 0;
            if (IsTrapped(battle.Player, out _)) return 0;
            if (battle.IsTrainerBattle && !_rules.CanFleeTrainerBattles) return 0;
            if (_rules.FleeAlwaysWorks) return 1;
            int mine = EffectiveStat(battle.Player, StatId.Speed);
            int theirs = battle.Enemy.IsFainted ? 0 : EffectiveStat(battle.Enemy, StatId.Speed);
            if (mine >= theirs) return 1;
            int f = mine * 128 / Math.Max(1, theirs) + 30 * (battle.FleeAttempts + 1);
            return f > 255 ? 1 : f / 256.0;
        }

        // CAPTURAR al rival activo con la fórmula clásica (con sacudidas). Contra un entrenador no se
        // puede (salvo que las reglas lo permitan). Éxito → fin del combate (Caught).
        private void ResolveCapture(Battle battle, bool isPlayer, Capturar action, List<IDomainEvent> events)
        {
            var target = isPlayer ? battle.Enemy : battle.Player;
            if (battle.IsTrainerBattle && !_rules.CanCatchTrainerMonsters)
            {
                events.Add(new CaptureBlockedEvent(target.Id));
                return;
            }

            var attempt = _catchFormula != null
                ? _catchFormula.Attempt(CatchContextFor(target, action.CatchBonus), _rng)
                : new CatchAttempt(false, 0);

            if (attempt.Caught)
            {
                events.Add(new MonsterCapturedEvent(target.Id, attempt.Shakes));
                battle.SetOutcome(BattleOutcome.Caught);
                events.Add(new BattleEndedEvent(BattleOutcome.Caught));
            }
            else
            {
                events.Add(new CaptureFailedEvent(target.Id, attempt.Shakes));
            }
        }

        /// <summary>Contexto de captura de un objetivo con una bola (PS, ratio, estado y reglas).</summary>
        public CatchContext CatchContextFor(Combatant target, float ballMultiplier)
        {
            float statusMult = 1f;
            if (target.Status.HasValue && TryGetStatus(target.Status.Value, out var def)) statusMult = def.CatchMultiplier;
            return new CatchContext(target.MaxHp, target.CurrentHp, target.CatchRate, ballMultiplier, statusMult, _rules.CatchRateMultiplier);
        }

        // Resuelve un UseMove: emite el evento de uso, tira precisión, y si conecta calcula el daño
        // (si lo hay) y aplica los efectos del movimiento (infligir estado, etc.), incluso para los
        // movimientos de Estado sin daño como Fuego Fatuo.
        private void ResolveMove(Combatant actor, Combatant target, UseMove useMove, List<IDomainEvent> events, bool isChargedRelease = false,
            bool payPp = true, bool bideRelease = false)
        {
            // Rompemoldes: mientras ataca, la habilidad del objetivo no cuenta.
            var previousIgnored = _abilityIgnored;
            if (target != null && target != actor && X(actor).MoldBreaker) _abilityIgnored = target;
            int start = events.Count;
            try { ResolveMoveCore(actor, target, useMove, events, isChargedRelease, payPp, bideRelease); }
            finally { _abilityIgnored = previousIgnored; }

            // ¿Salió bien? (para Rodar, Corte Furia y Última Baza)
            bool used = false, failed = false;
            Id<Move> usedMove = useMove.Move;
            for (int i = start; i < events.Count; i++)
            {
                var e = events[i];
                if (e is MoveUsedEvent mu && mu.Attacker == actor.Id) { used = true; usedMove = mu.Move; }
                if (e is MoveMissedEvent mm && mm.Attacker == actor.Id || e is MoveFailedEvent mf && mf.Combatant == actor.Id
                    || e is MoveBlockedEvent || e is MoveHadNoEffectEvent) failed = true;
            }
            if (used) actor.NoteMoveSuccess(usedMove, !failed);
            // Meloetta: cambia DESPUÉS de usar su movimiento.
            if (used && !actor.IsFainted && _moves.TryGet(usedMove, out var formMove)) CheckMoveForms(actor, formMove, true, events);

            // OBJETOS ELECCIÓN: se queda bloqueado en el primer movimiento que usa (hasta que se retire).
            if (!HeldHas(actor, EffectAction.ChoiceLock, OpponentOf(actor), null)) actor.ClearChoiceLock();
            else if (used && (!_struggleMove.HasValue || usedMove != _struggleMove.Value)) actor.LockChoice(usedMove);

            // CARGA: el refuerzo de tipo se gasta al usar ese tipo.
            if (used && !failed && _statuses != null && _moves.TryGet(usedMove, out var um) && um.DealsDirectDamage)
                foreach (var v in new List<ActiveVolatileStatus>(actor.Volatiles))
                    if (TryGetStatus(v.Id, out var d) && d.Extras.BoostConsumed && d.Extras.BoostedType.HasValue && d.Extras.BoostedType.Value == MoveTypeOf(actor, um))
                        actor.RemoveVolatile(v.Id);
        }

        private void ResolveMoveCore(Combatant actor, Combatant target, UseMove useMove, List<IDomainEvent> events, bool isChargedRelease,
            bool payPp, bool bideRelease)
        {
            // PP: se gastan aquí, cuando el movimiento REALMENTE se ejecuta (si la parálisis o el
            // retroceso impidieron actuar, no se llega aquí y no se gasta nada, como en los juegos).
            // Al soltar un movimiento cargado no se gasta: ya se pagó en el turno de carga.
            if (payPp && !isChargedRelease && !PayPp(actor, ref useMove, events)) return;

            var move = _moves.Get(useMove.Move); // resuelve el id -> Move vía catálogo

            // DOS TURNOS (CARGA): el primer uso solo carga; el golpe llega al turno siguiente.
            if (move.TwoTurn == TwoTurnKind.Charge && !isChargedRelease)
            {
                actor.SetChargingMove(move.Id);
                events.Add(new ChargingStartedEvent(actor.Id, move.Id));
                return;
            }

            CheckMoveForms(actor, move, false, events);   // Aegislash: cambia ANTES de golpear
            events.Add(new MoveUsedEvent(actor.Id, move.Id));
            actor.NoteMoveUsed(move.Id);

            // FURIA: se calma si usa cualquier otro movimiento.
            if (actor.Raging && !HasEffect(move, MoveEffectKind.Rage)) actor.ClearRage();

            // VENGANZA: el primer uso solo empieza a aguantar; al soltarlo, si no recibió nada, falla.
            if (move.FixedDamage == FixedDamageKind.Bide)
            {
                if (!bideRelease)
                {
                    actor.StartBide(move.Id, 2);
                    events.Add(new BideStoringEvent(actor.Id, true));
                    return;
                }
                events.Add(new BideUnleashedEvent(actor.Id));
                if (actor.BideStored <= 0) { events.Add(new MoveFailedEvent(actor.Id)); return; }
            }

            // CONTRAATAQUE / MANTO ESPEJO: fallan si este turno no recibió daño de esa clase.
            if (move.FixedDamage == FixedDamageKind.ReturnPhysical && actor.PhysicalDamageTakenThisTurn <= 0
                || move.FixedDamage == FixedDamageKind.ReturnSpecial && actor.SpecialDamageTakenThisTurn <= 0
                // REPRESIÓN METAL: falla si no le dieron este turno. ESFUERZO: falla si el rival no tiene más PS.
                || move.FixedDamage == FixedDamageKind.ReturnAny && (!actor.DamagedThisTurn || actor.LastDamageTaken <= 0)
                || move.FixedDamage == FixedDamageKind.Endeavor && target != null && target.CurrentHp <= actor.CurrentHp)
            {
                events.Add(new MoveFailedEvent(actor.Id));
                return;
            }

            // REQUISITOS del movimiento (Comesueños solo contra un rival dormido...): si no se cumplen, falla.
            // Sin rival (llamado por otro movimiento, rival ya caído...): un requisito sobre el rival no se cumple.
            if (move.Requirements.Count > 0 && (target == null && move.Requirements.Any(c => c.Subject == ConditionSubject.Other)
                                                || !AllConditions(move.Requirements, actor, target ?? actor, move)))
            {
                events.Add(new MoveFailedEvent(actor.Id));
                return;
            }

            bool aimed = target != null && target != actor && move.Target != MoveTarget.Self;

            // INMUNE POR ETIQUETA (Insonorizar: sonido; Surcavientos: viento). Puede subirle una estadística.
            if (aimed)
                foreach (var tag in X(target).ImmuneToMoveTags)
                    if (move.HasTag(tag))
                    {
                        events.Add(new AbilityTriggeredEvent(target.Id, AbilityIdOf(target), "inmune"));
                        events.Add(new MoveHadNoEffectEvent(actor.Id, target.Id, move.Id));
                        var ix = X(target);
                        if (ix.OnImmuneStat.HasValue && ix.OnImmuneStages != 0) ChangeStageFrom(target, target, ix.OnImmuneStat.Value, ix.OnImmuneStages, events);
                        RunHeld(target, EffectTrigger.OnAbsorb, actor, move, events);
                        return;
                    }

            // PROTECCIÓN: si el objetivo se protegió este turno, lo que se le lanza no le hace nada
            // (salvo Amago, que rompe la protección).
            if (aimed && IsProtectedFrom(target, move))
            {
                if (move.HasTag("rompe_proteccion"))
                {
                    foreach (var v in new List<ActiveVolatileStatus>(target.Volatiles))
                        if (TryGetStatus(v.Id, out var pd) && pd.BlocksIncomingMoves) { target.RemoveVolatile(v.Id); events.Add(new StatusFadedEvent(target.Id, v.Id)); }
                }
                else
                {
                    events.Add(new MoveBlockedEvent(target.Id));
                    // Escudo Real / Barrera Espinosa: castigan al que les golpea con contacto.
                    PunishProtectContact(actor, target, move, events);
                    return;
                }
            }

            // CAPA MÁGICA / ESPEJO MÁGICO: los movimientos de ESTADO que le lanzan rebotan al que los usó.
            if (aimed && move.Category == MoveCategory.Status && !_reflecting
                && (X(target).MagicBounce || HasVolatileFlag(target, d => d.Extras.ReflectsStatusMoves)))
            {
                events.Add(new MoveReflectedEvent(target.Id, move.Id, false));
                _reflecting = true;
                try { ApplyMoveEffects(target, actor, move, 0, events); }
                finally { _reflecting = false; }
                return;
            }
            // ROBO: las mejoras que el rival se pone a sí mismo se las queda quien usó Robo.
            if (!aimed && move.Category == MoveCategory.Status && !_reflecting)
            {
                var thief = OpponentOf(actor);
                if (thief != null && !thief.IsFainted && HasVolatileFlag(thief, d => d.Extras.StealsBoostMoves)
                    && move.SecondaryEffects.Any(e => e.Target == EffectTarget.Self && (e.Kind == MoveEffectKind.ChangeStatStage && e.Stages > 0
                        || e.Kind == MoveEffectKind.Heal || e.Kind == MoveEffectKind.HealSelf)))
                {
                    events.Add(new MoveReflectedEvent(thief.Id, move.Id, true));
                    _reflecting = true;
                    try { ApplyMoveEffects(thief, actor, move, 0, events); }
                    finally { _reflecting = false; }
                    return;
                }
            }

            // KO EN UN GOLPE (Guillotina): falla siempre contra un objetivo de MÁS nivel (y contra Robustez).
            if (move.FixedDamage == FixedDamageKind.OneHitKo && target != null)
            {
                if (target.Level > actor.Level) { events.Add(new MoveMissedEvent(actor.Id, move.Id)); return; }
                if (X(target).OhkoImmune)
                {
                    events.Add(new AbilityTriggeredEvent(target.Id, AbilityIdOf(target), "robustez"));
                    events.Add(new MoveHadNoEffectEvent(actor.Id, target.Id, move.Id));
                    return;
                }
            }

            // PRECISIÓN (con Indefenso, Fijar Blanco, semi-invulnerables, Gravedad, habilidades y etapas).
            if (!bideRelease && !RollHit(actor, target, move))
            {
                events.Add(new MoveMissedEvent(actor.Id, move.Id));
                return;
            }

            // MUTATIPO: pasa a ser del tipo del movimiento que usa.
            if (X(actor).Protean && !actor.IsFainted)
            {
                var pt = MoveTypeOf(actor, move);
                if (!(actor.Types.Count == 1 && actor.Types[0] == pt))
                {
                    actor.ChangeTypes(new[] { pt });
                    events.Add(new AbilityTriggeredEvent(actor.Id, AbilityIdOf(actor), "mutatipo"));
                    events.Add(new TypeChangedEvent(actor.Id, new[] { pt }));
                }
            }

            // INMUNIDAD DE TIPO EN MOVIMIENTOS DE ESTADO (opcional por movimiento): Onda Trueno no afecta
            // a un Tierra. La mayoría de movimientos de estado ignoran la tabla de tipos, por eso es opcional.
            if (!move.DealsDirectDamage && move.RespectsTypeImmunity && target != null
                && EffectivenessAgainst(actor, target, move, out _) <= 0f)
            {
                events.Add(new MoveHadNoEffectEvent(actor.Id, target.Id, move.Id));
                return;
            }

            // Movimientos sin daño directo (categoría Estado, como Fuego Fatuo): no calculan daño,
            // pero SÍ aplican sus efectos abajo. Por eso ya no salimos aquí.
            int damageDealt = 0;
            _lastEffectiveness = 1f;
            bool hitSubstitute = false;
            bool lastCrit = false;
            // PREMONICIÓN / DESEO DE MUERTE: no golpea ahora; el daño llega unos turnos después (efecto retardado).
            if (move.DealsDirectDamage && !HasEffect(move, MoveEffectKind.DelayedDamage))
            {
                // PREPARACIÓN DEL GOLPE (efectividad, inmunidad por habilidad, STAB, stats): en un método
                // aparte y SIN efectos, para que la vista previa de daño del editor use exactamente lo mismo.
                var setup = PrepareHit(actor, target, move);
                float effectiveness = setup.effectiveness;
                _lastEffectiveness = effectiveness;
                bool stab = setup.stab;
                float stabMult = setup.stabMult;
                int attackStat = setup.attackStat;
                int defenseStat = setup.defenseStat;
                int power = setup.power;

                // Absorbe Fuego, Electromotor, Pararrayos, Herbívoro: además de anular, se potencian.
                if (setup.abilityImmune && TryGetAbility(target, out var absorbAb) && absorbAb.IsImmuneToType(MoveTypeOf(actor, move)))
                {
                    var ax = absorbAb.Extras;
                    if (ax.OnImmuneStat.HasValue && ax.OnImmuneStages != 0)
                    {
                        events.Add(new AbilityTriggeredEvent(target.Id, absorbAb.Id.Value, "absorbe"));
                        ChangeStageFrom(target, target, ax.OnImmuneStat.Value, ax.OnImmuneStages, events);
                    }
                    if (ax.BoostsAbsorbedType)
                    {
                        target.SetAbsorbedTypeBoost(MoveTypeOf(actor, move));
                        events.Add(new AbilityTriggeredEvent(target.Id, absorbAb.Id.Value, "absorbe_potencia"));
                    }
                    RunHeld(target, EffectTrigger.OnAbsorb, actor, move, events);
                }

                // Si ABSORBE el tipo (Absorbe Agua), cura un % de PS máx en vez de solo anular.
                if (setup.abilityImmune && TryGetAbility(target, out var targetAbility))
                {
                    float healFrac = targetAbility.AbsorbImmuneHealPercent.AsFraction;
                    if (healFrac > 0f && !target.IsFainted)
                    {
                        int heal = (int)(target.MaxHp * healFrac);
                        if (heal > 0)
                        {
                            target.HealHp(heal);
                            events.Add(new HpRestoredEvent(target.Id, heal));
                        }
                    }
                }

                // INMUNE (tipo o habilidad): no hay daño NI efectos (Rayo no paraliza a un Tierra, y un
                // golpe que no conecta tampoco activa retroceso, drenaje ni subidas propias).
                if (effectiveness <= 0f)
                {
                    events.Add(new DamageDealtEvent(target.Id, 0, 0f));
                    return;
                }

                // GOLPE MÚLTIPLE: cuántas veces impacta (1 si es normal; al azar en el rango si no).
                int hits = move.MaxHits <= move.MinHits || X(actor).SkillLink ? move.MaxHits : _rng.Next(move.MinHits, move.MaxHits + 1);
                bool anyCrit = false;

                for (int h = 0; h < hits; h++)
                {
                    // Cada impacto recalcula su daño: la aleatoriedad y el crítico se tiran por golpe.
                    var result = ComputeHit(actor, target, move, effectiveness, stab, stabMult, attackStat, defenseStat, power, _rng);
                    int damage = result.Damage;
                    // BAYAS DE RESISTENCIA: el primer golpe muy eficaz de su tipo hace la mitad.
                    if (target != actor && !(target.HasSubstitute && !X(actor).Infiltrator))
                        damage = ApplyDamageTakenBlocks(target, actor, move, MoveTypeOf(actor, move), damage, events);
                    anyCrit |= result.WasCritical;
                    lastCrit = anyCrit;

                    // SUSTITUTO: si el rival tiene uno, el golpe se lo lleva él (Allanamiento lo atraviesa).
                    if (target != actor && target.HasSubstitute && !X(actor).Infiltrator)
                    {
                        hitSubstitute = true;
                        int taken = target.DamageSubstitute(damage);
                        damageDealt += taken;
                        events.Add(new SubstituteDamagedEvent(target.Id, taken));
                        if (result.WasCritical) events.Add(new CriticalHitEvent(target.Id));
                        if (!target.HasSubstitute) { events.Add(new SubstituteBrokeEvent(target.Id)); break; }
                        continue;
                    }

                    // AGUANTE: un golpe que lo debilitaría lo deja con 1 PS.
                    if (damage >= target.CurrentHp && target.CurrentHp > 1 && HasVolatileFlag(target, d => d.SurvivesLethalHit))
                    {
                        damage = target.CurrentHp - 1;
                        events.Add(new EnduredEvent(target.Id));
                    }
                    // ROBUSTEZ: con los PS al máximo, aguanta con 1.
                    else if (damage >= target.CurrentHp && target.CurrentHp > 1 && target.CurrentHp == target.MaxHp && target != actor && X(target).SurvivesFromFullHp)
                    {
                        damage = target.CurrentHp - 1;
                        events.Add(new AbilityTriggeredEvent(target.Id, AbilityIdOf(target), "robustez"));
                        events.Add(new EnduredEvent(target.Id));
                    }
                    // BANDA FOCUS: igual que Robustez, pero es un objeto y se gasta.
                    else if (target != actor) damage = ApplySurviveBlocks(target, actor, move, damage, events);

                    damageDealt += damage;

                    target.TakeDamage(damage);
                    if (target != actor) target.NoteDamageTaken(damage, CategoryOf(actor, move));
                    // El daño especial no es "muy eficaz" ni "poco eficaz": se narra como normal (salvo inmunidad).
                    float shownEffectiveness = move.FixedDamage != FixedDamageKind.None && effectiveness > 0f ? 1f : effectiveness;
                    events.Add(new DamageDealtEvent(target.Id, damage, shownEffectiveness));
                    if (result.WasCritical) events.Add(new CriticalHitEvent(target.Id));

                    // FURIA: cada golpe que recibe le enfurece más.
                    if (!target.IsFainted && target.Raging && damage > 0 && target != actor)
                    {
                        int applied = target.ChangeStage(target.RageStat, target.RageStages);
                        if (applied != 0) { events.Add(new RageBuildingEvent(target.Id)); events.Add(new StatStageChangedEvent(target.Id, target.RageStat, applied)); }
                    }

                    if (target.IsFainted)
                    {
                        events.Add(new MonsterFaintedEvent(target.Id));
                        // Objetivo debilitado: no hay más golpes, PERO los efectos sobre el propio atacante
                        // SÍ ocurren (como en los juegos: Doble Filo te daña y Megaagotar te cura aunque
                        // debilites al rival). Los efectos sobre el rival ya ignoran a un debilitado.
                        break;
                    }
                }
            }

            // EFECTOS DEL MOVIMIENTO: se aplican si el movimiento CONECTÓ. Da igual si fue un efecto
            // SECUNDARIO de un movimiento de daño (10% de quemar) o el efecto PRINCIPAL de uno de
            // estado (Fuego Fatuo, 100% de quemar y sin daño): es el MISMO mecanismo.
            ApplyMoveEffects(actor, target, move, damageDealt, events, hitSubstitute);

            // HEDOR: sus ataques pueden hacer retroceder.
            var sx = X(actor);
            if (sx.FlinchChanceOnAttack > 0f && damageDealt > 0 && target != null && target != actor && !target.IsFainted && !hitSubstitute
                && !HasEffect(move, MoveEffectKind.Flinch) && !X(target).BlocksIncomingSecondaries && _rng.NextFloat() * 100f < sx.FlinchChanceOnAttack)
                target.SetFlinched();

            // OBJETOS DEL ATACANTE (Vidasfera, Campana Concha, Roca del Rey).
            ApplyGen6AfterAttack(actor, target, move, damageDealt, hitSubstitute, events);

            // Reacciones al golpe (Cambio Color, Justiciero, Mismo Destino, Autoestima...).
            if (move.DealsDirectDamage && !hitSubstitute) AfterHit(actor, target, move, damageDealt, lastCrit, events);
            // Objetos del defensor al recibir el golpe (Seguro Debilidad, Globo Helio) y Prestidigitador.
            if (move.DealsDirectDamage && !hitSubstitute && damageDealt > 0 && target != null && target != actor)
                ApplyGen6OnHit(actor, target, move, _lastEffectiveness, events);

            // DOS TURNOS (RECARGA): si el movimiento conectó, el próximo turno deberá recargar.
            if (move.TwoTurn == TwoTurnKind.Recharge)
                actor.SetMustRecharge();

            // REACCIÓN POR CONTACTO (Estática, Cuerpo Llama): si el movimiento hizo contacto y dañó,
            // y tanto atacante como objetivo siguen en pie, la habilidad del objetivo puede infligir
            // un estado al ATACANTE.
            if (move.MakesContact && damageDealt > 0 && !target.IsFainted && !actor.IsFainted)
                ApplyContactReaction(actor, target, events);
            if (move.MakesContact && damageDealt > 0 && !actor.IsFainted && target != actor && !hitSubstitute)
            {
                ApplyGen4ContactEffects(actor, target, events);
                ApplyGen6ContactEffects(actor, target, move, events);
            }
        }

        // El objetivo, al ser golpeado por contacto, puede "devolver" un estado al atacante (su habilidad).
        private void ApplyContactReaction(Combatant attacker, Combatant defender, List<IDomainEvent> events)
        {
            if (!TryGetAbility(defender, out var ability)) return;
            if (!ability.ContactReactionStatus.HasValue && ability.Extras.ContactReactionStatuses.Count == 0) return;
            if (_rng.NextFloat() >= ability.ContactReactionChance.AsFraction) return;
            // Efecto Espora: uno de varios estados al azar.
            var alts = ability.Extras.ContactReactionStatuses;
            var status = alts.Count > 0 ? alts[_rng.Next(0, alts.Count)] : ability.ContactReactionStatus.Value;
            TryInflictStatus(attacker, status, events, defender);
        }

        // Aplica los efectos del movimiento. Cada uno:
        //   1) tira su probabilidad (o usa el MISMO dado que el anterior si así se marcó: Poder Pasado);
        //   2) comprueba sus CONDICIONES (todas; sin condiciones = siempre);
        //   3) se aplica a quien diga su objetivo (uno mismo o el rival).
        // Drenaje y retroceso usan el daño causado; curar usa los PS máximos de quien se cura.
        private void ApplyMoveEffects(Combatant actor, Combatant target, Move move, int damageDealt, List<IDomainEvent> events,
            bool hitSubstitute = false)
        {
            if (move.SecondaryEffects.Count == 0) return;

            bool previousRoll = false, substituteNarrated = false;
            var ax = X(actor);
            foreach (var effect in move.SecondaryEffects)
            {
                bool secondary = IsSecondary(move, effect);
                // POTENCIA BRUTA: sus movimientos pierden los efectos secundarios.
                if (secondary && ax.RemovesOwnSecondaries) continue;
                // DICHA: probabilidades de los secundarios ×2.
                float chance = effect.Chance.AsFraction * (secondary ? ax.SecondaryChanceMultiplier : 1f);
                bool rolled = effect.SharesPreviousRoll ? previousRoll : _rng.NextFloat() < chance;
                previousRoll = rolled;
                if (!rolled) continue;
                if (!AllConditions(effect.Conditions, actor, target, move)) continue;

                var who = effect.Target == EffectTarget.Self ? actor : target;

                // POLVO ESCUDO: los secundarios contra él no le afectan.
                if (secondary && who != null && who != actor && X(who).BlocksIncomingSecondaries) continue;
                // ANTICURA: no puede curarse.
                if ((effect.Kind == MoveEffectKind.Heal || effect.Kind == MoveEffectKind.HealSelf || effect.Kind == MoveEffectKind.Drain)
                    && HasVolatileFlag(effect.Kind == MoveEffectKind.Heal ? who : actor, d => d.Extras.BlocksHealing))
                    continue;
                // Efectos de la 3.ª y 4.ª generación.
                if (ApplyGen4Effect(actor, target, move, effect, who, damageDealt, events)) continue;
                if (ApplyGen6Effect(actor, target, move, effect, who, events)) continue;

                // SUSTITUTO: lo que se lanza CONTRA el rival (estados, bajadas, retroceso, Anulación...) no le
                // llega mientras tenga sustituto (o si el golpe dio en el sustituto).
                if (who != null && who != actor && (hitSubstitute || who.HasSubstitute) && BlockedBySubstitute(effect.Kind))
                {
                    if (!move.DealsDirectDamage && !substituteNarrated) { events.Add(new SubstituteBlockedEvent(who.Id)); substituteNarrated = true; }
                    continue;
                }

                switch (effect.Kind)
                {
                    case MoveEffectKind.InflictStatus:
                        // El objetivo puede ser el rival (lo normal) o uno mismo (Descanso, Protección).
                        // Velo Sagrado: el rival no puede ponerle estados a este lado.
                        if (who != actor && SideBlocks(who, d => d.BlocksStatus, out var safeguard))
                        { events.Add(new ProtectedBySideEvent(who.Id, safeguard)); break; }
                        if (_statuses != null) TryInflictStatus(who, effect.Status, events, who != actor ? actor : null);
                        break;

                    case MoveEffectKind.Drain:
                    {
                        int heal = (int)(damageDealt * effect.Amount.AsFraction);
                        if (heal > 0 && !actor.IsFainted)
                        {
                            // LODO LÍQUIDO: quien drena pierde esos PS.
                            if (target != null && target != actor && X(target).LiquidOoze)
                            {
                                events.Add(new AbilityTriggeredEvent(target.Id, AbilityIdOf(target), "lodo"));
                                actor.TakeDamage(heal);
                                events.Add(new StatusDamageEvent(actor.Id, InjuryStatus, heal));
                                if (actor.IsFainted) events.Add(new MonsterFaintedEvent(actor.Id));
                                break;
                            }
                            actor.HealHp(heal);
                            events.Add(new HpRestoredEvent(actor.Id, heal));
                        }
                        break;
                    }

                    case MoveEffectKind.Recoil:
                    {
                        if (ax.NoRecoil || ax.NoIndirectDamage) break;   // Cabeza Roca, Muro Mágico
                        int recoil = (int)(damageDealt * effect.Amount.AsFraction);
                        if (recoil > 0)
                        {
                            actor.TakeDamage(recoil);
                            events.Add(new RecoilDamageEvent(actor.Id, recoil));
                            if (actor.IsFainted)
                                events.Add(new MonsterFaintedEvent(actor.Id));
                        }
                        break;
                    }

                    case MoveEffectKind.RecoilMaxHp:
                    {
                        // Retroceso por % de los PS MÁXIMOS del atacante, no del daño (Forcejeo moderno: 1/4).
                        // Ocurre aunque el golpe no hiciera daño. Mínimo 1 punto.
                        if (actor.IsFainted) break;
                        int recoil = System.Math.Max(1, (int)(actor.MaxHp * effect.Amount.AsFraction));
                        actor.TakeDamage(recoil);
                        events.Add(new RecoilDamageEvent(actor.Id, recoil));
                        if (actor.IsFainted)
                            events.Add(new MonsterFaintedEvent(actor.Id));
                        break;
                    }

                    case MoveEffectKind.HealSelf: // (antiguo) siempre cura al usuario
                        HealPercent(actor, effect.Amount.AsFraction, events);
                        break;

                    case MoveEffectKind.Heal: // cura a quien diga el objetivo (Pulso Cura cura al rival)
                        HealPercent(who, effect.Amount.AsFraction, events);
                        break;

                    case MoveEffectKind.CureStatus:
                        CureStatus(who, effect.Status, events);
                        break;

                    case MoveEffectKind.SetWeather:
                        StartWeather(effect.WeatherId, WeatherTurnsFor(actor, effect.WeatherId, effect.WeatherTurns), events);
                        break;

                    case MoveEffectKind.SetHazard:
                        SetHazard(actor, who, effect.HazardId, events);
                        break;

                    case MoveEffectKind.ClearHazards:
                        ClearHazards(who, effect.HazardId, events);
                        break;

                    case MoveEffectKind.ForceSwitch:
                        ForceSwitch(actor, who, events);
                        break;

                    case MoveEffectKind.ChangeStatStage:
                    {
                        // Sube/baja una etapa. El objetivo puede ser uno mismo (Danza Espada) o el rival (Gruñido).
                        var affected = who;
                        if (affected == null || affected.IsFainted) break;
                        // Neblina: el rival no puede bajarle etapas a este lado.
                        if (affected != actor && effect.Stages < 0 && SideBlocks(affected, d => d.BlocksStatDrops, out var mist))
                        { events.Add(new ProtectedBySideEvent(affected.Id, mist)); break; }
                        // Cuerpo Puro, Vista Lince, Simple, Respondón, Competitivo...
                        ChangeStageFrom(actor, affected, effect.Stat, effect.Stages, events);
                        break;
                    }

                    case MoveEffectKind.Flinch:
                    {
                        // Marca el retroceso en el objetivo. Solo le hará perder el turno si todavía
                        // no actuó (el bucle de resolución comprueba la marca antes de cada acción).
                        if (!who.IsFainted && !X(who).FlinchImmune) who.SetFlinched();
                        break;
                    }

                    // ---------------- Cierre del combate ----------------

                    case MoveEffectKind.SetSideCondition:
                        SetSideCondition(actor, who, effect.SideConditionId, events);
                        break;

                    case MoveEffectKind.ResetStages:
                        if (who.IsFainted) break;
                        who.ResetStages();
                        events.Add(new StagesResetEvent(who.Id));
                        break;

                    case MoveEffectKind.CritBoost:
                        if (who.IsFainted) break;
                        if (who.CritBonus > 0) { events.Add(new MoveFailedEvent(actor.Id)); break; }
                        who.AddCritBonus(effect.Stages > 0 ? effect.Stages : 2);
                        events.Add(new CritBoostedEvent(who.Id));
                        break;

                    case MoveEffectKind.Substitute:
                    {
                        if (who.IsFainted) break;
                        float frac = effect.Amount.AsFraction > 0f ? effect.Amount.AsFraction : 0.25f;
                        int cost = Math.Max(1, (int)(who.MaxHp * frac));
                        if (who.HasSubstitute || who.CurrentHp <= cost) { events.Add(new MoveFailedEvent(actor.Id)); break; }
                        who.TakeDamage(cost);
                        who.SetSubstitute(cost);
                        events.Add(new SubstituteCreatedEvent(who.Id, cost));
                        break;
                    }

                    case MoveEffectKind.DisableMove:
                    {
                        var last = who.LastMoveUsed;
                        if (who.IsFainted || !last.HasValue || who.DisabledMove.HasValue || who.IndexOfMove(last.Value) < 0)
                        { events.Add(new MoveFailedEvent(actor.Id)); break; }
                        who.Disable(last.Value, effect.Turns > 0 ? effect.Turns : 4);
                        events.Add(new MoveDisabledEvent(who.Id, last.Value));
                        break;
                    }

                    case MoveEffectKind.Encore:
                    {
                        var last = who.LastMoveUsed;
                        if (who.IsFainted || !last.HasValue || who.EncoreMove.HasValue || who.IndexOfMove(last.Value) < 0
                            || _moves.TryGet(last.Value, out var lm) && (HasEffect(lm, MoveEffectKind.Encore) || IsCallingMove(lm)))
                        { events.Add(new MoveFailedEvent(actor.Id)); break; }
                        who.SetEncore(last.Value, effect.Turns > 0 ? effect.Turns : 3);
                        events.Add(new EncoreStartedEvent(who.Id, last.Value));
                        break;
                    }

                    case MoveEffectKind.Rampage:
                        // Solo el primer uso lo encadena; los turnos siguientes llegan aquí ya encadenados.
                        if (!actor.RampageMove.HasValue && !actor.IsFainted)
                        {
                            int max = Math.Max(2, effect.Turns > 0 ? effect.Turns : 3);
                            int total = _rng.Next(2, max + 1);
                            actor.StartRampage(move.Id, total - 1,
                                string.IsNullOrEmpty(effect.Status.Value) ? (StatusId?)null : effect.Status);
                        }
                        break;

                    case MoveEffectKind.Rage:
                        if (!actor.IsFainted)
                            actor.SetRage(string.IsNullOrEmpty(effect.Stat.Value) ? StatId.Attack : effect.Stat, effect.Stages != 0 ? effect.Stages : 1);
                        break;

                    case MoveEffectKind.CallRandomMove:
                        CallMove(actor, target, move, RandomCallableMove(move.Id), events);
                        break;

                    case MoveEffectKind.CallLastMove:
                    {
                        var last = target != null ? target.LastMoveUsed : null;
                        if (!last.HasValue || !_moves.TryGet(last.Value, out var lm) || IsCallingMove(lm)) { events.Add(new MoveFailedEvent(actor.Id)); break; }
                        CallMove(actor, target, move, last.Value, events);
                        break;
                    }

                    case MoveEffectKind.CopyLastMove:
                    {
                        var last = target != null ? target.LastMoveUsed : null;
                        int slot = actor.IndexOfMove(move.Id);
                        if (!last.HasValue || slot < 0 || actor.IndexOfMove(last.Value) >= 0 || !_moves.TryGet(last.Value, out var lm)
                            || IsCallingMove(lm) || _struggleMove.HasValue && last.Value == _struggleMove.Value)
                        { events.Add(new MoveFailedEvent(actor.Id)); break; }
                        actor.BorrowMove(slot, last.Value, Math.Min(5, Math.Max(1, lm.MaxPp)));
                        events.Add(new MoveCopiedEvent(actor.Id, last.Value));
                        break;
                    }

                    case MoveEffectKind.Transform:
                        if (target == null || target.IsFainted || target.HasSubstitute || target.IsTransformed || actor.IsTransformed)
                        { events.Add(new MoveFailedEvent(actor.Id)); break; }
                        actor.TransformInto(target, 5);
                        events.Add(new TransformedEvent(actor.Id, target.Id));
                        break;

                    case MoveEffectKind.ChangeType:
                    {
                        if (who.IsFainted) break;
                        Id<ElementType>? newType = null;
                        if (!string.IsNullOrEmpty(effect.TypeId)) newType = new Id<ElementType>(effect.TypeId);
                        else
                            foreach (var m in who.Moves)
                                if (m != move.Id && _moves.TryGet(m, out var mm) && !ContainsType(who.Types, mm.Type)) { newType = mm.Type; break; }
                        if (!newType.HasValue || who.Types.Count == 1 && who.Types[0] == newType.Value)
                        { events.Add(new MoveFailedEvent(actor.Id)); break; }
                        var types = new[] { newType.Value };
                        who.ChangeTypes(types);
                        events.Add(new TypeChangedEvent(who.Id, types));
                        break;
                    }

                    case MoveEffectKind.SwitchSelf:
                    {
                        if (actor.IsFainted || _battle == null) break;
                        var team = SideOf(actor);
                        if (team == null || team.Reserves().Count == 0)
                        {
                            if (!move.DealsDirectDamage) events.Add(new MoveFailedEvent(actor.Id)); // Relevo sin nadie a quien pasar
                            break;
                        }
                        _selfSwitchRequest = (ReferenceEquals(team, _battle.PlayerTeam), effect.Stages > 0);
                        break;
                    }

                    case MoveEffectKind.Teleport:
                    {
                        if (actor.IsFainted || _battle == null) break;
                        if (!_battle.IsTrainerBattle)
                        {
                            events.Add(new TeleportedEvent(actor.Id));
                            _battle.SetOutcome(BattleOutcome.Fled);
                            events.Add(new BattleEndedEvent(BattleOutcome.Fled));
                            break;
                        }
                        var team = SideOf(actor);
                        if (team == null || team.Reserves().Count == 0) { events.Add(new MoveFailedEvent(actor.Id)); break; }
                        _selfSwitchRequest = (ReferenceEquals(team, _battle.PlayerTeam), false);
                        break;
                    }
                }
            }
        }

        // Lo que el SUSTITUTO bloquea cuando viene del rival.
        private static bool BlockedBySubstitute(MoveEffectKind kind)
        {
            switch (kind)
            {
                case MoveEffectKind.InflictStatus:
                case MoveEffectKind.ChangeStatStage:
                case MoveEffectKind.Flinch:
                case MoveEffectKind.DisableMove:
                case MoveEffectKind.Encore:
                case MoveEffectKind.Heal:
                case MoveEffectKind.CureStatus:
                case MoveEffectKind.ResetStages:
                    return true;
                default:
                    return false;
            }
        }

        private static bool HasEffect(Move move, MoveEffectKind kind)
        {
            foreach (var e in move.SecondaryEffects) if (e.Kind == kind) return true;
            return false;
        }

        // ¿Es un movimiento que llama o copia a otros? (no se pueden llamar entre ellos sin fin)
        private static bool IsCallingMove(Move move)
            => HasEffect(move, MoveEffectKind.CallRandomMove) || HasEffect(move, MoveEffectKind.CallLastMove)
               || HasEffect(move, MoveEffectKind.CopyLastMove) || HasEffect(move, MoveEffectKind.Transform)
               || HasEffect(move, MoveEffectKind.CallMove) || HasEffect(move, MoveEffectKind.CallTeamMove)
               || HasEffect(move, MoveEffectKind.CallTargetMove) || HasEffect(move, MoveEffectKind.CallOwnMove)
               || move.FixedDamage == FixedDamageKind.Bide;

        /// <summary>Etiqueta que excluye un movimiento de Metrónomo (además de los que llaman a otros y Forcejeo).</summary>
        public const string NoMetronomeTag = "no_metronomo";

        // Un movimiento al azar para Metrónomo.
        private Id<Move>? RandomCallableMove(Id<Move> self)
        {
            var pool = new List<Id<Move>>();
            foreach (var m in _moves.All)
            {
                if (m.Id == self || IsCallingMove(m) || m.HasTag(NoMetronomeTag) || m.HasTag("no_metronome")) continue;
                if (_struggleMove.HasValue && m.Id == _struggleMove.Value) continue;
                pool.Add(m.Id);
            }
            if (pool.Count == 0) return null;
            return pool[_rng.Next(0, pool.Count)];
        }

        // Usa otro movimiento desde este (Metrónomo, Espejo): sin gastar PP, con tope de profundidad.
        private void CallMove(Combatant actor, Combatant target, Move via, Id<Move>? called, List<IDomainEvent> events)
        {
            if (!called.HasValue || _callDepth >= 3 || actor.IsFainted) { events.Add(new MoveFailedEvent(actor.Id)); return; }
            events.Add(new MoveCalledEvent(actor.Id, via.Id, called.Value));
            _callDepth++;
            try { ResolveMove(actor, target, new UseMove(called.Value), events, payPp: false); }
            finally { _callDepth--; }
        }

        private static void HealPercent(Combatant who, float fraction, List<IDomainEvent> events)
        {
            if (who == null || who.IsFainted) return;
            int before = who.CurrentHp;
            who.HealHp((int)(who.MaxHp * fraction));
            int healed = who.CurrentHp - before;
            if (healed > 0) events.Add(new HpRestoredEvent(who.Id, healed));
        }

        // Quita un estado: sin id = el principal; con id = ese (principal o volátil).
        private static void CureStatus(Combatant who, StatusId status, List<IDomainEvent> events)
        {
            if (who == null || who.IsFainted) return;
            if (string.IsNullOrEmpty(status.Value))
            {
                if (!who.Status.HasValue) return;
                var was = who.Status.Value;
                who.ClearStatus();
                events.Add(new StatusFadedEvent(who.Id, was));
                return;
            }
            if (who.Status.HasValue && who.Status.Value == status)
            {
                who.ClearStatus();
                events.Add(new StatusFadedEvent(who.Id, status));
            }
            else if (who.RemoveVolatile(status))
                events.Add(new StatusFadedEvent(who.Id, status));
        }

        // Activa un clima (si ya está ese mismo, no pasa nada). Turnos: los del efecto, o los de la ficha.
        private void StartWeather(string weatherId, int turns, List<IDomainEvent> events)
        {
            if (_battle == null || string.IsNullOrWhiteSpace(weatherId)) return;
            if (string.Equals(_battle.WeatherId, weatherId, StringComparison.OrdinalIgnoreCase)) return;
            int duration = turns;
            if (duration <= 0)
                duration = _weathers != null && _weathers.TryGet(new Id<WeatherDefinition>(weatherId), out var def) ? def.DefaultTurns : 5;
            _battle.SetWeather(weatherId, duration);
            events.Add(new WeatherStartedEvent(weatherId, duration));
        }

        // Fin de turno del clima: daño a los no inmunes y cuenta atrás.
        private void ApplyEndOfTurnWeather(Battle battle, List<IDomainEvent> events)
        {
            if (battle.WeatherId == null) return;
            if (TryGetWeather(out var weather) && weather.ResidualDamagePercent.Value > 0f)
            {
                foreach (var c in new[] { battle.Player, battle.Enemy })
                {
                    if (c.IsFainted) continue;
                    bool immune = WeatherImmune(c, battle.WeatherId);
                    foreach (var t in weather.ImmuneTypes) if (ContainsType(TypesOf(c), t)) { immune = true; break; }
                    if (immune) continue;
                    int amount = Math.Max(1, (int)(c.MaxHp * weather.ResidualDamagePercent.AsFraction));
                    c.TakeDamage(amount);
                    events.Add(new WeatherDamageEvent(c.Id, battle.WeatherId, amount));
                    if (c.IsFainted) events.Add(new MonsterFaintedEvent(c.Id));
                }
            }
            string id = battle.WeatherId;
            if (battle.TickWeather())
            {
                battle.ClearWeather();
                events.Add(new WeatherEndedEvent(id));
            }
        }

        // ---------- Objetos equipados (Lote de objetos) ----------

        private bool TryGetHeldItem(Combatant c, out ItemDefinition item)
        {
            item = null;
            if (!_rules.Generation.HeldItems) return false;   // reglas de generación: sin objetos equipados
            if (_items == null || c == null || string.IsNullOrEmpty(c.HeldItem)) return false;
            // Zoquete y Embargo: el objeto equipado no hace nada. Zona Mágica: ninguno funciona.
            if (X(c).Klutz || HasVolatileFlag(c, d => d.Extras.BlocksItems) || ItemsSuppressed()) return false;
            return _items.TryGet(new Id<ItemDefinition>(c.HeldItem), out item);
        }

        // Recupera PP: a todos los movimientos, o solo al primero que lo necesite.
        private void RestorePp(Combatant c, int amount, bool allMoves)
        {
            EnsurePp(c);
            for (int i = 0; i < c.Moves.Count; i++)
            {
                int cur = c.PpAt(i), max = c.MaxPpAt(i);
                if (cur < 0 || cur >= max) continue;
                c.AddPp(i, amount);
                if (!allMoves) break;
            }
        }

        // ---------- Estados volátiles: ayudas ----------

        // ¿Tiene algún estado (principal o volátil) cuya ficha cumpla 'flag'?
        private bool HasVolatileFlag(Combatant c, Func<StatusConditionDefinition, bool> flag)
        {
            if (_statuses == null || c == null) return false;
            foreach (var v in c.Volatiles)
                if (TryGetStatus(v.Id, out var d) && flag(d)) return true;
            return c.Status.HasValue && TryGetStatus(c.Status.Value, out var p) && flag(p);
        }

        /// <summary>¿Puede retirarse ahora (no está atrapado por Atadura, Mal de Ojo...)? Lo usa la IA para no perder el turno.</summary>
        public bool CanSwitchOut(Combatant c) => !IsTrapped(c, out _);

        // ¿Está atrapado (no puede cambiarse ni huir)? Devuelve qué estado lo atrapa.
        private bool IsTrapped(Combatant c, out StatusId by)
        {
            by = default;
            if (c == null) return false;
            // Sombratrampa, Trampa Arena, Imán (habilidad del RIVAL).
            var opp = OpponentOf(c);
            if (opp != null && !opp.IsFainted)
            {
                var ox = X(opp);
                if (ox.TrapsOpponent && !X(c).TrapsOpponent) // dos con Sombratrampa no se atrapan entre sí
                {
                    bool typeOk = ox.TrapOnlyTypes.Count == 0 || HasAnyType(c, ox.TrapOnlyTypes);
                    bool groundOk = !ox.TrapOnlyGrounded || IsGrounded(c)
                                    || !ContainsType(TypesOf(c), new Id<ElementType>("flying")) && !(TryGetAbility(c, out var la) && la.IsImmuneToType(new Id<ElementType>("ground")));
                    if (typeOk && groundOk) { by = new StatusId(AbilityIdOf(opp)); return true; }
                }
            }
            if (_statuses == null) return false;
            foreach (var v in c.Volatiles)
                if (TryGetStatus(v.Id, out var d) && d.PreventsSwitch) { by = v.Id; return true; }
            if (c.Status.HasValue && TryGetStatus(c.Status.Value, out var p) && p.PreventsSwitch) { by = c.Status.Value; return true; }
            return false;
        }

        // Duración sorteada: entre DurationTurns y DurationMaxTurns (si este es mayor).
        private int RollDuration(StatusConditionDefinition def)
        {
            if (def.DurationMaxTurns > def.DurationTurns && def.DurationTurns >= 0)
                return _rng.Next(Math.Max(1, def.DurationTurns), def.DurationMaxTurns + 1);
            return def.DurationTurns;
        }

        // Intenta infligir un estado. Reglas clásicas: no se apila otro estado principal y un
        // debilitado no se ve afectado. El estado debe existir en el catálogo (lo definió el autor).
        private void TryInflictStatus(Combatant target, StatusId statusId, List<IDomainEvent> events, Combatant source = null)
        {
            if (target == null || target.IsFainted) return;
            if (!TryGetStatus(statusId, out var statusDef)) return;

            // DEFENSA HOJA: con su clima no se le pueden poner estados principales.
            var tx = X(target);
            if (!statusDef.IsVolatile && !string.IsNullOrEmpty(tx.StatusImmuneInWeather) && WeatherIsActive
                && string.Equals(tx.StatusImmuneInWeather, _battle.WeatherId, StringComparison.OrdinalIgnoreCase))
                return;

            // Un estado PRINCIPAL no pisa a otro principal; un VOLÁTIL no se repite, pero se suma.
            if (statusDef.IsVolatile ? target.HasVolatile(statusId) : target.Status.HasValue) return;

            // ATRACCIÓN / GRAN ENCANTO: solo entre géneros opuestos (y nunca con quien no tiene género).
            if (statusDef.Extras.RequiresOppositeGender && (source == null || !GenderText.Opposite(source.Gender, target.Gender)))
            {
                events.Add(new StatusFailedEvent(target.Id, statusId));
                return;
            }

            // CAMPOS: Campo de Niebla impide los estados principales; Campo Eléctrico, dormir (a los que pisan el suelo).
            if (TerrainBlocksStatus(target, statusId) && (statusDef.IsVolatile ? statusId.Value == "confusion" || statusId.Value == "drowsy" : true))
            {
                events.Add(new StatusFailedEvent(target.Id, statusId));
                return;
            }

            // INMUNIDAD POR TIPO (configurable en la ficha del estado): un Fuego no se quema, etc.
            foreach (var immuneType in statusDef.ImmuneTypes)
                if (ContainsType(target.Types, immuneType)) return;

            // INMUNIDAD POR HABILIDAD (p.ej. Inmunidad bloquea veneno, Vigor bloquea parálisis).
            if (TryGetAbility(target, out var ability) && ability.IsImmuneToStatus(statusId))
                return;

            // MÁS DIFÍCIL SI SE REPITE (Protección): 1/3 la segunda vez seguida, 1/9 la tercera...
            if (statusDef.HarderWhenRepeated)
            {
                if (target.ConsecutiveGuards > 0 && _rng.NextFloat() >= (float)Math.Pow(1.0 / 3.0, target.ConsecutiveGuards))
                {
                    events.Add(new StatusFailedEvent(target.Id, statusId));
                    return;
                }
                target.NoteGuard();
            }

            int duration = RollDuration(statusDef);
            if (statusDef.IsVolatile) target.AddVolatile(statusId, duration);
            else target.SetStatus(statusId, duration);
            events.Add(new StatusInflictedEvent(target.Id, statusId));

            // SINCRONÍA: si el rival le pone quemadura, veneno o parálisis, se la devuelve.
            if (source != null && !ReferenceEquals(source, target) && !statusDef.IsVolatile && tx.SynchronizeStatus && !source.IsFainted
                && (statusId.Value == "burn" || statusId.Value == "poison" || statusId.Value == "toxic" || statusId.Value == "paralysis"))
            {
                events.Add(new AbilityTriggeredEvent(target.Id, AbilityIdOf(target), "sincronia"));
                TryInflictStatus(source, statusId, events, null);
            }

            // BAYA ZIUELA: se cura enseguida (y se gasta).
            CheckLum(target, statusId, events);
        }

        // ¿Algún estado del combatiente le impide actuar este turno? Primero el principal (sueño,
        // parálisis...) y luego los volátiles (confusión). Cada uno tira su propia probabilidad.
        private bool IsPreventedByStatus(Combatant actor, List<IDomainEvent> events, bool ignorePrincipal = false)
        {
            if (_statuses == null) return false;
            if (!ignorePrincipal && actor.Status.HasValue && TryGetStatus(actor.Status.Value, out var principal)
                && RollPrevention(actor, actor.Status.Value, principal, events))
                return true;
            foreach (var v in new List<ActiveVolatileStatus>(actor.Volatiles))
                if (TryGetStatus(v.Id, out var def) && RollPrevention(actor, v.Id, def, events))
                    return true;
            return false;
        }

        private bool RollPrevention(Combatant actor, StatusId id, StatusConditionDefinition def, List<IDomainEvent> events)
        {
            float chance = def.ActionPreventionChance.AsFraction;
            if (chance <= 0f || _rng.NextFloat() >= chance) return false;

            // Confusión y similares: al impedir la acción, el portador puede hacerse daño a sí mismo.
            float selfFrac = def.SelfDamageOnPreventedPercent.AsFraction;
            if (selfFrac > 0f)
            {
                int selfDamage = Math.Max(1, (int)(actor.MaxHp * selfFrac));
                actor.TakeDamage(selfDamage);
                events.Add(new StatusDamageEvent(actor.Id, id, selfDamage));
                if (actor.IsFainted)
                    events.Add(new MonsterFaintedEvent(actor.Id));
            }

            events.Add(new ActionPreventedEvent(actor.Id, id));
            return true;
        }

        // Fin de turno para un combatiente: su estado PRINCIPAL y cada VOLÁTIL avanzan un turno,
        // aplican su daño/curación residual y se acaban si cumplen su duración (o por azar).
        private void ApplyEndOfTurnStatus(Combatant combatant, List<IDomainEvent> events)
        {
            if (_statuses == null || combatant.IsFainted) return;

            // --- Principal ---
            if (combatant.Status.HasValue && TryGetStatus(combatant.Status.Value, out var def))
            {
                // Solo los estados que IMPIDEN actuar (dormido, congelado...) esperan al turno siguiente para contar.
                bool fresh = combatant.StatusIsFresh && def.ActionPreventionChance.AsFraction > 0f;
                bool countFromNext = def.ActionPreventionChance.AsFraction > 0f;
                combatant.AdvanceStatusTurn();
                // MADRUGAR: el sueño se le pasa el doble de rápido.
                if (!fresh && countFromNext && X(combatant).SleepAgesTwice) combatant.AdvanceStatusTurn(); // ahora lleva 1, 2, 3... turnos con este estado
                if (!ApplyResidual(combatant, combatant.Status.Value, def, combatant.StatusTurns, events)) return;

                // La DURACIÓN empieza a contar el turno siguiente al que se aplicó (no se despierta en el mismo turno).
                int duration = combatant.StatusDuration > 0 ? combatant.StatusDuration : def.DurationTurns;
                bool recovered = !fresh && def.RecoveryChancePerTurn.AsFraction > 0f && _rng.NextFloat() < def.RecoveryChancePerTurn.AsFraction;
                bool capped = !fresh && duration > 0 && (countFromNext ? combatant.StatusAge : combatant.StatusTurns) >= duration;
                if (recovered || capped)
                    EndOrTransformStatus(combatant, def, events);
            }

            // --- Volátiles (sobre una copia: pueden acabarse durante el recorrido) ---
            foreach (var v in new List<ActiveVolatileStatus>(combatant.Volatiles))
            {
                if (combatant.IsFainted) return;
                if (!TryGetStatus(v.Id, out var vdef)) { combatant.RemoveVolatile(v.Id); continue; }
                // PESADILLA: solo mientras siga dormido.
                if (vdef.Extras.ResidualRequiresStatus.HasValue && !combatant.HasStatus(vdef.Extras.ResidualRequiresStatus.Value))
                { combatant.RemoveVolatile(v.Id); events.Add(new StatusFadedEvent(combatant.Id, v.Id)); continue; }
                v.Turns++;
                // Confusión y demás que impiden actuar: su cuenta empieza el turno siguiente. Protección, Aguante,
                // Atadura...: duran «este turno» / cuentan desde ya, como en los juegos.
                bool vCountsFromNext = vdef.ActionPreventionChance.AsFraction > 0f;
                bool vfresh = v.Fresh && vCountsFromNext;
                if (v.Fresh) v.Fresh = false; else v.Age++;
                if (!ApplyResidual(combatant, v.Id, vdef, v.Turns, events)) return;

                int duration = v.Duration > 0 ? v.Duration : vdef.DurationTurns;
                bool recovered = !vfresh && vdef.RecoveryChancePerTurn.AsFraction > 0f && _rng.NextFloat() < vdef.RecoveryChancePerTurn.AsFraction;
                bool capped = !vfresh && duration > 0 && (vCountsFromNext ? v.Age : v.Turns) >= duration;

                // CANTO MORTAL: cuenta atrás visible.
                if (!recovered && !capped && vdef.Extras.FaintsWhenEnds && duration > 0)
                    events.Add(new PerishCountEvent(combatant.Id, duration - (vCountsFromNext ? v.Age : v.Turns)));
                if (!recovered && !capped) continue;

                combatant.RemoveVolatile(v.Id);
                if (vdef.Extras.FaintsWhenEnds)
                {
                    events.Add(new PerishCountEvent(combatant.Id, 0));
                    combatant.TakeDamage(combatant.CurrentHp);
                    events.Add(new MonsterFaintedEvent(combatant.Id));
                    return;
                }
                if (vdef.TransformsToStatus.HasValue)
                    TryInflictStatus(combatant, vdef.TransformsToStatus.Value, events); // somnoliento -> dormido
                else
                    events.Add(new StatusFadedEvent(combatant.Id, v.Id));
            }
        }

        // Daño (o curación) residual de un estado. Devuelve false si el portador cayó.
        private bool ApplyResidual(Combatant combatant, StatusId id, StatusConditionDefinition def, int turns, List<IDomainEvent> events)
        {
            float frac = def.ResidualDamagePercent.AsFraction;
            if (frac <= 0f) return true;

            // Si es progresivo, escala con los turnos (tóxico = n × base).
            float effective = def.ProgressiveResidual ? frac * turns : frac;
            int amount = Math.Max(1, (int)(combatant.MaxHp * effective));

            if (def.ResidualHeals)
            {
                HealPercent(combatant, effective, events);
                return true;
            }
            if (TryGetAbility(combatant, out var ndAb) && (ndAb.NegatesStatusDamage || ndAb.Extras.NoIndirectDamage))
                return true; // Cura Veneno / Muro Mágico: no recibe el daño residual

            int dealt = Math.Min(amount, combatant.CurrentHp);
            combatant.TakeDamage(amount);
            events.Add(new StatusDamageEvent(combatant.Id, id, amount));

            // DRENADORAS: lo que pierde el portador lo recupera el rival que está en el campo.
            if (def.ResidualHealsOpponent && _battle != null)
            {
                var opponent = _battle.PlayerTeam.Find(combatant.Id) != null ? _battle.Enemy : _battle.Player;
                if (opponent != null && !opponent.IsFainted && dealt > 0)
                {
                    int before = opponent.CurrentHp;
                    opponent.HealHp(dealt);
                    if (opponent.CurrentHp > before) events.Add(new HpRestoredEvent(opponent.Id, opponent.CurrentHp - before));
                }
            }

            if (combatant.IsFainted)
            {
                events.Add(new MonsterFaintedEvent(combatant.Id));
                return false;
            }
            return true;
        }

        // Habilidades de FIN DE TURNO: Impulso (sube stat), Cura Veneno (cura PS), Mudar (cura estado).
        private void ApplyEndOfTurnAbilities(Combatant c, List<IDomainEvent> events)
        {
            if (c == null || c.IsFainted) return;
            if (!TryGetAbility(c, out var ability)) return;

            // Impulso (Speed Boost): sube una etapa de una stat propia cada turno.
            if (ability.EndOfTurnStat.HasValue && ability.EndOfTurnStages != 0)
            {
                int applied = c.ChangeStage(ability.EndOfTurnStat.Value, ability.EndOfTurnStages);
                if (applied != 0)
                    events.Add(new StatStageChangedEvent(c.Id, ability.EndOfTurnStat.Value, applied));
            }

            // Cura Veneno: recupera PS (opcionalmente solo si tiene estado alterado).
            float healFrac = ability.EndOfTurnHealPercent.AsFraction;
            if (healFrac > 0f && (!ability.EndOfTurnHealRequiresStatus || c.Status.HasValue))
            {
                int heal = (int)(c.MaxHp * healFrac);
                if (heal > 0)
                {
                    c.HealHp(heal);
                    events.Add(new HpRestoredEvent(c.Id, heal));
                }
            }

            // Mudar (Shed Skin): probabilidad de curarse el estado alterado.
            float cureChance = ability.EndOfTurnCureStatusChance.AsFraction;
            if (cureChance > 0f && c.Status.HasValue && _rng.NextFloat() < cureChance)
            {
                var was = c.Status.Value;
                c.ClearStatus();
                events.Add(new StatusFadedEvent(c.Id, was));
            }
        }

        // Termina el estado: si está marcado para transformarse (somnoliento -> dormido) lo cambia; si
        // no, lo cura. Reusa eventos: StatusInflicted para el nuevo estado, StatusFaded al curarse.
        private void EndOrTransformStatus(Combatant combatant, StatusConditionDefinition def, List<IDomainEvent> events)
        {
            if (def.TransformsToStatus.HasValue)
            {
                var next = def.TransformsToStatus.Value;
                combatant.ClearStatus();
                if (TryGetStatus(next, out var nextDef) && nextDef.IsVolatile) combatant.AddVolatile(next, nextDef.DurationTurns);
                else combatant.SetStatus(next); // reinicia el contador para el nuevo estado
                events.Add(new StatusInflictedEvent(combatant.Id, next));
            }
            else
            {
                var faded = combatant.Status.Value;
                combatant.ClearStatus();
                events.Add(new StatusFadedEvent(combatant.Id, faded));
            }
        }

        // Calcula la stat EFECTIVA: base × los multiplicadores pasivos del estado actual (quemado
        // baja Ataque, parálisis baja Velocidad). Nunca toca la base; la efectiva se computa al vuelo.
        // ---------- Experiencia ----------
        // Recorre los eventos del turno buscando caídas (o capturas) de RIVALES, y por cada una inserta
        // la XP inmediatamente después ("se debilitó" -> "ganó XP"), repartida entre los combatientes
        // del jugador que le hicieron frente y siguen en pie (como los clásicos). Battle acumula los
        // totales para el BattleResult; los eventos son la línea de tiempo para la presentación.
        //
        // Lote 3: la misma fase reparte también los EVs (rendimiento de EVs de la especie caída).
        // Regla moderna (Gen III+): CADA participante recibe el rendimiento COMPLETO (no se divide
        // como la XP; el reparto era cosa del "stat experience" de Gen I-II). Desde Gen VI, capturar
        // también da EVs "como si se hubiera derrotado". Los topes NO se aplican aquí: Battle solo
        // anota; Party (EffortValues) los respeta al aplicarlos.
        private void AwardExperience(Battle battle, List<IDomainEvent> events)
        {
            if (_xpFormula == null && !_awardEffortValues) return;

            for (int i = 0; i < events.Count; i++)
            {
                Id<BattleParticipant> fallenId;
                bool captured;
                if (events[i] is MonsterFaintedEvent f) { fallenId = f.Combatant; captured = false; }
                else if (events[i] is MonsterCapturedEvent c) { fallenId = c.Target; captured = true; }
                else continue;

                var fallen = battle.EnemyTeam.Find(fallenId);
                if (fallen == null) continue;                      // solo los caídos del RIVAL dan XP
                if (!battle.TryMarkXpYielded(fallenId.Value)) continue; // ya entregó la suya

                // Participantes: le hicieron frente y siguen en pie.
                var recipients = new List<Combatant>();
                foreach (var pid in battle.ParticipantsAgainst(fallenId.Value))
                {
                    var member = battle.PlayerTeam.Find(new Id<BattleParticipant>(pid));
                    if (member != null && !member.IsFainted) recipients.Add(member);
                }

                // "Repartir Experiencia" para todo el equipo (regla): los que no lucharon también reciben
                // una parte, y los que lucharon NO se la dividen (como en 6ª gen.).
                var others = new List<Combatant>();
                if (_rules.ExpShareAll)
                    foreach (var m in battle.PlayerTeam.Members)
                        if (!m.IsFainted && !recipients.Contains(m)) others.Add(m);
                if (recipients.Count == 0 && others.Count == 0) continue;
                int split = _rules.ExpShareAll ? 1 : recipients.Count;

                int insertAt = i + 1;
                foreach (var r in recipients)
                {
                    // EVs: rendimiento completo para cada participante (silencioso, como en los juegos).
                    if (_awardEffortValues)
                        foreach (var ev in fallen.EvYield)
                            battle.RecordEvAward(r.Id, ev.Stat, ev.Amount);

                    // XP: la fórmula decide (y se anuncia con un evento).
                    if (_xpFormula == null) continue;
                    int amount = _xpFormula.Compute(new XpContext(
                        fallen.BaseExpYield, fallen.Level, r.Level,
                        battle.IsTrainerBattle, split, captured));
                    if (amount <= 0) continue;

                    battle.RecordXpAward(r.Id, amount);
                    events.Insert(insertAt++, new ExperienceAwardedEvent(r.Id, amount));
                }
                foreach (var o in others)
                {
                    if (_awardEffortValues)
                        foreach (var ev in fallen.EvYield)
                            battle.RecordEvAward(o.Id, ev.Stat, ev.Amount);
                    if (_xpFormula == null) continue;
                    int full = _xpFormula.Compute(new XpContext(
                        fallen.BaseExpYield, fallen.Level, o.Level, battle.IsTrainerBattle, 1, captured));
                    int amount = (int)(full * _rules.ExpShareOthersPercent / 100f);
                    if (amount <= 0) continue;
                    battle.RecordXpAward(o.Id, amount);
                    events.Insert(insertAt++, new ExperienceAwardedEvent(o.Id, amount));
                }
                i = insertAt - 1; // saltar lo insertado y seguir
            }
        }

        // ---------- PP ----------

        /// <summary>
        /// PP actuales y máximos de un movimiento de un combatiente (para la UI y la IA). Carga los PP
        /// desde el catálogo la primera vez que se piden.
        /// </summary>
        public (int current, int max) PpOf(Combatant c, int moveIndex)
        {
            EnsurePp(c);
            return (c.PpAt(moveIndex), c.MaxPpAt(moveIndex));
        }

        /// <summary>¿Está activada la regla de PP y hay Forcejeo para cuando se acaben?</summary>
        public bool UsesPp => _usePp;

        private void EnsurePp(Combatant c)
        {
            if (c.PpReady) return;
            var max = new int[c.Moves.Count];
            for (int i = 0; i < max.Length; i++)
                max[i] = _moves.TryGet(c.Moves[i], out var m) ? m.MaxPp : 1;
            c.InitPp(max);
        }

        // Cobra 1 PP. Si no quedan en NINGÚN movimiento, cambia la acción por Forcejeo. Si el elegido
        // está vacío pero otros no (la UI/IA no debería permitirlo), se pierde el turno.
        // Devuelve false si el movimiento no se ejecuta.
        private bool PayPp(Combatant actor, ref UseMove useMove, List<IDomainEvent> events)
        {
            if (!_usePp) return true;
            EnsurePp(actor);

            if (!actor.HasAnyPp)
            {
                if (_struggleMove.HasValue && _moves.Contains(_struggleMove.Value))
                {
                    events.Add(new StruggleEvent(actor.Id));
                    useMove = new UseMove(_struggleMove.Value);
                    return true;
                }
                events.Add(new OutOfPpEvent(actor.Id, useMove.Move));
                return false;
            }

            if (!actor.TryConsumePp(useMove.Move))
            {
                events.Add(new OutOfPpEvent(actor.Id, useMove.Move));
                return false;
            }
            // PRESIÓN: el rival gasta 1 PP más.
            var presser = OpponentOf(actor);
            if (presser != null && !presser.IsFainted && X(presser).Pressure) actor.TryConsumePp(useMove.Move);
            return true;
        }

        // ---------- Cálculo de un golpe (compartido por el combate y la vista previa) ----------

        // Todo lo que decide un golpe ANTES de tirar dados. Sin efectos secundarios.
        private (float effectiveness, bool abilityImmune, bool stab, float stabMult, int attackStat, int defenseStat, int power)
            PrepareHit(Combatant actor, Combatant target, Move move)
        {
            // EFECTIVIDAD (tabla de tipos: tipo del movimiento contra los tipos del objetivo).
            // (Con todas las reglas: tipos actuales, Normalidad, Meteorobola, Profecía, Gravedad, Levitación, Superguarda...)
            // INMUNIDAD POR HABILIDAD (Levitación -> inmune a Tierra): anula la efectividad.
            float effectiveness = EffectivenessAgainst(actor, target, move, out bool abilityImmune);

            // STAB: ¿el tipo (real) del movimiento está entre los tipos (actuales) del atacante?
            bool stab = ContainsType(TypesOf(actor), MoveTypeOf(actor, move));

            // Adaptable sustituye el 1.5 clásico por su propio multiplicador (p. ej. 2).
            float stabMult = 0f; // 0 = la fórmula usa el valor clásico según 'stab'
            if (stab && TryGetAbility(actor, out var stabAb) && stabAb.StabMultiplierOverride > 0f)
                stabMult = stabAb.StabMultiplierOverride;

            // SELECCIÓN DE STATS: la categoría decide cuáles (según el Ruleset: clásico Ataque/Defensa y
            // Atq.Esp./Def.Esp.), y cada movimiento puede cambiarlas (Psicocarga ataca la Defensa; Juego
            // Sucio usa el Ataque del RIVAL). Así una stat inventada ("suerte") también puede hacer daño.
            bool physical = CategoryOf(actor, move) == MoveCategory.Physical;
            var atkStatId = move.AttackStat ?? (physical ? _rules.PhysicalAttack : _rules.SpecialAttack);
            var defStatId = move.DefenseStat ?? (physical ? _rules.PhysicalDefense : _rules.SpecialDefense);
            // Zona Extraña: la Defensa y la Def. Esp. se intercambian para el daño.
            if (DefensesSwapped())
            {
                if (defStatId == _rules.PhysicalDefense) defStatId = _rules.SpecialDefense;
                else if (defStatId == _rules.SpecialDefense) defStatId = _rules.PhysicalDefense;
            }
            // IGNORANTE: quien la tiene ignora las etapas del otro (al atacar, la Defensa del rival; al
            // recibir, el Ataque del atacante). Un crítico también las ignora, pero eso lo hace la fórmula.
            // «ignora_etapas» (Guardia Baja / Espada Santa): no cuentan las etapas de defensa del objetivo.
            bool ignoreTargetStages = target != actor && (X(actor).IgnoresStages || move.HasTag("ignora_etapas"));
            bool ignoreActorStages = target != actor && X(target).IgnoresStages;
            var attacker = move.AttackStatFromTarget ? target : actor;
            int attackStat = EffectiveStat(attacker, atkStatId, attacker == actor ? ignoreActorStages : ignoreTargetStages);
            int defenseStat = EffectiveStat(target, defStatId, ignoreTargetStages);

            int power = EffectivePower(actor, target, move);
            return (effectiveness, abilityImmune, stab, stabMult, attackStat, defenseStat, power);
        }

        /// <summary>
        /// POTENCIA EFECTIVA de un movimiento en este momento:
        ///   1) base: la fórmula del movimiento si tiene (Estallido: 150 × vida / 100) o su potencia;
        ///   2) × modificadores del MOVIMIENTO cuyas condiciones se cumplan (Fachada: ×2 si tiene estado);
        ///   3) × modificadores de la HABILIDAD del atacante al atacar (Técnico) y del objetivo al recibir;
        ///   4) × el CLIMA según el tipo (lluvia: Agua ×1,5, Fuego ×0,5).
        /// Público para que el editor muestre "potencia real" en la calculadora.
        /// </summary>
        public int EffectivePower(Combatant actor, Combatant target, Move move)
        {
            double basePower = move.Power;
            if (move.PowerFormula != null)
            {
                var vars = new Dictionary<string, double>
                {
                    ["potencia"] = move.Power,
                    ["nivel"] = actor.Level,
                    ["nivel_rival"] = target.Level,
                    ["vida"] = HpPercent(actor),
                    ["vida_rival"] = HpPercent(target),
                    ["amistad"] = actor.Friendship,
                    ["velocidad"] = EffectiveStat(actor, StatId.Speed),
                    ["velocidad_rival"] = EffectiveStat(target, StatId.Speed),
                    // 3.ª y 4.ª generación
                    ["peso"] = WeightOf(actor),
                    ["peso_rival"] = WeightOf(target),
                    ["seguidos"] = actor.ConsecutiveUses,
                    ["reserva"] = actor.Stockpile,
                    ["pp"] = RemainingPp(actor, move),
                    ["subidas_rival"] = PositiveStages(target),
                    ["subidas"] = PositiveStages(actor),
                    ["ps"] = actor.CurrentHp,
                    ["ps_rival"] = target.CurrentHp,
                };
                // Magnitud: una tirada 0-100 propia (solo si la fórmula la usa, para no gastar azar de más).
                if (move.PowerFormulaText.IndexOf("azar", StringComparison.OrdinalIgnoreCase) >= 0) vars["azar"] = _rng.NextFloat() * 100.0;
                else vars["azar"] = 50.0;
                try { basePower = move.PowerFormula.Evaluate(vars); }
                catch (Exception) { basePower = move.Power; } // una fórmula rara (división por 0) no rompe el combate
                if (double.IsNaN(basePower) || double.IsInfinity(basePower)) basePower = move.Power;
            }

            double mult = 1.0;
            foreach (var m in move.PowerModifiers)
                if (AllConditions(m.Conditions, actor, target, move)) mult *= m.Multiplier;

            if (TryGetAbility(actor, out var atkAb))
                foreach (var m in atkAb.OffensivePowerModifiers)
                    if (AllConditions(m.Conditions, actor, target, move)) mult *= m.Multiplier;
            if (TryGetAbility(target, out var defAb))
                foreach (var m in defAb.DefensivePowerModifiers)
                    if (AllConditions(m.Conditions, target, actor, move)) mult *= m.Multiplier; // "propio" = quien recibe

            // Objeto EQUIPADO del atacante (Carbón: Fuego ×1,2). "Propio" = quien lo lleva.
            mult *= HeldProduct(actor, EffectAction.PowerMultiplier, target, move);

            var realType = MoveTypeOf(actor, move);
            if (TryGetWeather(out var weather)) mult *= weather.MultiplierFor(realType);
            // Piel Feérica y compañía: el movimiento Normal convertido pega más.
            var ax6 = X(actor);
            if (ax6.ConvertNormalTo.Length > 0 && ax6.ConvertBoost != 1f && string.Equals(move.Type.Value, "normal", StringComparison.OrdinalIgnoreCase)
                && realType.Value == ax6.ConvertNormalTo) mult *= ax6.ConvertBoost;
            // Campo Eléctrico / Hierba / Psíquico: su tipo pega más si el atacante pisa el suelo.
            mult *= TerrainPowerMultiplier(actor, realType);

            // CARGA (Carga, Absorbe Fuego activado): el tipo absorbido/cargado pega más.
            if (actor.AbsorbedTypeBoost.HasValue && actor.AbsorbedTypeBoost.Value == realType) mult *= 1.5;
            if (_statuses != null)
                foreach (var v in actor.Volatiles)
                    if (TryGetStatus(v.Id, out var vd) && vd.Extras.BoostedType.HasValue && vd.Extras.BoostedType.Value == realType)
                        mult *= vd.Extras.BoostMultiplier;
            // Refuerzo de un solo uso (Ayuda Ajena, Yo Primero...).
            mult *= _oneShotPowerBoost;

            double result = basePower * mult;
            if (result <= 0) return 0;
            return Math.Max(1, (int)Math.Round(result));
        }

        private static double HpPercent(Combatant c) => c.MaxHp <= 0 ? 0 : 100.0 * c.CurrentHp / c.MaxHp;

        // PP que le quedan a ese movimiento (para Última Baza). Si no lo conoce, 5 (no cambia la potencia).
        private static int RemainingPp(Combatant c, Move move)
        {
            int i = c.IndexOfMove(move.Id);
            int pp = i >= 0 ? c.PpAt(i) : -1;
            return pp < 0 ? 5 : pp;
        }

        // ---------- CONDICIONES (Lote A) ----------

        /// <summary>¿Se cumplen TODAS? (lista vacía = sí). 'self' = dueño de la ficha; 'other' = el otro.</summary>
        private bool AllConditions(IReadOnlyList<Condition> conditions, Combatant self, Combatant other, Move move)
        {
            if (conditions == null || conditions.Count == 0) return true;
            foreach (var c in conditions)
                if (!Evaluate(c, self, other, move)) return false;
            return true;
        }

        private bool Evaluate(Condition c, Combatant self, Combatant other, Move move)
        {
            bool result = EvaluateRaw(c, self, other, move);
            return c.Negate ? !result : result;
        }

        private bool EvaluateRaw(Condition c, Combatant self, Combatant other, Move move)
        {
            var who = c.Subject == ConditionSubject.Self ? self : other;
            var whoElse = c.Subject == ConditionSubject.Self ? other : self;
            switch (c.Kind)
            {
                case ConditionKind.HasAnyStatus: return who != null && who.HasAnyStatus;
                case ConditionKind.HasStatus: return who != null && who.HasStatus(new StatusId(c.Text));
                case ConditionKind.HpPercent: return who != null && c.Compare((float)HpPercent(who));
                case ConditionKind.IsType: return who != null && ContainsType(TypesOf(who), new Id<ElementType>(c.Text));
                case ConditionKind.Weather:
                    return _battle != null && _battle.WeatherId != null && WeatherIsActive
                        && string.Equals(_battle.WeatherId, c.Text, StringComparison.OrdinalIgnoreCase);
                case ConditionKind.Friendship: return who != null && c.Compare(who.Friendship);
                case ConditionKind.Level: return who != null && c.Compare(who.Level);
                case ConditionKind.LevelDifference: return who != null && whoElse != null && c.Compare(who.Level - whoElse.Level);
                case ConditionKind.StatStage: return who != null && c.Compare(who.GetStage(new StatId(c.Text)));
                case ConditionKind.AlreadyActed: return who != null && who.ActedThisTurn;
                case ConditionKind.MoveType: return move != null && string.Equals((_hitTypeOverride ?? MoveTypeOf(self, move)).Value, c.Text, StringComparison.OrdinalIgnoreCase);
                case ConditionKind.MoveCategory: return move != null && string.Equals(move.Category.ToString(), c.Text, StringComparison.OrdinalIgnoreCase);
                case ConditionKind.MovePower: return move != null && c.Compare(move.Power);
                case ConditionKind.MoveMakesContact: return move != null && move.MakesContact;
                case ConditionKind.MoveHasTag: return move != null && move.HasTag(c.Text);
                case ConditionKind.RandomChance: return _rng.NextFloat() * 100f < c.Number;
                // --- 3.ª y 4.ª generación ---
                case ConditionKind.DamagedThisTurn: return who != null && who.DamagedThisTurn;
                case ConditionKind.TurnsOnField: return who != null && c.Compare(who.TurnsOnField);
                case ConditionKind.TargetChoseAttack: return ChoseAttack(who);
                case ConditionKind.UsedAllOtherMoves: return who != null && move != null && who.HasUsedAllOtherMoves(move.Id);
                case ConditionKind.StockpileCount: return who != null && c.Compare(who.Stockpile);
                case ConditionKind.MoveEffectiveness:
                {
                    // Eficacia del movimiento contra el sujeto (quien lo recibe), lanzado por el otro.
                    if (move == null || who == null) return false;
                    var attacker = whoElse ?? who;
                    return c.Compare(EffectivenessAgainst(attacker, who, move, out _));
                }
                case ConditionKind.MoveHasSecondary: return move != null && HasSecondary(move);
                case ConditionKind.HasItem: return who != null && !string.IsNullOrEmpty(who.HeldItem);
                case ConditionKind.LostItem: return who != null && who.LostItem;
                case ConditionKind.WeightKg: return who != null && c.Compare((float)WeightOf(who));
                case ConditionKind.SameGender: return self != null && other != null && self.Gender != Gender.Genderless && self.Gender == other.Gender;
                case ConditionKind.OppositeGender: return self != null && other != null && GenderText.Opposite(self.Gender, other.Gender);
                case ConditionKind.FieldCondition: return FieldConditionActive(c.Text);
                case ConditionKind.CanEvolve: return who != null && who.CanEvolve;
                default: return false;
            }
        }

        // ---------- CLIMA (Lote A) ----------

        private bool TryGetWeather(out WeatherDefinition weather)
        {
            weather = null;
            if (_battle == null || _battle.WeatherId == null || _weathers == null || !WeatherIsActive) return false;
            return _weathers.TryGet(new Id<WeatherDefinition>(_battle.WeatherId), out weather);
        }

        // Un impacto: fórmula de daño + multiplicadores de habilidad (Espesura, Sebo...). El azar se
        // recibe como parámetro para que la vista previa pueda forzar el mínimo, el máximo o el crítico.
        private DamageResult ComputeHit(Combatant actor, Combatant target, Move move, float effectiveness,
            bool stab, float stabMult, int attackStat, int defenseStat, int power, IRng rng)
        {
            // DAÑO ESPECIAL: no usa la fórmula. Solo respeta la inmunidad (efectividad 0).
            if (move.FixedDamage != FixedDamageKind.None)
                return new DamageResult(effectiveness <= 0f ? 0 : SpecialDamage(actor, target, move), false);

            // CRÍTICOS: Armadura Batalla / Caparazón y Conjuro (lado) los impiden; Afilado / Garra Afilada suben la etapa.
            bool noCrit = target != actor && (X(target).CritImmune || SideBlocks(target, d => d.BlocksCrits, out _));
            var context = new DamageContext(
                actor.Level, attackStat, defenseStat, power, effectiveness, stab, rng,
                move.CritStage + actor.CritBonus + X(actor).CritStageBonus + (int)HeldSum(actor, EffectAction.CritStage, target, move), stabMult,
                noCrit ? NoCrits : _rules.CritDenominators, _rules.CritMultiplier * X(actor).CritDamageMultiplier);
            var result = _damageFormula.Compute(context);
            int damage = result.Damage;
            var realType = MoveTypeOf(actor, move);

            //  - Ofensivo: refuerzo a poca vida del ATACANTE para este tipo (Espesura/Torrente).
            //  - Defensivo: reducción por tipo del OBJETIVO (Sebo, Ignífugo).
            float abMult = 1f;
            if (TryGetAbility(actor, out var atkAb))
                abMult *= atkAb.OffensiveTypeMultiplier(realType, actor.CurrentHp, actor.MaxHp);
            if (TryGetAbility(target, out var defAb))
                abMult *= defAb.IncomingTypeMultiplier(realType);
            // Chapoteolodo / Hidrochorro (y cualquier efecto de campo que baje un tipo): de los dos lados.
            foreach (var d in FieldConditions())
                if (d.TypeDamageMultipliers.TryGetValue(realType.Value ?? "", out var tm)) abMult *= tm;
            // Reflejo / Pantalla de Luz del lado del objetivo (los críticos las atraviesan).
            if (!result.WasCritical && target != actor) abMult *= ScreenMultiplier(target, CategoryOf(actor, move));
            // Cinta Experto: los golpes muy eficaces pegan más.
            abMult *= HeldProduct(actor, EffectAction.DamageDealtMultiplier, target, move);
            if (abMult != 1f)
            {
                damage = (int)(damage * abMult);
                if (damage < 1 && result.Damage > 0) damage = 1; // lo que dañaba no cae a 0 por redondeo
            }
            return new DamageResult(damage, result.WasCritical);
        }

        // Denominadores de crítico «imposible» (Armadura Batalla, Conjuro).
        private static readonly int[] NoCrits = Array.Empty<int>();

        // Efectos de campo activos en CUALQUIERA de los dos lados (Espacio Raro, Gravedad, Chapoteolodo).
        private IEnumerable<SideConditionDefinition> FieldConditions()
        {
            if (_battle == null) yield break;
            foreach (var d in SideConditionsOf(_battle.Player)) yield return d;
            foreach (var d in SideConditionsOf(_battle.Enemy)) yield return d;
        }

        // Contraataque, Manto Espejo, Venganza: FixedDamageAmount es el % (0 = el doble, como en los juegos).
        private static int ReturnPercent(Move move) => move.FixedDamageAmount > 0 ? move.FixedDamageAmount : 200;

        // Cuánto quita un movimiento de daño especial (ver FixedDamageKind).
        private static int SpecialDamage(Combatant actor, Combatant target, Move move)
        {
            switch (move.FixedDamage)
            {
                case FixedDamageKind.Fixed: return move.FixedDamageAmount;
                case FixedDamageKind.UserLevel: return actor.Level;
                case FixedDamageKind.HalfTargetHp: return Math.Max(1, target.CurrentHp / 2);
                case FixedDamageKind.OneHitKo: return target.CurrentHp;
                // Sacrificio: tanto como los PS que le quedan al usuario.
                case FixedDamageKind.UserHp: return Math.Max(1, actor.CurrentHp);
                case FixedDamageKind.ReturnPhysical: return Math.Max(1, actor.PhysicalDamageTakenThisTurn * ReturnPercent(move) / 100);
                case FixedDamageKind.ReturnSpecial: return Math.Max(1, actor.SpecialDamageTakenThisTurn * ReturnPercent(move) / 100);
                case FixedDamageKind.Bide: return Math.Max(1, actor.BideStored * ReturnPercent(move) / 100);
                // Esfuerzo: deja al rival con tus PS (si tiene más).
                case FixedDamageKind.Endeavor: return Math.Max(0, target.CurrentHp - actor.CurrentHp);
                // Represión Metal: devuelve el último daño recibido (de cualquier categoría), ×1,5 por defecto.
                case FixedDamageKind.ReturnAny:
                    return Math.Max(1, actor.LastDamageTaken * (move.FixedDamageAmount > 0 ? move.FixedDamageAmount : 150) / 100);
                default: return 0;
            }
        }

        /// <summary>
        /// VISTA PREVIA de daño (para la calculadora del editor): cuánto haría 'moveId' de 'attacker'
        /// contra 'target' AHORA MISMO (con sus etapas, estados y habilidades), en los cuatro extremos:
        /// tirada mínima/máxima, con y sin crítico. Usa EXACTAMENTE el mismo cálculo que el combate, pero
        /// no modifica a nadie ni consume el azar del combate.
        /// </summary>
        public DamagePreview PreviewDamage(Combatant attacker, Combatant target, Id<Move> moveId, Battle battle = null)
        {
            if (battle != null) _battle = battle; // para que el clima y las condiciones se vean en la vista previa
            if (attacker == null) throw new ArgumentNullException(nameof(attacker));
            if (target == null) throw new ArgumentNullException(nameof(target));
            var move = _moves.Get(moveId);

            float? accuracy = move.NeverMisses ? (float?)null
                : Math.Min(100f, move.Accuracy.Value.AsFraction * HitStageMultiplier(attacker, target, move) * 100f);
            if (!move.DealsDirectDamage)
                return new DamagePreview(false, 1f, false, false, 0, 0, 0, 0, move.MinHits, move.MaxHits, accuracy, target.CurrentHp, target.MaxHp);

            var h = PrepareHit(attacker, target, move);
            int Roll(bool crit, bool high) => ComputeHit(attacker, target, move, h.effectiveness, h.stab, h.stabMult,
                h.attackStat, h.defenseStat, h.power, new ForcedRng(crit, high)).Damage;

            bool immune = h.effectiveness <= 0f;
            return new DamagePreview(true, h.effectiveness, h.stab, h.abilityImmune,
                immune ? 0 : Roll(false, false), immune ? 0 : Roll(false, true),
                immune ? 0 : Roll(true, false), immune ? 0 : Roll(true, true),
                move.MinHits, move.MaxHits, accuracy, target.CurrentHp, target.MaxHp);
        }

        // Azar "forzado" para la vista previa: crítico sí/no y tirada al mínimo (0.85) o al máximo (1.00).
        private sealed class ForcedRng : IRng
        {
            private readonly bool _crit, _high;
            public ForcedRng(bool crit, bool high) { _crit = crit; _high = high; }
            // La fórmula decide el crítico con Next(0, n) == 0.
            public int Next(int minInclusive, int maxExclusive) => _crit ? minInclusive : maxExclusive - 1;
            public float NextFloat() => _high ? 0.99999994f : 0f;
        }

        /// <summary>Stat EFECTIVA actual (base × estado × etapas × habilidad) para mostrar en UI/debug.</summary>
        public int CurrentStat(Combatant c, StatId stat) => EffectiveStat(c, stat);

        /// <summary>Velocidad real en combate (etapas, estado, habilidad, clima...): la usa la IA para saber quién va antes.</summary>
        public int EffectiveSpeed(Combatant c) => c == null ? 0 : EffectiveStat(c, StatId.Speed);

        private int EffectiveStat(Combatant c, StatId stat) => EffectiveStat(c, stat, false);

        // 'ignoreStages' = el otro tiene Ignorante: no cuentan sus etapas.
        private int EffectiveStat(Combatant c, StatId stat, bool ignoreStages)
        {
            float value = c.Stats.Of(stat);

            // 1) Modificadores pasivos del estado (quemar -> Ataque x0.5, parálisis -> Velocidad x0.5).
            if (_statuses != null && c.Status.HasValue && TryGetStatus(c.Status.Value, out var def))
            {
                foreach (var mod in def.PassiveModifiers)
                    if (mod.Stat == stat)
                        value *= mod.Multiplier;
            }
            // 1a) ... y los de cada estado VOLÁTIL activo.
            if (_statuses != null)
                foreach (var v in c.Volatiles)
                    if (TryGetStatus(v.Id, out var vdef))
                        foreach (var mod in vdef.PassiveModifiers)
                            if (mod.Stat == stat)
                                value *= mod.Multiplier;

            // 1b) Modificadores pasivos de la HABILIDAD (p.ej. "duplica el Ataque"). Mismo mecanismo.
            if (TryGetAbility(c, out var ability))
            {
                foreach (var mod in ability.PassiveModifiers)
                    if (mod.Stat == stat)
                        value *= mod.Multiplier;

                // 1c) LOTE 3: refuerzo CONDICIONAL por estado (Agallas: Ataque x1.5 si está alterado).
                value *= ability.StatusStatMultiplier(stat, c.Status.HasValue);

                // 1d) 3.ª/4.ª gen.: refuerzos con condición (Velo Arena ×1,5 Def.Esp. en tormenta, Inicio Lento ×0,5,
                //     Clorofila ×2 Velocidad al sol, Pies Rápidos, Cuerpo Llama...).
                foreach (var cs in ability.Extras.ConditionalStats)
                    if (cs.Stat == stat && AllConditions(cs.Conditions, c, OpponentOf(c), null)) value *= cs.Multiplier;
            }

            // 1e) Objeto EQUIPADO: Cinta/Gafas/Pañuelo Elección, Chaleco Asalto, Mineral Evolutivo...
            value *= HeldStatMultiplier(c, stat);

            // 2) Etapas de combate (-6..+6). Multiplicador clásico: etapa>=0 -> (2+etapa)/2;
            //    etapa<0 -> 2/(2-etapa). Así +1 = x1.5, +2 = x2, -1 = x0.66, etc.
            if (!ignoreStages) value *= StageMultiplier(c.GetStage(stat));

            // 3) Efectos de lado que cambian la velocidad (Viento Afín ×2).
            if (stat == StatId.Speed)
                foreach (var d in SideConditionsOf(c)) value *= d.SpeedMultiplier;

            return (int)value;
        }

        /// <summary>
        /// Multiplicador de ACIERTO por etapas: (precisión del atacante − evasión del objetivo), entre -6 y +6.
        /// Clásico: etapa>=0 -> (3+etapa)/3; etapa<0 -> 3/(3-etapa). Así -1 = x0.75, +1 = x1.33, -6 = x0.33.
        /// Si el movimiento apunta a uno mismo, no cuenta la evasión del rival.
        /// </summary>
        private float HitStageMultiplier(Combatant attacker, Combatant target, Move move)
        {
            bool selfTargeted = target == null || target == attacker || move.Target == MoveTarget.Self;
            // Ignorante / identificado (Profecía): no cuenta la evasión subida; Ignorante del objetivo: no cuenta la precisión del atacante.
            bool ignoreEvasion = selfTargeted || X(attacker).IgnoresStages
                                 || target.GetStage(StatId.Evasion) > 0 && HasVolatileFlag(target, d => d.Extras.Identified);
            int acc = !selfTargeted && X(target).IgnoresStages ? 0 : attacker.GetStage(StatId.Accuracy);
            int stage = acc - (ignoreEvasion ? 0 : target.GetStage(StatId.Evasion));
            stage = Math.Max(-6, Math.Min(6, stage));
            return stage >= 0 ? (3f + stage) / 3f : 3f / (3f - stage);
        }

        /// <summary>Multiplicador clásico de una etapa de stat (-6..+6).</summary>
        private static float StageMultiplier(int stage)
        {
            if (stage >= 0) return (2f + stage) / 2f;
            return 2f / (2f - stage);
        }

        // El catálogo se indexa por el texto del id; envolvemos el StatusId en un Id<...> tipado.
        private bool TryGetStatus(StatusId id, out StatusConditionDefinition def)
            => _statuses.TryGet(new Id<StatusConditionDefinition>(id.Value), out def);

        // Resuelve la AbilityDefinition de un combatiente (null si no tiene habilidad o no hay catálogo).
        private bool TryGetAbility(Combatant c, out AbilityDefinition def)
        {
            def = null;
            if (!_rules.Generation.Abilities) return false;   // reglas de generación: sin habilidades
            if (_abilities == null || c == null || !c.Ability.HasValue) return false;
            if (!AbilityWorks(c)) return false;   // Bilis, Gas Reactivo, Rompemoldes
            return _abilities.TryGet(new Id<AbilityDefinition>(c.Ability.Value.Value), out def);
        }

        // Ordena las dos jugadas: prioridad del movimiento, luego velocidad, luego moneda al aire.
        private void OrderPlays(Battle battle, List<(bool isPlayer, BattleAction action)> plays)
        {
            if (plays.Count < 2) return;
            // GARRA RÁPIDA: se tira UNA vez por turno para cada uno, antes de ordenar.
            _quickClawPlayer = HeldActsFirst(battle.Player);
            _quickClawEnemy = HeldActsFirst(battle.Enemy);
            if (FirstGoesAfter(battle, plays[0], plays[1]))
            {
                var tmp = plays[0];
                plays[0] = plays[1];
                plays[1] = tmp;
            }
        }

        // ¿La jugada 'a' debería ir DESPUÉS de 'b'? (es decir, 'b' es más rápida/prioritaria)
        private bool FirstGoesAfter(
            Battle battle,
            (bool isPlayer, BattleAction action) a,
            (bool isPlayer, BattleAction action) b)
        {
            var actorA = a.isPlayer ? battle.Player : battle.Enemy;
            var actorB = b.isPlayer ? battle.Player : battle.Enemy;

            int pa = PriorityOf(actorA, a.action), pb = PriorityOf(actorB, b.action);
            if (pa != pb) return pa < pb; // menor prioridad -> va después

            // Garra Rápida: dentro de su prioridad, actúa el primero (si le tocó este turno).
            bool qa = a.isPlayer ? _quickClawPlayer : _quickClawEnemy, qb = b.isPlayer ? _quickClawPlayer : _quickClawEnemy;
            if (qa != qb) return qb;

            // Rezagado / Cola Plúmbea: dentro de su prioridad, siempre al final.
            bool lastA = X(actorA).MovesLast, lastB = X(actorB).MovesLast;
            if (lastA != lastB) return lastA;

            int sa = EffectiveStat(actorA, StatId.Speed), sb = EffectiveStat(actorB, StatId.Speed);
            // ESPACIO RARO: el más lento va primero.
            bool trickRoom = false;
            foreach (var d in FieldConditions()) if (d.ReversesTurnOrder) { trickRoom = true; break; }
            if (sa != sb) return trickRoom ? sa > sb : sa < sb; // menor velocidad -> va después (al revés en Espacio Raro)

            return _rng.Next(0, 2) == 0;  // empate exacto: azar
        }

        private int PriorityOf(Combatant actor, BattleAction action)
        {
            if (action is Flee) return 100;                 // huir va antes que atacar
            if (action is SwitchMonster) return 100;        // cambiar también va antes que atacar
            if (action is UseItemAction) return 100;        // usar objeto va antes que atacar
            if (action is Capturar) return 100;             // capturar va antes que atacar
            if (action is UseMove useMove)
            {
                var move = _moves.Get(useMove.Move);
                int p = move.Priority;
                // Vista Lince (Prankster): +prioridad a los movimientos de ESTADO.
                if (move.Category == MoveCategory.Status && TryGetAbility(actor, out var ab))
                    p += ab.StatusMovePriorityBonus;
                // Alas Vendaval: +prioridad a los movimientos de un tipo (Volador).
                var px = X(actor);
                if (px.PriorityTypeBonus != 0 && px.PriorityType.Length > 0
                    && string.Equals(MoveTypeOf(actor, move).Value, px.PriorityType, StringComparison.OrdinalIgnoreCase))
                    p += px.PriorityTypeBonus;
                return p;
            }
            return 0;
        }

        private static bool ContainsType(IReadOnlyList<Id<ElementType>> types, Id<ElementType> type)
        {
            for (int i = 0; i < types.Count; i++)
                if (types[i] == type)
                    return true;
            return false;
        }
    }
}
