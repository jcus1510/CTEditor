using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Species;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.GameDefinition.Domain.Abilities;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Infrastructure.Acl
{
    /// <summary>
    /// La ACL para especies: SpeciesData (Unity, cómoda) -> Species (dominio, uniforme y pura).
    /// Es el mapper que hace más "trabajo de aduana", y muestra las tres traducciones típicas:
    /// resolver referencias a ids, colapsar campos amigables en una estructura uniforme, y armar
    /// listas inmutables.
    /// </summary>
    public static class SpeciesMapper
    {
        public static Species ToDomain(SpeciesData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            // 1) TIPOS: cada ficha arrastrada -> su id estable. Saltamos huecos vacíos (null) que el
            //    autor pudo dejar; si al final no queda ninguno, el constructor de Species protestará.
            var types = new List<Id<ElementType>>();
            if (data.Types != null)
            {
                foreach (var t in data.Types)
                {
                    if (t == null) continue;
                    types.Add(new Id<ElementType>(t.Id));
                }
            }

            // 2) STATS BASE: aquí ocurre el "colapso". Los 6 campos con nombre + los inventados se
            //    funden en UN StatBlock por clave. El dominio nunca ve "campos clásicos vs lista";
            //    solo ve un bloque uniforme. Esto es exactamente la decisión de stats que tomaste,
            //    materializada: comodidad arriba, un solo camino abajo.
            var statsBuilder = new StatBlock.Builder()
                .Set(StatId.Hp, data.Hp)
                .Set(StatId.Attack, data.Attack)
                .Set(StatId.Defense, data.Defense)
                .Set(StatId.SpAttack, data.SpAttack)
                .Set(StatId.SpDefense, data.SpDefense)
                .Set(StatId.Speed, data.Speed);

            if (data.CustomStats != null)
            {
                foreach (var cs in data.CustomStats)
                {
                    // Ignoramos entradas sin clave (campos a medio llenar).
                    if (string.IsNullOrWhiteSpace(cs.statId)) continue;
                    statsBuilder.Set(new StatId(cs.statId), cs.value);
                }
            }
            var baseStats = statsBuilder.Build();

            // 3) LEARNSET: cada entrada -> LearnableMove (id del movimiento + nivel).
            var learnset = new List<LearnableMove>();
            if (data.Learnset != null)
            {
                foreach (var e in data.Learnset)
                {
                    if (e.move == null) continue;
                    learnset.Add(new LearnableMove(new Id<Move>(e.move.Id), e.level));
                }
            }

            // 4) EVOLUCIONES: cada entrada -> Evolution (id de la especie destino + nivel).
            var evolutions = new List<Evolution>();
            if (data.Evolutions != null)
            {
                foreach (var e in data.Evolutions)
                {
                    if (e.target == null) continue;
                    // Un método "nivel" sin nivel (0) no tiene sentido: se trata como nivel 1.
                    int lvl = e.method == EvolutionMethod.Level ? Math.Max(1, e.requiredLevel) : e.requiredLevel;
                    var conditions = new List<EvolutionCondition>();
                    if (e.conditions != null)
                        foreach (var c in e.conditions)
                            if (c != null) conditions.Add(new EvolutionCondition(c.check, c.value, c.IdFor(), c.time, c.relation, c.negate));
                    evolutions.Add(new Evolution(new Id<Species>(e.target.Id), e.method, lvl, (e.itemId ?? "").Trim(), e.minFriendship, conditions));
                }
            }

            // El constructor de Species hará las copias defensivas y validará (al menos un tipo, etc.).
            AbilityId? ability = string.IsNullOrWhiteSpace(data.AbilityId)
                ? (AbilityId?)null
                : new AbilityId(data.AbilityId);

            // Curva de XP por id (si el autor la dejó vacía, queda null = por defecto).
            CTEditor.SharedKernel.ValueObjects.Id<CTEditor.GameDefinition.Domain.Growth.GrowthCurve>? growthCurve =
                string.IsNullOrWhiteSpace(data.GrowthCurveId)
                    ? (CTEditor.SharedKernel.ValueObjects.Id<CTEditor.GameDefinition.Domain.Growth.GrowthCurve>?)null
                    : new CTEditor.SharedKernel.ValueObjects.Id<CTEditor.GameDefinition.Domain.Growth.GrowthCurve>(data.GrowthCurveId);

            // 5) EVs que otorga derrotarla. Se ignoran filas a medio llenar (sin stat o con 0 puntos).
            var evYield = new List<EvYieldEntry>();
            if (data.EvYield != null)
            {
                foreach (var e in data.EvYield)
                {
                    if (string.IsNullOrWhiteSpace(e.statId) || e.amount <= 0) continue;
                    evYield.Add(new EvYieldEntry(new StatId(e.statId.Trim()), e.amount));
                }
            }

            return new Species(
                new Id<Species>(data.Id),
                data.DisplayName,
                types,
                baseStats,
                learnset,
                evolutions,
                ability,
                growthCurve,
                data.BaseExpYield,
                evYield,
                data.BaseFriendship,
                data.CatchRate,
                new PokedexEntry(data.DexNumber, data.Category, data.HeightM, data.WeightKg, data.DexColor, data.DexDescription,
                    data.FemalePercent, data.Legendary),
                OptionalAbility(data.SecondAbilityId),
                OptionalAbility(data.HiddenAbilityId),
                MoveIds(data.MachineMoves), MoveIds(data.TutorMoves), MoveIds(data.EggMoves),
                EggGroupIds(data.EggGroups));
        }

        private static AbilityId? OptionalAbility(string id)
            => string.IsNullOrWhiteSpace(id) ? (AbilityId?)null : new AbilityId(id.Trim());

        private static List<Id<Move>> MoveIds(MoveData[] moves)
        {
            var list = new List<Id<Move>>();
            if (moves == null) return list;
            foreach (var m in moves)
                if (m != null && !string.IsNullOrWhiteSpace(m.Id)) { var id = new Id<Move>(m.Id); if (!list.Contains(id)) list.Add(id); }
            return list;
        }

        private static List<string> EggGroupIds(EggGroupData[] groups)
        {
            var list = new List<string>();
            if (groups == null) return list;
            foreach (var g in groups)
                if (g != null && !string.IsNullOrWhiteSpace(g.Id) && !list.Contains(g.Id)) list.Add(g.Id);
            return list;
        }
    }
}
