using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// La ventana del validador. Menú: CTEditor → Validar Contenido. Corre el ContentValidator y lista
    /// los problemas; cada uno tiene un botón "Ver" que resalta el asset culpable en el Project.
    ///
    /// Es la primera ventana de Content Authoring (L.8). Las siguientes (crear/editar movimientos,
    /// estados, especies) se apoyarán en este mismo validador para avisar al autor en el momento.
    /// </summary>
    public sealed class ContentValidationWindow : EditorWindow
    {
        private List<ValidationIssue> _issues;
        private Vector2 _scroll;

        [MenuItem("CTEditor/Validar Contenido")]
        public static void Open()
        {
            var window = GetWindow<ContentValidationWindow>();
            window.titleContent = new GUIContent("Validar Contenido");
            window.Revalidate();
            window.Show();
        }

        private void OnEnable() => Revalidate();

        private void Revalidate() => _issues = ContentValidator.Validate();

        private void OnGUI()
        {
            EditorGUILayout.Space();
            if (GUILayout.Button("Revalidar"))
                Revalidate();
            EditorGUILayout.Space();

            if (_issues == null)
                return;

            if (_issues.Count == 0)
            {
                EditorGUILayout.HelpBox("Sin problemas. El contenido es coherente.", MessageType.Info);
                return;
            }

            int errors = 0, warnings = 0;
            foreach (var i in _issues)
            {
                if (i.Severity == IssueSeverity.Error) errors++;
                else warnings++;
            }

            EditorGUILayout.HelpBox(
                $"{errors} error(es), {warnings} advertencia(s).",
                errors > 0 ? MessageType.Error : MessageType.Warning);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var issue in _issues)
            {
                EditorGUILayout.BeginHorizontal("box");
                var tag = issue.Severity == IssueSeverity.Error ? "[ERROR] " : "[ADVERTENCIA] ";
                EditorGUILayout.LabelField(tag + issue.Message, EditorStyles.wordWrappedLabel);
                if (issue.Context != null && GUILayout.Button("Ver", GUILayout.Width(48)))
                    EditorGUIUtility.PingObject(issue.Context);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
