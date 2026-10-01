using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTEditor.App
{
    /// <summary>
    /// La rejilla del mapa dibujada como UNA malla (no cientos de elementos de 1 punto): cada línea cae en un píxel entero
    /// de la pantalla, así no desaparece a trozos al hacer zoom o mover el mapa, y lleva una sombra oscura al lado para
    /// verse igual sobre tiles claros y oscuros.
    /// </summary>
    public sealed class GridOverlay : VisualElement
    {
        private readonly List<(Vector2 a, Vector2 b)> _lines = new List<(Vector2, Vector2)>();
        public Color Color { get; set; } = new Color(1, 1, 1, 0.35f);
        public Color Shadow { get; set; } = new Color(0, 0, 0, 0.35f);
        /// <summary>Physical pixels per local point (to snap lines to whole pixels).</summary>
        public float PixelsPerPoint { get; set; } = 1f;

        public GridOverlay()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        public void ClearLines() { _lines.Clear(); MarkDirtyRepaint(); }

        /// <summary>A horizontal or vertical line (local points).</summary>
        public void Add(Vector2 a, Vector2 b) => _lines.Add((a, b));

        public void Commit() => MarkDirtyRepaint();

        private float Snap(float v) => Mathf.Round(v * PixelsPerPoint) / PixelsPerPoint;

        private void Draw(MeshGenerationContext mgc)
        {
            if (_lines.Count == 0) return;
            float px = 1f / Mathf.Max(0.01f, PixelsPerPoint);
            float t = Mathf.Max(px, 1f); // at least one point and one physical pixel
            int count = Mathf.Min(_lines.Count, 65535 / 8);
            var mesh = mgc.Allocate(count * 8, count * 12);
            ushort v = 0;
            for (int i = 0; i < count; i++)
            {
                var (a, b) = _lines[i];
                bool vertical = Mathf.Approximately(a.x, b.x);
                float x0, y0, x1, y1;
                if (vertical) { x0 = Snap(a.x); x1 = x0 + t; y0 = Mathf.Min(a.y, b.y); y1 = Mathf.Max(a.y, b.y); }
                else { y0 = Snap(a.y); y1 = y0 + t; x0 = Mathf.Min(a.x, b.x); x1 = Mathf.Max(a.x, b.x); }
                // Shadow one pixel to the right / below, then the line.
                float sx = vertical ? t : 0, sy = vertical ? 0 : t;
                Quad(mesh, ref v, x0 + sx, y0 + sy, x1 + sx, y1 + sy, Shadow);
                Quad(mesh, ref v, x0, y0, x1, y1, Color);
            }
        }

        private static void Quad(MeshWriteData mesh, ref ushort v, float x0, float y0, float x1, float y1, Color c)
        {
            Color32 c32 = c;
            mesh.SetNextVertex(new Vertex { position = new Vector3(x0, y0, Vertex.nearZ), tint = c32 });
            mesh.SetNextVertex(new Vertex { position = new Vector3(x1, y0, Vertex.nearZ), tint = c32 });
            mesh.SetNextVertex(new Vertex { position = new Vector3(x1, y1, Vertex.nearZ), tint = c32 });
            mesh.SetNextVertex(new Vertex { position = new Vector3(x0, y1, Vertex.nearZ), tint = c32 });
            mesh.SetNextIndex(v); mesh.SetNextIndex((ushort)(v + 1)); mesh.SetNextIndex((ushort)(v + 2));
            mesh.SetNextIndex(v); mesh.SetNextIndex((ushort)(v + 2)); mesh.SetNextIndex((ushort)(v + 3));
            v += 4;
        }
    }
}
