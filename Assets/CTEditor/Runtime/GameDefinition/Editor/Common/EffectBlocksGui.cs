using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CTEditor.GameDefinition.Domain.Conditions;
using CTEditor.GameDefinition.Domain.Effects;
using CTEditor.GameDefinition.Infrastructure.Acl;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// Draws an EffectBlockData[] as CARDS grouped by «when»: each card is «if [conditions] → [action] [parameters]» with
    /// dropdowns of what exists in the project (types, statuses, stats, weathers, moves...), never ids typed by hand.
    /// Used by the item inspector (and later the ability inspector).
    /// </summary>
    public static class EffectBlocksGui
    {
        // ---------------- Names and dropdown options ----------------

        private static (string[] ids, string[] names) Options<T>() where T : ScriptableObject, IContentAsset
        {
            var list = ContentAssets.LoadAll<T>().Where(a => !string.IsNullOrWhiteSpace(a.Id))
                .OrderBy(a => ContentAssets.Label(a), StringComparer.CurrentCultureIgnoreCase).ToList();
            return (list.Select(a => a.Id).ToArray(), list.Select(a => ContentAssets.Label(a)).ToArray());
        }

        private static (string[] ids, string[] names) StatOptions(bool withAccuracy)
        {
            var ids = StatLabels.ClassicIds.ToList();
            if (withAccuracy) { ids.Add("accuracy"); ids.Add("evasion"); }
            return (ids.ToArray(), ids.Select(StatLabels.NameOf).ToArray());
        }

        private static (string[] ids, string[] names) OptionsFor(EffectRefKind k, EffectAction a)
        {
            switch (k)
            {
                case EffectRefKind.Type: return Options<ElementTypeData>();
                case EffectRefKind.Status: case EffectRefKind.StatusList: return Options<StatusConditionData>();
                case EffectRefKind.Stat: return StatOptions(a == EffectAction.ChangeStage);
                case EffectRefKind.Weather: return Options<WeatherData>();
                case EffectRefKind.Move: return Options<MoveData>();
                case EffectRefKind.Mechanic: return Options<MechanicData>();
                default: return (new string[0], new string[0]);
            }
        }

        /// <summary>Readable name of an id (for the sentences).</summary>
        public static string Name(EffectRefKind k, string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return "?";
            switch (k)
            {
                case EffectRefKind.Stat: return StatLabels.NameOf(id);
                case EffectRefKind.Type: return NameOf<ElementTypeData>(id);
                case EffectRefKind.Status: case EffectRefKind.StatusList: return NameOf<StatusConditionData>(id);
                case EffectRefKind.Weather: return NameOf<WeatherData>(id);
                case EffectRefKind.Move: return NameOf<MoveData>(id);
                case EffectRefKind.Mechanic: return NameOf<MechanicData>(id);
                default: return id;
            }
        }

        private static string NameOf<T>(string id) where T : ScriptableObject, IContentAsset
        {
            var a = ContentAssets.FindById<T>(id);
            return a != null ? ContentAssets.Label(a) : id + " (no existe)";
        }

        // ---------------- The list ----------------

        /// <summary>Draws the whole list. 'accent' = the editor's colour.</summary>
        public static void Draw(SerializedProperty effects, Color accent)
        {
            if (effects == null) return;
            EditorTheme.Section($"Qué hace  ({effects.arraySize} efecto{(effects.arraySize == 1 ? "" : "s")})", accent);
            if (effects.arraySize == 0)
                EditorTheme.Paragraph("Todavía no hace nada. Añade un efecto («cuándo → qué») o parte de una plantilla por piezas.");

            int remove = -1, up = -1, down = -1;
            foreach (var trigger in EffectText.AllTriggers)
            {
                var indices = Enumerable.Range(0, effects.arraySize)
                    .Where(i => effects.GetArrayElementAtIndex(i).FindPropertyRelative("trigger").intValue == (int)trigger).ToList();
                if (indices.Count == 0) continue;
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField(new GUIContent("▸ " + EffectText.Label(trigger), EffectText.Help(trigger)), EditorStyles.boldLabel);
                foreach (int i in indices)
                {
                    var r = DrawCard(effects.GetArrayElementAtIndex(i), i, effects.arraySize, accent);
                    if (r == CardResult.Remove) remove = i;
                    else if (r == CardResult.Up) up = i;
                    else if (r == CardResult.Down) down = i;
                }
            }
            if (remove >= 0) effects.DeleteArrayElementAtIndex(remove);
            else if (up > 0) effects.MoveArrayElement(up, up - 1);
            else if (down >= 0 && down < effects.arraySize - 1) effects.MoveArrayElement(down, down + 1);

            EditorGUILayout.Space(4);
            if (GUILayout.Button("+ Añadir efecto…", GUILayout.Height(22))) AddMenu(effects).ShowAsContext();
        }

        private static GenericMenu AddMenu(SerializedProperty effects)
        {
            var menu = new GenericMenu();
            var so = effects.serializedObject;
            string path = effects.propertyPath;
            foreach (var t in EffectText.AllTriggers)
                foreach (var a in EffectText.ActionsFor(t))
                {
                    var trig = t; var act = a;
                    menu.AddItem(new GUIContent($"{EffectText.Label(t)}/{EffectText.Label(a)}"), false, () =>
                    {
                        so.Update();
                        Append(so.FindProperty(path), EffectText.Default(trig, act));
                        so.ApplyModifiedProperties();
                    });
                }
            return menu;
        }

        /// <summary>Adds a block at the end.</summary>
        public static void Append(SerializedProperty effects, EffectBlock b)
        {
            int i = effects.arraySize;
            effects.arraySize++;
            EffectText.Write(effects.GetArrayElementAtIndex(i), b);
        }

        private enum CardResult { None, Remove, Up, Down }

        private static CardResult DrawCard(SerializedProperty el, int index, int count, Color accent)
        {
            var result = CardResult.None;
            var pTrigger = el.FindPropertyRelative("trigger");
            var pAction = el.FindPropertyRelative("action");
            var trigger = (EffectTrigger)pTrigger.intValue;
            var action = (EffectAction)pAction.intValue;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            // Row 1: when / what / order / delete
            EditorGUILayout.BeginHorizontal();
            var triggers = EffectText.AllTriggers;
            int ti = EditorGUILayout.Popup(Array.IndexOf(triggers, trigger), triggers.Select(EffectText.Label).ToArray(), GUILayout.MinWidth(120));
            if (ti >= 0 && triggers[ti] != trigger) { pTrigger.intValue = (int)triggers[ti]; trigger = triggers[ti]; }
            var actions = EffectText.ActionsFor(trigger).ToList();
            if (!actions.Contains(action)) actions.Insert(0, action);
            int ai = EditorGUILayout.Popup(actions.IndexOf(action), actions.Select(EffectText.Label).ToArray(), GUILayout.MinWidth(140));
            if (ai >= 0 && actions[ai] != action)
            {
                var d = EffectText.Default(trigger, actions[ai]);
                pAction.intValue = (int)actions[ai];
                el.FindPropertyRelative("amount").floatValue = d.Amount;
                el.FindPropertyRelative("reference").stringValue = "";
                el.FindPropertyRelative("target").intValue = (int)d.Target;
                action = actions[ai];
            }
            using (new EditorGUI.DisabledScope(index == 0)) if (GUILayout.Button("▲", EditorStyles.miniButtonLeft, GUILayout.Width(22))) result = CardResult.Up;
            using (new EditorGUI.DisabledScope(index >= count - 1)) if (GUILayout.Button("▼", EditorStyles.miniButtonMid, GUILayout.Width(22))) result = CardResult.Down;
            if (GUILayout.Button("✕", EditorStyles.miniButtonRight, GUILayout.Width(22))) result = CardResult.Remove;
            EditorGUILayout.EndHorizontal();

            // Low HP threshold
            if (trigger == EffectTrigger.LowHp)
            {
                var th = el.FindPropertyRelative("threshold");
                th.floatValue = EditorGUILayout.Slider(new GUIContent("Con este % de PS o menos"), th.floatValue, 1f, 100f);
            }

            // Parameters of the action
            DrawReference(el.FindPropertyRelative("reference"), EffectText.RefOf(action), action);
            DrawAmount(el.FindPropertyRelative("amount"), EffectText.AmountOf(action));
            if (EffectText.UsesTarget(action))
            {
                var tp = el.FindPropertyRelative("target");
                tp.intValue = EditorGUILayout.Popup("A quién", tp.intValue, new[] { "A quien lo lleva", "Al rival" });
            }

            // Conditions
            var conds = el.FindPropertyRelative("conditions");
            int removeCond = -1;
            for (int c = 0; c < conds.arraySize; c++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(conds.GetArrayElementAtIndex(c), new GUIContent(c == 0 ? "Si" : "y"), true);
                if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22))) removeCond = c;
                EditorGUILayout.EndHorizontal();
            }
            if (removeCond >= 0) conds.DeleteArrayElementAtIndex(removeCond);

            // Options
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Condición", EditorStyles.miniButton, GUILayout.Width(90))) { conds.arraySize++; }
            var consumes = el.FindPropertyRelative("consumes");
            consumes.boolValue = GUILayout.Toggle(consumes.boolValue, "Se gasta", GUILayout.Width(80));
            GUILayout.Label("Prob. %", GUILayout.Width(48));
            var chance = el.FindPropertyRelative("chance");
            chance.floatValue = Mathf.Clamp(EditorGUILayout.FloatField(chance.floatValue, GUILayout.Width(40)), 0f, 100f);
            GUILayout.Label("Veces/combate", GUILayout.Width(88));
            var times = el.FindPropertyRelative("maxPerBattle");
            times.intValue = Mathf.Max(0, EditorGUILayout.IntField(times.intValue, GUILayout.Width(32)));
            EditorGUILayout.EndHorizontal();
            if (times.intValue == 0) EditorGUILayout.LabelField("   (0 veces = sin límite)", EditorStyles.miniLabel);

            // The sentence (what the engine will do)
            el.serializedObject.ApplyModifiedProperties();
            var block = ItemMapper.ToDomain(ReadData(el));
            EditorTheme.Paragraph("→ " + EffectText.Describe(block, Name));
            if (!EffectRules.IsSupported(block))
                EditorGUILayout.HelpBox("Este efecto se guarda, pero el motor todavía no lo aplica con este «cuándo».", MessageType.Warning);
            EditorGUILayout.EndVertical();
            return result;
        }

        /// <summary>Reads a card back into data (to describe it).</summary>
        private static EffectBlockData ReadData(SerializedProperty el)
        {
            var conds = new List<ConditionData>();
            var arr = el.FindPropertyRelative("conditions");
            for (int i = 0; i < arr.arraySize; i++)
            {
                var c = arr.GetArrayElementAtIndex(i);
                conds.Add(new ConditionData
                {
                    kind = (ConditionKind)c.FindPropertyRelative("kind").intValue,
                    subject = (ConditionSubject)c.FindPropertyRelative("subject").intValue,
                    comparison = (Comparison)c.FindPropertyRelative("comparison").intValue,
                    number = c.FindPropertyRelative("number").floatValue,
                    text = c.FindPropertyRelative("text").stringValue,
                    negate = c.FindPropertyRelative("negate").boolValue,
                });
            }
            return new EffectBlockData
            {
                trigger = (EffectTrigger)el.FindPropertyRelative("trigger").intValue,
                action = (EffectAction)el.FindPropertyRelative("action").intValue,
                target = (BlockTarget)el.FindPropertyRelative("target").intValue,
                reference = el.FindPropertyRelative("reference").stringValue,
                amount = el.FindPropertyRelative("amount").floatValue,
                threshold = el.FindPropertyRelative("threshold").floatValue,
                consumes = el.FindPropertyRelative("consumes").boolValue,
                chance = el.FindPropertyRelative("chance").floatValue,
                maxPerBattle = el.FindPropertyRelative("maxPerBattle").intValue,
                conditions = conds.ToArray(),
            };
        }

        // ---------------- Parameters ----------------

        private static void DrawReference(SerializedProperty p, EffectRefKind kind, EffectAction action)
        {
            switch (kind)
            {
                case EffectRefKind.None: return;
                case EffectRefKind.Form:
                    p.stringValue = EditorGUILayout.TextField(new GUIContent("Forma (id)", "El id de la forma, como aparece en la especie."), p.stringValue);
                    return;
                case EffectRefKind.StatusList:
                    StatusListButton(p);
                    return;
                case EffectRefKind.Move:
                    MoveButton(p);
                    return;
            }
            var (ids, names) = OptionsFor(kind, action);
            string label = kind == EffectRefKind.Type ? "Tipo" : kind == EffectRefKind.Status ? "Estado" : kind == EffectRefKind.Stat ? "Estadística"
                : kind == EffectRefKind.Weather ? "Clima" : kind == EffectRefKind.Mechanic ? "Mecánica" : "Qué";
            bool allowAny = kind == EffectRefKind.Weather;
            var shownIds = (allowAny ? new[] { "" } : new string[0]).Concat(ids).ToList();
            var shownNames = (allowAny ? new[] { "(cualquiera)" } : new string[0]).Concat(names).ToList();
            int cur = shownIds.IndexOf(p.stringValue ?? "");
            if (cur < 0 && !string.IsNullOrEmpty(p.stringValue)) { shownIds.Insert(0, p.stringValue); shownNames.Insert(0, p.stringValue + " (no existe)"); cur = 0; }
            if (cur < 0) { shownIds.Insert(0, ""); shownNames.Insert(0, "— elige —"); cur = 0; }
            int pick = EditorGUILayout.Popup(label, cur, shownNames.ToArray());
            if (pick != cur) p.stringValue = shownIds[pick];
        }

        private static void StatusListButton(SerializedProperty p)
        {
            var current = (p.stringValue ?? "").Split('|').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            string shown = current.Count == 0 ? "Cualquier estado" : string.Join(", ", current.Select(id => Name(EffectRefKind.Status, id)));
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(new GUIContent("Estados", "Vacío = cualquier estado principal (y la confusión)."));
            if (GUILayout.Button(shown + "  ▾", EditorStyles.popup))
            {
                var menu = new GenericMenu();
                var so = p.serializedObject; string path = p.propertyPath;
                menu.AddItem(new GUIContent("Cualquier estado"), current.Count == 0, () => { so.Update(); so.FindProperty(path).stringValue = ""; so.ApplyModifiedProperties(); });
                menu.AddSeparator("");
                var (ids, names) = Options<StatusConditionData>();
                for (int i = 0; i < ids.Length; i++)
                {
                    string id = ids[i];
                    bool on = current.Contains(id);
                    menu.AddItem(new GUIContent(names[i]), on, () =>
                    {
                        so.Update();
                        var list = (so.FindProperty(path).stringValue ?? "").Split('|').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                        if (list.Contains(id)) list.Remove(id); else list.Add(id);
                        so.FindProperty(path).stringValue = string.Join("|", list);
                        so.ApplyModifiedProperties();
                    });
                }
                menu.ShowAsContext();
            }
            EditorGUILayout.EndHorizontal();
        }

        private static void MoveButton(SerializedProperty p)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Movimiento");
            if (GUILayout.Button(Name(EffectRefKind.Move, p.stringValue) + "  ▾", EditorStyles.popup))
            {
                var menu = new GenericMenu();
                var so = p.serializedObject; string path = p.propertyPath;
                var (ids, names) = Options<MoveData>();
                for (int i = 0; i < ids.Length; i++)
                {
                    string id = ids[i];
                    string first = names[i].Length > 0 ? char.ToUpperInvariant(names[i][0]).ToString() : "?";
                    menu.AddItem(new GUIContent(first + "/" + names[i]), p.stringValue == id, () =>
                    { so.Update(); so.FindProperty(path).stringValue = id; so.ApplyModifiedProperties(); });
                }
                menu.ShowAsContext();
            }
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawAmount(SerializedProperty p, EffectAmountKind kind)
        {
            string label = EffectText.AmountLabel(kind);
            switch (kind)
            {
                case EffectAmountKind.None: return;
                case EffectAmountKind.Percent: p.floatValue = EditorGUILayout.Slider(label, p.floatValue, 0f, 100f); return;
                case EffectAmountKind.Stages: p.floatValue = EditorGUILayout.IntSlider(label, Mathf.RoundToInt(p.floatValue), -6, 6); return;
                case EffectAmountKind.Multiplier: case EffectAmountKind.Ball: p.floatValue = Mathf.Max(0f, EditorGUILayout.FloatField(label, p.floatValue)); return;
                default: p.floatValue = EditorGUILayout.IntField(label, Mathf.RoundToInt(p.floatValue)); return;
            }
        }
    }
}
