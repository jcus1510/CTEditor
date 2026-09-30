using System;

namespace CTEditor.GameDefinition.Domain.Species
{
    /// <summary>
    /// QUÉ comprueba una condición extra de evolución. Se combinan con Y: TODAS deben cumplirse (además
    /// del "cómo evoluciona" de la evolución). Solo se añaden valores AL FINAL (Unity guarda el número).
    /// </summary>
    public enum EvolutionConditionKind
    {
        MinLevel,         // nivel mínimo (Value)
        MinFriendship,    // amistad mínima (Value, 0-255)
        HoldsItem,        // lleva equipado el objeto (Id) — Scyther con Revestimiento Metálico
        KnowsMove,        // sabe el movimiento (Id) — Aipom con Doble Golpe
        KnowsMoveOfType,  // sabe algún movimiento de ese tipo (Id) — Eevee → Sylveon (Hada)
        TimeOfDay,        // en ese momento del día (Time) — Espeon de día, Umbreon de noche
        AtLocation,       // en ese lugar del mapa (Id) — Magnezone en un sitio especial
        MapWeather,       // con ese clima en el mapa (Id) — Goodra con lluvia
        StatRelation,     // comparación de Ataque y Defensa (Relation) — Tyrogue
        PartyHasSpecies,  // hay esa especie en el equipo (Id) — Mantyke con Remoraid
        PartyHasType,     // hay un monstruo de ese tipo en el equipo (Id) — Pancham con Siniestro
        Nature,           // tiene esa naturaleza (Id)
        Chance,           // según su "personalidad": el Value % de los individuos (siempre el mismo para cada uno) — Wurmple
        GameFlag,         // una marca de la partida está activa (Id) — para eventos de la historia
        Gender            // es de ese género (Value: 1 = macho, 2 = hembra) — Gallade, Froslass, Vespiquen
    }

    /// <summary>Momentos del día (hora de 0 a 23).</summary>
    public enum DayTime
    {
        Day,      // de 6:00 a 17:59
        Night,    // de 18:00 a 5:59
        Morning,  // de 6:00 a 9:59
        Evening   // de 17:00 a 19:59
    }

    /// <summary>Cómo se comparan el Ataque y la Defensa del monstruo.</summary>
    public enum StatRelation
    {
        AttackHigher,   // Ataque > Defensa (Hitmonlee)
        DefenseHigher,  // Ataque < Defensa (Hitmonchan)
        AttackEqualsDefense // Ataque = Defensa (Hitmontop)
    }

    /// <summary>
    /// Una condición EXTRA de una evolución: "y además de noche", "y llevando tal objeto", "y con 160 de
    /// amistad"... Con 'Negate' se invierte ("y SIN llevar la Piedra Eterna"). Contenido inmutable.
    /// </summary>
    public sealed class EvolutionCondition
    {
        public EvolutionConditionKind Kind { get; }
        /// <summary>Número (nivel, amistad, porcentaje).</summary>
        public int Value { get; }
        /// <summary>Id de lo que se mira (objeto, movimiento, tipo, especie, lugar, clima, naturaleza, marca).</summary>
        public string Id { get; }
        public DayTime Time { get; }
        public StatRelation Relation { get; }
        /// <summary>true = se cumple cuando NO pasa.</summary>
        public bool Negate { get; }

        public EvolutionCondition(EvolutionConditionKind kind, int value = 0, string id = null,
            DayTime time = DayTime.Day, StatRelation relation = StatRelation.AttackHigher, bool negate = false)
        {
            Kind = kind;
            Value = Math.Max(0, value);
            Id = id?.Trim() ?? "";
            Time = time;
            Relation = relation;
            Negate = negate;
        }

        /// <summary>¿Esta hora (0-23) está en ese momento del día?</summary>
        public static bool IsTime(DayTime time, int hour)
        {
            hour = ((hour % 24) + 24) % 24;
            switch (time)
            {
                case DayTime.Day: return hour >= 6 && hour < 18;
                case DayTime.Night: return hour >= 18 || hour < 6;
                case DayTime.Morning: return hour >= 6 && hour < 10;
                case DayTime.Evening: return hour >= 17 && hour < 20;
                default: return false;
            }
        }
    }
}
