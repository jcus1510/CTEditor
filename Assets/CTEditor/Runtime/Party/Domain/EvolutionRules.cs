using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Species;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Types;

namespace CTEditor.Party.Domain
{
    /// <summary>Qué acaba de pasar, para saber qué evoluciones mirar.</summary>
    public enum EvolutionTrigger
    {
        LevelUp,  // subió de nivel (evoluciones por nivel y por amistad)
        UseItem,  // se le usó un objeto (piedras)
        Trade     // se intercambió
    }

    /// <summary>
    /// Lo que las condiciones de evolución necesitan saber del MUNDO y de la PARTIDA: la hora, el lugar,
    /// el clima del mapa, el equipo, las marcas de la historia y cómo consultar tipos de movimientos y
    /// especies. Todo es opcional: sin contexto, la hora es mediodía y no hay lugar, clima ni marcas.
    /// </summary>
    public sealed class EvolutionContext
    {
        /// <summary>Hora del juego (0-23). Por defecto, mediodía.</summary>
        public int Hour { get; set; } = 12;
        /// <summary>Id del lugar actual (mapa). Vacío = ninguno.</summary>
        public string LocationId { get; set; } = "";
        /// <summary>Id del clima del mapa. Vacío = despejado.</summary>
        public string MapWeatherId { get; set; } = "";
        /// <summary>El equipo del jugador (para "hay tal especie o tipo en el equipo").</summary>
        public IReadOnlyList<MonsterInstance> Party { get; set; } = Array.Empty<MonsterInstance>();
        /// <summary>El tipo de un movimiento (null si no se sabe).</summary>
        public Func<Id<Move>, Id<ElementType>?> MoveType { get; set; }
        /// <summary>Los tipos de una especie (null si no se sabe).</summary>
        public Func<Id<Species>, IReadOnlyList<Id<ElementType>>> SpeciesTypes { get; set; }
        /// <summary>¿Está activa esa marca de la partida?</summary>
        public Func<string, bool> HasFlag { get; set; }

        public static readonly EvolutionContext Empty = new EvolutionContext();
    }

    /// <summary>
    /// Decide SI un monstruo puede evolucionar ahora y EN QUÉ, según las evoluciones de su especie.
    /// No cambia nada: quien llama aplica MonsterInstance.Evolve con la especie elegida. Así la
    /// presentación puede preguntar "¿quieres que evolucione?" y permitir cancelarlo.
    /// </summary>
    public static class EvolutionRules
    {
        public static Evolution Find(MonsterInstance mon, Species species, EvolutionTrigger trigger, string itemId = null,
            EvolutionContext context = null)
        {
            if (mon == null || species == null) return null;
            foreach (var evo in species.Evolutions)
                if (Matches(evo, mon, trigger, itemId, context)) return evo;
            return null;
        }

        public static bool Matches(Evolution evo, MonsterInstance mon, EvolutionTrigger trigger, string itemId = null,
            EvolutionContext context = null)
            => MethodMatches(evo, mon, trigger, itemId) && ConditionsMet(evo, mon, context);

        /// <summary>¿Se cumplen TODAS sus condiciones extra ahora?</summary>
        public static bool ConditionsMet(Evolution evo, MonsterInstance mon, EvolutionContext context = null)
        {
            foreach (var c in evo.Conditions)
                if (Check(c, mon, context ?? EvolutionContext.Empty) == c.Negate) return false;
            return true;
        }

