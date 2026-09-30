using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Rules;
using CTEditor.Party.Domain;
using PartyAggregate = CTEditor.Party.Domain.Party;

namespace CTEditor.Adventure.Domain
{
    /// <summary>Dónde acabó un monstruo recibido (capturado, regalado...).</summary>
    public enum StoredIn
    {
        Party,    // entró en el equipo
        Box,      // el equipo estaba lleno: fue al PC
        Released  // equipo lleno y el PC desactivado por las reglas: se liberó
    }

    /// <summary>
    /// LA PARTIDA DEL JUGADOR: su equipo, su PC (la caja), su mochila, su dinero y los entrenadores
    /// que ya venció. Es la ÚNICA fuente de verdad de "lo que tiene el jugador": el combate recibe
    /// fotos (snapshots) de su equipo y, al acabar, los resultados vuelven aquí.
    ///
    /// Con esta misma idea se armarán más adelante los equipos de los NPC (TeamBuilder usa la misma
    /// receta para ambos). Dominio puro: se puede guardar, cargar y probar sin Unity.
    /// </summary>
    public sealed class PlayerSave
    {
        public string PlayerName { get; set; }
        public PartyAggregate Party { get; }
        public Bag Bag { get; }
        public int Money { get; private set; }

        private readonly List<MonsterInstance> _box = new List<MonsterInstance>();
        private readonly HashSet<string> _defeatedTrainers = new HashSet<string>();
        private int _nextId;

        /// <summary>El PC: los monstruos que no caben en el equipo.</summary>
        public IReadOnlyList<MonsterInstance> Box => _box;

        /// <summary>Entrenadores ya vencidos (por id).</summary>
        public IReadOnlyCollection<string> DefeatedTrainers => _defeatedTrainers;

        /// <summary>El estado del MUNDO de esta partida: hora, lugar, clima del mapa y marcas de la historia.</summary>
        public WorldState World { get; } = new WorldState();

        public PlayerSave(Ruleset rules, string playerName = "Jugador", int? money = null)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            Party = new PartyAggregate(rules);
            Bag = new Bag();
            PlayerName = string.IsNullOrWhiteSpace(playerName) ? "Jugador" : playerName;
            Money = Math.Max(0, money ?? rules.Adventure.StartingMoney);
        }

        // ---------------- Ids ----------------

        /// <summary>Un id NUEVO y único para un monstruo de esta partida ("m1", "m2"...).</summary>
        public Id<MonsterInstance> NewMonsterId() => new Id<MonsterInstance>("m" + (++_nextId));

        // ---------------- Dinero ----------------

        public void AddMoney(int amount) { if (amount > 0) Money += amount; }

        /// <summary>Gasta dinero si alcanza. Devuelve false (y no gasta nada) si no hay suficiente.</summary>
        public bool Spend(int amount)
        {
            if (amount < 0 || amount > Money) return false;
            Money -= amount;
            return true;
        }

        /// <summary>Pierde un % del dinero (al perder un combate). Devuelve cuánto se perdió.</summary>
        public int LosePercent(float percent)
        {
            int lost = (int)(Money * Math.Max(0f, Math.Min(100f, percent)) / 100f);
            Money -= lost;
            return lost;
        }

        // ---------------- Monstruos ----------------

        /// <summary>
        /// Recibe un monstruo nuevo (capturado o regalado): al equipo si cabe; si no, al PC (o se libera
        /// si las reglas desactivan el PC).
        /// </summary>
        public StoredIn Receive(MonsterInstance mon, bool sendToBoxWhenFull = true)
        {
            if (mon == null) throw new ArgumentNullException(nameof(mon));
            if (!Party.IsFull && Party.Add(mon).IsSuccess) return StoredIn.Party;
            if (!sendToBoxWhenFull) return StoredIn.Released;
            _box.Add(mon);
            return StoredIn.Box;
        }

        /// <summary>Busca un monstruo del jugador (equipo o PC) por id.</summary>
        public MonsterInstance Find(Id<MonsterInstance> id)
        {
            foreach (var m in Party.Members) if (m.Id == id) return m;
            foreach (var m in _box) if (m.Id == id) return m;
            return null;
        }

        /// <summary>¿Puede combatir? (al menos un miembro del equipo en pie).</summary>
        public bool CanBattle => Party.HasUsableMonster();

        /// <summary>Centro Pokémon: cura PS, estado y PP de TODO el equipo.</summary>
        public void HealAll()
        {
            foreach (var m in Party.Members) m.FullRestore();
        }

        /// <summary>
        /// Deja un miembro del equipo en el PC. No se permite dejar el equipo sin nadie que pueda
        /// luchar (como en los juegos).
        /// </summary>
        public Result Deposit(int partyIndex)
        {
            if (partyIndex < 0 || partyIndex >= Party.Count) return Result.Failure("No hay nadie en esa posición.");
            var mon = Party.Members[partyIndex];
            int usableAfter = 0;
            foreach (var m in Party.Members) if (m != mon && !m.IsFainted) usableAfter++;
            if (usableAfter == 0) return Result.Failure("No puedes quedarte sin nadie que pueda luchar.");
            var r = Party.Remove(mon.Id);
            if (!r.IsSuccess) return r;
            _box.Add(mon);
            return Result.Success();
        }

        /// <summary>Saca un monstruo del PC al equipo (si hay hueco).</summary>
        public Result Withdraw(int boxIndex)
        {
            if (boxIndex < 0 || boxIndex >= _box.Count) return Result.Failure("No hay nadie en ese hueco del PC.");
            if (Party.IsFull) return Result.Failure($"El equipo está lleno (máximo {Party.MaxSize}).");
            var mon = _box[boxIndex];
            var r = Party.Add(mon);
            if (r.IsSuccess) _box.RemoveAt(boxIndex);
            return r;
        }

        // ---------------- Entrenadores ----------------

        public bool HasDefeated(string trainerId) => trainerId != null && _defeatedTrainers.Contains(trainerId);
        public void MarkDefeated(string trainerId) { if (!string.IsNullOrEmpty(trainerId)) _defeatedTrainers.Add(trainerId); }
        public void ForgetDefeated(string trainerId) => _defeatedTrainers.Remove(trainerId);
    }

    /// <summary>
    /// EL MUNDO de la partida: la hora del juego, el lugar donde está el jugador, el clima del mapa y
    /// las MARCAS de la historia ("venció al Alto Mando", "tiene la Bici"...). Lo usan las condiciones
    /// de evolución y lo usarán los eventos y el editor de mapas. Lo actualiza la escena (por ejemplo,
    /// la hora con el reloj del sistema, o el lugar al entrar en un mapa).
    /// </summary>
    public sealed class WorldState
    {
        private int _hour = 12;
        private readonly HashSet<string> _flags = new HashSet<string>();

        /// <summary>Hora del juego (0-23). Por defecto, mediodía.</summary>
        public int Hour { get => _hour; set => _hour = ((value % 24) + 24) % 24; }
        /// <summary>Id del lugar actual (vacío = ninguno).</summary>
        public string LocationId { get; set; } = "";
        /// <summary>Id del clima del mapa (vacío = despejado).</summary>
        public string MapWeatherId { get; set; } = "";

        public IReadOnlyCollection<string> Flags => _flags;
        public bool HasFlag(string flag) => !string.IsNullOrEmpty(flag) && _flags.Contains(flag.Trim());
        public void SetFlag(string flag, bool on = true)
        {
            if (string.IsNullOrWhiteSpace(flag)) return;
            if (on) _flags.Add(flag.Trim()); else _flags.Remove(flag.Trim());
        }
    }
}
