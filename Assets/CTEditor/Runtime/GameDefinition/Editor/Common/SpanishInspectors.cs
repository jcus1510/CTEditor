using UnityEditor;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    // Un inspector en español por cada tipo de ficha. Toda la lógica vive en SpanishInspector; aquí solo
    // se dice "este tipo usa el inspector en español". (Unity necesita una clase por tipo.)
    [CustomEditor(typeof(MoveData))] public sealed class MoveDataInspector : SpanishInspector { }
    [CustomEditor(typeof(StatusConditionData))] public sealed class StatusDataInspector : SpanishInspector { }
    [CustomEditor(typeof(SpeciesData))] public sealed class SpeciesDataInspector : SpanishInspector { }
    [CustomEditor(typeof(ElementTypeData))] public sealed class TypeDataInspector : SpanishInspector { }
    [CustomEditor(typeof(TypeChartData))] public sealed class TypeChartDataInspector : SpanishInspector { }
    [CustomEditor(typeof(NatureData))] public sealed class NatureDataInspector : SpanishInspector { }
    [CustomEditor(typeof(GrowthCurveData))] public sealed class CurveDataInspector : SpanishInspector { }
    [CustomEditor(typeof(RulesetData))] public sealed class RulesetDataInspector : SpanishInspector { }
    [CustomEditor(typeof(WeatherData))] public sealed class WeatherDataInspector : SpanishInspector { }
    // ItemData: ItemDataInspector (ItemInspector.cs) draws its effects as cards.
    [CustomEditor(typeof(HazardData))] public sealed class HazardDataInspector : SpanishInspector { }
    [CustomEditor(typeof(SideConditionData))] public sealed class SideConditionDataInspector : SpanishInspector { }
    [CustomEditor(typeof(TrainerData))] public sealed class TrainerDataInspector : SpanishInspector { }
    [CustomEditor(typeof(TeamPresetData))] public sealed class TeamPresetDataInspector : SpanishInspector { }
    [CustomEditor(typeof(EncounterZoneData))] public sealed class EncounterZoneDataInspector : SpanishInspector { }
}
