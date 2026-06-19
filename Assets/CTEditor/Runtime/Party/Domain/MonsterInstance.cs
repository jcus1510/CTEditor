using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Species;

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
        public Id<Species> SpeciesId { get; }

        public Level Level { get; }

        /// <summary>Stats EFECTIVOS de este individuo (ya calculados desde la base + nivel).</summary>
        public StatBlock Stats { get; }

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
            IReadOnlyList<Id<Move>> moves)
        {
            if (stats == null) throw new ArgumentNullException(nameof(stats));

            Id = id;
            SpeciesId = speciesId;
            Level = level;
            Stats = stats;
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

        public void FullRestore() => CurrentHp = MaxHp;
    }
}
