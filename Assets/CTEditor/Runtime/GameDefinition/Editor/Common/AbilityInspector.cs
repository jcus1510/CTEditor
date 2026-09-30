using UnityEditor;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>ABILITY inspector: its name and then its EFFECTS as cards («when / if / then», like items).</summary>
    [CustomEditor(typeof(AbilityData))]
    public sealed class AbilityDataInspector : SpanishInspector
    {
        protected override bool DrawsItself(string propertyName) => propertyName == "effects";

        protected override void DrawCustom()
        {
            EditorGUILayout.Space(4);
            EffectBlocksGui.Draw(serializedObject.FindProperty("effects"), EditorTheme.Abilities, ability: true);
        }
    }
}
