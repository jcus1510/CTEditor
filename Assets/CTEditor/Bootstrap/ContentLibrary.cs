using System;
using System.Collections.Generic;
using UnityEngine;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Species;
using CTEditor.GameDefinition.Domain.Status;
using CTEditor.GameDefinition.Domain.Abilities;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.GameDefinition.Domain.Rules;
using CTEditor.GameDefinition.Domain.Growth;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Catalog;
using CTEditor.GameDefinition.Domain.Weather;
using CTEditor.GameDefinition.Domain.Items;
using CTEditor.GameDefinition.Domain.Trainers;
using CTEditor.GameDefinition.Domain.Encounters;
using CTEditor.GameDefinition.Domain.Hazards;
using CTEditor.GameDefinition.Domain.Battlefield;
using CTEditor.Adventure.Domain;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;
using CTEditor.GameDefinition.Infrastructure.Acl;
using CTEditor.GameDefinition.Infrastructure.Catalog;

namespace CTEditor.Bootstrap
{
    /// <summary>
    /// La BIBLIOTECA de contenido, ESTÁTICA y autocargable. No se arrastra ni se referencia: cualquier
    /// escena (combate, overworld, editor en runtime) llama a ContentLibrary.Moves / .Ruleset / ... y
    /// se carga solo desde Assets/GameContent/Resources/&lt;categoría&gt;, de forma PEREZOSA (solo lo que
    /// se usa) y cacheada. Así, al compilar el juego, el contenido se resuelve dinámicamente sin que
    /// nadie tenga que "cargar" nada a mano.
    ///
    /// (Es contenido inmutable de solo lectura cargado de disco; por eso un estático es legítimo aquí
    /// y no choca con el anti-patrón de "estado mutable global" de M.6.)
    /// </summary>
    public static class ContentLibrary
    {
        private static ICatalog<Move> _moves;
        private static ICatalog<StatusConditionDefinition> _statuses;
        private static ICatalog<AbilityDefinition> _abilities;
        private static ICatalog<Species> _species;
        private static ICatalog<GrowthCurve> _curves;
        private static ICatalog<Nature> _natures;
        private static ICatalog<WeatherDefinition> _weathers;
        private static ICatalog<ItemDefinition> _items;
        private static TypeChart _typeChart;
        private static bool _typeChartLoaded;
        private static Ruleset _ruleset;
        private static bool _rulesetLoaded;
        private static Dictionary<string, float> _moveAnim;
        private static ICatalog<TrainerDefinition> _trainers;
        private static ICatalog<TeamPreset> _teams;
        private static ICatalog<EncounterZone> _zones;
        private static GameData _gameData;
        private static ICatalog<ElementType> _types;
        private static ICatalog<HazardDefinition> _hazards;
        private static ICatalog<SideConditionDefinition> _sideConditions;

        /// <summary>Efectos de lado autorados (GameContent/Resources/SideConditions): Reflejo, Pantalla de Luz...</summary>
        public static ICatalog<SideConditionDefinition> SideConditions =>
            _sideConditions ?? (_sideConditions = new ResourcesContentCatalog<SideConditionData, SideConditionDefinition>(ContentPaths.SideConditions, d => d.Id, SideConditionMapper.ToDomain));

        /// <summary>Trampas de campo autoradas (GameContent/Resources/Hazards).</summary>
        public static ICatalog<HazardDefinition> Hazards =>
            _hazards ?? (_hazards = new ResourcesContentCatalog<HazardData, HazardDefinition>(ContentPaths.Hazards, d => d.Id, HazardMapper.ToDomain));

        /// <summary>Tipos autorados (para mostrar su nombre).</summary>
        public static ICatalog<ElementType> Types =>
            _types ?? (_types = new ResourcesContentCatalog<ElementTypeData, ElementType>(ContentPaths.Types, d => d.Id, ElementTypeMapper.ToDomain));

        /// <summary>
        /// Vacía las cachés (al entrar en Play sin recargar el dominio, o tras editar contenido). La
        /// próxima consulta vuelve a leer las fichas.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reload()
        {
            _moves = null; _statuses = null; _abilities = null; _species = null; _curves = null; _natures = null;
            _weathers = null; _items = null; _typeChart = null; _typeChartLoaded = false; _ruleset = null; _rulesetLoaded = false;
            _moveAnim = null; _trainers = null; _teams = null; _zones = null; _gameData = null; _types = null; _hazards = null; _sideConditions = null;
        }

        /// <summary>Niveles de IA del autor (GameContent/Resources/AiLevels). Los que falten usan el clásico.</summary>
        public static List<AiProfile> AiProfiles
        {
            get
            {
                var list = new List<AiProfile>();
                foreach (var d in Resources.LoadAll<AiLevelData>(ContentPaths.AiLevels))
                {
                    if (d == null) continue;
                    try { list.Add(AiLevelMapper.ToDomain(d)); }
                    catch (Exception e) { Debug.LogWarning($"[CTEditor] Nivel de IA '{d.name}' no válido: {e.Message}"); }
                }
                return list;
            }
        }

