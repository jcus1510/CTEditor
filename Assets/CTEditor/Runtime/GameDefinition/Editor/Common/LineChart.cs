using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>Una serie del gráfico: nombre, color, grosor y un valor por cada X (índice = X).</summary>
    public sealed class ChartSeries
    {
        public string Name;
        public Color Color;
        public float Width = 1.5f;
        public Func<int, long> ValueAt;   // valor en una X concreta (p. ej. XP total al nivel X)

        public ChartSeries(string name, Color color, float width, Func<int, long> valueAt)
        {
            Name = name; Color = color; Width = width; ValueAt = valueAt;
        }
    }

    /// <summary>
    /// Gráfico de líneas mínimo para ventanas de editor (IMGUI): rejilla, ejes con etiquetas, varias
    /// series y una LÍNEA DE LECTURA que sigue al ratón y muestra el valor de cada serie en esa X.
    ///
    /// No usa librerías externas: dibuja rectángulos (EditorGUI.DrawRect), líneas suavizadas
    /// (Handles.DrawAAPolyLine) y texto (GUI.Label). Solo dibuja en el evento Repaint, que es cuando
    /// Unity pinta la ventana (dibujar en otros eventos no se vería y gastaría tiempo).
    /// </summary>
    public static class LineChart
    {
        private const float LeftPad = 58f, BottomPad = 20f, TopPad = 8f, RightPad = 10f;

        /// <summary>
        /// Dibuja el gráfico en 'height' píxeles de alto. Devuelve la X bajo el ratón (o -1).
        /// </summary>
        public static int Draw(float height, IList<ChartSeries> series, int minX, int maxX, string xTitle, Func<long, string> formatY)
        {
            Rect rect = GUILayoutUtility.GetRect(200f, 4000f, height, height);
            if (series == null || series.Count == 0 || maxX <= minX) return -1;
            formatY = formatY ?? (v => v.ToString("N0"));

            // Máximo del eje Y entre todas las series (con un 5% de margen).
            long maxY = 1;
            foreach (var s in series)
                for (int x = minX; x <= maxX; x++)
                    maxY = Math.Max(maxY, s.ValueAt(x));
            maxY = (long)(maxY * 1.05);

            var plot = new Rect(rect.x + LeftPad, rect.y + TopPad, rect.width - LeftPad - RightPad, rect.height - TopPad - BottomPad);
            float Px(int x) => plot.x + (x - minX) / (float)(maxX - minX) * plot.width;
            float Py(long y) => plot.yMax - (y / (float)maxY) * plot.height;

            // X bajo el ratón.
            int hover = -1;
            Vector2 mouse = Event.current.mousePosition;
            if (plot.Contains(mouse))
                hover = Mathf.Clamp(Mathf.RoundToInt(minX + (mouse.x - plot.x) / plot.width * (maxX - minX)), minX, maxX);

            if (Event.current.type != EventType.Repaint) return hover;

            bool dark = EditorGUIUtility.isProSkin;
            EditorGUI.DrawRect(rect, dark ? new Color(0.16f, 0.16f, 0.16f) : new Color(0.93f, 0.93f, 0.93f));
            var grid = dark ? new Color(1, 1, 1, 0.07f) : new Color(0, 0, 0, 0.08f);
            var axisText = EditorStyles.miniLabel;

            // Rejilla horizontal con etiquetas de Y (5 líneas).
            for (int i = 0; i <= 4; i++)
            {
                long v = maxY * i / 4;
                float y = Py(v);
                EditorGUI.DrawRect(new Rect(plot.x, y, plot.width, 1), grid);
                GUI.Label(new Rect(rect.x + 2, y - 8, LeftPad - 6, 16), formatY(v), axisText);
            }

            // Marcas del eje X cada 10 (o menos si el rango es corto).
            int step = (maxX - minX) > 40 ? 10 : 5;
            for (int x = ((minX + step - 1) / step) * step; x <= maxX; x += step)
            {
                float px = Px(x);
                EditorGUI.DrawRect(new Rect(px, plot.y, 1, plot.height), grid);
                GUI.Label(new Rect(px - 12, plot.yMax + 2, 30, 16), x.ToString(), axisText);
            }
            if (!string.IsNullOrEmpty(xTitle))
                GUI.Label(new Rect(plot.xMax - 60, plot.yMax + 2, 60, 16), xTitle, axisText);

            // Series (primero las finas, así la destacada queda encima si viene al final).
            foreach (var s in series)
            {
                var points = new Vector3[maxX - minX + 1];
                for (int x = minX; x <= maxX; x++)
                    points[x - minX] = new Vector3(Px(x), Py(s.ValueAt(x)), 0);
                Handles.color = s.Color;
                Handles.DrawAAPolyLine(s.Width, points);
            }

            // Línea de lectura + tooltip con el valor de cada serie en esa X.
            if (hover >= 0)
            {
                float hx = Px(hover);
                EditorGUI.DrawRect(new Rect(hx, plot.y, 1, plot.height), dark ? new Color(1, 1, 1, 0.5f) : new Color(0, 0, 0, 0.5f));

                float boxW = 190f, lineH = 15f;
                float boxH = lineH * (series.Count + 1) + 6;
                float bx = hx + 10 + boxW > plot.xMax ? hx - boxW - 10 : hx + 10;
                var box = new Rect(bx, plot.y + 4, boxW, boxH);
                EditorGUI.DrawRect(box, dark ? new Color(0.1f, 0.1f, 0.1f, 0.92f) : new Color(1, 1, 1, 0.95f));
                GUI.Label(new Rect(box.x + 6, box.y + 2, boxW, lineH), $"{xTitle} {hover}", EditorStyles.boldLabel);
                for (int i = 0; i < series.Count; i++)
                {
                    var s = series[i];
                    float ly = box.y + 3 + lineH * (i + 1);
                    EditorGUI.DrawRect(new Rect(box.x + 6, ly + 5, 8, 4), s.Color);
                    GUI.Label(new Rect(box.x + 18, ly, boxW - 20, lineH), $"{s.Name}: {formatY(s.ValueAt(hover))}", axisText);
                }
            }
            return hover;
        }

        /// <summary>Formato compacto de números grandes: 1.250 / 64,0 mil / 1,64 M.</summary>
        public static string Compact(long v)
        {
            if (v >= 1_000_000) return (v / 1_000_000f).ToString("0.##") + " M";
            if (v >= 10_000) return (v / 1000f).ToString("0.#") + " mil";
            return v.ToString("N0");
        }
    }
}
