using UnityEditor;
using UnityEngine;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// ZOOM de los editores de CTEditor (80 % a 160 %), para leer mejor los textos.
    ///   • Botones «A−  100 %  A+» arriba a la derecha de cada editor.
    ///   • Atajos: Ctrl + rueda del ratón, Ctrl + «+» / Ctrl + «−», Ctrl + 0 (volver al 100 %).
    /// Se guarda por ordenador (EditorPrefs) y vale para todos los editores de CTEditor.
    ///
    /// Cómo funciona: se escala TODO lo que dibuja la ventana (textos, botones y zonas de clic) con una
    /// matriz, y se le da a la ventana un espacio «virtual» más pequeño (o más grande) para que quepa.
    /// </summary>
    public static class EditorZoom
    {
        private const string Pref = "CTEditor.EditorZoom";
        public const float Min = 0.8f, Max = 1.6f, Step = 0.1f;
        private const float TabHeight = 21f;   // la pestaña de la ventana (Unity la dibuja fuera de nuestro OnGUI)

        private static bool _active;
        private static Matrix4x4 _previous;

        public static float Value
        {
            get => Mathf.Clamp(EditorPrefs.GetFloat(Pref, 1f), Min, Max);
            set => EditorPrefs.SetFloat(Pref, Mathf.Clamp(Mathf.Round(value * 10f) / 10f, Min, Max));
        }

        /// <summary>Empieza la zona escalada. Llama SIEMPRE a End (mejor en un finally).</summary>
        public static void Begin(EditorWindow window)
        {
            HandleShortcuts(window);
            float z = Value;
            _active = !Mathf.Approximately(z, 1f);
            if (!_active) return;

            // Unity abre un grupo de recorte para cada ventana que NO se escala: se cierra y se abre otro
            // del tamaño virtual (ventana / zoom) para que nada se recorte.
            GUI.EndGroup();
            var area = new Rect(0f, TabHeight, window.position.width / z, window.position.height / z);
            GUI.BeginGroup(area);
            _previous = GUI.matrix;
            var translation = Matrix4x4.TRS(new Vector3(area.x, area.y, 0f), Quaternion.identity, Vector3.one);
            var scale = Matrix4x4.Scale(new Vector3(z, z, 1f));
            GUI.matrix = translation * scale * translation.inverse * GUI.matrix;
            GUILayout.BeginArea(new Rect(0f, 0f, area.width, area.height));
        }

        public static void End()
        {
            if (!_active) return;
            _active = false;
            GUILayout.EndArea();
            GUI.matrix = _previous;
            GUI.EndGroup();
            GUI.BeginGroup(new Rect(0f, TabHeight, Screen.width, Screen.height));
        }

        /// <summary>Botones «A− 100 % A+». Van dentro de una fila horizontal.</summary>
        public static void Buttons()
        {
            var mini = EditorStyles.miniButton;
            if (GUILayout.Button(new GUIContent("A−", "Reducir el zoom (Ctrl + rueda / Ctrl + −)"), mini, GUILayout.Width(28))) Value -= Step;
            if (GUILayout.Button(new GUIContent(Mathf.RoundToInt(Value * 100f) + " %", "Volver al 100 % (Ctrl + 0)"), mini, GUILayout.Width(46))) Value = 1f;
            if (GUILayout.Button(new GUIContent("A+", "Aumentar el zoom (Ctrl + rueda / Ctrl + +)"), mini, GUILayout.Width(28))) Value += Step;
        }

        private static void HandleShortcuts(EditorWindow window)
        {
            var e = Event.current;
            if (e == null || !(e.control || e.command)) return;
            float before = Value;
            if (e.type == EventType.ScrollWheel) Value -= Mathf.Sign(e.delta.y) * Step;
            else if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Equals || e.keyCode == KeyCode.Plus || e.keyCode == KeyCode.KeypadPlus)) Value += Step;
            else if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Minus || e.keyCode == KeyCode.KeypadMinus)) Value -= Step;
            else if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Alpha0 || e.keyCode == KeyCode.Keypad0)) Value = 1f;
            else return;
            if (!Mathf.Approximately(before, Value)) { e.Use(); window.Repaint(); }
        }
    }
}
