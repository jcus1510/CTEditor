using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Species;
using CTEditor.GameDefinition.Domain.Status;
using CTEditor.GameDefinition.Domain.Growth;

namespace CTEditor.Party.Domain
{
    /// <summary>
    /// Un INDIVIDUO concreto: "Chispa", el Charizard nivel 34 con SUS PS y SUS movimientos.
    /// Es la primera ENTIDAD del motor (Parte B), y entender por qué es entidad es clave:
    ///
    ///   - Tiene IDENTIDAD propia (Id). Dos Charizard nivel 5 con stats idénticos siguen siendo
    ///     individuos DISTINTOS, porque cada uno es "cuál", no solo "cuánto vale". Eso lo hace
    ///     entidad y no value object.
    ///   - Tiene ESTADO MUTABLE y CICLO DE VIDA: sus PS bajan, suben, se cura... y sigue siendo el
    ///     mismo individuo a lo largo del tiempo.
    ///
    /// Contrasta con la Species, que es la DEFINICIÓN inmutable y compartida (C.1). Aquí vive el
    /// estado de partida; en la Species, jamás.
    ///
    /// El constructor es 'internal': solo se crea a través del MonsterFactory, que sabe armar un
    /// individuo coherente (stats calculados, PS llenos, movimientos por defecto). Nadie construye
    /// un MonsterInstance "a mano" e incompleto.
    /// </summary>
    public sealed class MonsterInstance
    {
        // IDENTIDAD: lo que lo convierte en entidad.
        public Id<MonsterInstance> Id { get; }

        // De qué Species es (por id, M.4). La Species real se resuelve por catálogo cuando hace falta.
        public Id<Species> SpeciesId { get; private set; }

        public Level Level { get; private set; }

        /// <summary>Stats EFECTIVOS de este individuo (ya calculados desde la base + nivel).</summary>
        public StatBlock Stats { get; private set; }

        /// <summary>Experiencia TOTAL acumulada. Junto a la curva, determina el nivel.</summary>
        public Experience Experience { get; private set; }

        // ---------- La "genética" del individuo (Lote 3) ----------
        // Tres ingredientes que hacen que dos Pikachu del mismo nivel NO sean idénticos:
        //   IVs        -> genética FIJA de nacimiento (clásico 0-31 por stat). Nunca cambia.
        //   EVs        -> "entrenamiento" acumulado al derrotar rivales. Crece con topes.
        //   Naturaleza -> personalidad que inclina una stat arriba y otra abajo.

        /// <summary>IVs del individuo. Null = juego/individuo sin IVs (se leen como 0).</summary>
        public StatBlock Ivs { get; }

        // EVs: mutable, con sus topes custodiados por EffortValues. Null = sin EVs.
        private readonly EffortValues _efforts;

        /// <summary>Naturaleza del individuo. Null = neutra. Es POR INDIVIDUO, no por especie.</summary>
        public Nature Nature { get; }

        /// <summary>IV de una stat (0 si no tiene IVs).</summary>
        public int IvOf(StatId stat) => Ivs != null ? Ivs.Of(stat) : 0;

        /// <summary>EVs actuales de una stat (0 si no gana EVs).</summary>
        public int EvOf(StatId stat) => _efforts != null ? _efforts.Of(stat) : 0;

        /// <summary>Total de EVs acumulados.</summary>
        public int EvTotal => _efforts != null ? _efforts.Total : 0;

        /// <summary>
        /// Suma EVs a una stat respetando los topes. Devuelve cuántos entraron de verdad. NO recalcula
        /// los stats por sí solo: quien orquesta llama a RecomputeStats después (así puede aplicar
        /// varios EVs y recalcular UNA vez).
        /// </summary>
        public int AddEffort(StatId stat, int amount) => _efforts != null ? _efforts.Add(stat, amount) : 0;

        /// <summary>PS actuales. Es el estado mutable central; se mueve entre 0 y MaxHp.</summary>
        public int CurrentHp { get; private set; }

        private readonly List<Id<Move>> _moves;
        public IReadOnlyList<Id<Move>> Moves => _moves;

        // 'internal' = visible solo dentro de Party.Domain (lo usa el factory). El '?? ' protege de
        // una lista de movimientos nula.
        internal MonsterInstance(
            Id<MonsterInstance> id,
            Id<Species> speciesId,
            Level level,
            StatBlock stats,
            int currentHp,
            IReadOnlyList<Id<Move>> moves,
            Experience experience = default,
            StatBlock ivs = null,
            EffortValues efforts = null,
            Nature nature = null)
        {
            if (stats == null) throw new ArgumentNullException(nameof(stats));

            Id = id;
            SpeciesId = speciesId;
            Level = level;
            Stats = stats;
            Experience = experience;
            Ivs = ivs;
            _efforts = efforts;
            Nature = nature;
            _moves = moves == null ? new List<Id<Move>>() : new List<Id<Move>>(moves);

            // PS de arranque, recortados al rango válido por las dudas.
            int maxHp = stats.Of(StatId.Hp);
            CurrentHp = currentHp < 0 ? 0 : (currentHp > maxHp ? maxHp : currentHp);
        }

