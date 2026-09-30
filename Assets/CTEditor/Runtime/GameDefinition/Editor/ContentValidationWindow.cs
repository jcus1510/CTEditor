using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// La ventana del validador. Menú: CTEditor → Herramientas → Validar contenido. Corre el ContentValidator y lista
    /// los problemas; cada uno tiene un botón "Ver" que resalta el asset culpable en el Project.
    ///
    /// Es la primera ventana de Content Authoring (L.8). Las siguientes (crear/editar movimientos,
    /// estados, especies) se apoyarán en este mismo validador para avisar al autor en el momento.
    /// </summary>
    public sealed class ContentValidationWindow : EditorWindow
    {
        private List<ValidationIssue> _issues;
        private Vector2 _scroll;

        [MenuItem(EditorMenus.Tools + "Validar contenido", false, EditorMenus.ToolsOrder + 2)]
        public static void Open()
        {
            var window = GetWindow<ContentValidationWindow>();
            window.titleContent = new GUIContent("Validar Contenido");
            window.Revalidate();
            window.Show();
        }

        private void OnEnable() => Revalidate();

        private void Revalidate() => _issues = ContentValidator.Validate();

        private bool _showErrors = true, _showWarnings = true;
        private string _filter = "";

        // Zoom de texto (Ctrl + rueda, o los botones A− / A+ de los editores).
        private void OnGUI()
        {
            EditorZoom.Begin(this);
            try { DrawWindow(); }
            finally { EditorZoom.End(); }
        }

        private void DrawWindow()
        {
            EditorTheme.TitleBand("Validar contenido",
                "Revisa TODAS las fichas y te avisa de lo que no cuadra (ids repetidos, referencias rotas, valores imposibles). " +
                "Rojo = impide jugar; amarillo = conviene revisarlo.", EditorTheme.Tools, "🔎");

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("↻ Revisar de nuevo", GUILayout.Height(24))) Revalidate();
            if (GUILayout.Button("Abrir el Centro de Contenido", GUILayout.Height(24))) ContentHubWindow.Open();
            EditorGUILayout.EndHorizontal();

            if (_issues == null) return;

            if (_issues.Count == 0)
            {
                EditorTheme.Chip("✔ Sin problemas. El contenido es coherente.", EditorTheme.Ok);
                return;
            }

            int errors = 0, warnings = 0;
            foreach (var i in _issues)
            {
                if (i.Severity == IssueSeverity.Error) errors++;
                else warnings++;
            }

            EditorTheme.Chip($"{(errors > 0 ? "✖" : "⚠")} {errors} error(es) y {warnings} aviso(s)", errors > 0 ? EditorTheme.Bad : EditorTheme.Warn);

            EditorGUILayout.BeginHorizontal();
            _showErrors = EditorGUILayout.ToggleLeft($"Errores ({errors})", _showErrors, GUILayout.Width(120));
            _showWarnings = EditorGUILayout.ToggleLeft($"Avisos ({warnings})", _showWarnings, GUILayout.Width(120));
            _filter = EditorGUILayout.TextField("Buscar", _filter);
            EditorGUILayout.EndHorizontal();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var issue in _issues)
            {
                bool isError = issue.Severity == IssueSeverity.Error;
                if (isError && !_showErrors || !isError && !_showWarnings) continue;
                if (!string.IsNullOrEmpty(_filter) && issue.Message.IndexOf(_filter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.BeginVertical();
                EditorTheme.Tip(issue.Message, isError ? EditorTheme.Bad : EditorTheme.Warn, isError ? "✖" : "⚠");
                EditorGUILayout.EndVertical();
                if (issue.Context != null && GUILayout.Button(new GUIContent("Ver", "Resalta la ficha en la ventana Project"), GUILayout.Width(44)))
                    EditorGUIUtility.PingObject(issue.Context);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
