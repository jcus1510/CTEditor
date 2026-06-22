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
                    evolutions.Add(new Evolution(new Id<Species>(e.target.Id), e.requiredLevel));
                }
            }

            // El constructor de Species hará las copias defensivas y validará (al menos un tipo, etc.).
            AbilityId? ability = string.IsNullOrWhiteSpace(data.AbilityId)
                ? (AbilityId?)null
                : new AbilityId(data.AbilityId);

            return new Species(
                new Id<Species>(data.Id),
                data.DisplayName,
                types,
                baseStats,
                learnset,
                evolutions,
                ability);
        }
    }
}
