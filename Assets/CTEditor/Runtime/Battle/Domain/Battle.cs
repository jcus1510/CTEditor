using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Status;
using CTEditor.GameDefinition.Domain.Stats;

namespace CTEditor.Battle.Domain
{
    /// <summary>
    /// El agregado raíz del combate. Sostiene el ESTADO: los dos EQUIPOS (cada uno con su activo y su
    /// banca) y el desenlace. La LÓGICA del turno vive en el TurnResolver (un domain service): el
    /// agregado guarda el estado y expone transiciones simples; el servicio ejecuta el pipeline.
    ///
    /// Se arma desde SNAPSHOTS (J.5): construye sus Combatant internos y a partir de ahí ya no
    /// necesita saber de Party para nada. 'Player'/'Enemy' son atajos al ACTIVO de cada equipo, así
    /// el grueso del resolvedor (que pelea 1 contra 1) no cambia al introducir equipos.
    /// </summary>
    public sealed class Battle
    {
        public BattleTeam PlayerTeam { get; }
        public BattleTeam EnemyTeam { get; }

        /// <summary>El combatiente activo del jugador (atajo a PlayerTeam.Active).</summary>
        public Combatant Player => PlayerTeam.Active;
        /// <summary>El combatiente activo del rival (atajo a EnemyTeam.Active).</summary>
        public Combatant Enemy => EnemyTeam.Active;

        public BattleOutcome Outcome { get; private set; }
        public bool IsOver => Outcome != BattleOutcome.InProgress;

        /// <summary>¿El rival es un ENTRENADOR? (afecta la XP; los clásicos dan ×1.5 vs entrenadores).</summary>
        public bool IsTrainerBattle { get; }

        // --- Registro de PARTICIPACIÓN (para repartir XP como los clásicos) ---
        // Por cada rival: qué combatientes del jugador le "hicieron frente" (estuvieron activos contra él).
        private readonly Dictionary<string, HashSet<string>> _facedBy = new Dictionary<string, HashSet<string>>();
        // XP acumulada por participante del jugador durante el combate (va al BattleResult).
        private readonly Dictionary<string, int> _xpAccumulated = new Dictionary<string, int>();
        // EVs acumulados por participante del jugador: participante -> (stat -> puntos).
        private readonly Dictionary<string, Dictionary<StatId, int>> _evAccumulated =
            new Dictionary<string, Dictionary<StatId, int>>();
        // Rivales que ya entregaron su XP (evita duplicar si un evento se re-procesa).
        private readonly HashSet<string> _xpYielded = new HashSet<string>();

        /// <summary>Constructor 1v1 (compatibilidad): un participante por bando.</summary>
        public Battle(BattleParticipant player, BattleParticipant enemy, bool isTrainerBattle = false)
            : this(new[] { player ?? throw new ArgumentNullException(nameof(player)) },
                   new[] { enemy ?? throw new ArgumentNullException(nameof(enemy)) },
                   isTrainerBattle)
        {
        }

        /// <summary>Constructor de EQUIPOS: una lista de participantes por bando (el primero entra al campo).</summary>
        public Battle(IReadOnlyList<BattleParticipant> playerTeam, IReadOnlyList<BattleParticipant> enemyTeam, bool isTrainerBattle = false)
        {
            if (playerTeam == null) throw new ArgumentNullException(nameof(playerTeam));
            if (enemyTeam == null) throw new ArgumentNullException(nameof(enemyTeam));

            PlayerTeam = new BattleTeam(playerTeam);
            EnemyTeam = new BattleTeam(enemyTeam);
            IsTrainerBattle = isTrainerBattle;
            Outcome = BattleOutcome.InProgress;
            NoteFaceOff(); // los dos activos iniciales ya se enfrentan
        }

        // ---------------- Clima (Lote A) ----------------

        /// <summary>Id del clima activo ("rain", "sun"...). Null = despejado.</summary>
        public string WeatherId { get; private set; }

        /// <summary>Turnos que le quedan al clima (0 = indefinido mientras haya clima).</summary>
        public int WeatherTurnsLeft { get; private set; }

        internal void SetWeather(string id, int turns) { WeatherId = id; WeatherTurnsLeft = turns < 0 ? 0 : turns; }
        internal void ClearWeather() { WeatherId = null; WeatherTurnsLeft = 0; }

        /// <summary>Resta un turno al clima. Devuelve true si se ACABÓ ahora.</summary>
        internal bool TickWeather()
        {
            if (WeatherId == null || WeatherTurnsLeft <= 0) return false;
            WeatherTurnsLeft--;
            if (WeatherTurnsLeft > 0) return false;
            return true;
        }

        // ---------------- Huida ----------------

        /// <summary>Intentos de huida del jugador en este combate (la fórmula clásica los usa: cada intento facilita el siguiente).</summary>
        public int FleeAttempts { get; private set; }
        internal void NoteFleeAttempt() => FleeAttempts++;

        // ---------------- Crecimiento en mitad del combate ----------------

        /// <summary>
        /// Un monstruo del JUGADOR subió de nivel en mitad del combate: el orquestador (que conoce la
        /// especie y la fórmula) le pasa su nuevo nivel y estadísticas. Battle no sabe de Party: recibe
        /// los datos ya calculados, igual que recibió el snapshot. Devuelve false si el id no es suyo.
        /// </summary>
        public bool ApplyLevelUp(Id<BattleParticipant> id, int newLevel, StatBlock newStats)
        {
            var c = PlayerTeam.Find(id);
            if (c == null || newStats == null) return false;
            c.ApplyGrowth(newLevel, newStats);
            return true;
        }

