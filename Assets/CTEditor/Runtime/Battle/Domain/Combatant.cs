using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Status;
using CTEditor.GameDefinition.Domain.Abilities;
using CTEditor.GameDefinition.Domain.Species;

namespace CTEditor.Battle.Domain
{
    /// <summary>
    /// El COMBATIENTE: la copia de trabajo INTERNA de Battle. Se crea desde el snapshot
    /// (BattleParticipant) al empezar el combate, y es lo que Battle MUTA durante la pelea (sus PS
    /// bajan, etc.). El snapshot original y, más atrás, el MonsterInstance, no se tocan: por eso el
    /// daño cae aquí y no allá. Al final, la diferencia entre el estado de este Combatant y el
    /// snapshot se convierte en los "resultados-out".
    ///
    /// Conserva el MISMO id que su participante, para poder mapear resultados al final.
    /// El constructor es 'internal': solo Battle (este assembly) crea combatientes, desde un snapshot.
    /// </summary>
    /// <summary>
    /// Un estado VOLÁTIL activo (confusión, atrapado, drenadoras, protegido...). Se apilan: puede
    /// haber varios a la vez además del estado principal, pero nunca dos iguales.
    /// </summary>
    public sealed class ActiveVolatileStatus
    {
        public StatusId Id { get; }
        /// <summary>Turnos que lleva activo.</summary>
        public int Turns { get; internal set; }
        /// <summary>Duración sorteada al aplicarlo (0 = hasta que se cure o se retire).</summary>
        public int Duration { get; }
        /// <summary>Turnos que CUENTAN para su duración: el turno en que se aplica no cuenta (empieza el siguiente).</summary>
        public int Age { get; internal set; }
        /// <summary>Recién aplicado este turno (su cuenta aún no ha empezado).</summary>
        public bool Fresh { get; internal set; } = true;
        public ActiveVolatileStatus(StatusId id, int duration) { Id = id; Duration = duration; }
    }

    public sealed partial class Combatant
    {
        public Id<BattleParticipant> Id { get; }

        /// <summary>De qué especie es (la que se ve en combate; Transformación no la cambia).</summary>
        public Id<Species> SpeciesId { get; }

        /// <summary>Género (Atracción solo funciona entre géneros opuestos).</summary>
        public Gender Gender { get; }
        /// <summary>¿Puede evolucionar todavía? (Mineral Evolutivo).</summary>
        public bool CanEvolve { get; }
        /// <summary>Nivel. Puede SUBIR en mitad del combate (al ganar experiencia), como en los juegos.</summary>
        public int Level { get; private set; }
        private StatBlock _stats;
        private StatBlock _statsOverride;                     // Transformación
        private IReadOnlyList<Id<ElementType>> _types;
        private IReadOnlyList<Id<ElementType>> _typesOverride; // Transformación / Conversión

        /// <summary>Estadísticas. Se recalculan si sube de nivel en mitad del combate (y cambian al transformarse).</summary>
        public StatBlock Stats { get => _statsOverride ?? _stats; private set => _stats = value; }
        /// <summary>Tipos actuales (cambian con Conversión o Transformación hasta que se retire).</summary>
        public IReadOnlyList<Id<ElementType>> Types => _typesOverride ?? _types;
        private readonly List<Id<Move>> _moves;

        // Movimientos PRESTADOS durante el combate (Mimético: un hueco; Transformación: todos). Tienen sus
        // propios PP y no tocan los del monstruo: al retirarse, vuelve a lo suyo.
        private readonly Dictionary<int, (Id<Move> move, int pp, int max)> _slotOverride = new Dictionary<int, (Id<Move>, int, int)>();
        private List<(Id<Move> move, int pp, int max)> _transformMoves;

        /// <summary>Movimientos que puede usar AHORA (con los prestados). Puede aprender uno nuevo al subir de nivel.</summary>
        public IReadOnlyList<Id<Move>> Moves
        {
            get
            {
                if (_transformMoves != null) return _transformMoves.ConvertAll(t => t.move);
                if (_slotOverride.Count == 0) return _moves;
                var list = new List<Id<Move>>(_moves);
                foreach (var kv in _slotOverride) if (kv.Key < list.Count) list[kv.Key] = kv.Value.move;
                return list;
            }
        }

