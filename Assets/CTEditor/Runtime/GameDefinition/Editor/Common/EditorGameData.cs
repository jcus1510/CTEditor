using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using CTEditor.GameDefinition.Domain.Abilities;
using CTEditor.GameDefinition.Domain.Battlefield;
using CTEditor.GameDefinition.Domain.Catalog;
using CTEditor.GameDefinition.Domain.Growth;
using CTEditor.GameDefinition.Domain.Hazards;
using CTEditor.GameDefinition.Domain.Items;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Rules;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Status;
using CTEditor.GameDefinition.Domain.Trainers;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.GameDefinition.Domain.Weather;
using CTEditor.GameDefinition.Infrastructure.Acl;
using CTEditor.GameDefinition.Infrastructure.Catalog;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;
using CTEditor.Adventure.Domain;
using SpeciesDef = CTEditor.GameDefinition.Domain.Species.Species;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// Los datos del juego (GameData) armados en el EDITOR con las fichas del proyecto, sin darle a Play. Los usan las
    /// herramientas que tienen que calcular EXACTAMENTE lo que haría el juego (p. ej. «Sugerir según la IA»).
    /// Se reconstruyen solos cuando cambia alguna ficha. Las fichas rotas se saltan (el validador ya las reporta).
    /// </summary>
    public static class EditorGameData
    {
        private static GameData _data;
        private static int _version = -1;

        public static GameData Get()
        {
            if (_data != null && _version == ContentAssets.Version) return _data;
            var chart = ContentAssets.LoadAll<TypeChartData>();
            var rules = ContentAssets.LoadAll<RulesetData>();
            var profiles = new List<AiProfile>();
            foreach (var a in ContentAssets.LoadAll<AiLevelData>())
                try { profiles.Add(AiLevelMapper.ToDomain(a)); } catch (Exception) { /* ficha a medio hacer */ }
            _data = new GameData(
                Catalog<SpeciesData, SpeciesDef>(SpeciesMapper.ToDomain),
                Catalog<MoveData, Move>(MoveMapper.ToDomain),
                chart.Count > 0 ? TypeChartMapper.ToDomain(chart[0]) : new TypeChart.Builder().Build(),
                rules.Count > 0 ? RulesetMapper.ToDomain(rules[0]) : Ruleset.Classic,
                Catalog<StatusConditionData, StatusConditionDefinition>(StatusMapper.ToDomain),
                Catalog<AbilityData, AbilityDefinition>(AbilityMapper.ToDomain),
                Catalog<ItemData, ItemDefinition>(ItemMapper.ToDomain),
                Catalog<WeatherData, WeatherDefinition>(WeatherMapper.ToDomain),
                Catalog<NatureData, Nature>(NatureMapper.ToDomain),
                Catalog<GrowthCurveData, GrowthCurve>(GrowthCurveMapper.ToDomain),
                types: Catalog<ElementTypeData, ElementType>(ElementTypeMapper.ToDomain),
                hazards: Catalog<HazardData, HazardDefinition>(HazardMapper.ToDomain),
                sideConditions: Catalog<SideConditionData, SideConditionDefinition>(SideConditionMapper.ToDomain),
                aiProfiles: profiles);
            _version = ContentAssets.Version;
            return _data;
        }

        // Catálogo tolerante: sin id, repetidas o que no se pueden traducir, se saltan.
        private static ICatalog<T> Catalog<TData, T>(Func<TData, T> map) where TData : ScriptableObject, IContentAsset where T : class
        {
            var ok = new List<TData>();
            var seen = new HashSet<string>();
            foreach (var a in ContentAssets.LoadAll<TData>())
            {
                if (string.IsNullOrWhiteSpace(a.Id) || !seen.Add(a.Id)) continue;
                try { map(a); ok.Add(a); } catch (Exception) { /* el validador la reporta */ }
            }
            return new ScriptableObjectCatalog<TData, T>(ok, a => a.Id, map);
        }
    }
}
