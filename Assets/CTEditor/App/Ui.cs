using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using CTEditor.Art.Domain;
using CTEditor.Workspace;

namespace CTEditor.App
{
    /// <summary>
    /// Piezas de interfaz con el tema aplicado (colores del tema del usuario, tamaños coherentes). Todo el estilo va en
    /// código: así un cambio de tema reconstruye la interfaz y no hay hojas de estilo que mantener aparte.
    /// </summary>
    public static class Ui
    {
        public static Theme Theme { get; set; } = Workspace.Theme.Dark();
        public static int FontSize { get; set; } = 13;

        public const float Gap = 6f;
        public const float Radius = 4f;

        // ── Colors ───────────────────────────────────────────────────────────────────────────────

        private static readonly Dictionary<string, Color> Cache = new Dictionary<string, Color>();
        private static Theme _cachedFor;

        public static Color C(string token)
        {
            if (_cachedFor != Theme) { Cache.Clear(); _cachedFor = Theme; }
            if (Cache.TryGetValue(token, out var c)) return c;
            if (!ColorUtility.TryParseHtmlString(Theme.Get(token), out c)) c = Color.magenta;
            Cache[token] = c;
            return c;
        }

        public static void InvalidateColors() => Cache.Clear();

        public static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);
        public static Color Mix(Color a, Color b, float t) => Color.Lerp(a, b, t);

        public static Color32 ToColor32(Rgba32 p) => new Color32(p.R, p.G, p.B, p.A);

        // ── Layout helpers ───────────────────────────────────────────────────────────────────────

        public static VisualElement Row(float gap = Gap)
        {
            var e = new VisualElement();
            e.style.flexDirection = FlexDirection.Row;
            e.style.alignItems = Align.Center;
            e.userData = gap;
            return e;
        }

        public static VisualElement Column(float gap = 0)
        {
            var e = new VisualElement();
            e.style.flexDirection = FlexDirection.Column;
            e.userData = gap;
            return e;
        }

        public static VisualElement Spacer()
        {
            var e = new VisualElement();
            e.style.flexGrow = 1;
            return e;
        }

        public static T Grow<T>(this T e, float grow = 1) where T : VisualElement
        {
            e.style.flexGrow = grow;
            e.style.flexShrink = 1;
            e.style.flexBasis = 0;
            return e;
        }

        public static T Pad<T>(this T e, float all) where T : VisualElement
        {
            e.style.paddingLeft = all; e.style.paddingRight = all; e.style.paddingTop = all; e.style.paddingBottom = all;
            return e;
        }

        public static T Pad<T>(this T e, float horizontal, float vertical) where T : VisualElement
        {
            e.style.paddingLeft = horizontal; e.style.paddingRight = horizontal; e.style.paddingTop = vertical; e.style.paddingBottom = vertical;
            return e;
        }

        public static T Margin<T>(this T e, float left, float top, float right, float bottom) where T : VisualElement
        {
            e.style.marginLeft = left; e.style.marginTop = top; e.style.marginRight = right; e.style.marginBottom = bottom;
            return e;
        }

        public static T Bg<T>(this T e, string token) where T : VisualElement
        {
            e.style.backgroundColor = C(token);
            return e;
        }

        public static T Border<T>(this T e, float width, string token = "borde", float radius = -1) where T : VisualElement
        {
            var c = C(token);
            e.style.borderLeftWidth = width; e.style.borderRightWidth = width; e.style.borderTopWidth = width; e.style.borderBottomWidth = width;
            e.style.borderLeftColor = c; e.style.borderRightColor = c; e.style.borderTopColor = c; e.style.borderBottomColor = c;
            if (radius >= 0) e.Round(radius);
            return e;
        }

        public static T Round<T>(this T e, float radius = Radius) where T : VisualElement
        {
            e.style.borderTopLeftRadius = radius; e.style.borderTopRightRadius = radius;
            e.style.borderBottomLeftRadius = radius; e.style.borderBottomRightRadius = radius;
            return e;
        }

        public static T Absolute<T>(this T e, float left, float top, float width, float height) where T : VisualElement
        {
            e.style.position = Position.Absolute;
            e.style.left = left; e.style.top = top; e.style.width = width; e.style.height = height;
            return e;
        }

        public static T Fill<T>(this T e) where T : VisualElement
        {
            e.style.position = Position.Absolute;
            e.style.left = 0; e.style.top = 0; e.style.right = 0; e.style.bottom = 0;
            return e;
        }

        public static T Show<T>(this T e, bool visible) where T : VisualElement
        {
            e.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            return e;
        }

        /// <summary>Adds children and spaces them by the row's gap.</summary>
        public static VisualElement With(this VisualElement parent, params VisualElement[] children)
        {
            float gap = parent.userData is float g ? g : 0f;
            bool row = parent.style.flexDirection.value == FlexDirection.Row;
            foreach (var c in children)
            {
                if (c == null) continue;
                if (gap > 0 && parent.childCount > 0)
                {
                    if (row) c.style.marginLeft = gap;
                    else c.style.marginTop = gap;
                }
                parent.Add(c);
            }
            return parent;
        }