        // ¿Ese hueco es prestado? Devuelve sus PP.
        private bool Borrowed(int index, out int pp, out int max)
        {
            pp = max = 0;
            if (_transformMoves != null)
            {
                if (index < 0 || index >= _transformMoves.Count) return true;
                pp = _transformMoves[index].pp; max = _transformMoves[index].max;
                return true;
            }
            if (_slotOverride.TryGetValue(index, out var o)) { pp = o.pp; max = o.max; return true; }
            return false;
        }

        /// <summary>Ratio de captura de su especie (1-255).</summary>
        public int CatchRate { get; }

        /// <summary>
        /// SUBIDA DE NIVEL en mitad del combate: nuevo nivel y estadísticas (las calcula quien conoce
        /// la especie). Los PS actuales suben lo mismo que los PS máximos (como en los juegos).
        /// </summary>
        internal void ApplyGrowth(int newLevel, StatBlock newStats)
        {
            if (newStats == null) return;
            int oldMax = MaxHp;
            Level = newLevel;
            Stats = newStats;
            if (!IsFainted) CurrentHp = Math.Max(1, Math.Min(MaxHp, CurrentHp + (MaxHp - oldMax)));
        }

        /// <summary>
        /// Aprende un movimiento en mitad del combate: en un hueco libre (slot = Moves.Count) o
        /// sustituyendo al del hueco 'slot'. El nuevo empieza con los PP llenos.
        /// </summary>
        internal void LearnMove(int slot, Id<Move> move, int maxPp)
        {
            if (slot < 0 || slot > _moves.Count) return;
            if (slot == _moves.Count) _moves.Add(move); else _moves[slot] = move;
            if (_pp == null) return; // los PP aún no se cargaron: se cargarán con el nuevo movimiento
            int n = _moves.Count;
            if (_pp.Length != n)
            {
                Array.Resize(ref _pp, n);
                Array.Resize(ref _maxPp, n);
            }
            _maxPp[slot] = Math.Max(1, maxPp);
            _pp[slot] = _maxPp[slot];
        }

        /// <summary>PS actuales DENTRO del combate. Es lo único mutable aquí (de momento).</summary>
        public int CurrentHp { get; private set; }

        /// <summary>Estado alterado actual (quemado, etc.). Null = sano. Mutable durante el combate.</summary>
        public StatusId? Status { get; private set; }

        /// <summary>La habilidad del combatiente (la de su especie, salvo que un movimiento la cambie: Imitación, Abatidoras...).</summary>
        public AbilityId? Ability => _abilityChanged ? _abilityOverride : _baseAbility;
        private readonly AbilityId? _baseAbility;

        /// <summary>Cuántos turnos lleva activo el estado actual (para tóxico progresivo y duraciones).</summary>
        public int StatusTurns { get; private set; }

        /// <summary>Duración sorteada del estado principal (0 = la de la ficha / permanente).</summary>
        public int StatusDuration { get; private set; }

        /// <summary>
        /// Turnos que CUENTAN para la duración del estado. Como en los juegos, el turno en que te duermen (o te
        /// congelan...) no cuenta: la cuenta empieza en el siguiente. Así nunca se despierta en el mismo turno.
        /// </summary>
        public int StatusAge { get; private set; }

        /// <summary>El estado principal se puso ESTE turno (su cuenta aún no empezó).</summary>
        public bool StatusIsFresh { get; private set; }

        // ---------- Estados VOLÁTILES (Lote B) ----------
        private readonly List<ActiveVolatileStatus> _volatiles = new List<ActiveVolatileStatus>();

        /// <summary>Estados volátiles activos (se suman al principal).</summary>
        public IReadOnlyList<ActiveVolatileStatus> Volatiles => _volatiles;

        public bool HasVolatile(StatusId id)
        {
            foreach (var v in _volatiles) if (v.Id == id) return true;
            return false;
        }

        /// <summary>¿Tiene este estado, sea el principal o uno volátil?</summary>
        public bool HasStatus(StatusId id) => (Status.HasValue && Status.Value == id) || HasVolatile(id);

