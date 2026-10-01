using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using CTEditor.GameDefinition.Domain.Effects;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;
using CTEditor.GameDefinition.Text;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>EffectText for Unity: writes blocks into SerializedProperty arrays (EffectBlockData).</summary>
    public static class EffectTextUnity
    {
        /// <summary>Writes blocks into an EffectBlockData[] property.</summary>
        public static void WriteAll(SerializedProperty array, IList<EffectBlock> blocks)
        {
            array.arraySize = blocks.Count;
            for (int i = 0; i < blocks.Count; i++) Write(array.GetArrayElementAtIndex(i), blocks[i]);
        }

        public static void Write(SerializedProperty el, EffectBlock b)
        {
            el.FindPropertyRelative(nameof(EffectBlockData.trigger)).intValue = (int)b.Trigger;
            el.FindPropertyRelative(nameof(EffectBlockData.action)).intValue = (int)b.Action;
            el.FindPropertyRelative(nameof(EffectBlockData.target)).intValue = (int)b.Target;
            el.FindPropertyRelative(nameof(EffectBlockData.reference)).stringValue = b.Ref;
            el.FindPropertyRelative(nameof(EffectBlockData.amount)).floatValue = b.Amount;
            el.FindPropertyRelative(nameof(EffectBlockData.threshold)).floatValue = b.Threshold;
            el.FindPropertyRelative(nameof(EffectBlockData.consumes)).boolValue = b.Consumes;
            el.FindPropertyRelative(nameof(EffectBlockData.chance)).floatValue = b.Chance;
            el.FindPropertyRelative(nameof(EffectBlockData.maxPerBattle)).intValue = b.MaxPerBattle;
            ConditionTextUnity.WriteAll(el.FindPropertyRelative(nameof(EffectBlockData.conditions)), b.Conditions.ToList());
        }
    }
}