        // ── Text ─────────────────────────────────────────────────────────────────────────────────

        public static Label Text(string text, float sizeFactor = 1f, bool dim = false, bool bold = false, bool wrap = false)
        {
            var l = new Label(text);
            l.style.color = C(dim ? "texto_suave" : "texto");
            l.style.fontSize = Mathf.Round(FontSize * sizeFactor);
            l.style.unityFontStyleAndWeight = bold ? FontStyle.Bold : FontStyle.Normal;
            l.style.marginLeft = 0; l.style.marginRight = 0; l.style.marginTop = 0; l.style.marginBottom = 0;
            l.style.paddingLeft = 0; l.style.paddingRight = 0; l.style.paddingTop = 0; l.style.paddingBottom = 0;
            l.style.unityTextAlign = TextAnchor.MiddleLeft;
            if (wrap) l.style.whiteSpace = WhiteSpace.Normal;
            return l;
        }

        public static Label Title(string text) => Text(text, 1.6f, bold: true);
        public static Label Heading(string text) => Text(text, 1.15f, bold: true);
        public static Label Hint(string text) => Text(text, 0.92f, dim: true, wrap: true);

        public static Label Colored(this Label l, string token)
        {
            l.style.color = C(token);
            return l;
        }

        // ── Controls ─────────────────────────────────────────────────────────────────────────────

        public enum ButtonKind { Normal, Primary, Flat, Danger }

        public static Button Button(string text, Action onClick, ButtonKind kind = ButtonKind.Normal, string tooltip = null)
        {
            var b = new Button(onClick) { text = text, tooltip = tooltip ?? "" };
            StyleButton(b, kind);
            return b;
        }

        public static void StyleButton(Button b, ButtonKind kind)
        {
            Color bg = kind switch
            {
                ButtonKind.Primary => C("acento"),
                ButtonKind.Danger => C("error"),
                ButtonKind.Flat => new Color(0, 0, 0, 0),
                _ => Mix(C("panel_alt"), C("texto"), 0.08f),
            };
            Color fg = kind == ButtonKind.Primary || kind == ButtonKind.Danger ? Color.white : C("texto");
            b.style.backgroundColor = bg;
            b.style.color = fg;
            b.style.fontSize = FontSize;
            b.style.unityTextAlign = TextAnchor.MiddleCenter;
            b.style.height = FontSize + 14;
            b.style.paddingLeft = 10; b.style.paddingRight = 10; b.style.paddingTop = 0; b.style.paddingBottom = 0;
            b.style.marginLeft = 0; b.style.marginRight = 0; b.style.marginTop = 0; b.style.marginBottom = 0;
            b.Border(kind == ButtonKind.Flat ? 0 : 1, kind == ButtonKind.Normal ? "borde" : "acento", Radius);
            if (kind == ButtonKind.Danger) b.Border(1, "error", Radius);
            var hover = kind == ButtonKind.Flat ? WithAlpha(C("texto"), 0.08f) : Mix(bg, Color.white, 0.12f);
            b.RegisterCallback<PointerEnterEvent>(_ => { if (b.enabledSelf) b.style.backgroundColor = hover; });
            b.RegisterCallback<PointerLeaveEvent>(_ => b.style.backgroundColor = bg);
        }

        public static Button SetEnabledLook(this Button b, bool enabled)
        {
            b.SetEnabled(enabled);
            b.style.opacity = enabled ? 1f : 0.45f;
            return b;
        }

        /// <summary>A pill button used as a choice among several (sizes, kinds...).</summary>
        public static Button Chip(string text, bool selected, Action onClick, string tooltip = null)
        {
            var b = Button(text, onClick, selected ? ButtonKind.Primary : ButtonKind.Normal, tooltip);
            b.style.height = FontSize + 10;
            b.Round(12);
            return b;
        }

        public static TextField TextBox(string label, string value, Action<string> onChange = null, bool delayed = false)
        {
            var t = new TextField(label ?? "") { value = value ?? "", isDelayed = delayed };
            StyleField(t);
            if (onChange != null) t.RegisterValueChangedCallback(e => onChange(e.newValue));
            return t;
        }

        public static void StyleField(VisualElement field)
        {
            field.style.marginLeft = 0; field.style.marginRight = 0; field.style.marginTop = 0; field.style.marginBottom = 0;
            field.style.fontSize = FontSize;
            var label = field.Q<Label>();
            if (label != null)
            {
                label.style.color = C("texto_suave");
                label.style.fontSize = FontSize;
                label.style.minWidth = 0;
            }
            var input = field.Q(className: TextField.inputUssClassName);
            if (input != null)
            {
                input.style.backgroundColor = C("fondo");
                input.style.color = C("texto");
                input.Border(1, "borde", Radius);
                input.style.minHeight = FontSize + 12;
                input.style.unityTextAlign = TextAnchor.MiddleLeft;
                input.style.paddingLeft = 6;
            }
        }

