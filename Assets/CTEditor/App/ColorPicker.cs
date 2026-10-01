using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTEditor.App
{
    /// <summary>
    /// Selector de color con degradado: un cuadro de saturación (izquierda → derecha) y brillo (arriba → abajo), una
    /// barra de tono, la muestra de antes y de ahora, y el color en hexadecimal y en R, G, B. Sirve para las zonas de
    /// encuentros y para cualquier color del proyecto.
    /// </summary>
    public static class ColorPicker
    {
        private const int Square = 180, HueWidth = 18, TexSize = 64;

        public static void Show(AppShell shell, string title, Color start, Action<Color> picked)
        {
            Color.RGBToHSV(start, out float h, out float s, out float v);
            var d = shell.ShowDialog(title);

            var svTex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            var hueTex = new Texture2D(1, 128, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            for (int y = 0; y < 128; y++) hueTex.SetPixel(0, y, Color.HSVToRGB(y / 127f, 1, 1));
            hueTex.Apply();
            d.OnClose += () => { UnityEngine.Object.Destroy(svTex); UnityEngine.Object.Destroy(hueTex); };

            var sv = new VisualElement();
            sv.style.width = Square; sv.style.height = Square;
            sv.style.backgroundImage = svTex;
            sv.Border(1, "borde", 4);
            var svKnob = new VisualElement { pickingMode = PickingMode.Ignore };
            svKnob.style.position = Position.Absolute;
            svKnob.style.width = 12; svKnob.style.height = 12;
            svKnob.Border(2, "texto", 6);
            sv.Add(svKnob);

            var hue = new VisualElement();
            hue.style.width = HueWidth; hue.style.height = Square;
            hue.style.backgroundImage = hueTex;
            hue.Border(1, "borde", 4);
            var hueKnob = new VisualElement { pickingMode = PickingMode.Ignore };
            hueKnob.style.position = Position.Absolute;
            hueKnob.style.left = -3; hueKnob.style.width = HueWidth + 4; hueKnob.style.height = 4;
            hueKnob.Bg("texto").Round(2);
            hue.Add(hueKnob);

            var before = new VisualElement(); before.style.width = 56; before.style.height = 28; before.style.backgroundColor = start;
            var after = new VisualElement(); after.style.width = 56; after.style.height = 28;
            before.Border(1, "borde", 4); after.Border(1, "borde", 4);
            var hex = Ui.TextBox("Hex", "", null, delayed: true);
            hex.style.width = 150;
            TextField r = null, g = null, b = null;
            bool updating = false;

            Color Current() => Color.HSVToRGB(h, s, v);

            void Redraw()
            {
                updating = true;
                var px = new Color32[TexSize * TexSize];
                for (int y = 0; y < TexSize; y++)
                for (int x = 0; x < TexSize; x++)
                    px[y * TexSize + x] = Color.HSVToRGB(h, x / (TexSize - 1f), y / (TexSize - 1f));
                svTex.SetPixels32(px);
                svTex.Apply();
                svKnob.style.left = s * Square - 6;
                svKnob.style.top = (1 - v) * Square - 6;
                hueKnob.style.top = (1 - h) * Square - 2;
                var c = Current();
                after.style.backgroundColor = c;
                hex.SetValueWithoutNotify("#" + ColorUtility.ToHtmlStringRGB(c));
                Color32 c32 = c;
                r?.SetValueWithoutNotify(c32.r.ToString()); g?.SetValueWithoutNotify(c32.g.ToString()); b?.SetValueWithoutNotify(c32.b.ToString());
                updating = false;
            }

            void SetRgb(Color c)
            {
                Color.RGBToHSV(c, out h, out s, out v);
                Redraw();
            }

            void Drag(VisualElement area, Action<Vector2> apply)
            {
                area.RegisterCallback<PointerDownEvent>(e => { area.CapturePointer(e.pointerId); apply(e.localPosition); e.StopPropagation(); });
                area.RegisterCallback<PointerMoveEvent>(e => { if (area.HasPointerCapture(e.pointerId)) apply(e.localPosition); });
                area.RegisterCallback<PointerUpEvent>(e => { if (area.HasPointerCapture(e.pointerId)) area.ReleasePointer(e.pointerId); });
            }
            Drag(sv, p => { s = Mathf.Clamp01(p.x / Square); v = 1 - Mathf.Clamp01(p.y / Square); Redraw(); });
            Drag(hue, p => { h = 1 - Mathf.Clamp01(p.y / Square); Redraw(); });

            hex.RegisterValueChangedCallback(e =>
            {
                if (updating) return;
                var t = (e.newValue ?? "").Trim();
                if (!t.StartsWith("#")) t = "#" + t;
                if (ColorUtility.TryParseHtmlString(t, out var c)) SetRgb(c); else Redraw();
            });
            Color32 s32 = start;
            r = Ui.MiniNumber(s32.r, 0, 255, x => { if (!updating) { Color32 c = Current(); c.r = (byte)x; SetRgb(c); } }, "Rojo (0-255)");
            g = Ui.MiniNumber(s32.g, 0, 255, x => { if (!updating) { Color32 c = Current(); c.g = (byte)x; SetRgb(c); } }, "Verde (0-255)");
            b = Ui.MiniNumber(s32.b, 0, 255, x => { if (!updating) { Color32 c = Current(); c.b = (byte)x; SetRgb(c); } }, "Azul (0-255)");

            // A few quick colours.
            var swatches = Ui.Row(4).Wrap();
            foreach (var q in new[] { "#E0B34A", "#5FB865", "#4E8CF7", "#E0605A", "#B070E0", "#40C0C0", "#E08040", "#F0F0F0", "#808080", "#303030" })
            {
                ColorUtility.TryParseHtmlString(q, out var qc);
                var sw = Ui.Swatch(qc, 20);
                sw.RegisterCallback<PointerUpEvent>(_ => SetRgb(qc));
                sw.tooltip = q;
                swatches.Add(sw.Margin(0, 0, 0, 4));
            }

            var left = Ui.Row(10);
            left.style.alignItems = Align.FlexStart;
            var right = Ui.Column(8);
            var compare = Ui.Row(0);
            compare.With(before, after);
            var rgb = Ui.Row(6);
            rgb.With(Ui.Text("R", 0.85f, dim: true), r, Ui.Text("G", 0.85f, dim: true), g, Ui.Text("B", 0.85f, dim: true), b);
            right.With(Ui.Text("Antes · ahora", 0.8f, dim: true), compare, hex, rgb, Ui.Text("Rápidos", 0.8f, dim: true), swatches);
            right.style.width = 200;
            left.With(sv, hue, right);
            d.Body.Add(left);
            d.Buttons.With(Ui.Button("Cancelar", () => shell.CloseDialog(d)),
                Ui.Button("Usar este color", () => { var c = Current(); shell.CloseDialog(d); picked(c); }, Ui.ButtonKind.Primary));
            Redraw();
        }
    }
}
