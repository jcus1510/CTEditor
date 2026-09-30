using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// INSPECTOR EN ESPAÑOL para las fichas de CTEditor (y los componentes de escena). Dibuja cada campo
    /// con su etiqueta de <see cref="Etiquetas"/>, las opciones de las listas en español, y además:
    ///
    ///   • Oculta lo que no aplica (un efecto "Drenar" no muestra "Estado"; una evolución "por nivel" no
    ///     muestra "Objeto"). Menos campos = menos dudas.
    ///   • Las listas se ven como tarjetas con título en palabras ("Efecto 1: Infligir un estado · 30%")
    ///     y botones "+ Añadir" / "✕".
    ///   • Respeta los dibujantes propios (condiciones, desplegables de ids) y los [Header]/[Tooltip].
    ///
    /// Las clases hijas solo dicen para qué tipo son (ver SpanishInspectors.cs).
    /// </summary>
    public abstract class SpanishInspector : UnityEditor.Editor
    {
        private static readonly HashSet<string> TypesWithDrawer = new HashSet<string> { "ConditionData" };
        private readonly Dictionary<string, bool> _foldouts = new Dictionary<string, bool>();

        // Color de la categoría de la ficha (secciones y tarjetas del mismo color que su editor).
        private Color _accent = EditorTheme.Tools;

        private static Color AccentFor(Type t)
        {
            switch (t?.Name)
            {
                case "MoveData": return EditorTheme.Moves;
                case "SpeciesData": return EditorTheme.Species;
                case "StatusConditionData": return EditorTheme.Status;
                case "AbilityData": return EditorTheme.Abilities;
                case "ElementTypeData": case "TypeChartData": return EditorTheme.Types;
                case "ItemData": return EditorTheme.Items;
                case "WeatherData": return EditorTheme.Weathers;
                case "GrowthCurveData": return EditorTheme.Curves;
                case "NatureData": return EditorTheme.Natures;
                case "RulesetData": return EditorTheme.Rules;
                case "TrainerData": return EditorTheme.Trainers;
                case "TeamPresetData": return EditorTheme.Teams;
                case "EncounterZoneData": return EditorTheme.Zones;
                case "HazardData": return EditorTheme.Hazards;
                case "SideConditionData": return EditorTheme.SideConditions;
                default: return EditorTheme.Tools;
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            _accent = AccentFor(target != null ? target.GetType() : null);

            // Las etiquetas largas en español necesitan más sitio: el ancho de etiqueta crece con la ventana.
            float oldLabel = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Clamp(EditorGUIUtility.currentViewWidth * 0.40f, 150f, 300f);

            // Conmutador: ver la ayuda de cada campo escrita debajo (además de al pasar el ratón).
            bool help = EditorGUILayout.ToggleLeft("📝 Ver la ayuda de cada campo debajo de él", EditorTheme.ShowFieldHelp, EditorStyles.miniLabel);
            if (help != EditorTheme.ShowFieldHelp) EditorTheme.ShowFieldHelp = help;

            var it = serializedObject.GetIterator();
            bool enter = true;
            while (it.NextVisible(enter))
            {
                enter = false;
                if (it.name == "m_Script") continue;
                var p = it.Copy();
                if (!IsVisible(p.name, n => serializedObject.FindProperty(n))) continue;
                if (DrawsItself(p.name)) continue;
                DrawProperty(p, target.GetType(), 0);
            }
            DrawCustom();
            serializedObject.ApplyModifiedProperties();
            EditorGUIUtility.labelWidth = oldLabel;
        }

        /// <summary>Fields a subclass draws on its own (in <see cref="DrawCustom"/>) instead of the generic drawing.</summary>
        protected virtual bool DrawsItself(string propertyName) => false;

        /// <summary>Extra drawing after the generic fields (e.g. the item's effect cards).</summary>
        protected virtual void DrawCustom() { }

        // Ayuda escrita bajo el campo (si el autor lo pidió y el campo tiene ayuda).
        private static void DrawHelp(string tooltip)
        {
            if (!EditorTheme.ShowFieldHelp || string.IsNullOrEmpty(tooltip)) return;
            EditorGUI.indentLevel++;
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(EditorGUI.indentLevel * 15f);
            EditorGUILayout.BeginVertical();
            EditorTheme.Paragraph("↳ " + tooltip);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            EditorGUI.indentLevel--;
        }

        // ---------------- Dibujo recursivo ----------------

        private void DrawProperty(SerializedProperty p, Type ownerType, int depth)
        {
            var field = FindField(ownerType, p.name);
            var label = new GUIContent(LabelFor(p.name, ownerType, p), TooltipOf(field));

            if (p.isArray && p.propertyType != SerializedPropertyType.String)
            {
                DrawHeaderOf(field);
                DrawArray(p, label, ElementType(field), depth);
                return;
            }

            if (p.propertyType == SerializedPropertyType.Enum)
            {
                DrawHeaderOf(field);
                var names = p.enumNames;
                var shown = new string[names.Length];
                var options = new GUIContent[names.Length];
                for (int i = 0; i < names.Length; i++) options[i] = new GUIContent(Etiquetas.Enum(names[i]));
                p.enumValueIndex = EditorGUILayout.Popup(label, Mathf.Max(0, p.enumValueIndex), options);
                DrawHelp(label.tooltip);
                return;
            }

            if (p.propertyType == SerializedPropertyType.Generic && !TypesWithDrawer.Contains(p.type))
            {
                // Clase/estructura anidada sin dibujante propio: sus hijos, sangrados.
                DrawHeaderOf(field);
                EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
                DrawHelp(label.tooltip);
                EditorGUI.indentLevel++;
                DrawChildren(p, field != null ? field.FieldType : null, depth + 1);
                EditorGUI.indentLevel--;
                return;
            }

            DrawLeaf(p, label, field);
            DrawHelp(label.tooltip);
        }

        // Un campo simple. Si tiene un dibujante propio ([Range], [TextArea], desplegable de ids...), lo
        // dibuja Unity (PropertyField). Si no, lo dibujamos nosotros: así su [Header] sale como sección
        // de color en vez del encabezado gris de Unity (que PropertyField pinta siempre).
        private void DrawLeaf(SerializedProperty p, GUIContent label, FieldInfo field)
        {
            if (HasCustomDrawer(field))
            {
                EditorGUILayout.PropertyField(p, label, true);
                return;
            }
            DrawHeaderOf(field);
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer: p.intValue = EditorGUILayout.IntField(label, p.intValue); break;
                case SerializedPropertyType.Float: p.floatValue = EditorGUILayout.FloatField(label, p.floatValue); break;
                case SerializedPropertyType.Boolean: p.boolValue = EditorGUILayout.Toggle(label, p.boolValue); break;
                case SerializedPropertyType.String: p.stringValue = EditorGUILayout.TextField(label, p.stringValue); break;
                case SerializedPropertyType.Color: p.colorValue = EditorGUILayout.ColorField(label, p.colorValue); break;
                case SerializedPropertyType.ObjectReference:
                    p.objectReferenceValue = EditorGUILayout.ObjectField(label, p.objectReferenceValue,
                        field != null ? field.FieldType : typeof(UnityEngine.Object), true);
                    break;
                default: EditorGUILayout.PropertyField(p, label, true); break;
            }
        }

        private static readonly Dictionary<FieldInfo, bool> DrawerCache = new Dictionary<FieldInfo, bool>();

        // ¿El campo tiene algún atributo de dibujo que NO sea [Header]/[Tooltip]/[SerializeField]?
        private static bool HasCustomDrawer(FieldInfo f)
        {
            if (f == null) return true; // sin info: que lo dibuje Unity
            if (DrawerCache.TryGetValue(f, out var cached)) return cached;
            bool any = false;
            foreach (var a in f.GetCustomAttributes(typeof(PropertyAttribute), true))
                if (!(a is HeaderAttribute) && !(a is TooltipAttribute)) { any = true; break; }
            DrawerCache[f] = any;
            return any;
        }

        private void DrawChildren(SerializedProperty parent, Type ownerType, int depth)
        {
            var end = parent.GetEndProperty();
            var child = parent.Copy();
            bool enter = true;
            while (child.NextVisible(enter) && !SerializedProperty.EqualContents(child, end))
            {
                enter = false;
                var c = child.Copy();
                var par = parent;
                if (!IsVisible(c.name, n => par.FindPropertyRelative(n), ownerType)) continue;
                DrawProperty(c, ownerType, depth);
            }
        }

        private void DrawArray(SerializedProperty array, GUIContent label, Type elementType, int depth)
        {
            string key = array.propertyPath;
            _foldouts.TryGetValue(key, out bool open);
            if (!_foldouts.ContainsKey(key)) open = array.arraySize > 0 && array.arraySize <= 6;

            EditorGUILayout.BeginHorizontal();
            open = EditorGUILayout.Foldout(open, $"{label.text}  ({array.arraySize})", true);
            if (GUILayout.Button("+ Añadir", EditorStyles.miniButton, GUILayout.Width(70)))
            {
                array.arraySize++;
                open = true;
            }
            EditorGUILayout.EndHorizontal();
            _foldouts[key] = open;
            DrawHelp(label.tooltip);
            if (!open) return;

            EditorGUI.indentLevel++;
            for (int i = 0; i < array.arraySize; i++)
            {
                var el = array.GetArrayElementAtIndex(i);
                bool complex = el.propertyType == SerializedPropertyType.Generic && !TypesWithDrawer.Contains(el.type);
                string title = ElementTitle(array.name, el, i);

                EditorGUILayout.BeginHorizontal();
                if (complex)
                {
                    var r = EditorGUILayout.GetControlRect(GUILayout.Height(18));
                    EditorGUI.DrawRect(r, EditorTheme.WithAlpha(_accent, 0.20f));
                    EditorGUI.DrawRect(new Rect(r.x, r.y, 3, r.height), _accent);
                    EditorGUI.LabelField(new Rect(r.x + 6, r.y, r.width - 6, r.height), title, EditorStyles.boldLabel);
                }
                else
                {
                    EditorGUILayout.BeginVertical();
                    EditorGUILayout.PropertyField(el, new GUIContent(title), true);
                    EditorGUILayout.EndVertical();
                }
                bool remove = GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(24));
                EditorGUILayout.EndHorizontal();
                if (remove)
                {
                    array.DeleteArrayElementAtIndex(i);
                    break; // la lista cambió: se redibuja en el próximo frame
                }
                if (complex)
                {
                    EditorGUI.indentLevel++;
                    DrawChildren(el, elementType, depth + 1);
                    EditorGUI.indentLevel--;
                    EditorGUILayout.Space();
                }
            }
            EditorGUI.indentLevel--;
        }

        // ---------------- Qué se ve y qué no ----------------

        /// <summary>
        /// Reglas para OCULTAR campos que no aplican, mirando el valor de otros campos del mismo nivel.
        /// </summary>
        private static bool IsVisible(string name, Func<string, SerializedProperty> sibling, Type owner = null)
        {
            int Int(string n) { var s = sibling(n); return s != null ? (s.propertyType == SerializedPropertyType.Enum ? s.enumValueIndex : s.intValue) : 0; }
            bool Bool(string n) { var s = sibling(n); return s != null && s.boolValue; }
            float Float(string n) { var s = sibling(n); return s != null ? s.floatValue : 0f; }
            string Str(string n) { var s = sibling(n); return s != null ? s.stringValue : ""; }
            bool Has(string n) => sibling(n) != null;

            // Efecto de movimiento: según "Qué hace".
            if (Has("kind") && Has("amountPercent"))
            {
                int k = Int("kind"); // orden de MoveEffectKind
                const int Inflict = 0, Drain = 1, Recoil = 2, HealSelf = 3, Stage = 4, Flinch = 5, RecoilMax = 6, Heal = 7, Cure = 8, Weather = 9,
                          SetHazard = 10, ClearHazards = 11, ForceSwitch = 12, SetSide = 13, ResetStages = 14, CritBoost = 15, Substitute = 16,
                          Disable = 17, Encore = 18, Rampage = 19, Rage = 20, ChangeType = 25, SwitchSelf = 26;
                switch (name)
                {
                    case "statusId": return k == Inflict || k == Cure || k == Rampage;
                    case "amountPercent": return k == Drain || k == Recoil || k == HealSelf || k == RecoilMax || k == Heal || k == Substitute;
                    case "statStatId": return k == Stage || k == Rage;
                    case "statStages": return k == Stage || k == Rage || k == CritBoost || k == SwitchSelf;
                    case "weatherId": case "weatherTurns": return k == Weather;
                    case "hazardId": return k == SetHazard || k == ClearHazards;
                    case "sideConditionId": return k == SetSide;
                    case "turns": return k == Disable || k == Encore || k == Rampage;
                    case "typeId": return k == ChangeType;
                    case "target": return k == Inflict || k == Stage || k == Heal || k == Cure || k == Flinch || k == SetHazard || k == ClearHazards
                                          || k == ForceSwitch || k == SetSide || k == ResetStages || k == CritBoost || k == Substitute
                                          || k == Disable || k == Encore || k == ChangeType;
                }
                return true;
            }

            // Condición extra de evolución: solo los campos de lo que comprueba.
            if (Has("check") && Has("relation") && Has("negate"))
            {
                int k = Int("check"); // orden de EvolutionConditionKind
                const int MinLevel = 0, MinFriendship = 1, HoldsItem = 2, KnowsMove = 3, KnowsMoveOfType = 4, TimeOfDay = 5, AtLocation = 6,
                          MapWeather = 7, StatRel = 8, PartySpecies = 9, PartyType = 10, NatureK = 11, Chance = 12, Flag = 13;
                switch (name)
                {
                    case "value": return k == MinLevel || k == MinFriendship || k == Chance;
                    case "itemId": return k == HoldsItem;
                    case "move": return k == KnowsMove;
                    case "type": return k == KnowsMoveOfType || k == PartyType;
                    case "species": return k == PartySpecies;
                    case "nature": return k == NatureK;
                    case "weatherId": return k == MapWeather;
                    case "text": return k == AtLocation || k == Flag;
                    case "time": return k == TimeOfDay;
                    case "relation": return k == StatRel;
                }
                return true;
            }

            // Evolución: según "Cómo evoluciona".
            if (Has("method") && Has("target") && Has("requiredLevel"))
            {
                int m = Int("method"); // Level, Item, Friendship, Trade
                if (name == "itemId") return m == 1 || m == 3;
                if (name == "minFriendship") return m == 2;
                return true;
            }

            // Partida del jugador: solo lo del modo de inicio elegido (Preset, Starter, Manual).
            if (Has("startMode") && Has("teamPreset"))
            {
                int mode = Int("startMode");
                switch (name)
                {
                    case "teamPreset": return mode == 0;
                    case "starterSpecies": case "starterLevel": return mode == 1;
                    case "members": return mode == 2;
                    case "startingItems": return mode != 0;
                }
            }

            // Entrenador: la mochila y cuándo se cura solo si usa objetos; cambiar solo lo hace la IA Experta.
            if (Has("useItems") && Has("healBelowPercent"))
            {
                switch (name)
                {
                    case "items": case "healBelowPercent": return Bool("useItems");
                    case "canSwitch": return Int("ai") == 2;
                }
            }

            // Pantalla de combate: solo el rival del tipo elegido (Trainer, Zone, Wild, Legacy).
            if (Has("opponentMode") && Has("wildLevel"))
            {
                int mode = Int("opponentMode");
                switch (name)
                {
                    case "trainer": return mode == 0;
                    case "zone": return mode == 1;
                    case "wildSpecies": case "wildLevel": return mode == 2;
                    case "opponent": return mode == 3;
                }
            }

            switch (name)
            {
                // Partida del jugador: la hora fija solo sin reloj del sistema.
                case "fixedHour": return !Has("useSystemClock") || !Bool("useSystemClock");
                // Movimiento
                case "accuracy": return !Has("neverMisses") || !Bool("neverMisses");
                case "fixedDamageAmount": { int f = Int("fixedDamage"); return f == 1 || f >= 5; } // fijo, o % de Contraataque / Manto Espejo / Venganza
                // Objeto
                case "reviveHpPercent": return Bool("revives");
                case "restorePpAllMoves": return Int("restorePp") > 0;
                case "battleStages": return Str("battleStatId").Length > 0;
                case "heldTriggerHealHp": case "heldTriggerHealPercent": case "heldConsumedOnTrigger":
                    return Float("heldTriggerHpPercent") > 0f;
                // Curva de XP (Fast..Fluctuating = 0..5, Custom = 6, Expression = 7)
                case "customTable": return Int("formula") == 6;
                case "formulaSegments": return Int("formula") == 7;
                case "xpMultiplierPercent": return Int("formula") <= 5;
                // Estado
                case "durationMaxTurns": return Int("durationTurns") > 0;
                case "selfDamageOnPreventedPercent": return Float("actionPreventionChance") > 0f;
                // Habilidad
                case "absorbImmuneHealPercent": { var t = sibling("typeImmunities"); return t != null && t.arraySize > 0; }
                case "contactReactionChance": return Str("contactReactionStatus").Length > 0;
                case "onEntryStages": case "onEntryTargetsSelf": return Str("onEntryStatId").Length > 0;
                case "statusStatBoostMultiplier": return Str("statusStatBoostStatId").Length > 0;
                case "endOfTurnStages": return Str("endOfTurnStatId").Length > 0;
                case "endOfTurnHealRequiresStatus": return Float("endOfTurnHealPercent") > 0f;
            }
            return true;
        }

        // Etiqueta con matices según el contexto (el nivel de una evolución que no es "por nivel" es un MÍNIMO).
        private static string LabelFor(string name, Type owner, SerializedProperty p)
        {
            if (name == "requiredLevel")
            {
                var method = p.serializedObject.FindProperty(p.propertyPath.Replace(".requiredLevel", ".method"));
                if (method != null && method.enumValueIndex != 0) return "Nivel mínimo (0 = cualquiera)";
            }
            return Etiquetas.Field(name);
        }

        // ---------------- Títulos de las tarjetas de las listas ----------------

        private static string ElementTitle(string arrayName, SerializedProperty el, int i)
        {
            string Rel(string n) { var r = el.FindPropertyRelative(n); return r == null ? "" : ValueText(r); }
            switch (arrayName)
            {
                case "secondaryEffects":
                {
                    var chance = el.FindPropertyRelative("chancePercent");
                    return $"Efecto {i + 1}: {Rel("kind")}" + (chance != null && chance.floatValue < 100f ? $" · {chance.floatValue:0.#}%" : "") +
                           ((el.FindPropertyRelative("conditions")?.arraySize ?? 0) > 0 ? " · con condiciones" : "");
                }
                case "powerModifiers": case "offensivePowerModifiers": case "defensivePowerModifiers": case "heldPowerModifiers":
                {
                    var n = el.FindPropertyRelative("conditions")?.arraySize ?? 0;
                    return $"×{Rel("multiplier")} " + (n == 0 ? "(siempre)" : $"si se cumplen {n} condición(es)");
                }
                case "conditions": return $"Condición {i + 1}";
                case "learnset": return $"Nv. {Rel("level")}: {Rel("move")}";
                case "evolutions": return $"→ {Rel("target")} ({Rel("method")})";
                case "evYield": case "customStats": return $"{StatLabels.NameOf(Rel("statId"))}: {(Rel("amount").Length > 0 ? Rel("amount") : Rel("value"))}";
                case "passiveModifiers": return $"{StatLabels.NameOf(Rel("statId"))} ×{Rel("multiplier")}";
                case "typePowerMultipliers": case "incomingTypeMultipliers": return $"{Rel("type")} ×{Rel("multiplier")}";
                case "formulaSegments": return $"Desde nivel {Rel("fromLevel")}: {Rel("expression")}";
                case "startingItems": return $"{Rel("itemId")} ×{Rel("quantity")}";
                case "members": case "team":
                {
                    var nick = el.FindPropertyRelative("nickname");
                    string mote = nick != null && !string.IsNullOrWhiteSpace(nick.stringValue) ? $" «{nick.stringValue}»" : "";
                    return $"{i + 1}. {Rel("species")}{mote} · Nv. {Rel("level")}";
                }
                case "entries": return $"{Rel("species")} · Nv. {Rel("minLevel")}-{Rel("maxLevel")} · frecuencia {Rel("weight")}";
                case "items": return $"{Rel("itemId")} ×{Rel("quantity")}";
                case "matchups": return $"{Rel("attacking")} → {Rel("defending")} ×{Rel("multiplier")}";
                case "types": return $"Tipo {i + 1}";
                case "moves": return $"Movimiento {i + 1}";
                case "tags": return $"Etiqueta {i + 1}";
                case "critDenominators": return $"Etapa {i}";
                case "customTable": return $"Nivel {i}";
                default: return $"{Etiquetas.Field(arrayName)} {i + 1}";
            }
        }

        private static string ValueText(SerializedProperty p)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Enum: return p.enumValueIndex >= 0 && p.enumValueIndex < p.enumNames.Length ? Etiquetas.Enum(p.enumNames[p.enumValueIndex]) : "";
                case SerializedPropertyType.Integer: return p.intValue.ToString();
                case SerializedPropertyType.Float: return p.floatValue.ToString("0.##");
                case SerializedPropertyType.Boolean: return p.boolValue ? "sí" : "no";
                case SerializedPropertyType.String: return string.IsNullOrEmpty(p.stringValue) ? "—" : p.stringValue;
                case SerializedPropertyType.ObjectReference:
                    return p.objectReferenceValue == null ? "(vacío)" : ContentAssets.Label(p.objectReferenceValue);
                default: return "";
            }
        }

        // ---------------- Reflexión: [Header] / [Tooltip] de campos dibujados a mano ----------------

        private static readonly Dictionary<(Type, string), FieldInfo> FieldCache = new Dictionary<(Type, string), FieldInfo>();

        private static FieldInfo FindField(Type owner, string name)
        {
            if (owner == null || name == null) return null;
            if (FieldCache.TryGetValue((owner, name), out var cached)) return cached;
            FieldInfo found = null;
            for (var t = owner; t != null && found == null; t = t.BaseType)
                found = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            FieldCache[(owner, name)] = found;
            return found;
        }

        private static Type ElementType(FieldInfo f)
        {
            if (f == null) return null;
            var t = f.FieldType;
            if (t.IsArray) return t.GetElementType();
            if (t.IsGenericType && t.GetGenericArguments().Length == 1) return t.GetGenericArguments()[0];
            return null;
        }

        private static string TooltipOf(FieldInfo f)
        {
            var tip = f?.GetCustomAttribute<TooltipAttribute>();
            return tip != null ? tip.tooltip : "";
        }

        // Los [Header] se dibujan como SECCIONES de color (antes: una línea en negrita perdida).
        private void DrawHeaderOf(FieldInfo f)
        {
            if (f == null) return;
            HeaderAttribute h = null;
            foreach (var a in f.GetCustomAttributes(typeof(HeaderAttribute), false)) { h = (HeaderAttribute)a; break; }
            if (h == null) return;
            EditorTheme.Section(h.header, _accent);
        }
    }
}
