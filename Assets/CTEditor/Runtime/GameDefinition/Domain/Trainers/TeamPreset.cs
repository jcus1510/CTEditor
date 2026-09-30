using System;
using System.Collections.Generic;

namespace CTEditor.GameDefinition.Domain.Trainers
{
    /// <summary>
    /// Un EQUIPO PREARMADO para empezar una partida (o probar): los miembros, el dinero y la mochila
    /// inicial. Sirve para probar combates con un equipo listo sin tener que jugar desde el principio.
    /// </summary>
    public sealed class TeamPreset
    {
        public string Id { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public IReadOnlyList<TeamMemberSpec> Members { get; }

        /// <summary>Dinero inicial. Negativo = el de las reglas del juego.</summary>
        public int Money { get; }

        /// <summary>Mochila inicial: (id del objeto, cantidad).</summary>
        public IReadOnlyList<(string itemId, int quantity)> Items { get; }

        public TeamPreset(string id, string displayName, IReadOnlyList<TeamMemberSpec> members,
            string description = "", int money = -1, IReadOnlyList<(string itemId, int quantity)> items = null)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("El equipo necesita un id.", nameof(id));
            Id = id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName;
            Description = description ?? "";
            Members = members == null ? Array.Empty<TeamMemberSpec>() : new List<TeamMemberSpec>(members);
            Money = money;
            Items = items == null ? Array.Empty<(string, int)>() : new List<(string, int)>(items);
        }
    }
}
