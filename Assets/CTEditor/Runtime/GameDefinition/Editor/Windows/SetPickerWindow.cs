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
    /// «🏆 Set de Smogon…»: para cada miembro de un equipo, elige uno de los sets de competición de su especie (con buscador
    /// de formato) y lo guarda en la ficha: movimientos (la primera alternativa de cada hueco), objeto, habilidad, naturaleza,
    /// EVs e IVs. Así el moveset queda FIJO (se guarda una vez); para que cambie en cada combate, deja los movimientos
    /// vacíos y marca «Moveset cambiante» en el entrenador.
    /// </summary>
    public sealed class SetPickerWindow : EditorWindow
    {
        private string _field;
        private TeamMemberData[] _members;
        private Action<Action<SerializedObject>> _edit;
        private string _format = "";
        private int[] _choice = new int[0];
        private Vector2 _scroll;

        public static void Open(string field, TeamMemberData[] members, Action<Action<SerializedObject>> edit)
        {
            var w = GetWindow<SetPickerWindow>(true, "Sets de Smogon");
            w.minSize = new Vector2(640, 420);
            w._field = field; w._members = members ?? new TeamMemberData[0]; w._edit = edit;
            w._choice = new int[w._members.Length];
            w.Show();
        }

        private List<CompetitiveSetData> SetsFor(TeamMemberData m)
            => ContentAssets.LoadAll<CompetitiveSetData>()
                .Where(s => m != null && string.Equals(s.SpeciesId, m.SpeciesKey, StringComparison.OrdinalIgnoreCase)
                            && (_format.Length == 0 || (s.Format ?? "").IndexOf(_format.Trim(), StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderByDescending(s => s.Score).ToList();

        private void OnGUI()
        {
            if (_members == null || _edit == null) { EditorGUILayout.HelpBox("Ábrelo desde el editor de entrenadores o de equipos.", MessageType.Info); return; }
            if (ContentAssets.LoadAll<CompetitiveSetData>().Count == 0)
            {
                EditorGUILayout.HelpBox("No hay sets de competición. Importa el pack de tu generación (trae sets.csv) o crea sets en " +
                                        "Personajes → Sets de competición.", MessageType.Warning);
                return;
            }
            EditorGUILayout.HelpBox("Elige un set para cada miembro y pulsa «Aplicar»: se guarda en su ficha (moveset FIJO). " +
                                    "«— sin cambios —» deja ese miembro como está.", MessageType.None);
            _format = EditorGUILayout.TextField(new GUIContent("Buscar formato", "ou, uu, ubers, lc... Vacío = todos."), _format);
            if (_choice.Length != _members.Length) _choice = new int[_members.Length];

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < _members.Length; i++)
            {
                var m = _members[i];
                if (m == null || m.SpeciesKey.Length == 0) continue;
                var sets = SetsFor(m);
                string name = m.species != null ? m.species.DisplayName : m.SpeciesKey;
                var labels = new[] { "— sin cambios —" }.Concat(sets.Select(s => $"{(s.Format ?? "").ToUpperInvariant()} · {s.DisplayName}  ({s.Score})")).ToArray();
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"{name}  Nv.{m.level}", GUILayout.Width(180));
                if (sets.Count == 0) EditorGUILayout.LabelField("sin sets" + (_format.Length > 0 ? " en ese formato" : ""), EditorStyles.miniLabel);
                else _choice[i] = EditorGUILayout.Popup(Mathf.Clamp(_choice[i], 0, labels.Length - 1), labels);
                if (sets.Count > 0 && GUILayout.Button(new GUIContent("El más usado", "Elige el set de más puntuación."), EditorStyles.miniButton, GUILayout.Width(90)))
                    _choice[i] = 1;
                EditorGUILayout.EndHorizontal();
                if (sets.Count > 0 && _choice[i] > 0 && _choice[i] <= sets.Count)
                {
                    var s = sets[_choice[i] - 1];
                    EditorGUILayout.LabelField("      " + string.Join(" / ", CompetitiveSet.ParseSlots(s.Moves).Select(sl => sl[0])) +
                                               (string.IsNullOrWhiteSpace(s.Items) ? "" : "  @ " + CompetitiveSet.ParseOptions(s.Items)[0]),
                        EditorStyles.miniLabel);
                }
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("El más usado para todos")) for (int i = 0; i < _choice.Length; i++) _choice[i] = 1;
            if (GUILayout.Button("✔ Aplicar", GUILayout.Height(26))) Apply();
            EditorGUILayout.EndHorizontal();
        }

        private void Apply()
        {
            var picks = new List<(int index, CompetitiveSetData set)>();
            for (int i = 0; i < _members.Length; i++)
            {
                if (_members[i] == null || _choice[i] <= 0) continue;
                var sets = SetsFor(_members[i]);
                if (_choice[i] <= sets.Count) picks.Add((i, sets[_choice[i] - 1]));
            }
            if (picks.Count == 0) { ShowNotification(new GUIContent("No elegiste ningún set")); return; }
            var members = _members;
            string field = _field;
            _edit(so =>
            {
                var arr = so.FindProperty(field);
                foreach (var (index, set) in picks)
                {
                    if (index >= arr.arraySize) continue;
                    var m = members[index];
                    var member = ShowdownWindow.ToMember(m);
                    member.MoveIds = CompetitiveSet.ParseSlots(set.Moves).Select(sl => sl.FirstOrDefault(id => ContentAssets.FindById<MoveData>(id) != null))
                        .Where(id => id != null).Distinct().ToList();
                    member.ItemId = CompetitiveSet.ParseOptions(set.Items).FirstOrDefault(id => ContentAssets.FindById<ItemData>(id) != null) ?? "";
                    member.AbilityId = CompetitiveSet.ParseOptions(set.Abilities).FirstOrDefault() ?? "";
                    member.NatureId = CompetitiveSet.ParseOptions(set.Natures).FirstOrDefault(id => ContentAssets.FindById<NatureData>(id) != null) ?? "";
                    member.Evs = StatSpread.TryParse(set.Evs, out var evs, out _) ? evs : StatSpread.Empty;
                    member.Ivs = StatSpread.TryParse(set.Ivs, out var ivs, out _) ? ivs : StatSpread.Empty;
                    ShowdownWindow.WriteMember(arr.GetArrayElementAtIndex(index), member);
                }
            });
            ShowNotification(new GUIContent($"{picks.Count} set(s) aplicados"));
        }
    }
}