        public int MaxHp => Stats.Of(StatId.Hp);
        public bool IsFainted => CurrentHp <= 0;

        // --- Mutaciones controladas del estado ---
        // En COMBATE, el daño no se aplica aquí: Battle trabaja sobre un "snapshot" (J.5) y nunca
        // toca al MonsterInstance. Estos métodos aplican los RESULTADOS cuando vuelven del combate,
        // o efectos del overworld (una poción, un evento). Math.Max/Min mantienen el invariante
        // 0 <= CurrentHp <= MaxHp sin que tengas que pensarlo cada vez.

        public void TakeDamage(int amount)
        {
            if (amount <= 0) return;
            CurrentHp = Math.Max(0, CurrentHp - amount);
        }

        public void Heal(int amount)
        {
            if (amount <= 0 || IsFainted) return;
            CurrentHp = Math.Min(MaxHp, CurrentHp + amount);
        }

        public void Revive(int hp)
        {
            if (!IsFainted) return;
            CurrentHp = Math.Min(MaxHp, Math.Max(1, hp));
        }

        /// <summary>Cura por completo: PS, estado y PP (lo que hace un Centro Pokémon).</summary>
        public void FullRestore()
        {
            CurrentHp = MaxHp;
            Status = null;
            CurrentPp = null;
        }

        /// <summary>
        /// Estado alterado PERSISTENTE (quemado, envenenado...). Null = sano. Lo clásico es que los
        /// estados no-volátiles sobrevivan al combate; el orquestador lo aplica desde el resultado.
        /// (Los volátiles tipo confusión no deberían persistir: no se devuelven en el resultado.)
        /// </summary>
        public StatusId? Status { get; private set; }

        public void SetStatus(StatusId status) => Status = status;

        /// <summary>Mote (nombre propio que le puso su entrenador). Vacío = se muestra el nombre de la especie.</summary>
        public string Nickname { get; private set; } = "";

        public void SetNickname(string nickname) => Nickname = nickname?.Trim() ?? "";

        /// <summary>
        /// Aprende un movimiento: en un hueco libre (slot = Moves.Count) o SUSTITUYENDO al del hueco
        /// 'slot' (lo olvida). El nuevo empieza con sus PP llenos (maxPp).
        /// </summary>
        public bool LearnMove(int slot, Id<Move> move, int maxPp)
        {
            if (slot < 0 || slot > _moves.Count) return false;
            if (_moves.Contains(move)) return false; // no se repiten movimientos
            if (slot == _moves.Count) _moves.Add(move); else _moves[slot] = move;

            if (CurrentPp != null)
            {
                var pp = new List<int>(CurrentPp);
                while (pp.Count < _moves.Count) pp.Add(maxPp);
                pp[slot] = Math.Max(0, maxPp);
                CurrentPp = pp;
            }
            return true;
        }

        /// <summary>¿Ya conoce este movimiento?</summary>
        public bool KnowsMove(Id<Move> move) => _moves.Contains(move);

        /// <summary>
        /// Qué habilidad de su especie tiene: 0 = la primera, 1 = la segunda, 2 = la oculta. Si su especie no
        /// tiene segunda (u oculta), usa la primera. Se conserva al evolucionar, como en los juegos.
        /// </summary>
        public int AbilitySlot { get; private set; }

        /// <summary>Cambia la ranura de habilidad (0, 1 o 2). Lo usan el autor (equipos) y objetos futuros (Cápsula Habilidad).</summary>
        public void SetAbilitySlot(int slot) => AbilitySlot = slot < 0 ? 0 : slot > 2 ? 2 : slot;

        /// <summary>Objeto EQUIPADO (id). Null = no lleva nada.</summary>
        public string HeldItem { get; private set; }

        /// <summary>Equipa un objeto (null o vacío = quitárselo). Devuelve el que llevaba antes.</summary>
        public string SetHeldItem(string itemId)
        {
            var previous = HeldItem;
            HeldItem = string.IsNullOrWhiteSpace(itemId) ? null : itemId;
            return previous;
        }

        /// <summary>
        /// EVOLUCIONA: pasa a ser de otra especie. Conserva nivel, experiencia, genética (IVs, EVs,
        /// naturaleza), movimientos, PP, estado, amistad y objeto; recalcula las stats con las stats base
        /// nuevas. Los PS actuales suben lo mismo que los máximos.
        /// </summary>
        public void Evolve(Id<Species> newSpecies, StatBlock newBaseStats, IStatGrowthFormula growth)
        {
            SpeciesId = newSpecies;
            RecomputeStats(newBaseStats, growth);
        }

