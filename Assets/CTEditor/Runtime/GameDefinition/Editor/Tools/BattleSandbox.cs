using System;
using System.Collections.Generic;
using UnityEngine;
using CTEditor.SharedKernel.Abstractions;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Catalog;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.GameDefinition.Domain.Rules;
using CTEditor.GameDefinition.Domain.Status;
using CTEditor.GameDefinition.Domain.Abilities;
using CTEditor.GameDefinition.Infrastructure.Acl;
using CTEditor.GameDefinition.Infrastructure.Catalog;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;
using CTEditor.Party.Domain;
using CTEditor.Battle.Domain;
using CTEditor.Battle.Domain.Turn;
using CTEditor.Battle.Domain.Formulas;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>Cómo de "preparado" llega un combatiente a la prueba (su genética).</summary>
    public enum Preparation
    {
        Untrained, // IV 0, sin EVs, naturaleza neutra: el peor caso
        Normal,    // IV 31, sin EVs, naturaleza neutra: un ejemplar "bueno" sin entrenar
        Maxed      // IV 31, 252 EVs y naturaleza favorable en su stat clave: el mejor caso
    }

    /// <summary>
    /// "Banco de pruebas" del editor: arma el MOTOR REAL de combate (TurnResolver, fórmulas, tabla de
    /// tipos, estados y habilidades) con el contenido del proyecto, y crea combatientes reales a partir
    /// de las fichas de especie. Lo usan la Calculadora de daño y el Simulador de combate.
    ///
    /// Así lo que ves en esas herramientas es EXACTAMENTE lo que pasaría en partida, no una imitación.
    /// Las fichas con errores (sin id, duplicadas o que no se pueden leer) se saltan y se reportan en
    /// Problems, para que una ficha rota no impida probar el resto.
    /// </summary>
    public sealed class BattleSandbox
    {
        public ICatalog<Move> Moves { get; private set; }
        public TypeChart Chart { get; private set; }
        public Ruleset Rules { get; private set; }
        public ICatalog<StatusConditionDefinition> Statuses { get; private set; }
        public ICatalog<AbilityDefinition> Abilities { get; private set; }
        public ICatalog<CTEditor.GameDefinition.Domain.Weather.WeatherDefinition> Weathers { get; private set; }
        public ICatalog<CTEditor.GameDefinition.Domain.Items.ItemDefinition> Items { get; private set; }
        public List<string> Problems { get; } = new List<string>();

        private static readonly ClassicStatGrowthFormula Growth = new ClassicStatGrowthFormula();

        /// <summary>Lee todo el contenido del proyecto y prepara el motor.</summary>
        public static BattleSandbox Load()
        {
            var s = new BattleSandbox();
            s.Moves = s.SafeCatalog<MoveData, Move>(ContentAssets.LoadAll<MoveData>(), MoveMapper.ToDomain, "movimiento");
            s.Statuses = s.SafeCatalog<StatusConditionData, StatusConditionDefinition>(ContentAssets.LoadAll<StatusConditionData>(), StatusMapper.ToDomain, "estado");
            s.Abilities = s.SafeCatalog<AbilityData, AbilityDefinition>(ContentAssets.LoadAll<AbilityData>(), AbilityMapper.ToDomain, "habilidad");
            s.Items = s.SafeCatalog<ItemData, CTEditor.GameDefinition.Domain.Items.ItemDefinition>(ContentAssets.LoadAll<ItemData>(), ItemMapper.ToDomain, "objeto");
            s.Weathers = s.SafeCatalog<WeatherData, CTEditor.GameDefinition.Domain.Weather.WeatherDefinition>(ContentAssets.LoadAll<WeatherData>(), WeatherMapper.ToDomain, "clima");

            var chart = ContentAssets.LoadAll<TypeChartData>();
            s.Chart = chart.Count > 0 ? TypeChartMapper.ToDomain(chart[0]) : new TypeChart.Builder().Build();
            if (chart.Count == 0) s.Problems.Add("No hay Tabla de Tipos: todos los ataques serán neutros (×1).");

            var rules = ContentAssets.LoadAll<RulesetData>();
            s.Rules = rules.Count > 0 ? RulesetMapper.ToDomain(rules[0], EditorGameData.MechanicById) : Ruleset.Classic;
            return s;
        }

        /// <summary>El motor de combate real, con el azar que le pases.</summary>
        public TurnResolver Resolver(IRng rng)
            => new TurnResolver(Moves, Chart, new ClassicDamageFormula(), rng, Statuses, abilities: Abilities,
                usePp: Rules.UsePp,
                struggleMove: string.IsNullOrWhiteSpace(Rules.StruggleMoveId) ? (Id<Move>?)null : new Id<Move>(Rules.StruggleMoveId),
                weathers: Weathers, rules: BattleRules.From(Rules), items: Items);

        /// <summary>
        /// Crea el snapshot de combate de una especie a un nivel, con la preparación indicada.
        /// 'keyStats' = stats que reciben EVs y naturaleza favorable en modo Maxed (p. ej. Ataque y PS).
        /// </summary>
        public BattleParticipant Participant(SpeciesData data, int level, string id, Preparation prep,
            IReadOnlyList<StatId> keyStats = null, IReadOnlyList<Id<Move>> moves = null)
        {
            var species = SpeciesMapper.ToDomain(data);

            Nature nature = null;
            if (prep == Preparation.Maxed && keyStats != null)
                foreach (var st in keyStats)
                    if (st != StatId.Hp) { nature = new Nature(new Id<Nature>("sandbox"), "Favorable", st, null, 10); break; }

            int? iv = prep == Preparation.Untrained ? 0 : Rules.MaxIv;
            var mon = MonsterFactory.Create(new Id<MonsterInstance>(id), species, level, Rules, Growth,
                moves, null, null, nature, iv);

            if (prep == Preparation.Maxed && keyStats != null)
            {
                foreach (var st in keyStats) mon.AddEffort(st, Rules.MaxEvPerStat);
                mon.RecomputeStats(species.BaseStats, Growth);
            }

            return new BattleParticipant(new Id<BattleParticipant>(id), mon.SpeciesId, mon.Level.Value,
                mon.Stats, mon.CurrentHp, species.Types, mon.Moves, null, species.Ability,
                species.BaseExpYield, species.EvYield, null, mon.Friendship);
        }

        // Catálogo tolerante: salta (y reporta) fichas sin id, repetidas o que no se pueden leer.
        private ICatalog<T> SafeCatalog<TData, T>(List<TData> assets, Func<TData, T> map, string family)
            where TData : ScriptableObject, IContentAsset where T : class
        {
            var ok = new List<TData>();
            var seen = new HashSet<string>();
            foreach (var a in assets)
            {
                if (string.IsNullOrWhiteSpace(a.Id)) { Problems.Add($"Se ignoró un {family} sin id ({a.name})."); continue; }
                if (!seen.Add(a.Id)) { Problems.Add($"Se ignoró un {family} con id repetido '{a.Id}'."); continue; }
                try { map(a); ok.Add(a); }
                catch (Exception e) { Problems.Add($"Se ignoró el {family} '{a.Id}': {e.Message}"); }
            }
            return new ScriptableObjectCatalog<TData, T>(ok, a => a.Id, map);
        }
    }

    /// <summary>Azar del editor (System.Random con semilla: mismo número = mismos resultados).</summary>
    public sealed class EditorRng : IRng
    {
        private readonly System.Random _r;
        public EditorRng(int seed) => _r = new System.Random(seed);
        public int Next(int minInclusive, int maxExclusive) => _r.Next(minInclusive, maxExclusive);
        public float NextFloat() => (float)_r.NextDouble();
    }
}
