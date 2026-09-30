using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Trainers;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// IMPORTAR y EXPORTAR equipos en formato SHOWDOWN (el estándar para compartir equipos, en inglés). Se abre desde el
    /// editor de entrenadores y el de equipos prearmados. Los nombres se reconocen por el nombre en inglés de cada ficha o,
    /// si no tiene, por su id («U-turn» = u_turn, «Rotom-Wash» = rotom_wash).
    /// </summary>
    public sealed class ShowdownWindow : EditorWindow
    {
        private UnityEngine.Object _target;
        private string _field;
        private string _text = "";
        private int _defaultLevel = 50;
        private bool _append;
        private Vector2 _scroll, _problemsScroll;
        private List<string> _problems = new List<string>();
        private List<ShowdownMember> _preview;

        /// <summary>Los dos botones (exportar / importar) para el equipo 'field' de una ficha.</summary>
        public static void DrawButtons(UnityEngine.Object target, string field, TeamMemberData[] team)
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("📤 Exportar a Showdown", "Copia el equipo al portapapeles en formato Showdown (nombres en inglés).")))
            {
                string text = Export(team);
                EditorGUIUtility.systemCopyBuffer = text;
                Open(target, field, text);
            }
            if (GUILayout.Button(new GUIContent("📥 Importar de Showdown…", "Pega un equipo de Showdown (o de Smogon) y se convierte en este equipo.")))
                Open(target, field, "");
            EditorGUILayout.EndHorizontal();
        }

        public static void Open(UnityEngine.Object target, string field, string text)
        {
            var w = GetWindow<ShowdownWindow>(true, "Showdown");
            w.minSize = new Vector2(620, 520);
            w._target = target; w._field = field; w._text = text ?? ""; w._preview = null; w._problems.Clear();
            w.Show();
        }

        // ---------------- Conversión ----------------

        private static ShowdownConverter _converter;
        private static int _version = -1;

        /// <summary>El traductor de nombres con las fichas del proyecto (se rehace si cambia alguna).</summary>
        public static ShowdownConverter Converter()
        {
            if (_converter != null && _version == ContentAssets.Version) return _converter;
            var c = new ShowdownConverter();
            foreach (var s in ContentAssets.LoadAll<SpeciesData>()) c.Species.Add(s.Id, s.EnglishName, s.DisplayName);
            foreach (var m in ContentAssets.LoadAll<MoveData>()) c.Moves.Add(m.Id, m.EnglishName, m.DisplayName);
            foreach (var a in ContentAssets.LoadAll<AbilityData>()) c.Abilities.Add(a.Id, a.EnglishName, a.DisplayName);
            foreach (var i in ContentAssets.LoadAll<ItemData>()) c.Items.Add(i.Id, i.EnglishName, i.DisplayName);
            foreach (var n in ContentAssets.LoadAll<NatureData>()) c.Natures.Add(n.Id, n.EnglishName, n.DisplayName);
            _converter = c;
            _version = ContentAssets.Version;
            return c;
        }

        /// <summary>El equipo de una ficha en texto de Showdown.</summary>
        public static string Export(TeamMemberData[] team)
            => Converter().Export((team ?? new TeamMemberData[0]).Where(m => m != null && m.SpeciesKey.Length > 0).Select(ToMember));

        public static ShowdownMember ToMember(TeamMemberData m)
        {
            var ivs = TeamPreview.Spread(m.ivs, out _);
            // «IVs fijos» distintos de 31 (lo que Showdown da por hecho): se escriben en todas las estadísticas.
            if (m.fixedIvs >= 0 && m.fixedIvs != 31)
            {
                int v = m.fixedIvs;
                var all = StatSpread.Parse($"{v} PS/{v} Atq/{v} Def/{v} AtqE/{v} DefE/{v} Vel").Values.ToDictionary(kv => kv.Key, kv => kv.Value);
                foreach (var kv in ivs.Values) all[kv.Key] = kv.Value;
                ivs = new StatSpread(all);
            }
            return new ShowdownMember
            {
                SpeciesId = m.SpeciesKey, Level = m.level, Nickname = m.nickname ?? "",
                Gender = m.gender == MemberGender.Male ? "M" : m.gender == MemberGender.Female ? "F" : "",
                ItemId = m.heldItem ?? "", AbilityId = m.abilityId ?? "", NatureId = m.NatureKey,
                Evs = TeamPreview.Spread(m.evs, out _), Ivs = ivs,
                MoveIds = m.MoveKeys().ToList(),
            };
        }

        /// <summary>Escribe los miembros en el equipo de la ficha (reemplazando o añadiendo al final).</summary>
        public static void Write(SerializedProperty arr, IList<ShowdownMember> members, bool append)
        {
            int start = append ? arr.arraySize : 0;
            arr.arraySize = start + members.Count;
            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                var el = arr.GetArrayElementAtIndex(start + i);
                el.FindPropertyRelative("species").objectReferenceValue = ContentAssets.FindById<SpeciesData>(m.SpeciesId);
                el.FindPropertyRelative("speciesId").stringValue = m.SpeciesId;
                el.FindPropertyRelative("level").intValue = m.Level;
                var found = m.MoveIds.Select(id => ContentAssets.FindById<MoveData>(id)).Where(x => x != null).ToList();
                var moves = el.FindPropertyRelative("moves");
                var moveIds = el.FindPropertyRelative("moveIds");
                moves.arraySize = found.Count;
                moveIds.arraySize = found.Count;
                for (int k = 0; k < found.Count; k++)
                {
                    moves.GetArrayElementAtIndex(k).objectReferenceValue = found[k];
                    moveIds.GetArrayElementAtIndex(k).stringValue = found[k].Id;
                }
                el.FindPropertyRelative("heldItem").stringValue = m.ItemId;
                var nature = string.IsNullOrEmpty(m.NatureId) ? null : ContentAssets.FindById<NatureData>(m.NatureId);
                el.FindPropertyRelative("nature").objectReferenceValue = nature;
                el.FindPropertyRelative("natureId").stringValue = m.NatureId;
                // Showdown da por hecho IV 31 en lo que no escribe.
                el.FindPropertyRelative("fixedIvs").intValue = 31;
                el.FindPropertyRelative("ivs").stringValue = m.Ivs.Values.Any(kv => kv.Value != 31)
                    ? new StatSpread(m.Ivs.Values.Where(kv => kv.Value != 31)).Format(" / ") : "";
                el.FindPropertyRelative("evs").stringValue = m.Evs.Format(" / ");
                el.FindPropertyRelative("abilityId").stringValue = m.AbilityId;
                el.FindPropertyRelative("nickname").stringValue = m.Nickname;
                el.FindPropertyRelative("gender").enumValueIndex = m.Gender == "M" ? (int)MemberGender.Male : m.Gender == "F" ? (int)MemberGender.Female : 0;
            }
        }

        // ---------------- Ventana ----------------

        private void OnGUI()
        {
            if (_target == null) { EditorGUILayout.HelpBox("La ficha ya no existe. Ábrelo otra vez desde su editor.", MessageType.Warning); return; }
            EditorGUILayout.LabelField($"Equipo de {ContentAssets.Label(_target)}", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Pega un equipo de Showdown o Smogon (un bloque por Pokémon) y pulsa «Analizar», o copia el equipo actual. " +
                                    "Lo que no exista en tu juego se deja en automático y se avisa.", MessageType.None);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("📤 Poner el equipo actual", GUILayout.Width(180))) { _text = Export(TeamOf(_target)); _preview = null; GUI.FocusControl(null); }
            if (GUILayout.Button("📋 Copiar", GUILayout.Width(90))) EditorGUIUtility.systemCopyBuffer = _text;
            if (GUILayout.Button("📋 Pegar", GUILayout.Width(90))) { _text = EditorGUIUtility.systemCopyBuffer ?? ""; _preview = null; GUI.FocusControl(null); }
            EditorGUILayout.EndHorizontal();

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(260));
            string newText = EditorGUILayout.TextArea(_text, GUILayout.ExpandHeight(true));
            if (newText != _text) { _text = newText; _preview = null; }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            _defaultLevel = Mathf.Clamp(EditorGUILayout.IntField(new GUIContent("Nivel si no lo dice", "Showdown da por hecho el 100; aquí eliges otro."), _defaultLevel), 1, 100);
            _append = EditorGUILayout.ToggleLeft("Añadir al final (no reemplazar)", _append);
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("🔍 Analizar")) Analyze();
            if (_preview == null) return;

            EditorGUILayout.HelpBox($"{_preview.Count} miembro(s) reconocido(s)." +
                (_problems.Count > 0 ? $" {_problems.Count} aviso(s): lo que no existe en tu juego se deja automático." : " Todo reconocido."),
                _problems.Count > 0 ? MessageType.Warning : MessageType.Info);
            _problemsScroll = EditorGUILayout.BeginScrollView(_problemsScroll, GUILayout.MaxHeight(110));
            foreach (var p in _problems) EditorGUILayout.LabelField("• " + p, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndScrollView();

            GUI.enabled = _preview.Count > 0;
            if (GUILayout.Button(_append ? $"📥 Añadir {_preview.Count} al equipo" : $"📥 Reemplazar el equipo por estos {_preview.Count}"))
            {
                var members = _preview;
                bool append = _append;
                string field = _field;
                ContentAssets.Edit(_target, so => Write(so.FindProperty(field), members, append));
                AssetDatabase.SaveAssets();
                ShowNotification(new GUIContent("Equipo importado"));
            }
            GUI.enabled = true;
        }

        private void Analyze()
        {
            _problems = new List<string>();
            _preview = Converter().Import(_text, _defaultLevel, _problems);
            if (_preview.Count == 0 && _problems.Count == 0) _problems.Add("No hay ningún miembro: pega el texto de Showdown (un bloque por Pokémon).");
        }

        private static TeamMemberData[] TeamOf(UnityEngine.Object target)
            => target is TrainerData t ? t.Team : target is TeamPresetData p ? p.Members : new TeamMemberData[0];
    }
}
