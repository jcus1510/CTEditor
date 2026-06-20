using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// La ventana de autoría de Movimientos (menú: CTEditor → Editor de Movimientos). Lista todos los
    /// movimientos, deja crear uno nuevo, y al seleccionar uno muestra su inspector + la validación de
    /// ESE movimiento en vivo.
    ///
    /// Decisión de diseño: en vez de dibujar cada campo a mano (frágil y duplicado), INCRUSTAMOS el
    /// inspector estándar de Unity (Editor.CreateEditor + OnInspectorGUI). Así el formulario siempre
    /// refleja la ficha real (incluido el desplegable de estados que añadimos), y nosotros solo
    /// aportamos lo que el inspector suelto no da: lista, creación y validación contextual.
    /// </summary>
    public sealed class MoveEditorWindow : EditorWindow
    {
        private List<MoveData> _moves = new List<MoveData>();
        private MoveData _selected;
        private UnityEditor.Editor _embedded;
        private List<ValidationIssue> _issues = new List<ValidationIssue>();
        private Vector2 _listScroll, _detailScroll;

        [MenuItem("CTEditor/Editor de Movimientos")]
        public static void Open()
        {
            var window = GetWindow<MoveEditorWindow>();
            window.titleContent = new GUIContent("Movimientos");
            window.Refresh();
            window.Show();
        }

        private void OnEnable() => Refresh();
        private void OnDisable() { if (_embedded != null) DestroyImmediate(_embedded); }

        private void Refresh()
        {
            _moves = LoadAllMoves();
            _moves.Sort((a, b) => string.Compare(IdOf(a), IdOf(b), System.StringComparison.OrdinalIgnoreCase));

            if (_moves.Count == 0)
                Select(null);
            else if (_selected == null || !_moves.Contains(_selected))
                Select(_moves[0]);
        }

        private void Select(MoveData move)
        {
            _selected = move;
            if (_embedded != null) DestroyImmediate(_embedded);
            _embedded = move != null ? UnityEditor.Editor.CreateEditor(move) : null;
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

            if (GUILayout.Button("+ Nuevo movimiento")) CreateNew();
            if (GUILayout.Button("Refrescar")) Refresh();
            EditorGUILayout.Space();

            _listScroll = EditorGUILayout.BeginScrollView(_listScroll);
            foreach (var move in _moves)
            {
                if (move == null) continue;
                var style = move == _selected ? EditorStyles.boldLabel : EditorStyles.label;
                if (GUILayout.Button(IdOf(move), style)) Select(move);
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
        }

        private void DrawDetail()
        {
            EditorGUILayout.BeginVertical();

            if (_selected == null)
            {
                EditorGUILayout.HelpBox("Crea o selecciona un movimiento a la izquierda.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);

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
                EditorGUILayout.HelpBox("Sin problemas en este movimiento.", MessageType.Info);
                return;
            }
            foreach (var issue in _issues)
            {
                var type = issue.Severity == IssueSeverity.Error ? MessageType.Error : MessageType.Warning;
                EditorGUILayout.HelpBox(issue.Message, type);
            }
        }

        private void CreateNew()
        {
            var path = EditorUtility.SaveFilePanelInProject(
                "Nuevo movimiento", "NewMove", "asset", "Elige dónde guardar el movimiento");
            if (string.IsNullOrEmpty(path)) return;

            var asset = ScriptableObject.CreateInstance<MoveData>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            Refresh();
            Select(asset);
        }

        private static string IdOf(MoveData m)
            => m == null ? "" : (string.IsNullOrWhiteSpace(m.Id) ? m.name : m.Id);

        private static List<MoveData> LoadAllMoves()
        {
            var list = new List<MoveData>();
            foreach (var guid in AssetDatabase.FindAssets("t:MoveData"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<MoveData>(path);
                if (asset != null) list.Add(asset);
            }
            return list;
        }
    }
}
