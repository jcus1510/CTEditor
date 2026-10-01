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
        /// <summary>Physical pixels per interface point (user scale × screen dpi): icons are drawn at this resolution.</summary>
        public static float PixelsPerPoint { get; set; } = 1f;
        /// <summary>One height for every control in a row (buttons, fields, steppers), so they always line up.</summary>
        public static float ControlHeight => FontSize + 14;
        public static float IconSize => Mathf.Round(FontSize * 1.25f);

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

        /// <summary>
        /// How much an element is enlarged by the transforms above it (the zoom of its window, Alt + wheel). Texture
        /// sizes use PixelsPerPoint × this, so a zoomed window still gets sharp maps.
        /// </summary>
        public static float ScaleOf(VisualElement e)
        {
            if (e == null || float.IsNaN(e.layout.width) || e.layout.width <= 0) return 1f;
            float s = e.worldBound.width / e.layout.width;
            return float.IsNaN(s) || s <= 0 ? 1f : s;
        }

        /// <summary>Lets a row flow onto more lines when the panel is narrow (instead of squeezing its items).</summary>
        public static T Wrap<T>(this T e) where T : VisualElement
        {
            e.style.flexWrap = UnityEngine.UIElements.Wrap.Wrap;
            return e;
        }

        /// <summary>Keeps its natural size in a row (it never squeezes).</summary>
        public static T NoShrink<T>(this T e) where T : VisualElement
        {
            e.style.flexShrink = 0;
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
            else
            {
                // A single line that ends in «…» when there is no room: texts never spill over their neighbours.
                l.style.whiteSpace = WhiteSpace.NoWrap;
                l.style.overflow = Overflow.Hidden;
                l.style.textOverflow = TextOverflow.Ellipsis;
                l.style.minWidth = 0;
            }
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
            b.style.height = ControlHeight;
            b.style.minHeight = ControlHeight;
            b.style.flexShrink = 0;
            b.style.whiteSpace = WhiteSpace.NoWrap;
            // A long text never spills out of its button: it ends in «…» when the button reaches its container's width.
            b.style.maxWidth = Length.Percent(100);
            b.style.overflow = Overflow.Hidden;
            b.style.textOverflow = TextOverflow.Ellipsis;
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
            b.style.height = ControlHeight - 4;
            b.style.minHeight = ControlHeight - 4;
            b.Round(12);
            return b;
        }

        /// <summary>A square button with an icon (tools). Selected = filled with the accent colour.</summary>
        public static Button IconButton(string icon, Action onClick, string tooltip, bool selected = false, float iconSize = 0)
        {
            var b = Button("", onClick, selected ? ButtonKind.Primary : ButtonKind.Flat, tooltip);
            b.style.width = ControlHeight;
            b.style.paddingLeft = 0; b.style.paddingRight = 0;
            b.style.alignItems = Align.Center;
            b.style.justifyContent = Justify.Center;
            b.Add(Icons.Element(icon, iconSize > 0 ? iconSize : IconSize, selected ? Color.white : C("texto")));
            return b;
        }

        /// <summary>An icon button that stays pressed while on (grid, neighbours...).</summary>
        public static Button IconToggle(string icon, bool on, Action<bool> onChange, string tooltip, float iconSize = 0)
        {
            var b = IconButton(icon, null, tooltip, false, iconSize);
            void Look()
            {
                b.style.backgroundColor = on ? WithAlpha(C("acento"), 0.35f) : new Color(0, 0, 0, 0);
                b.Border(1, on ? "acento" : "panel", Radius);
            }
            b.clicked += () => { on = !on; Look(); onChange?.Invoke(on); };
            Look();
            b.RegisterCallback<PointerLeaveEvent>(_ => Look());
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
            // Label and box on one line, centred: the box has the same height as the buttons next to it.
            field.style.flexDirection = FlexDirection.Row;
            field.style.alignItems = Align.Center;
            field.style.minHeight = ControlHeight;
            var label = field.Q<Label>();
            if (label != null)
            {
                label.style.color = C("texto_suave");
                label.style.fontSize = FontSize;
                label.style.minWidth = 0;
                label.style.flexShrink = 0;
                label.style.unityTextAlign = TextAnchor.MiddleLeft;
                label.style.paddingLeft = 0; label.style.paddingTop = 0; label.style.paddingBottom = 0;
                label.style.marginLeft = 0; label.style.marginTop = 0; label.style.marginBottom = 0;
                label.style.marginRight = string.IsNullOrEmpty(label.text) ? 0 : 8;
                if (string.IsNullOrEmpty(label.text)) label.style.display = DisplayStyle.None;
            }
            var input = field.Q(className: TextField.inputUssClassName);
            if (input != null)
            {
                input.style.backgroundColor = C("fondo");
                input.style.color = C("texto");
                input.Border(1, "borde", Radius);
                input.style.height = ControlHeight;
                input.style.minHeight = ControlHeight;
                input.style.flexGrow = 1;
                input.style.marginLeft = 0; input.style.marginRight = 0; input.style.marginTop = 0; input.style.marginBottom = 0;
                input.style.paddingTop = 0; input.style.paddingBottom = 0; input.style.paddingLeft = 8; input.style.paddingRight = 8;
                input.style.unityTextAlign = TextAnchor.MiddleLeft;
                // The focused box gets the accent border.
                input.RegisterCallback<FocusInEvent>(_ => input.Border(1, "acento", Radius));
                input.RegisterCallback<FocusOutEvent>(_ => input.Border(1, "borde", Radius));
            }
        }

        /// <summary>Whole-number box with − / + buttons. Calls onChange with the clamped value.</summary>
        /// <param name="labelWidth">
        /// 0 = the label takes just its text and sits right next to its box (several boxes in one row: «Ancho [ ] Alto [ ]»);
        /// a width = labels of stacked boxes line up in a column.
        /// </param>
        public static VisualElement NumberBox(string label, int value, int min, int max, Action<int> onChange, string tooltip = null,
            float labelWidth = 0)
        {
            // [label] [−][ value ][+]: the three boxes joined in one stepper.
            var row = Row(0).NoShrink();
            if (!string.IsNullOrEmpty(label))
            {
                var l = Text(label, dim: true).NoShrink();
                if (labelWidth > 0) l.style.minWidth = labelWidth;
                l.style.marginRight = 6;
                row.Add(l);
            }
            var field = new TextField { value = value.ToString(), isDelayed = true, tooltip = tooltip ?? "" };
            StyleField(field);
            field.style.width = Mathf.Max(56, FontSize * 4.4f);
            field.style.flexShrink = 0;
            var box = field.Q(className: TextField.inputUssClassName);
            if (box != null)
            {
                box.Round(0);
                box.style.unityTextAlign = TextAnchor.MiddleCenter;
                box.style.borderLeftWidth = 0; box.style.borderRightWidth = 0;
                box.RegisterCallback<FocusOutEvent>(_ => { box.style.borderLeftWidth = 0; box.style.borderRightWidth = 0; });
            }
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
            var minus = IconButton("menos", () => SetValue(current - 1, true), "Menos");
            var plus = IconButton("mas", () => SetValue(current + 1, true), "Más");
            foreach (var b in new[] { minus, plus })
            {
                b.style.width = ControlHeight - 2;
                b.style.backgroundColor = Mix(C("panel_alt"), C("texto"), 0.08f);
                b.Border(1, "borde");
            }
            minus.style.borderTopLeftRadius = Radius; minus.style.borderBottomLeftRadius = Radius;
            minus.style.borderTopRightRadius = 0; minus.style.borderBottomRightRadius = 0;
            plus.style.borderTopRightRadius = Radius; plus.style.borderBottomRightRadius = Radius;
            plus.style.borderTopLeftRadius = 0; plus.style.borderBottomLeftRadius = 0;
            row.Add(minus);
            row.Add(field);
            row.Add(plus);
            return row;
        }

        /// <summary>
        /// A small number box for tables (no − / + buttons): type it, or use the mouse wheel / ↑ ↓ over it.
        /// </summary>
        public static TextField MiniNumber(int value, int min, int max, Action<int> onChange, string tooltip = null, float width = 0)
        {
            var field = new TextField { value = value.ToString(), isDelayed = true, tooltip = tooltip ?? "" };
            StyleField(field);
            field.style.width = width > 0 ? width : Mathf.Max(40, FontSize * 3.2f);
            field.style.flexShrink = 0;
            field.style.minHeight = ControlHeight - 4;
            var box = field.Q(className: TextField.inputUssClassName);
            if (box != null) { box.style.height = ControlHeight - 4; box.style.minHeight = ControlHeight - 4; box.style.unityTextAlign = TextAnchor.MiddleCenter; box.style.paddingLeft = 2; box.style.paddingRight = 2; }
            int current = value;
            void Set(int v)
            {
                v = Mathf.Clamp(v, min, max);
                field.SetValueWithoutNotify(v.ToString());
                if (v == current) return;
                current = v;
                onChange?.Invoke(v);
            }
            field.RegisterValueChangedCallback(e => { if (int.TryParse((e.newValue ?? "").Trim(), out int v)) Set(v); else field.SetValueWithoutNotify(current.ToString()); });
            field.RegisterCallback<WheelEvent>(e => { Set(current + (e.delta.y < 0 ? 1 : -1)); e.StopPropagation(); });
            field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.UpArrow) { Set(current + 1); e.StopPropagation(); }
                else if (e.keyCode == KeyCode.DownArrow) { Set(current - 1); e.StopPropagation(); }
            }, TrickleDown.TrickleDown);
            return field;
        }

        public static Toggle Check(string label, bool value, Action<bool> onChange)
        {
            var t = new Toggle(label) { value = value };
            t.style.marginLeft = 0; t.style.marginRight = 0; t.style.marginTop = 0; t.style.marginBottom = 0;
            t.style.flexShrink = 0;
            t.style.alignItems = Align.Center;
            t.style.minHeight = ControlHeight;
            var l = t.Q<Label>();
            if (l != null)
            {
                l.style.color = C("texto"); l.style.fontSize = FontSize; l.style.minWidth = 0;
                l.style.marginRight = 6; l.style.unityTextAlign = TextAnchor.MiddleLeft;
            }
            var mark = t.Q(className: Toggle.checkmarkUssClassName);
            if (mark != null)
            {
                float box = Mathf.Round(FontSize * 1.15f);
                mark.style.width = box; mark.style.height = box;
                mark.style.backgroundColor = C("fondo");
                mark.style.unityBackgroundImageTintColor = C("acento");
                mark.Border(1, "borde", 3);
            }
            if (onChange != null) t.RegisterValueChangedCallback(e => onChange(e.newValue));
            return t;
        }

        public static Slider SliderBox(string label, float value, float min, float max, Action<float> onChange)
        {
            var s = new Slider(label, min, max) { value = value };
            StyleSlider(s);
            var l = s.Q<Label>();
            if (l != null) { l.style.color = C("texto_suave"); l.style.fontSize = FontSize; l.style.minWidth = 90; }
            if (onChange != null) s.RegisterValueChangedCallback(e => onChange(e.newValue));
            return s;
        }

        /// <summary>Theme colours for a slider; it keeps its width in a row.</summary>
        public static void StyleSlider(Slider s)
        {
            s.style.marginLeft = 0; s.style.marginRight = 0; s.style.marginTop = 0; s.style.marginBottom = 0;
            s.style.flexShrink = 0;
            var tracker = s.Q(className: "unity-base-slider__tracker");
            if (tracker != null) { tracker.style.backgroundColor = C("fondo"); tracker.Border(1, "borde", 2); }
            var dragger = s.Q(className: "unity-base-slider__dragger");
            if (dragger != null) { dragger.style.backgroundColor = C("acento"); dragger.Border(0, "acento", 3); }
        }

        /// <summary>
        /// A slider of our own: drag anywhere on it (or click) and it follows the pointer; it shows its value
        /// («75 %»). onChange is called while dragging; dragging(true/false) tells when a drag starts and ends.
        /// </summary>
        public static VisualElement Range(float value, float min, float max, Action<float> onChange, string tooltip = null,
            Func<float, string> format = null, float width = 72, Action<bool> dragging = null)
        {
            format ??= v => Mathf.RoundToInt((v - min) / (max - min) * 100) + " %";
            var root = Row(6).NoShrink();
            root.tooltip = tooltip ?? "";
            var hit = new VisualElement();
            hit.style.width = width;
            hit.style.height = ControlHeight - 6;
            hit.style.justifyContent = Justify.Center;
            var track = new VisualElement { pickingMode = PickingMode.Ignore }.Bg("fondo").Border(1, "borde", 3);
            track.style.height = 6;
            var fill = new VisualElement { pickingMode = PickingMode.Ignore }.Bg("acento").Round(3);
            fill.style.position = Position.Absolute;
            fill.style.left = 0; fill.style.top = 0; fill.style.bottom = 0;
            track.Add(fill);
            float knobSize = Mathf.Round(FontSize * 0.95f);
            var knob = new VisualElement { pickingMode = PickingMode.Ignore }.Bg("texto").Round(knobSize / 2);
            knob.style.position = Position.Absolute;
            knob.style.width = knobSize; knob.style.height = knobSize;
            knob.style.top = (ControlHeight - 6 - knobSize) / 2f;
            knob.Border(2, "acento", knobSize / 2);
            hit.Add(track);
            hit.Add(knob);
            var label = Text(format(value), 0.85f, dim: true).NoShrink();
            label.style.minWidth = FontSize * 2.6f;
            label.style.unityTextAlign = TextAnchor.MiddleRight;
            root.Add(hit);
            root.Add(label);

            float current = value;
            void Draw()
            {
                float t = Mathf.InverseLerp(min, max, current);
                fill.style.width = Length.Percent(t * 100);
                knob.style.left = t * (width - knobSize);
                label.text = format(current);
            }
            void SetFrom(Vector2 local)
            {
                float t = Mathf.Clamp01((local.x - knobSize / 2) / Mathf.Max(1, width - knobSize));
                float v = Mathf.Lerp(min, max, t);
                if (Mathf.Approximately(v, current)) return;
                current = v;
                Draw();
                onChange?.Invoke(v);
            }
            hit.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                hit.CapturePointer(e.pointerId);
                dragging?.Invoke(true);
                SetFrom(e.localPosition);
                e.StopPropagation();
            });
            hit.RegisterCallback<PointerMoveEvent>(e => { if (hit.HasPointerCapture(e.pointerId)) SetFrom(e.localPosition); });
            hit.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!hit.HasPointerCapture(e.pointerId)) return;
                hit.ReleasePointer(e.pointerId);
                dragging?.Invoke(false);
                e.StopPropagation();
            });
            Draw();
            return root;
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
            // A thin, discreet bar: no arrows, no track, a rounded «pill» that lights up when touched.
            if (scroller == null) return;
            bool vertical = scroller.direction == SliderDirection.Vertical;
            scroller.style.backgroundColor = new Color(0, 0, 0, 0);
            scroller.style.borderLeftWidth = 0; scroller.style.borderRightWidth = 0; scroller.style.borderTopWidth = 0; scroller.style.borderBottomWidth = 0;
            if (vertical) { scroller.style.width = 10; scroller.style.minWidth = 10; }
            else { scroller.style.height = 10; scroller.style.minHeight = 10; }
            scroller.lowButton.style.display = DisplayStyle.None;
            scroller.highButton.style.display = DisplayStyle.None;
            scroller.slider.style.marginLeft = 0; scroller.slider.style.marginRight = 0; scroller.slider.style.marginTop = 0; scroller.slider.style.marginBottom = 0;
            var tracker = scroller.slider.Q(className: "unity-base-slider__tracker");
            if (tracker != null) { tracker.style.backgroundColor = new Color(0, 0, 0, 0); tracker.Border(0); }
            var dragger = scroller.slider.Q(className: "unity-base-slider__dragger");
            if (dragger != null)
            {
                var normal = Mix(C("borde"), C("texto"), 0.15f);
                dragger.style.backgroundColor = normal;
                dragger.Border(0);
                dragger.Round(4);
                if (vertical) { dragger.style.width = 6; dragger.style.left = 2; dragger.style.marginLeft = 0; }
                else { dragger.style.height = 6; dragger.style.top = 2; dragger.style.marginTop = 0; }
                dragger.RegisterCallback<PointerEnterEvent>(_ => dragger.style.backgroundColor = C("texto_suave"));
                dragger.RegisterCallback<PointerLeaveEvent>(_ => dragger.style.backgroundColor = normal);
            }
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
