using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using SpeciesDef = CTEditor.GameDefinition.Domain.Species.Species;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Stats;

namespace CTEditor.GameDefinition.Domain.Trainers
{
    /// <summary>
    /// UN miembro de un equipo diseñado por el autor (el de un entrenador o un equipo prearmado del
    /// jugador): qué especie, a qué nivel y, si se quiere, con qué movimientos, objeto, naturaleza e IVs.
    ///
    /// Es una RECETA, no un monstruo: con ella la capa de partida fabrica un MonsterInstance real
    /// (TeamBuilder). La misma receta sirve para el jugador y para los NPC: una sola lógica.
    /// </summary>
    public sealed class TeamMemberSpec
    {
        public Id<SpeciesDef> Species { get; }
        public int Level { get; }

        /// <summary>Movimientos elegidos. Vacío = los últimos que aprende por nivel (como un salvaje).</summary>
        public IReadOnlyList<Id<Move>> Moves { get; }

        /// <summary>Objeto equipado (id). Vacío = ninguno.</summary>
        public string HeldItem { get; }

        /// <summary>Naturaleza fija (id). Null = una al azar.</summary>
        public Id<Nature>? Nature { get; }

        /// <summary>IV fijo para todas las estadísticas (0-31). Null = al azar.</summary>
        public int? FixedIv { get; }

        /// <summary>Mote (nombre propio). Vacío = el nombre de la especie.</summary>
        public string Nickname { get; }

        /// <summary>Género fijo. Null = al azar según su especie (una especie sin género siempre queda sin género).</summary>
        public CTEditor.GameDefinition.Domain.Species.Gender? Gender { get; }

        /// <summary>IVs por estadística («0 Atq / 0 Vel»). Las no escritas: FixedIv o al azar. Vacío = ninguno fijado.</summary>
        public StatSpread Ivs { get; }

        /// <summary>EVs por estadística («252 Atq / 4 PS / 252 Vel»). Vacío = los decide su IA (entrenamiento de competición o nada).</summary>
        public StatSpread Evs { get; }

        /// <summary>Habilidad elegida (debe ser una de las de su especie; también la oculta). Null = la que le toque.</summary>
        public CTEditor.GameDefinition.Domain.Abilities.AbilityId? Ability { get; }

        public TeamMemberSpec(Id<SpeciesDef> species, int level, IReadOnlyList<Id<Move>> moves = null,
            string heldItem = null, Id<Nature>? nature = null, int? fixedIv = null, string nickname = null,
            CTEditor.GameDefinition.Domain.Species.Gender? gender = null,
            StatSpread ivs = null, StatSpread evs = null, CTEditor.GameDefinition.Domain.Abilities.AbilityId? ability = null)
        {
            Ivs = ivs ?? StatSpread.Empty;
            Evs = evs ?? StatSpread.Empty;
            Ability = ability;
            Gender = gender;
            if (string.IsNullOrWhiteSpace(species.Value)) throw new ArgumentException("El miembro necesita una especie.", nameof(species));
            Species = species;
            Level = Math.Max(1, level);
            Moves = moves == null ? Array.Empty<Id<Move>>() : new List<Id<Move>>(moves);
            HeldItem = heldItem ?? "";
            Nature = nature;
            FixedIv = fixedIv;
            Nickname = nickname ?? "";
        }
    }
}