        /// <summary>¿Se cumple esta condición (sin tener en cuenta 'Negate')?</summary>
        public static bool Check(EvolutionCondition c, MonsterInstance mon, EvolutionContext ctx)
        {
            switch (c.Kind)
            {
                case EvolutionConditionKind.MinLevel: return mon.Level.Value >= c.Value;
                case EvolutionConditionKind.MinFriendship: return mon.Friendship >= c.Value;
                case EvolutionConditionKind.HoldsItem: return c.Id.Length > 0 && mon.HeldItem == c.Id;
                case EvolutionConditionKind.KnowsMove: return c.Id.Length > 0 && mon.KnowsMove(new Id<Move>(c.Id));
                case EvolutionConditionKind.KnowsMoveOfType:
                    if (ctx.MoveType == null || c.Id.Length == 0) return false;
                    foreach (var m in mon.Moves)
                    {
                        var t = ctx.MoveType(m);
                        if (t.HasValue && t.Value.Value == c.Id) return true;
                    }
                    return false;
                case EvolutionConditionKind.TimeOfDay: return EvolutionCondition.IsTime(c.Time, ctx.Hour);
                case EvolutionConditionKind.AtLocation: return c.Id.Length > 0 && ctx.LocationId == c.Id;
                case EvolutionConditionKind.MapWeather: return c.Id.Length > 0 && ctx.MapWeatherId == c.Id;
                case EvolutionConditionKind.StatRelation:
                {
                    int atk = mon.Stats.Of(StatId.Attack), def = mon.Stats.Of(StatId.Defense);
                    return c.Relation == StatRelation.AttackHigher ? atk > def
                         : c.Relation == StatRelation.DefenseHigher ? atk < def : atk == def;
                }
                case EvolutionConditionKind.PartyHasSpecies:
                    foreach (var m in ctx.Party) if (m != mon && m.SpeciesId.Value == c.Id) return true;
                    return false;
                case EvolutionConditionKind.PartyHasType:
                    if (ctx.SpeciesTypes == null) return false;
                    foreach (var m in ctx.Party)
                    {
                        if (m == mon) continue;
                        var types = ctx.SpeciesTypes(m.SpeciesId);
                        if (types != null) foreach (var t in types) if (t.Value == c.Id) return true;
                    }
                    return false;
                case EvolutionConditionKind.Nature: return mon.Nature != null && mon.Nature.Id.Value == c.Id;
                case EvolutionConditionKind.Chance: return Personality(mon) < c.Value;
                case EvolutionConditionKind.GameFlag: return c.Id.Length > 0 && ctx.HasFlag != null && ctx.HasFlag(c.Id);
                default: return false;
            }
        }

        /// <summary>
        /// "Personalidad" de 0 a 99, SIEMPRE la misma para cada individuo (sale de su id). Sirve para
        /// evoluciones al azar pero estables: un Wurmple concreto siempre evoluciona en lo mismo.
        /// </summary>
        public static int Personality(MonsterInstance mon)
        {
            unchecked
            {
                int h = 17;
                foreach (char ch in mon.Id.Value) h = h * 31 + ch;
                return ((h % 100) + 100) % 100;
            }
        }

        private static bool MethodMatches(Evolution evo, MonsterInstance mon, EvolutionTrigger trigger, string itemId)
        {
            int level = mon.Level.Value;
            bool levelOk = evo.RequiredLevel <= 0 || level >= evo.RequiredLevel;
            switch (evo.Method)
            {
                case EvolutionMethod.LevelUp:
                    return trigger == EvolutionTrigger.LevelUp && levelOk;
                case EvolutionMethod.Level:
                    return trigger == EvolutionTrigger.LevelUp && level >= evo.RequiredLevel;
                case EvolutionMethod.Friendship:
                    return trigger == EvolutionTrigger.LevelUp && levelOk && mon.Friendship >= evo.MinFriendship;
                case EvolutionMethod.Item:
                    return trigger == EvolutionTrigger.UseItem && levelOk && evo.ItemId.Length > 0 && evo.ItemId == itemId;
                case EvolutionMethod.Trade:
                    return trigger == EvolutionTrigger.Trade && levelOk && (evo.ItemId.Length == 0 || evo.ItemId == mon.HeldItem);
                default:
                    return false;
            }
        }

        /// <summary>¿Este objeto hace evolucionar a esta especie? (para mostrar "puede usarse" en la mochila).</summary>
        public static bool ItemCanEvolve(Species species, string itemId)
        {
            if (species == null) return false;
            foreach (var evo in species.Evolutions)
                if (evo.Method == EvolutionMethod.Item && evo.ItemId == itemId) return true;
            return false;
        }
    }
}
