using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;

namespace CTEditor.GameDefinition.Domain.Species
{
    /// <summary>
    /// Una evolución posible de una especie: EN QUÉ se convierte y BAJO QUÉ CONDICIÓN.
    ///
    /// Una especie puede tener VARIAS evoluciones (piensa en criaturas que ramifican según el
    /// caso), por eso Species guarda una lista de Evolution.
    ///
    /// CÓMO se dispara lo dice el método (al subir de nivel, al usar un objeto, al intercambiarlo) y,
    /// además, puede tener CONDICIONES EXTRA que deben cumplirse todas a la vez (EvolutionCondition):
    /// amistad Y de día, nivel 20 Y Ataque &gt; Defensa, llevar un objeto Y de noche, saber un movimiento
    /// de tipo Hada... Así se replican todas las evoluciones de los juegos y se inventan otras.
    /// </summary>
    /// <summary>CÓMO evoluciona. Solo se añaden valores AL FINAL (Unity guarda el número).</summary>
    public enum EvolutionMethod
    {
        Level,       // al llegar al nivel (Bulbasaur -> Ivysaur a nivel 16)
        Item,        // al usar un objeto (Pikachu + Piedra Trueno -> Raichu)
        Friendship,  // al subir de nivel con mucha amistad (Golbat -> Crobat)
        Trade,       // al intercambiarlo (Kadabra -> Alakazam); opcionalmente llevando un objeto
        LevelUp      // al subir de nivel cuando se cumplan sus CONDICIONES (sin nivel fijo): Sylveon, Tyrogue...
    }

    public sealed class Evolution
    {
        public Id<Species> Target { get; }

        /// <summary>Nivel mínimo. Con el método Level es EL nivel; con los demás, un mínimo opcional (0 = cualquiera).</summary>
        public int RequiredLevel { get; }

        public EvolutionMethod Method { get; }

        /// <summary>Id del objeto (método Item; o el que debe llevar equipado en un intercambio). Vacío = ninguno.</summary>
        public string ItemId { get; }

        /// <summary>Amistad mínima (método Friendship). Clásico: 220.</summary>
        public int MinFriendship { get; }

        /// <summary>Condiciones extra: TODAS deben cumplirse (vacío = ninguna).</summary>
        public IReadOnlyList<EvolutionCondition> Conditions { get; }

        public Evolution(Id<Species> target, int requiredLevel)
            : this(target, EvolutionMethod.Level, requiredLevel) { }

        public Evolution(Id<Species> target, EvolutionMethod method, int requiredLevel = 0, string itemId = null, int minFriendship = 220,
            IReadOnlyList<EvolutionCondition> conditions = null)
        {
            if (method == EvolutionMethod.Level && requiredLevel < 1)
                throw new ArgumentOutOfRangeException(nameof(requiredLevel), "El nivel de evolución empieza en 1.");
            Target = target;
            Method = method;
            RequiredLevel = requiredLevel < 0 ? 0 : requiredLevel;
            ItemId = itemId ?? "";
            MinFriendship = minFriendship <= 0 ? 220 : minFriendship;
            var list = new List<EvolutionCondition>();
            if (conditions != null) foreach (var c in conditions) if (c != null) list.Add(c);
            Conditions = list;
        }
    }
}