        /// <summary>¿Tiene algún estado (principal o volátil)?</summary>
        public bool HasAnyStatus => Status.HasValue || _volatiles.Count > 0;

        internal void AddVolatile(StatusId id, int duration) { if (!HasVolatile(id)) _volatiles.Add(new ActiveVolatileStatus(id, duration)); }
        internal bool RemoveVolatile(StatusId id) => _volatiles.RemoveAll(v => v.Id == id) > 0;

        /// <summary>Cuántas veces SEGUIDAS se aplicó un estado "más difícil si se repite" (Protección).</summary>
        public int ConsecutiveGuards { get; private set; }
        internal bool GuardedThisTurn { get; private set; }
        internal void NoteGuard() { GuardedThisTurn = true; ConsecutiveGuards++; }
        internal void EndTurnGuards() { if (!GuardedThisTurn) ConsecutiveGuards = 0; GuardedThisTurn = false; }

        /// <summary>¿Ya actuó en este turno? (para condiciones como "si el rival ya se movió").</summary>
        public bool ActedThisTurn { get; private set; }
        internal void MarkActed() => ActedThisTurn = true;
        internal void ClearActed() => ActedThisTurn = false;

        /// <summary>Amistad (0-255). Solo lectura: la sube/baja el juego fuera del combate.</summary>
        public int Friendship { get; }

        /// <summary>Objeto equipado. Null = ninguno (o ya lo consumió, como una baya).</summary>
        public string HeldItem { get; private set; }
        internal void ConsumeHeldItem()
        {
            if (HeldItem == null) return;
            ConsumedItem = HeldItem;
            HeldItem = null;
            LostItem = true;
        }

        /// <summary>
        /// Etapas de stats en combate (-6..+6 por stat). NO modifica el StatBlock base (inmutable):
        /// es un modificador temporal que vive solo durante la batalla. El TurnResolver lo combina
        /// con la stat base y los modificadores de estado en EffectiveStat.
        /// </summary>
        private readonly Dictionary<StatId, int> _stages = new Dictionary<StatId, int>();

        /// <summary>Límite clásico de etapas por stat.</summary>
        public const int MinStage = -6;
        public const int MaxStage = 6;

        /// <summary>
        /// Retroceso (flinch): marca transitoria de UN turno. Si está activa cuando le toca actuar,
        /// el combatiente pierde el turno. Se limpia al final de cada turno. Solo "pega" si quien
        /// provocó el flinch actuó ANTES (si ya actuó, la marca no le afecta este turno).
        /// </summary>
        public bool Flinched { get; private set; }

        /// <summary>Si tiene valor, el combatiente está CARGANDO ese movimiento y lo lanzará el próximo turno.</summary>
        public Id<Move>? ChargingMove { get; private set; }

        /// <summary>Si true, este turno debe RECARGAR (tras un movimiento de recarga) y pierde la acción.</summary>
        public bool MustRecharge { get; private set; }

        // ---------- PP (puntos de poder) ----------
        // Paralelos a Moves: _pp[i] son los PP que le quedan al movimiento Moves[i]. Se INICIALIZAN la
        // primera vez que el TurnResolver los necesita, porque el MaxPp de cada movimiento vive en el
        // catálogo de movimientos, que el combatiente no conoce (mantiene al Combatant simple).
        private int[] _pp, _maxPp;
        private readonly IReadOnlyList<int> _initialPp;

        /// <summary>¿Ya se cargaron los PP desde el catálogo?</summary>
        public bool PpReady => _pp != null;

        /// <summary>PP que le quedan al movimiento en esa posición (-1 si aún no se inicializaron).</summary>
        public int PpAt(int moveIndex)
            => Borrowed(moveIndex, out var bp, out _) ? bp : _pp != null && moveIndex >= 0 && moveIndex < _pp.Length ? _pp[moveIndex] : -1;

        /// <summary>PP máximos del movimiento en esa posición (-1 si aún no se inicializaron).</summary>
        public int MaxPpAt(int moveIndex)
            => Borrowed(moveIndex, out _, out var bm) ? bm : _maxPp != null && moveIndex >= 0 && moveIndex < _maxPp.Length ? _maxPp[moveIndex] : -1;

