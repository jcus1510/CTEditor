using System.Collections.Generic;
using UnityEditor;
using CTEditor.GameDefinition.Domain.Conditions;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;
using CTEditor.GameDefinition.Text;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>ConditionText for Unity: from the serialized data and into SerializedProperty arrays.</summary>
    public static class ConditionTextUnity
    {
        public static string Describe(ConditionData d) => ConditionText.Describe(FromData(d));

        public static Condition FromData(ConditionData d)
            => d == null ? new Condition(ConditionKind.HasAnyStatus) : new Condition(d.kind, d.subject, d.comparison, d.number, d.text, d.negate);

        /// <summary>Escribe una condición en un elemento de array serializado (ConditionData).</summary>
        public static void Write(SerializedProperty el, Condition c)
        {
            el.FindPropertyRelative("kind").intValue = (int)c.Kind;
            el.FindPropertyRelative("subject").intValue = (int)c.Subject;
            el.FindPropertyRelative("comparison").intValue = (int)c.Comparison;
            el.FindPropertyRelative("number").floatValue = c.Number;
            el.FindPropertyRelative("text").stringValue = c.Text ?? "";
            el.FindPropertyRelative("negate").boolValue = c.Negate;
        }

        public static void WriteAll(SerializedProperty array, IList<Condition> conditions)
        {
            array.arraySize = conditions?.Count ?? 0;
            for (int i = 0; i < array.arraySize; i++) Write(array.GetArrayElementAtIndex(i), conditions[i]);
        }

        /// <summary>Escribe una lista de modificadores de potencia (PowerModifierData[]).</summary>
        public static void WriteModifiers(SerializedProperty array, IList<(float multiplier, Condition[] conditions)> mods)
        {
            array.arraySize = mods?.Count ?? 0;
            for (int i = 0; i < array.arraySize; i++)
            {
                var el = array.GetArrayElementAtIndex(i);
                el.FindPropertyRelative("multiplier").floatValue = mods[i].multiplier;
                WriteAll(el.FindPropertyRelative("conditions"), mods[i].conditions);
            }
        }
    }
}
