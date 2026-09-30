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

        public TeamMemberSpec(Id<SpeciesDef> species, int level, IReadOnlyList<Id<Move>> moves = null,
            string heldItem = null, Id<Nature>? nature = null, int? fixedIv = null, string nickname = null,
            CTEditor.GameDefinition.Domain.Species.Gender? gender = null)
        {
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