        /// <summary>¿Le queda algún movimiento con PP? (Si no se inicializaron, se asume que sí.)</summary>
        public bool HasAnyPp
        {
            get
            {
                if (_transformMoves != null) return _transformMoves.Exists(t => t.pp > 0);
                if (_pp == null) return Moves.Count > 0;
                for (int i = 0; i < Moves.Count; i++) if (PpAt(i) > 0) return true;
                return false;
            }
        }

        /// <summary>¿Se puede elegir este movimiento? (tiene PP, o los PP aún no se cargaron).</summary>
        public bool CanUse(Id<Move> move)
        {
            int i = IndexOfMove(move);
            return i >= 0 && (_pp == null && !Borrowed(i, out _, out _) || PpAt(i) > 0) && !IsDisabled(move);
        }

        public int IndexOfMove(Id<Move> move)
        {
            var list = Moves;
            for (int i = 0; i < list.Count; i++) if (list[i] == move) return i;
            return -1;
        }

        /// <summary>¿Se puede ELEGIR el movimiento del hueco 'index'? (tiene PP y no está anulado). Lo usan la IA y la interfaz.</summary>
        public bool CanChoose(int index)
        {
            var list = Moves;
            if (index < 0 || index >= list.Count) return false;
            return PpAt(index) != 0 && !IsDisabled(list[index]);
        }

        /// <summary>Copia de los PP actuales (para el resultado del combate). Null si no se usaron.</summary>
        public IReadOnlyList<int> CurrentPpSnapshot() => _pp == null ? _initialPp : (int[])_pp.Clone();

        // Carga los PP: máximo desde el catálogo; actuales desde el snapshot (o llenos si no venían).
        internal void InitPp(int[] maxPp)
        {
            if (_pp != null) return;
            _maxPp = maxPp;
            _pp = new int[maxPp.Length];
            for (int i = 0; i < maxPp.Length; i++)
            {
                int start = _initialPp != null && i < _initialPp.Count ? _initialPp[i] : maxPp[i];
                _pp[i] = Math.Max(0, Math.Min(maxPp[i], start));
            }
        }

        // Suma PP a un movimiento (Éter), sin pasar del máximo.
        internal void AddPp(int moveIndex, int amount)
        {
            if (Borrowed(moveIndex, out _, out _)) return; // los prestados no se recargan
            if (_pp == null || moveIndex < 0 || moveIndex >= _pp.Length || amount <= 0) return;
            _pp[moveIndex] = Math.Min(_maxPp[moveIndex], _pp[moveIndex] + amount);
        }

        // Gasta 1 PP del movimiento (si lo tiene). Devuelve false si no quedaban.
        internal bool TryConsumePp(Id<Move> move)
        {
            int i = IndexOfMove(move);
            if (i < 0) return true; // movimiento ajeno (Forcejeo, uno llamado por Metrónomo...)
            if (_transformMoves != null)
            {
                var t = _transformMoves[i];
                if (t.pp <= 0) return false;
                _transformMoves[i] = (t.move, t.pp - 1, t.max);
                return true;
            }
            if (_slotOverride.TryGetValue(i, out var o))
            {
                if (o.pp <= 0) return false;
                _slotOverride[i] = (o.move, o.pp - 1, o.max);
                return true;
            }
            if (_pp == null) return true; // PP desactivados
            if (_pp[i] <= 0) return false;
            _pp[i]--;
            return true;
        }

        internal Combatant(BattleParticipant snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            Id = snapshot.Id;
            SpeciesId = snapshot.SpeciesId;
            Gender = snapshot.Gender;
            CanEvolve = snapshot.CanEvolve;
            Level = snapshot.Level;
            _stats = snapshot.Stats;      // StatBlock ya es inmutable
            _types = snapshot.Types;      // ya vienen como listas de solo lectura copiadas
            _moves = new List<Id<Move>>(snapshot.Moves);
            CatchRate = snapshot.CatchRate;
            CurrentHp = snapshot.CurrentHp;
            Status = snapshot.InitialStatus;
            _baseAbility = snapshot.Ability;
            WeightKg = snapshot.WeightKg;
            BaseExpYield = snapshot.BaseExpYield;
            _initialPp = snapshot.InitialPp;
            Friendship = snapshot.Friendship;
            HeldItem = snapshot.HeldItem;
            EvYield = snapshot.EvYield;
        }