        /// <summary>Amistad (0-255). Sube o baja fuera del combate (caminar, objetos, eventos).</summary>
        public int Friendship { get; private set; } = 70;

        /// <summary>Fija la amistad (recortada a 0-255).</summary>
        public void SetFriendship(int value) => Friendship = Math.Max(0, Math.Min(255, value));

        /// <summary>Suma o resta amistad (recortada a 0-255).</summary>
        public void ChangeFriendship(int delta) => SetFriendship(Friendship + delta);
        public void ClearStatus() => Status = null;

        // ---------- PP ----------

        /// <summary>
        /// PP que le quedan a cada movimiento (mismo orden que Moves). Null = TODOS AL MÁXIMO (así un
        /// monstruo recién creado no necesita conocer el catálogo de movimientos para saber sus PP).
        /// </summary>
        public IReadOnlyList<int> CurrentPp { get; private set; }

        /// <summary>Guarda los PP que le quedaron tras un combate (se recortan a 0 como mínimo).</summary>
        public void SetCurrentPp(IReadOnlyList<int> pp)
        {
            if (pp == null) { CurrentPp = null; return; }
            var copy = new List<int>(pp.Count);
            foreach (var v in pp) copy.Add(v < 0 ? 0 : v);
            CurrentPp = copy;
        }

        /// <summary>Recupera todos los PP (Centro Pokémon, Éter Máximo...).</summary>
        public void RestoreAllPp() => CurrentPp = null;

        // ---------- Experiencia y subida de nivel ----------

        /// <summary>
        /// Suma experiencia y sube de nivel según la CURVA. Recalcula los stats con la fórmula y las
        /// stats BASE de la especie (que pasa quien llama: el dominio sigue puro, sin servicios dentro).
        ///
        /// Detalles clásicos: nunca BAJA de nivel (aunque la XP guardada fuera inconsistente), respeta
        /// el tope de la curva, y al subir suma a los PS actuales el incremento de PS máximos (si no
        /// está debilitado). Devuelve cuántos niveles subió y el nivel final, para que la presentación
        /// pueda anunciarlo.
        /// </summary>
        public LevelUpResult AddExperience(int amount, GrowthCurve curve, StatBlock baseStats, IStatGrowthFormula growth)
        {
            if (amount <= 0 || curve == null || baseStats == null || growth == null)
                return new LevelUpResult(0, Level.Value, false);

            // Coherencia: la XP nunca debe quedar por debajo del umbral del nivel actual.
            int total = Math.Max(Experience.Value, curve.XpToReachLevel(Level.Value));
            total += amount;
            Experience = new Experience(total);

            int target = Math.Max(Level.Value, curve.LevelForXp(total)); // jamás baja
            int gained = target - Level.Value;
            if (gained <= 0)
                return new LevelUpResult(0, Level.Value, false);

            Level = new Level(target);
            RecomputeStats(baseStats, growth); // con TODA la genética, al nuevo nivel

            return new LevelUpResult(gained, target, true);
        }

        /// <summary>
        /// Recalcula los stats efectivos con la fórmula y TODA la genética del individuo (nivel actual,
        /// IVs, EVs y naturaleza). Mantiene coherentes los PS: si los PS máximos suben, los actuales
        /// suben lo mismo (si sigue en pie); si quedaran por encima del máximo, se recortan.
        ///
        /// Se llama al subir de nivel y al terminar un combate en el que ganó EVs.
        /// </summary>
        public void RecomputeStats(StatBlock baseStats, IStatGrowthFormula growth)
        {
            if (baseStats == null || growth == null) return;

            int oldMaxHp = MaxHp;

            var b = new StatBlock.Builder();
            foreach (var statId in baseStats.Stats)
            {
                int naturePct = Nature != null ? Nature.PercentFor(statId) : 100;
                b.Set(statId, growth.Compute(statId, baseStats.Of(statId), Level.Value,
                    IvOf(statId), EvOf(statId), naturePct));
            }
            Stats = b.Build();

            int newMaxHp = MaxHp;
            if (CurrentHp > 0)
            {
                int delta = newMaxHp - oldMaxHp;
                if (delta > 0) CurrentHp += delta;
                if (CurrentHp > newMaxHp) CurrentHp = newMaxHp;
            }
        }
    }

    /// <summary>Resultado de sumar XP: cuántos niveles subió, el nivel final y si hubo subida.</summary>
    public readonly struct LevelUpResult
    {
        public int LevelsGained { get; }
        public int NewLevel { get; }
        public bool LeveledUp { get; }

        public LevelUpResult(int levelsGained, int newLevel, bool leveledUp)
        {
            LevelsGained = levelsGained;
            NewLevel = newLevel;
            LeveledUp = leveledUp;
        }
    }
}
