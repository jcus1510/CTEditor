using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Rules;

namespace CTEditor.Party.Domain
{
    /// <summary>
    /// El EQUIPO: el primer AGGREGATE ROOT del motor (Parte B, A.8). Un agregado es un grupo de
    /// datos tratado como una unidad, con UNA SOLA PUERTA de entrada (esta clase). El símil del
    /// documento: es el PORTERO de una discoteca con aforo. No puedes colar a nadie por la ventana
    /// (no puedes manipular la lista de miembros por fuera); TODO pasa por la puerta, y el portero
    /// garantiza que el aforo nunca se rebase.
    ///
    /// Por eso la lista de miembros es PRIVADA: afuera solo se ve de lectura (Members), y para
    /// añadir/quitar hay que usar los métodos, que validan el INVARIANTE.
    ///
    /// Y el invariante es RELATIVO AL RULESET (A.8): no es "máximo 6", sino "máximo
    /// Ruleset.MaxPartySize". El portero sigue ahí; solo lee el aforo de la ficha de reglas. Así
    /// configurabilidad y orden conviven.
    ///
    /// Los métodos devuelven Result en vez de lanzar: un "equipo lleno" es un rechazo NORMAL de un
    /// comando (A.7), no un error excepcional. Quien llama decide qué hacer con el rechazo.
    /// </summary>
    public sealed class Party
    {
        private readonly List<MonsterInstance> _members;
        private readonly int _maxSize;

        public Party(Ruleset ruleset)
        {
            if (ruleset == null) throw new ArgumentNullException(nameof(ruleset));
            // El portero lee el aforo de la ficha de reglas y lo guarda.
            _maxSize = ruleset.MaxPartySize;
            _members = new List<MonsterInstance>();
        }

        /// <summary>Vista de solo lectura: puedes mirar el equipo, pero no modificarlo por aquí.</summary>
        public IReadOnlyList<MonsterInstance> Members => _members;

        public int Count => _members.Count;
        public int MaxSize => _maxSize;
        public bool IsFull => _members.Count >= _maxSize;

        /// <summary>
        /// Añade un monstruo al equipo, respetando el aforo y la regla "un individuo está en un solo
        /// lugar" (no se admite el mismo id dos veces).
        /// </summary>
        public Result Add(MonsterInstance monster)
        {
            if (monster == null)
                return Result.Failure("No se puede añadir un monstruo nulo.");
            if (IsFull)
                return Result.Failure($"El equipo está lleno (máximo {_maxSize}).");
            if (Contains(monster.Id))
                return Result.Failure("Ese monstruo ya está en el equipo.");

            _members.Add(monster);
            return Result.Success();
        }

        /// <summary>Quita un monstruo por su id.</summary>
        public Result Remove(Id<MonsterInstance> id)
        {
            int index = _members.FindIndex(m => m.Id == id);
            if (index < 0)
                return Result.Failure("Ese monstruo no está en el equipo.");

            _members.RemoveAt(index);
            return Result.Success();
        }

        public bool Contains(Id<MonsterInstance> id) => _members.FindIndex(m => m.Id == id) >= 0;

        /// <summary>¿Queda al menos uno que pueda pelear? (Útil para saber si la partida sigue.)</summary>
        public bool HasUsableMonster()
        {
            foreach (var m in _members)
                if (!m.IsFainted)
                    return true;
            return false;
        }
    }
}