        /// <summary>Rendimiento base de XP de su especie (cuánto "vale" derrotarlo).</summary>
        public int BaseExpYield { get; }

        /// <summary>EVs que otorga derrotarlo (el rendimiento de EVs de su especie).</summary>
        public IReadOnlyList<EvYieldEntry> EvYield { get; }

        public int MaxHp => Stats.Of(StatId.Hp);
        public bool IsFainted => CurrentHp <= 0;

        // Mutaciones internas del combate. 'internal' porque solo la lógica de Battle las dispara,
        // nunca código de fuera del contexto.
        internal void TakeDamage(int amount)
        {
            if (amount <= 0) return;
            CurrentHp = Math.Max(0, CurrentHp - amount);
        }

        internal void HealHp(int amount)
        {
            if (amount <= 0 || IsFainted) return;
            CurrentHp = Math.Min(MaxHp, CurrentHp + amount);
        }

        // Revive: cura a un combatiente DEBILITADO (HealHp se niega a tocar a un caído). Solo aplica
        // si está debilitado; deja al menos 1 PS y nunca supera el máximo.
        internal void Revive(int hp)
        {
            if (!IsFainted) return;
            CurrentHp = Math.Min(MaxHp, Math.Max(1, hp));
        }

        // Estado alterado: lo fija/limpia solo la lógica de Battle (TurnResolver).
        internal void SetStatus(StatusId status, int duration = 0)
        {
            Status = status;
            StatusTurns = 0; // arranca el contador para este estado
            StatusDuration = duration;
            StatusAge = 0;
            StatusIsFresh = true;
        }

        internal void ClearStatus()
        {
            Status = null;
            StatusTurns = 0;
            StatusDuration = 0;
            StatusAge = 0;
            StatusIsFresh = false;
        }

        /// <summary>
        /// Al RETIRARSE del campo: se van los volátiles, las etapas vuelven a 0 y se cancelan cargas y
        /// recargas (como en los juegos). El estado principal se queda (salvo que la ficha diga lo contrario,
        /// eso lo decide el resolvedor).
        /// </summary>
        internal void OnSwitchedOut()
        {
            _volatiles.Clear();
            _stages.Clear();
            ChargingMove = null;
            MustRecharge = false;
            Flinched = false;
            ConsecutiveGuards = 0;
            GuardedThisTurn = false;
            // Lo que solo dura mientras está en el campo.
            SubstituteHp = 0;
            CritBonus = 0;
            ClearDisable(); ClearEncore(); ClearRampage(); ClearRage(); ClearBide();
            _slotOverride.Clear();
            _transformMoves = null;
            _typesOverride = null;
            _statsOverride = null;
            LastMoveUsed = null;
            ResetTurnDamage();
            ResetFieldState();
        }

        // =====================================================================
        // CIERRE DEL COMBATE: sustituto, crítico, anular, otra vez, saña, furia,
        // venganza, daño recibido, movimientos prestados, transformación y tipos.
        // =====================================================================

        /// <summary>PS del SUSTITUTO (0 = no tiene). Recibe los golpes del rival en su lugar.</summary>
        public int SubstituteHp { get; private set; }
        public bool HasSubstitute => SubstituteHp > 0;
        internal void SetSubstitute(int hp) => SubstituteHp = Math.Max(0, hp);
        /// <summary>El sustituto recibe daño; devuelve cuánto absorbió.</summary>
        internal int DamageSubstitute(int amount)
        {
            int taken = Math.Min(Math.Max(0, amount), SubstituteHp);
            SubstituteHp -= taken;
            return taken;
        }

        /// <summary>Índice de crítico extra (Foco Energía) mientras siga en el campo.</summary>
        public int CritBonus { get; private set; }
        internal void AddCritBonus(int stages) => CritBonus = Math.Max(0, Math.Min(4, CritBonus + stages));

        /// <summary>El último movimiento que usó (para Anulación, Otra Vez, Espejo, Mimético).</summary>
        public Id<Move>? LastMoveUsed { get; private set; }
        internal void NoteMoveUsed(Id<Move> move) => LastMoveUsed = move;

