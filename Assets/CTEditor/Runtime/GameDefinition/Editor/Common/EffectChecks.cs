using System.Collections.Generic;
using System.Linq;
using CTEditor.GameDefinition.Domain.Abilities;
using CTEditor.GameDefinition.Domain.Conditions;
using CTEditor.GameDefinition.Domain.Effects;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>Warnings about a list of effect blocks (an item's or an ability's): missing ids, unsupported moments...</summary>
    public static class EffectChecks
    {
        private static readonly HashSet<string> Stats = new HashSet<string>(StatLabels.ClassicIds.Concat(new[] { "accuracy", "evasion" }));

        /// <summary>One sentence per problem. 'ability' = the blocks of an ability (its own actions and specials).</summary>
        public static List<string> Warnings(IEnumerable<EffectBlock> blocks, bool ability)
        {
            var w = new List<string>();
            foreach (var b in blocks)
            {
                string what = $"«{EffectText.Label(b.Trigger, ability)} → {EffectText.Label(b.Action)}»";
                if (!(ability ? AbilityEffects.IsSupported(b) : EffectRules.IsSupported(b))) w.Add($"{what} se guarda, pero el motor aún no lo aplica.");
                var kind = EffectText.RefOf(b.Action);
                switch (kind)
                {
                    case EffectRefKind.Status: case EffectRefKind.StatusList:
                        foreach (var s in b.RefList) if (ContentAssets.FindById<StatusConditionData>(s) == null) w.Add($"Usa el estado '{s}', que no existe.");
                        break;
                    case EffectRefKind.Type: case EffectRefKind.TypeList:
                        foreach (var t in b.RefList) if (ContentAssets.FindById<ElementTypeData>(t) == null) w.Add($"Usa el tipo '{t}', que no existe.");
                        break;
                    case EffectRefKind.Weather: case EffectRefKind.WeatherList:
                        foreach (var x in b.RefList) if (x != "*" && ContentAssets.FindById<WeatherData>(x) == null) w.Add($"Usa el clima '{x}', que no existe.");
                        break;
                    case EffectRefKind.Stat: case EffectRefKind.StatList:
                        foreach (var s in b.RefList) if (!Stats.Contains(s)) w.Add($"Usa la estadística '{s}', que no existe.");
                        break;
                    case EffectRefKind.Move:
                        if (b.Ref.Length > 0 && ContentAssets.FindById<MoveData>(b.Ref) == null) w.Add($"Enseña '{b.Ref}', que no existe.");
                        break;
                    case EffectRefKind.SideCondition:
                        if (b.Ref.Length > 0 && ContentAssets.FindById<SideConditionData>(b.Ref) == null) w.Add($"Usa el efecto de lado '{b.Ref}', que no existe.");
                        break;
                    case EffectRefKind.Special:
                        if (AbilityEffects.Special(b.Ref) == null) w.Add($"El comportamiento especial '{b.Ref}' no existe.");
                        break;
                }
                bool emptyOk = kind == EffectRefKind.None || kind == EffectRefKind.StatusList || kind == EffectRefKind.StatList
                    || kind == EffectRefKind.TypeList && b.Action == EffectAction.Trap || kind == EffectRefKind.Weather && b.Action != EffectAction.SetWeather;
                if (!emptyOk && b.Ref.Length == 0)
                    w.Add($"{what}: falta elegir {(kind == EffectRefKind.Stat ? "la estadística" : kind == EffectRefKind.Type ? "el tipo" : "qué")}.");
                foreach (var c in b.Conditions)
                {
                    if ((c.Kind == ConditionKind.MoveType || c.Kind == ConditionKind.IsType) && ContentAssets.FindById<ElementTypeData>(c.Text) == null)
                        w.Add($"Una condición usa el tipo '{c.Text}', que no existe.");
                    if (c.Kind == ConditionKind.Weather && ContentAssets.FindById<WeatherData>(c.Text) == null) w.Add($"Una condición usa el clima '{c.Text}', que no existe.");
                    if (c.Kind == ConditionKind.HasStatus && ContentAssets.FindById<StatusConditionData>(c.Text) == null) w.Add($"Una condición usa el estado '{c.Text}', que no existe.");
                }
            }
            return w.Distinct().ToList();
        }
    }
}
