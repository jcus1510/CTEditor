using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// Dibuja cualquier campo string marcado con [StatusIdReference] como un desplegable con los ids
    /// de los estados existentes. Si el valor actual no corresponde a ningún estado (p.ej. un id mal
    /// escrito), se conserva y se muestra tal cual para que el autor lo vea — el validador además lo
    /// marcará como error.
    /// </summary>
    [CustomPropertyDrawer(typeof(StatusIdReferenceAttribute))]
    public sealed class StatusIdReferenceDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            var options = new List<string> { "(ninguno)" };
            foreach (var guid in AssetDatabase.FindAssets("t:StatusConditionData"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var data = AssetDatabase.LoadAssetAtPath<StatusConditionData>(path);
                if (data != null && !string.IsNullOrWhiteSpace(data.Id) && !options.Contains(data.Id))
                    options.Add(data.Id);
            }

            var current = property.stringValue;
            int index;
            if (string.IsNullOrEmpty(current))
            {
                index = 0; // "(ninguno)"
            }
            else
            {
                index = options.IndexOf(current);
                if (index < 0)
                {
                    options.Add(current); // valor desconocido: lo preservamos y lo mostramos
                    index = options.Count - 1;
                }
            }

            int newIndex = EditorGUI.Popup(position, label.text, index, options.ToArray());
            if (newIndex != index)
                property.stringValue = newIndex == 0 ? string.Empty : options[newIndex];
        }
    }
}