        // --- Anulación ---
        public Id<Move>? DisabledMove { get; private set; }
        public int DisabledTurns { get; private set; }
        public bool IsDisabled(Id<Move> move) => DisabledMove.HasValue && DisabledMove.Value == move && DisabledTurns > 0;
        internal void Disable(Id<Move> move, int turns) { DisabledMove = move; DisabledTurns = Math.Max(1, turns); }
        internal void ClearDisable() { DisabledMove = null; DisabledTurns = 0; }
        /// <summary>Resta un turno; true si la anulación terminó ahora.</summary>
        internal bool TickDisable()
        {
            if (!DisabledMove.HasValue) return false;
            if (--DisabledTurns > 0) return false;
            ClearDisable();
            return true;
        }

        // --- Otra Vez ---
        public Id<Move>? EncoreMove { get; private set; }
        public int EncoreTurns { get; private set; }
        internal void SetEncore(Id<Move> move, int turns) { EncoreMove = move; EncoreTurns = Math.Max(1, turns); }
        internal void ClearEncore() { EncoreMove = null; EncoreTurns = 0; }
        internal bool TickEncore()
        {
            if (!EncoreMove.HasValue) return false;
            if (--EncoreTurns > 0) return false;
            ClearEncore();
            return true;
        }

        // --- Saña / Danza Pétalo ---
        public Id<Move>? RampageMove { get; private set; }
        public int RampageTurnsLeft { get; private set; }
        public StatusId? RampageEndStatus { get; private set; }
        internal void StartRampage(Id<Move> move, int turnsLeft, StatusId? endStatus)
        { RampageMove = move; RampageTurnsLeft = Math.Max(0, turnsLeft); RampageEndStatus = endStatus; }
        /// <summary>Gasta un turno encadenado; true si se acabó (toca el estado final).</summary>
        internal bool AdvanceRampage()
        {
            if (!RampageMove.HasValue) return false;
            if (--RampageTurnsLeft > 0) return false;
            RampageMove = null;
            return true;
        }
        internal void ClearRampage() { RampageMove = null; RampageTurnsLeft = 0; RampageEndStatus = null; }

        // --- Furia ---
        public bool Raging { get; private set; }
        public StatId RageStat { get; private set; }
        public int RageStages { get; private set; }
        internal void SetRage(StatId stat, int stages) { Raging = true; RageStat = stat; RageStages = stages; }
        internal void ClearRage() { Raging = false; RageStages = 0; }

        // --- Venganza ---
        public Id<Move>? BideMove { get; private set; }
        public int BideTurnsLeft { get; private set; }
        public int BideStored { get; private set; }
        internal void StartBide(Id<Move> move, int turns) { BideMove = move; BideTurnsLeft = Math.Max(1, turns); BideStored = 0; }
        internal void ClearBide() { BideMove = null; BideTurnsLeft = 0; BideStored = 0; }
        internal bool AdvanceBide() => BideMove.HasValue && --BideTurnsLeft <= 0;

        /// <summary>¿Está obligado a seguir con un movimiento? (carga, recarga, Saña, Venganza). La interfaz no muestra el menú.</summary>
        public bool IsLockedIntoMove => ChargingMove.HasValue || MustRecharge || RampageMove.HasValue || BideMove.HasValue;

        // --- Daño recibido este turno (Contraataque, Manto Espejo) ---
        public int PhysicalDamageTakenThisTurn { get; private set; }
        public int SpecialDamageTakenThisTurn { get; private set; }
        internal void NoteDamageTaken(int amount, MoveCategory category)
        {
            LastDamageTaken = amount;
            if (amount > 0) DamagedThisTurn = true;
            NoteDamageTakenCore(amount, category);
        }

        private void NoteDamageTakenCore(int amount, MoveCategory category)
        {
            if (amount <= 0) return;
            if (category == MoveCategory.Physical) PhysicalDamageTakenThisTurn += amount;
            else if (category == MoveCategory.Special) SpecialDamageTakenThisTurn += amount;
            if (BideMove.HasValue) BideStored += amount;
        }
        internal void ResetTurnDamage() { PhysicalDamageTakenThisTurn = 0; SpecialDamageTakenThisTurn = 0; DamagedThisTurn = false; }

