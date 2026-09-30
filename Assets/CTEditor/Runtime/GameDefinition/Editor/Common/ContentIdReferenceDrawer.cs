using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// Dibuja un campo [ContentIdReference(typeof(X))] como DESPLEGABLE con todas las fichas del tipo X
    /// ("Rápida (fast)", "Lenta (slow)"...). Guarda el ID (texto), así el dominio no cambia.
    ///
    /// Si el valor actual no existe (id mal escrito o ficha borrada) NO se pierde: aparece marcado con
    /// "⚠ ... (no existe)" para que el autor lo vea y lo corrija (el validador también lo reporta).
    ///
    /// Pequeña caché de 2 s por tipo: el Inspector se redibuja muchas veces por segundo y buscar en
    /// todo el proyecto cada vez sería lento.
    /// </summary>
    [CustomPropertyDrawer(typeof(ContentIdReferenceAttribute))]
    public sealed class ContentIdReferenceDrawer : PropertyDrawer
    {
        private const string None = "(ninguno)";
        private static readonly Dictionary<Type, (double time, List<(string id, string label)> items)> Cache =
            new Dictionary<Type, (double, List<(string, string)>)>();

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var attr = (ContentIdReferenceAttribute)attribute;
            if (property.propertyType != SerializedPropertyType.String || attr.DataType == null)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            var items = ItemsFor(attr.DataType);
            var ids = new List<string> { "" };
            var labels = new List<string> { None };
            foreach (var (id, text) in items) { ids.Add(id); labels.Add(text); }

            string current = property.stringValue ?? "";
            int index = ids.IndexOf(current);
            if (index < 0)
            {
                ids.Add(current);
                labels.Add($"⚠ {current} (no existe)");
                index = ids.Count - 1;
            }

            int chosen = EditorGUI.Popup(position, label.text, index, labels.ToArray());
            if (chosen != index) property.stringValue = ids[chosen];
        }

        private static List<(string id, string label)> ItemsFor(Type type)
        {
            double now = EditorApplication.timeSinceStartup;
            if (Cache.TryGetValue(type, out var cached) && now - cached.time < 2.0) return cached.items;

            var items = new List<(string, string)>();
            foreach (var asset in ContentAssets.LoadAll(type))
            {
                if (asset is IContentAsset c && !string.IsNullOrWhiteSpace(c.Id))
                    items.Add((c.Id, ContentAssets.Label(asset)));
            }
            items.Sort((a, b) => string.Compare(a.Item2, b.Item2, StringComparison.OrdinalIgnoreCase));
            Cache[type] = (now, items);
            return items;
        }
    }

    /// <summary>
    /// Dibuja un campo [StatIdReference] como desplegable: "(ninguna)", las 6 stats clásicas en español
    /// y "Otra (inventada)…". Si eliges "Otra", aparece al lado un cuadro de texto para escribir el id
    /// de tu stat inventada (p. ej. "suerte"). Así no hay que recordar "sp_attack" de memoria.
    /// </summary>
    [CustomPropertyDrawer(typeof(StatIdReferenceAttribute))]
    public sealed class StatIdReferenceDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            // Opciones: 0 = ninguna, 1..6 = clásicas, precisión/evasión (etapas de combate), última = otra.
            var ids = new List<string>(StatLabels.ClassicIds) { "accuracy", "evasion" };
            var options = new List<string> { "(ninguna)" };
            foreach (var id in ids) options.Add($"{StatLabels.NameOf(id)} ({id})");
            options.Add("Otra (inventada)…");
            int customIndex = options.Count - 1;

            string current = property.stringValue ?? "";
            int index = string.IsNullOrEmpty(current) ? 0 : ids.IndexOf(current) + 1;
            bool isCustom = !string.IsNullOrEmpty(current) && index == 0;
            if (isCustom) index = customIndex;

            // Si es inventada, el desplegable ocupa el 60% y el texto el resto.
            var popupRect = isCustom ? new Rect(position.x, position.y, position.width * 0.6f, position.height) : position;
            int chosen = EditorGUI.Popup(popupRect, label.text, index, options.ToArray());

            if (chosen != index)
            {
                if (chosen == 0) property.stringValue = "";
                else if (chosen == customIndex) property.stringValue = isCustom ? current : "mi_stat";
                else property.stringValue = ids[chosen - 1];
            }
            else if (isCustom)
            {
                var textRect = new Rect(position.x + position.width * 0.6f + 4, position.y, position.width * 0.4f - 4, position.height);
                property.stringValue = EditorGUI.TextField(textRect, current);
            }
        }
    }
}