        /// <summary>Whole-number box with − / + buttons. Calls onChange with the clamped value.</summary>
        public static VisualElement NumberBox(string label, int value, int min, int max, Action<int> onChange, string tooltip = null)
        {
            var row = Row(2);
            if (!string.IsNullOrEmpty(label))
            {
                var l = Text(label, dim: true);
                l.style.minWidth = 90;
                row.Add(l);
            }
            var field = new TextField { value = value.ToString(), isDelayed = true, tooltip = tooltip ?? "" };
            StyleField(field);
            field.style.width = 58;
            int current = value;
            void SetValue(int v, bool notify)
            {
                v = Mathf.Clamp(v, min, max);
                current = v;
                field.SetValueWithoutNotify(v.ToString());
                if (notify) onChange?.Invoke(v);
            }
            field.RegisterValueChangedCallback(e =>
            {
                if (int.TryParse(e.newValue.Trim(), out int v)) SetValue(v, true);
                else SetValue(current, false);
            });
            var minus = Button("-", () => SetValue(current - 1, true), ButtonKind.Normal);
            var plus = Button("+", () => SetValue(current + 1, true), ButtonKind.Normal);
            foreach (var b in new[] { minus, plus }) { b.style.width = 24; b.style.paddingLeft = 0; b.style.paddingRight = 0; }
            row.Add(minus);
            row.Add(field);
            row.Add(plus);
            return row;
        }

        public static Toggle Check(string label, bool value, Action<bool> onChange)
        {
            var t = new Toggle(label) { value = value };
            t.style.marginLeft = 0; t.style.marginRight = 0; t.style.marginTop = 0; t.style.marginBottom = 0;
            var l = t.Q<Label>();
            if (l != null) { l.style.color = C("texto"); l.style.fontSize = FontSize; l.style.minWidth = 0; }
            if (onChange != null) t.RegisterValueChangedCallback(e => onChange(e.newValue));
            return t;
        }

        public static Slider SliderBox(string label, float value, float min, float max, Action<float> onChange)
        {
            var s = new Slider(label, min, max) { value = value };
            s.style.marginLeft = 0; s.style.marginRight = 0;
            var l = s.Q<Label>();
            if (l != null) { l.style.color = C("texto_suave"); l.style.fontSize = FontSize; l.style.minWidth = 90; }
            if (onChange != null) s.RegisterValueChangedCallback(e => onChange(e.newValue));
            return s;
        }

        public static ScrollView Scroll(ScrollViewMode mode = ScrollViewMode.Vertical)
        {
            var s = new ScrollView(mode);
            StyleScroller(s.verticalScroller);
            StyleScroller(s.horizontalScroller);
            return s;
        }

        private static void StyleScroller(Scroller scroller)
        {
            if (scroller == null) return;
            scroller.style.backgroundColor = C("panel");
            scroller.lowButton.style.display = DisplayStyle.None;
            scroller.highButton.style.display = DisplayStyle.None;
            var tracker = scroller.slider.Q(className: "unity-base-slider__tracker");
            if (tracker != null) { tracker.style.backgroundColor = C("panel"); tracker.style.borderLeftWidth = 0; tracker.style.borderRightWidth = 0; tracker.style.borderTopWidth = 0; tracker.style.borderBottomWidth = 0; }
            var dragger = scroller.slider.Q(className: "unity-base-slider__dragger");
            if (dragger != null) { dragger.style.backgroundColor = C("borde"); dragger.Round(3); dragger.style.borderLeftWidth = 0; dragger.style.borderRightWidth = 0; dragger.style.borderTopWidth = 0; dragger.style.borderBottomWidth = 0; }
        }

        public static VisualElement Separator(bool vertical = false)
        {
            var e = new VisualElement();
            e.style.backgroundColor = C("borde");
            if (vertical) { e.style.width = 1; e.style.alignSelf = Align.Stretch; }
            else { e.style.height = 1; e.style.flexShrink = 0; }
            return e;
        }

        /// <summary>A rounded box with a subtle border (cards on the start screen, sections of dialogs).</summary>
        public static VisualElement Card()
        {
            var e = Column().Bg("panel").Border(1, "borde", 6).Pad(14);
            return e;
        }

        /// <summary>A colored square showing a color (theme editor).</summary>
        public static VisualElement Swatch(Color c, float size = 18)
        {
            var e = new VisualElement();
            e.style.width = size; e.style.height = size;
            e.style.backgroundColor = c;
            e.Border(1, "borde", 3);
            return e;
        }
    }
}
