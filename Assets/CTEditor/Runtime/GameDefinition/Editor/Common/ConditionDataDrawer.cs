using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CTEditor.GameDefinition.Domain.Conditions;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// Dibuja una CONDICIÓN como una frase editable en tres renglones:
    ///
    ///   [ De quién ▼ ]  [ Qué se pregunta ▼ ]                 [ ] NO
    ///   [ comparación ▼ ] [ número ]   o   [ id ▼ ] según lo que se pregunte
    ///   ▌ Si el rival tiene como mucho 50% de vida          ← la frase final, sobre fondo de color
    ///
    /// Los desplegables solo muestran lo que tiene sentido (el clima no es "de nadie"; la vida necesita
    /// un número; el estado, un id de estado). Así el autor no puede construir algo sin sentido.
    /// </summary>
    [CustomPropertyDrawer(typeof(ConditionData))]
    public sealed class ConditionDataDrawer : PropertyDrawer
    {
        private const float Line = 18f, Gap = 2f;

        private static readonly string[] KindLabels =
        {
            "tiene algún estado", "tiene el estado…", "% de vida", "es del tipo…", "el clima es…",
            "amistad (0-255)", "nivel", "nivel − nivel del otro", "etapa de una estadística", "ya actuó este turno",
            "movimiento: es del tipo…", "movimiento: categoría…", "movimiento: potencia base",
            "movimiento: hace contacto", "movimiento: tiene la etiqueta…", "azar (%)"
        };
        private static readonly string[] SubjectLabels = { "Propio", "Rival" };
        private static readonly string[] CmpLabels = { "menos de", "como mucho", "exactamente", "al menos", "más de", "distinto de" };
        private static readonly string[] Categories = { "Physical", "Special", "Status" };
        private static readonly string[] CategoryLabels = { "Físico", "Especial", "Estado" };

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => Line * 3 + Gap * 3;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var kindP = property.FindPropertyRelative("kind");
            var subjP = property.FindPropertyRelative("subject");
            var cmpP = property.FindPropertyRelative("comparison");
            var numP = property.FindPropertyRelative("number");
            var textP = property.FindPropertyRelative("text");
            var negP = property.FindPropertyRelative("negate");
            var kind = (ConditionKind)kindP.intValue;

            float x = position.x, w = position.width;
            var r1 = new Rect(x, position.y, w, Line);
            var r2 = new Rect(x, r1.yMax + Gap, w, Line);
            var r3 = new Rect(x, r2.yMax + Gap, w, Line);

            // --- Renglón 1: de quién + qué + NO ---
            float negW = 48f;
            if (Condition.UsesSubject(kind))
            {
                subjP.intValue = EditorGUI.Popup(new Rect(x, r1.y, 70f, Line), subjP.intValue, SubjectLabels);
                kindP.intValue = EditorGUI.Popup(new Rect(x + 74f, r1.y, w - 74f - negW - 4f, Line), kindP.intValue, KindLabels);
            }
            else
                kindP.intValue = EditorGUI.Popup(new Rect(x, r1.y, w - negW - 4f, Line), kindP.intValue, KindLabels);
            negP.boolValue = EditorGUI.ToggleLeft(new Rect(x + w - negW, r1.y, negW, Line), "NO", negP.boolValue);
            kind = (ConditionKind)kindP.intValue;

            // --- Renglón 2: el valor ---
            float half = w * 0.5f;
            if (kind == ConditionKind.StatStage)
            {
                textP.stringValue = IdPopup(new Rect(x, r2.y, half - 2f, Line), "stat", textP.stringValue);
                cmpP.intValue = EditorGUI.Popup(new Rect(x + half, r2.y, half * 0.55f, Line), cmpP.intValue, CmpLabels);
                numP.floatValue = EditorGUI.FloatField(new Rect(x + half + half * 0.55f + 4f, r2.y, half * 0.45f - 4f, Line), numP.floatValue);
            }
            else if (kind == ConditionKind.RandomChance)
            {
                EditorGUI.LabelField(new Rect(x, r2.y, 90f, Line), "Probabilidad %");
                numP.floatValue = Mathf.Clamp(EditorGUI.FloatField(new Rect(x + 94f, r2.y, 60f, Line), numP.floatValue), 0f, 100f);
            }
            else if (Condition.UsesNumber(kind))
            {
                cmpP.intValue = EditorGUI.Popup(new Rect(x, r2.y, half - 2f, Line), cmpP.intValue, CmpLabels);
                numP.floatValue = EditorGUI.FloatField(new Rect(x + half + 2f, r2.y, half - 2f, Line), numP.floatValue);
            }
            else if (kind == ConditionKind.MoveCategory)
            {
                int i = Array.IndexOf(Categories, ConditionText.NormalizeCategory(textP.stringValue));
                int chosen = EditorGUI.Popup(r2, Mathf.Max(0, i), CategoryLabels);
                textP.stringValue = Categories[chosen];
            }
            else if (kind == ConditionKind.HasStatus) textP.stringValue = IdPopup(r2, "status", textP.stringValue);
            else if (kind == ConditionKind.IsType || kind == ConditionKind.MoveType) textP.stringValue = IdPopup(r2, "type", textP.stringValue);
            else if (kind == ConditionKind.Weather) textP.stringValue = IdPopup(r2, "weather", textP.stringValue);
            else if (kind == ConditionKind.MoveHasTag) textP.stringValue = EditorGUI.TextField(r2, "Etiqueta", textP.stringValue);
            else EditorGUI.LabelField(r2, "(no necesita valor)", EditorStyles.miniLabel);

            // --- Renglón 3: la frase, sobre fondo de color ---
            var sentence = ConditionText.Describe(new Condition(kind, (ConditionSubject)subjP.intValue,
                (Comparison)cmpP.intValue, numP.floatValue, textP.stringValue, negP.boolValue));
            bool missing = Condition.UsesText(kind) && string.IsNullOrWhiteSpace(textP.stringValue);
            EditorGUI.DrawRect(r3, missing ? new Color(0.85f, 0.35f, 0.25f, 0.25f) : new Color(0.3f, 0.7f, 0.4f, 0.18f));
            EditorGUI.LabelField(new Rect(r3.x + 4f, r3.y, r3.width - 4f, r3.height),
                missing ? "⚠ Falta elegir el valor" : "→ Si " + LowerFirst(sentence), EditorStyles.miniLabel);
        }

        private static string LowerFirst(string s) => string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);

        // Desplegable de ids (estados, tipos, climas, stats) con opción de conservar un id que no exista.
        private static string IdPopup(Rect r, string family, string current)
        {
            var ids = IdOptions.For(family);
            var labels = new List<string> { "(elegir)" };
            var values = new List<string> { "" };
            foreach (var (id, text) in ids) { values.Add(id); labels.Add(text); }
            int index = values.IndexOf(current ?? "");
            if (index < 0) { values.Add(current); labels.Add($"⚠ {current} (no existe)"); index = values.Count - 1; }
            int chosen = EditorGUI.Popup(r, index, labels.ToArray());
            return values[chosen];
        }
    }

    /// <summary>Opciones de ids para los desplegables (con caché de 2 s: el Inspector se redibuja mucho).</summary>
    public static class IdOptions
    {
        private static readonly Dictionary<string, (double time, List<(string id, string label)> items)> Cache =
            new Dictionary<string, (double, List<(string, string)>)>();

        public static List<(string id, string label)> For(string family)
        {
            double now = EditorApplication.timeSinceStartup;
            if (Cache.TryGetValue(family, out var c) && now - c.time < 2.0) return c.items;

            var items = new List<(string, string)>();
            switch (family)
            {
                case "stat":
                    foreach (var id in StatLabels.ClassicIds) items.Add((id, $"{StatLabels.NameOf(id)} ({id})"));
                    items.Add(("accuracy", "Precisión (accuracy)"));
                    items.Add(("evasion", "Evasión (evasion)"));
                    break;
                case "status": Add<StatusConditionData>(items); break;
                case "type": Add<ElementTypeData>(items); break;
                case "weather": Add<WeatherData>(items); break;
            }
            Cache[family] = (now, items);
            return items;
        }

        private static void Add<T>(List<(string, string)> items) where T : ScriptableObject, IContentAsset
        {
            var list = ContentAssets.LoadAll<T>();
            list.Sort((a, b) => string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase));
            foreach (var a in list)
                if (!string.IsNullOrWhiteSpace(a.Id)) items.Add((a.Id, ContentAssets.Label(a)));
        }
    }
}
