using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Species;
using CTEditor.GameDefinition.Domain.Status;

namespace CTEditor.Battle.Domain
{
    /// <summary>
    /// El SNAPSHOT de entrada al combate (J.5: "snapshot-in"). Es una foto inmutable de TODO lo que
    /// Battle necesita de un monstruo para pelear — y nada más.
    ///
    /// Lo más importante, mira el asmdef: Battle.Domain NO referencia a Party. Battle NO conoce
    /// MonsterInstance. ¿Por qué? Porque si lo conociera, Battle dependería de Party y "no tocar el
    /// original hasta el final" sería mentira. En su lugar, un orquestador (que sí conoce ambos
    /// contextos) arma este BattleParticipant a partir de un MonsterInstance y le asigna un id
    /// PROPIO de combate (Id&lt;BattleParticipant&gt;). Battle trabaja con esa foto; al terminar, los
    /// resultados se devuelven referenciando ese id, y el orquestador los aplica de vuelta al
    /// MonsterInstance real. Battle queda puro e ignorante de Party.
    /// </summary>
    public sealed class BattleParticipant
    {
        /// <summary>Id propio de combate, asignado por el orquestador. Mapea de vuelta al original.</summary>
        public Id<BattleParticipant> Id { get; }

        /// <summary>De qué especie es (por id). Solo informativo/para presentación.</summary>
        public Id<Species> SpeciesId { get; }

        public int Level { get; }

        /// <summary>Sus stats efectivos en el momento de entrar al combate.</summary>
        public StatBlock Stats { get; }

        /// <summary>PS con los que entra.</summary>
        public int CurrentHp { get; }

        /// <summary>Sus tipos (para efectividad y STAB).</summary>
        public IReadOnlyList<Id<ElementType>> Types { get; }

        /// <summary>Los movimientos que puede usar.</summary>
        public IReadOnlyList<Id<Move>> Moves { get; }

        /// <summary>Estado alterado con el que entra al combate (normalmente ninguno). Null = sano.</summary>
        public StatusId? InitialStatus { get; }

        public BattleParticipant(
            Id<BattleParticipant> id,
            Id<Species> speciesId,
            int level,
            StatBlock stats,
            int currentHp,
            IReadOnlyList<Id<ElementType>> types,
            IReadOnlyList<Id<Move>> moves,
            StatusId? initialStatus = null)
        {
            if (stats == null) throw new ArgumentNullException(nameof(stats));

            Id = id;
            SpeciesId = speciesId;
            Level = level;
            Stats = stats;
            CurrentHp = currentHp;
            // Copias defensivas: la foto no debe poder alterarse desde fuera.
            Types = types == null ? Array.Empty<Id<ElementType>>() : new List<Id<ElementType>>(types);
            Moves = moves == null ? Array.Empty<Id<Move>>() : new List<Id<Move>>(moves);
            InitialStatus = initialStatus;
        }
    }
}
