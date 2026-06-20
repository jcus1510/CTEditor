using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// La ventana de autoría de Estados (menú: CTEditor → Editor de Estados). Mismo patrón que la de
    /// Movimientos: lista a la izquierda, crear nuevo, e inspector incrustado + validación en vivo a
    /// la derecha. Aquí el autor inventa "Quemado", "Veneno", "Maldición"... rellenando daño por turno
    /// y probabilidad de impedir la acción.
    ///
    /// Nota: esta ventana y MoveEditorWindow comparten estructura; cuando hagamos la de Especies (más
    /// rica), valdrá la pena extraer una clase base genérica. Por ahora, con dos, la duplicación es
    /// menor que la indirección (J.6).
    /// </summary>
    public sealed class StatusEditorWindow : EditorWindow
    {
        private List<StatusConditionData> _statuses = new List<StatusConditionData>();
        private StatusConditionData _selected;
        private UnityEditor.Editor _embedded;
        private List<ValidationIssue> _issues = new List<ValidationIssue>();
        private Vector2 _listScroll, _detailScroll;

        [MenuItem("CTEditor/Editor de Estados")]
        public static void Open()
        {
            var window = GetWindow<StatusEditorWindow>();
            window.titleContent = new GUIContent("Estados");
            window.Refresh();
            window.Show();
        }

        private void OnEnable() => Refresh();
        private void OnDisable() { if (_embedded != null) DestroyImmediate(_embedded); }

        private void Refresh()
        {
            _statuses = LoadAll();
            _statuses.Sort((a, b) => string.Compare(IdOf(a), IdOf(b), System.StringComparison.OrdinalIgnoreCase));

            if (_statuses.Count == 0)
                Select(null);
            else if (_selected == null || !_statuses.Contains(_selected))
                Select(_statuses[0]);
        }

        private void Select(StatusConditionData status)
        {
            _selected = status;
            if (_embedded != null) DestroyImmediate(_embedded);
            _embedded = status != null ? UnityEditor.Editor.CreateEditor(status) : null;
            Revalidate();
        }

        private void Revalidate()
            => _issues = _selected != null ? ContentValidator.IssuesFor(_selected) : new List<ValidationIssue>();

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal();
            DrawList();
            DrawDetail();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawList()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(220));

            if (GUILayout.Button("+ Nuevo estado")) CreateNew();
            if (GUILayout.Button("Refrescar")) Refresh();
            EditorGUILayout.Space();

            _listScroll = EditorGUILayout.BeginScrollView(_listScroll);
            foreach (var status in _statuses)
            {
                if (status == null) continue;
                var style = status == _selected ? EditorStyles.boldLabel : EditorStyles.label;
                if (GUILayout.Button(IdOf(status), style)) Select(status);
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
        }

        private void DrawDetail()
        {
            EditorGUILayout.BeginVertical();

            if (_selected == null)
            {
                EditorGUILayout.HelpBox("Crea o selecciona un estado a la izquierda.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);

            DrawPresets();

            EditorGUI.BeginChangeCheck();
            if (_embedded != null) _embedded.OnInspectorGUI();
            if (EditorGUI.EndChangeCheck()) Revalidate();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Validación", EditorStyles.boldLabel);
            DrawIssues();

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawIssues()
        {
            if (_issues == null || _issues.Count == 0)
            {
                EditorGUILayout.HelpBox("Sin problemas en este estado.", MessageType.Info);
                return;
            }
            foreach (var issue in _issues)
            {
                var type = issue.Severity == IssueSeverity.Error ? MessageType.Error : MessageType.Warning;
                EditorGUILayout.HelpBox(issue.Message, type);
            }
        }

        // Plantillas clásicas: rellenan los campos de comportamiento (no el id, que es del autor).
        // Son un punto de partida; todo queda editable después.
        private void DrawPresets()
        {
            EditorGUILayout.LabelField("Plantillas clásicas (rellenan los campos; luego ajusta)", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Quemado")) ApplyPreset("burn");
            if (GUILayout.Button("Veneno")) ApplyPreset("poison");
            if (GUILayout.Button("Tóxico")) ApplyPreset("toxic");
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Parálisis")) ApplyPreset("paralysis");
            if (GUILayout.Button("Sueño")) ApplyPreset("sleep");
            if (GUILayout.Button("Congelado")) ApplyPreset("freeze");
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
        }

        private void ApplyPreset(string kind)
        {
            if (_selected == null) return;

            string display;
            float residual, prevention;
            bool progressive;
            int duration;
            string passiveStat = null;
            float passiveMul = 1f;

            switch (kind)
            {
                case "burn":      display = "Quemado";   residual = 6.25f; progressive = false; prevention = 0f;   duration = 0; passiveStat = "attack"; passiveMul = 0.5f; break;
                case "poison":    display = "Veneno";    residual = 12.5f; progressive = false; prevention = 0f;   duration = 0; break;
                case "toxic":     display = "Tóxico";    residual = 6.25f; progressive = true;  prevention = 0f;   duration = 0; break;
                case "paralysis": display = "Parálisis"; residual = 0f;    progressive = false; prevention = 25f;  duration = 0; passiveStat = "speed"; passiveMul = 0.5f; break;
                case "sleep":     display = "Sueño";     residual = 0f;    progressive = false; prevention = 100f; duration = 3; break;
                case "freeze":    display = "Congelado"; residual = 0f;    progressive = false; prevention = 100f; duration = 0; break;
                default: return;
            }

            var so = new SerializedObject(_selected);
            so.FindProperty("displayName").stringValue = display;
            so.FindProperty("residualDamagePercent").floatValue = residual;
            so.FindProperty("progressiveResidual").boolValue = progressive;
            so.FindProperty("actionPreventionChance").floatValue = prevention;
            so.FindProperty("durationTurns").intValue = duration;

            var mods = so.FindProperty("passiveModifiers");
            if (passiveStat == null)
            {
                mods.arraySize = 0;
            }
            else
            {
                mods.arraySize = 1;
                var element = mods.GetArrayElementAtIndex(0);
                element.FindPropertyRelative("statId").stringValue = passiveStat;
                element.FindPropertyRelative("multiplier").floatValue = passiveMul;
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(_selected);
            Revalidate();
        }

        private void CreateNew()
        {
            var path = EditorUtility.SaveFilePanelInProject(
                "Nuevo estado", "NewStatus", "asset", "Elige dónde guardar el estado");
            if (string.IsNullOrEmpty(path)) return;

            var asset = ScriptableObject.CreateInstance<StatusConditionData>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            Refresh();
            Select(asset);
        }

        private static string IdOf(StatusConditionData s)
            => s == null ? "" : (string.IsNullOrWhiteSpace(s.Id) ? s.name : s.Id);

        private static List<StatusConditionData> LoadAll()
        {
            var list = new List<StatusConditionData>();
            foreach (var guid in AssetDatabase.FindAssets("t:StatusConditionData"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<StatusConditionData>(path);
                if (asset != null) list.Add(asset);
            }
            return list;
        }
    }
}