        // --- Movimientos prestados, tipos y transformación ---

        /// <summary>Mimético: el hueco 'slot' pasa a ser 'move' (con 'pp' PP) hasta que se retire.</summary>
        internal void BorrowMove(int slot, Id<Move> move, int pp)
        {
            if (slot < 0 || slot >= Moves.Count) return;
            if (_transformMoves != null) { _transformMoves[slot] = (move, pp, pp); return; }
            _slotOverride[slot] = (move, pp, pp);
        }

        internal void ChangeTypes(IReadOnlyList<Id<ElementType>> types) => _typesOverride = new List<Id<ElementType>>(types);

        /// <summary>Transformación: tipos, estadísticas (menos los PS), etapas y movimientos (con 'ppEach' PP) del objetivo.</summary>
        internal void TransformInto(Combatant other, int ppEach)
        {
            _typesOverride = new List<Id<ElementType>>(other.Types);
            var b = new StatBlock.Builder();
            foreach (var st in other.Stats.Stats) b.Set(st, st == StatId.Hp ? _stats.Of(StatId.Hp) : other.Stats.Of(st));
            if (!other.Stats.Has(StatId.Hp)) b.Set(StatId.Hp, _stats.Of(StatId.Hp));
            _statsOverride = b.Build();
            _stages.Clear();
            foreach (var kv in other._stages) _stages[kv.Key] = kv.Value;
            _transformMoves = new List<(Id<Move>, int, int)>();
            foreach (var m in other.Moves) _transformMoves.Add((m, ppEach, ppEach));
            _slotOverride.Clear();
        }

        /// <summary>¿Está transformado?</summary>
        public bool IsTransformed => _transformMoves != null;

        /// <summary>Todas las etapas vuelven a 0 (Niebla).</summary>
        internal void ResetStages() => _stages.Clear();

        /// <summary>¿Tiene alguna etapa distinta de 0? (para la IA con Niebla).</summary>
        public bool HasAnyStage { get { foreach (var v in _stages.Values) if (v != 0) return true; return false; } }

        /// <summary>Copia de sus etapas (para Relevo).</summary>
        internal Dictionary<StatId, int> StagesSnapshot() => new Dictionary<StatId, int>(_stages);
        internal void SetStages(Dictionary<StatId, int> stages) { _stages.Clear(); foreach (var kv in stages) _stages[kv.Key] = kv.Value; }

        // Avanza el contador de turnos del estado (lo llama el fin de turno).
        // La edad (lo que cuenta para la duración) solo avanza si el estado NO se puso en este mismo turno.
        internal void AdvanceStatusTurn()
        {
            StatusTurns++;
            if (StatusIsFresh) StatusIsFresh = false; else StatusAge++;
        }

        // Retroceso (flinch): lo marca un movimiento durante la resolución; se limpia al fin de turno.
        internal void SetFlinched() => Flinched = true;
        internal void ClearFlinch() => Flinched = false;

        // Dos turnos: cargar un movimiento y, al turno siguiente, lanzarlo.
        internal void SetChargingMove(Id<Move> move) => ChargingMove = move;
        internal void ClearChargingMove() => ChargingMove = null;

        // Recarga obligatoria tras un movimiento de recarga.
        internal void SetMustRecharge() => MustRecharge = true;
        internal void ClearRecharge() => MustRecharge = false;

        /// <summary>Etapa actual de una stat (0 si nunca se modificó).</summary>
        public int GetStage(StatId stat)
            => _stages.TryGetValue(stat, out var s) ? s : 0;

        /// <summary>
        /// Cambia la etapa de una stat aplicando el delta y recortando a [-6, +6].
        /// Devuelve el delta REAL aplicado (0 si ya estaba en el tope): así el resolvedor sabe
        /// si emitir el evento o no.
        /// </summary>
        internal int ChangeStage(StatId stat, int delta)
        {
            int current = GetStage(stat);
            int next = Math.Max(MinStage, Math.Min(MaxStage, current + delta));
            _stages[stat] = next;
            return next - current;
        }
    }
}
