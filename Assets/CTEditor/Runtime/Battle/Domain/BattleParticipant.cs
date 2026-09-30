using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Species;
using CTEditor.GameDefinition.Domain.Status;
using CTEditor.GameDefinition.Domain.Abilities;

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
        /// <summary>Género (Atracción, Rivalidad, Gran Encanto...).</summary>
        public Gender Gender { get; }
        /// <summary>¿Puede evolucionar todavía? (Mineral Evolutivo).</summary>
        public bool CanEvolve { get; }
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

        /// <summary>La habilidad del monstruo (de su especie). Null = ninguna.</summary>
        public AbilityId? Ability { get; }

        /// <summary>Rendimiento base de XP de su especie (cuánto "vale" derrotarlo).</summary>
        public int BaseExpYield { get; }

        /// <summary>EVs que otorga derrotarlo (el rendimiento de EVs de su especie).</summary>
        public IReadOnlyList<EvYieldEntry> EvYield { get; }

        /// <summary>
        /// PP ACTUALES de cada movimiento, en el mismo orden que Moves (lo que le quedaba al entrar al
        /// combate). Null = todos al máximo (el motor los llena con el MaxPp de cada movimiento).
        /// </summary>
        public IReadOnlyList<int> InitialPp { get; }

        /// <summary>Amistad del monstruo (0-255; clásico 70 al capturarlo). La usan las condiciones.</summary>
        public int Friendship { get; }

        /// <summary>Ratio de captura de su especie (1-255; más alto = más fácil).</summary>
        public int CatchRate { get; }

        /// <summary>Objeto equipado (id) al entrar al combate. Null = ninguno.</summary>
        public string HeldItem { get; }

        /// <summary>Peso en kilos (de su Pokédex): Patada Baja, Hierba Lazo...</summary>
        public float WeightKg { get; }

        /// <summary>Formas de combate ya calculadas para este individuo (Modo Daruma, megas...). Vacía = ninguna.</summary>
        public IReadOnlyList<BattleForm> Forms { get; }

        /// <summary>Reglas de cambio de forma de su especie.</summary>
        public IReadOnlyList<FormChange> FormChanges { get; }

        public BattleParticipant(
            Id<BattleParticipant> id,
            Id<Species> speciesId,
            int level,
            StatBlock stats,
            int currentHp,
            IReadOnlyList<Id<ElementType>> types,
            IReadOnlyList<Id<Move>> moves,
            StatusId? initialStatus = null,
            AbilityId? ability = null,
            int baseExpYield = 64,
            IReadOnlyList<EvYieldEntry> evYield = null,
            IReadOnlyList<int> initialPp = null,
            int friendship = 70,
            string heldItem = null,
            int catchRate = 45,
            float weightKg = 0f,
            Gender gender = Gender.Genderless,
            bool canEvolve = false,
            IReadOnlyList<BattleForm> forms = null,
            IReadOnlyList<FormChange> formChanges = null)
        {
            Forms = forms == null ? Array.Empty<BattleForm>() : new List<BattleForm>(forms);
            FormChanges = formChanges == null ? Array.Empty<FormChange>() : new List<FormChange>(formChanges);
            Gender = gender;
            CanEvolve = canEvolve;
            WeightKg = Math.Max(0f, weightKg);
            CatchRate = Math.Max(1, Math.Min(255, catchRate));
            HeldItem = string.IsNullOrWhiteSpace(heldItem) ? null : heldItem;
            Friendship = Math.Max(0, Math.Min(255, friendship));
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
            Ability = ability;
            BaseExpYield = baseExpYield < 1 ? 1 : baseExpYield;
            EvYield = evYield == null ? Array.Empty<EvYieldEntry>() : new List<EvYieldEntry>(evYield);
            InitialPp = initialPp == null ? null : new List<int>(initialPp);
        }
    }
}