        /// <summary>
        /// Un monstruo del JUGADOR aprendió un movimiento en mitad del combate (hueco libre = Moves.Count,
        /// o el hueco que olvida). Queda usable desde el siguiente turno, con los PP llenos.
        /// </summary>
        public bool TeachMove(Id<BattleParticipant> id, int slot, Id<CTEditor.GameDefinition.Domain.Moves.Move> move, int maxPp)
        {
            var c = PlayerTeam.Find(id);
            if (c == null || slot < 0 || slot > c.Moves.Count) return false;
            c.LearnMove(slot, move, maxPp);
            return true;
        }

        // 'internal': solo el TurnResolver (mismo assembly) fija el desenlace; nadie de fuera.
        internal void SetOutcome(BattleOutcome outcome) => Outcome = outcome;

        // 'internal': cambio durante el turno (lo invoca el resolvedor al procesar una acción de cambio).
        internal bool SwitchActive(bool playerSide, Id<BattleParticipant> target)
        {
            bool ok = (playerSide ? PlayerTeam : EnemyTeam).SwitchTo(target);
            if (ok) NoteFaceOff();
            return ok;
        }

        /// <summary>
        /// RELEVO FORZADO entre turnos: cuando el activo de un bando cayó y quedan reservas, el
        /// orquestador (fuera de este assembly) envía el reemplazo con esta transición pública. Solo
        /// procede si el activo de ese bando está debilitado.
        /// </summary>
        public bool SendReplacement(bool playerSide, Id<BattleParticipant> target)
        {
            var team = playerSide ? PlayerTeam : EnemyTeam;
            if (!team.Active.IsFainted) return false; // solo se releva a un activo caído
            bool ok = team.SwitchTo(target);
            if (ok) NoteFaceOff();
            return ok;
        }

        // Registra que el activo del jugador está haciendo frente al activo rival AHORA. Con esto, al
        // caer un rival, sabemos entre quiénes repartir su XP (como los juegos clásicos).
        private void NoteFaceOff()
        {
            var p = PlayerTeam.Active;
            var e = EnemyTeam.Active;
            if (p == null || e == null || p.IsFainted || e.IsFainted) return;

            if (!_facedBy.TryGetValue(e.Id.Value, out var set))
            {
                set = new HashSet<string>();
                _facedBy[e.Id.Value] = set;
            }
            set.Add(p.Id.Value);
        }

        /// <summary>Ids de los combatientes del jugador que hicieron frente a ese rival.</summary>
        internal IReadOnlyCollection<string> ParticipantsAgainst(string enemyId)
            => _facedBy.TryGetValue(enemyId, out var set) ? (IReadOnlyCollection<string>)set : Array.Empty<string>();

        /// <summary>Marca a un rival como "ya entregó su XP". Devuelve false si ya estaba marcado.</summary>
        internal bool TryMarkXpYielded(string enemyId) => _xpYielded.Add(enemyId);

        /// <summary>Acumula XP ganada por un participante del jugador (irá en el BattleResult).</summary>
        internal void RecordXpAward(Id<BattleParticipant> participant, int amount)
        {
            if (amount <= 0) return;
            _xpAccumulated.TryGetValue(participant.Value, out var current);
            _xpAccumulated[participant.Value] = current + amount;
        }

        /// <summary>
        /// Acumula EVs ganados por un participante del jugador (irán en el BattleResult). Aquí NO se
        /// aplican topes: el combate solo anota lo ganado; es Party (EffortValues) quien custodia los
        /// límites al aplicarlo. Cada contexto vigila SUS invariantes.
        /// </summary>
        internal void RecordEvAward(Id<BattleParticipant> participant, StatId stat, int amount)
        {
            if (amount <= 0) return;
            if (!_evAccumulated.TryGetValue(participant.Value, out var perStat))
            {
                perStat = new Dictionary<StatId, int>();
                _evAccumulated[participant.Value] = perStat;
            }
            perStat.TryGetValue(stat, out var current);
            perStat[stat] = current + amount;
        }

        /// <summary>¿Ese bando necesita enviar un relevo? (su activo cayó pero aún tiene reservas).</summary>
        public bool NeedsReplacement(bool playerSide)
        {
            var team = playerSide ? PlayerTeam : EnemyTeam;
            return team.Active.IsFainted && team.HasUnfaintedReserves;
        }

        /// <summary>
        /// Empaqueta el estado final como BattleResult (los "resultados-out" de J.5) para que el
        /// orquestador lo aplique de vuelta a los MonsterInstance. Incluye a TODOS los miembros de
        /// ambos equipos (activos y banca), con sus PS y estado finales. Battle nunca tocó Party.
        /// </summary>
        public BattleResult ToResult()
        {
            var results = new List<ParticipantResult>();
            foreach (var m in PlayerTeam.Members) results.Add(ToParticipantResult(m));
            foreach (var m in EnemyTeam.Members) results.Add(ToParticipantResult(m));

            var awards = new List<XpAward>();
            foreach (var kv in _xpAccumulated)
                awards.Add(new XpAward(new Id<BattleParticipant>(kv.Key), kv.Value));

            var evAwards = new List<EvAward>();
            foreach (var kv in _evAccumulated)
                foreach (var stat in kv.Value)
                    evAwards.Add(new EvAward(new Id<BattleParticipant>(kv.Key), stat.Key, stat.Value));

            return new BattleResult(Outcome, results, awards, evAwards);
        }

        private static ParticipantResult ToParticipantResult(Combatant c)
            => new ParticipantResult(c.Id, c.CurrentHp, c.IsFainted, c.Status, c.CurrentPpSnapshot(), c.HeldItem);
    }
}