        /// <summary>Entrenadores autorados (GameContent/Resources/Trainers).</summary>
        public static ICatalog<TrainerDefinition> Trainers =>
            _trainers ?? (_trainers = new ResourcesContentCatalog<TrainerData, TrainerDefinition>(ContentPaths.Trainers, d => d.Id, TrainerMapper.ToDomain));

        /// <summary>Equipos prearmados (GameContent/Resources/Teams).</summary>
        public static ICatalog<TeamPreset> Teams =>
            _teams ?? (_teams = new ResourcesContentCatalog<TeamPresetData, TeamPreset>(ContentPaths.Teams, d => d.Id, TrainerMapper.ToDomain));

        /// <summary>Zonas salvajes (GameContent/Resources/Encounters).</summary>
        public static ICatalog<EncounterZone> Zones =>
            _zones ?? (_zones = new ResourcesContentCatalog<EncounterZoneData, EncounterZone>(ContentPaths.Encounters, d => d.Id, TrainerMapper.ToDomain));

        /// <summary>
        /// TODO el contenido del juego en un solo paquete para la lógica de partida y combate. Null si falta
        /// lo imprescindible (tabla de tipos o reglas).
        /// </summary>
        public static GameData GameData
        {
            get
            {
                if (_gameData != null) return _gameData;
                if (TypeChart == null || Ruleset == null) return null;
                _gameData = new GameData(Species, Moves, TypeChart, Ruleset, Statuses, Abilities, Items, Weathers, Natures, Curves, types: Types, hazards: Hazards, sideConditions: SideConditions,
                    aiProfiles: AiProfiles);
                return _gameData;
            }
        }

        public static ICatalog<Move> Moves =>
            _moves ?? (_moves = new ResourcesContentCatalog<MoveData, Move>(ContentPaths.Moves, d => d.Id, MoveMapper.ToDomain));

        public static ICatalog<StatusConditionDefinition> Statuses =>
            _statuses ?? (_statuses = new ResourcesContentCatalog<StatusConditionData, StatusConditionDefinition>(ContentPaths.Status, d => d.Id, StatusMapper.ToDomain));

        public static ICatalog<AbilityDefinition> Abilities =>
            _abilities ?? (_abilities = new ResourcesContentCatalog<AbilityData, AbilityDefinition>(ContentPaths.Abilities, d => d.Id, AbilityMapper.ToDomain));

        public static ICatalog<Species> Species =>
            _species ?? (_species = new ResourcesContentCatalog<SpeciesData, Species>(ContentPaths.Species, d => d.Id, SpeciesMapper.ToDomain));

        public static ICatalog<GrowthCurve> Curves =>
            _curves ?? (_curves = new ResourcesContentCatalog<GrowthCurveData, GrowthCurve>(ContentPaths.Curves, d => d.Id, GrowthCurveMapper.ToDomain));

        /// <summary>Naturalezas autoradas (GameContent/Resources/Natures). Vacío = todos neutros.</summary>
        /// <summary>Climas autorados (GameContent/Resources/Weathers). Vacío = el combate no tiene clima.</summary>
        public static ICatalog<WeatherDefinition> Weathers =>
            _weathers ?? (_weathers = new ResourcesContentCatalog<WeatherData, WeatherDefinition>(ContentPaths.Weathers, d => d.Id, WeatherMapper.ToDomain));

        /// <summary>Objetos autorados (GameContent/Resources/Items).</summary>
        public static ICatalog<ItemDefinition> Items =>
            _items ?? (_items = new ResourcesContentCatalog<ItemData, ItemDefinition>(ContentPaths.Items, d => d.Id, ItemMapper.ToDomain));

        public static ICatalog<Nature> Natures =>
            _natures ?? (_natures = new ResourcesContentCatalog<NatureData, Nature>(ContentPaths.Natures, d => d.Id, NatureMapper.ToDomain));

        public static TypeChart TypeChart
        {
            get
            {
                if (!_typeChartLoaded)
                {
                    var data = FirstInFolder<TypeChartData>(ContentPaths.Types);
                    _typeChart = data != null ? TypeChartMapper.ToDomain(data) : null;
                    _typeChartLoaded = true;
                }
                return _typeChart;
            }
        }

        public static Ruleset Ruleset
        {
            get
            {
                if (!_rulesetLoaded)
                {
                    var data = FirstInFolder<RulesetData>(ContentPaths.Rulesets);
                    _ruleset = data != null ? RulesetMapper.ToDomain(data) : null;
                    _rulesetLoaded = true;
                }
                return _ruleset;
            }
        }

        /// <summary>Duración de animación (presentación) de un movimiento; 0 si no se definió.</summary>
        public static float MoveAnimationSeconds(Id<Move> id)
        {
            if (_moveAnim == null)
            {
                _moveAnim = new Dictionary<string, float>();
                var data = Resources.LoadAll<MoveData>(ContentPaths.Moves);
                foreach (var d in data)
                    if (d != null) _moveAnim[d.Id] = d.AnimationSeconds;
            }
            return _moveAnim.TryGetValue(id.Value, out var s) ? s : 0f;
        }

        private static TX FirstInFolder<TX>(string folder) where TX : UnityEngine.Object
        {
            var all = Resources.LoadAll<TX>(folder);
            return (all != null && all.Length > 0) ? all[0] : null;
        }
    }
}
