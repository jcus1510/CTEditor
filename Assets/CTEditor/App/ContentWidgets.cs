using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using CTEditor.Content;

namespace CTEditor.App
{
    /// <summary>
    /// The pieces the data windows are built with: foldable sections (remembered), text boxes that grow downwards,
    /// dropdowns, type chips in their colour, move tables (type, category, power), EV/IV sliders and stat-number pairs.
    /// Nothing asks the author to type a code: everything is chosen from lists, numbers or sliders.
    /// </summary>
    public static class ContentWidgets
    {
        /// <summary>A section with a title that folds and unfolds (its state is remembered per window).</summary>
        public static VisualElement Section(AppShell shell, string key, string title, VisualElement content, bool open = true, string right = null)
        {
            var box = Ui.Column(4);
            string pref = "plegado_" + key;
            bool isOpen = shell.Workspace.Pref(pref, open ? "abierto" : "cerrado") == "abierto";
            var head = Ui.Row(6).Pad(2, 3).Round(4);
            head.style.alignItems = Align.Center;
            var arrow = Ui.Text(isOpen ? "▾" : "▸", 1f, bold: true);
            arrow.style.width = 14;
            var label = Ui.Text(title.ToUpperInvariant(), 0.82f, bold: true).Colored("acento");
            head.With(arrow, label);
            if (right != null) head.Add(Ui.Text(right, 0.82f, dim: true));
            head.RegisterCallback<PointerEnterEvent>(_ => head.style.backgroundColor = Ui.C("panel_alt"));
            head.RegisterCallback<PointerLeaveEvent>(_ => head.style.backgroundColor = new Color(0, 0, 0, 0));
            head.tooltip = "Clic: plegar o desplegar";
            content.style.marginLeft = 4;
            content.Show(isOpen);
            head.RegisterCallback<ClickEvent>(_ =>
            {
                isOpen = !isOpen;
                content.Show(isOpen);
                arrow.text = isOpen ? "▾" : "▸";
                shell.Workspace.SetPref(pref, isOpen ? "abierto" : "cerrado");
                shell.SaveWorkspaceSoon();
            });
            box.With(head, content);
            return box;
        }

        /// <summary>A text box whose text wraps: it grows downwards when the text does not fit (never out of the window). Enter = done.</summary>
        public static TextField WrapBox(string value, Action<string> onChange, bool multiLine = false)
        {
            var t = Ui.TextBox("", value, onChange, delayed: true);
            t.multiline = true;
            t.style.whiteSpace = WhiteSpace.Normal;
            t.style.flexShrink = 1;
            t.style.minWidth = 40;
            var input = t.Q(className: TextField.inputUssClassName);
            if (input != null) { input.style.whiteSpace = WhiteSpace.Normal; input.style.flexShrink = 1; }
            foreach (var te in t.Query<TextElement>().ToList()) te.style.whiteSpace = WhiteSpace.Normal;
            if (!multiLine)
                t.RegisterCallback<KeyDownEvent>(e =>
                {
                    if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter && e.character != '\n') return;
                    e.StopImmediatePropagation();
                    t.Blur();
                }, TrickleDown.TrickleDown);
            return t;
        }

        /// <summary>A number box (narrow).</summary>
        public static TextField NumberBox(string value, Action<string> onChange, float width = 90)
        {
            var t = Ui.TextBox("", value, onChange, delayed: true);
            t.style.width = width;
            t.style.flexGrow = 0;
            return t;
        }

        /// <summary>A dropdown: shows the current choice (and a small text) and opens a menu or a list.</summary>
        public static Button Dropdown(AppShell shell, string text, Func<IList<MenuItem>> items, string small = null, string tooltip = null)
        {
            var b = Ui.Button("", null, Ui.ButtonKind.Normal, tooltip);
            b.style.flexDirection = FlexDirection.Row;
            b.style.justifyContent = Justify.SpaceBetween;
            b.style.minWidth = 140;
            var main = Ui.Text(string.IsNullOrEmpty(text) ? "—" : text);
            var right = Ui.Row(6);
            if (!string.IsNullOrEmpty(small)) right.Add(Ui.Text(small, 0.78f, dim: true));
            right.Add(Ui.Text("▾", 0.9f, dim: true));
            b.With(main, right);
            b.clicked += () =>
            {
                var r = b.worldBound;
                shell.ShowMenu(new Vector2(r.x, r.yMax + 2), items());
            };
            return b;
        }

        /// <summary>A button that looks like a dropdown and opens the searchable list with filters.</summary>
        public static Button RefDropdown(AppShell shell, string category, string current, Action<string> picked, string none = "Elegir…",
            IReadOnlyCollection<string> only = null, string onlyLabel = null)
        {
            var content = shell.Content;
            var t = content.Db.Table(category);
            var r = t.Find(current);
            string text = r != null ? t.NameOf(r) : string.IsNullOrWhiteSpace(current) ? none : current + " (no existe)";
            var b = Dropdown(shell, text, () => new List<MenuItem>(), r == null ? null : ContentLook.Subtitle(content.Db, category, r));
            // A list with search and filters instead of a menu (they can be long).
            b.clickable = new Clickable(() => ContentPicker.Show(shell, category, picked, only: only, onlyLabel: onlyLabel));
            if (r == null && !string.IsNullOrWhiteSpace(current)) b.Border(1, "error", 4);
            return b;
        }

        /// <summary>A type shown in its colour (always), name readable on top.</summary>
        public static VisualElement TypeChip(ContentDatabase db, string typeId, bool selected = false, Action onClick = null)
        {
            var t = db.Table(ContentSchemas.Types).Find(typeId);
            var c = ContentLook.TypeColor(db, typeId) ?? Color.gray;
            var chip = ContentLook.Badge(t != null ? t["nombre"] : typeId, c);
            chip.style.opacity = selected || onClick == null ? 1f : 0.62f;
            if (selected) { chip.style.borderTopWidth = chip.style.borderBottomWidth = chip.style.borderLeftWidth = chip.style.borderRightWidth = 2; chip.style.borderTopColor = chip.style.borderBottomColor = chip.style.borderLeftColor = chip.style.borderRightColor = Color.white; }
            if (onClick != null) chip.RegisterCallback<ClickEvent>(_ => onClick());
            chip.style.marginRight = 3;
            chip.style.marginBottom = 3;
            return chip;
        }

        /// <summary>A move as a table row: name (clic = open), type in its colour, category and power.</summary>
        public static VisualElement MoveInfo(AppShell shell, string moveId)
        {
            var db = shell.Content.Db;
            var row = Ui.Row(6);
            row.style.alignItems = Align.Center;
            var moves = db.Table(ContentSchemas.Moves);
            var m = moves.Find(moveId);
            var name = Ui.Text(m != null ? moves.NameOf(m) : moveId + " (no existe)", 0.92f);
            name.style.width = 150;
            name.style.overflow = Overflow.Hidden;
            name.style.textOverflow = TextOverflow.Ellipsis;
            if (m == null) name.Colored("error");
            else name.RegisterCallback<ClickEvent>(_ => shell.OpenContentItem(ContentSchemas.Moves, moveId));
            name.tooltip = moveId;
            row.Add(name);
            if (m != null)
            {
                var type = TypeChip(db, m["tipo"].Trim());
                type.style.width = 74;
                row.Add(type);
                var cat = Ui.Text(CategoryLabel(m["categoria"]), 0.82f, dim: true); cat.style.width = 60;
                var pow = Ui.Text(m["potencia"].Trim() == "" || m["potencia"].Trim() == "0" ? "—" : m["potencia"], 0.85f); pow.style.width = 34;
                var acc = Ui.Text(m["precision"].Trim().Length == 0 ? "" : m["precision"] + (int.TryParse(m["precision"], out _) ? "%" : ""), 0.82f, dim: true); acc.style.width = 44;
                row.With(cat, pow, acc);
            }
            return row;
        }

        public static string CategoryLabel(string c) => (c ?? "").Trim().ToLowerInvariant() switch
        {
            "fisico" => "Físico",
            "especial" => "Especial",
            "estado" => "Estado",
            var x => x,
        };

        /// <summary>A list of moves as a table with × and «+ Añadir» (MT, tutor, egg...).</summary>
        public static VisualElement MoveTable(AppShell shell, IList<string> moves, Action<List<string>> save, Func<string, VisualElement> extra = null, int max = 0)
        {
            var box = Ui.Column(2);
            for (int i = 0; i < moves.Count; i++)
            {
                int idx = i;
                var row = MoveInfo(shell, moves[i]);
                row.Add(Ui.Spacer());
                if (extra != null) row.Add(extra(moves[i]));
                row.Add(Ui.IconButton("cerrar", () => { var l = moves.ToList(); l.RemoveAt(idx); save(l); }, "Quitar", iconSize: Ui.IconSize * 0.7f));
                box.Add(row);
            }
            if (max == 0 || moves.Count < max)
                box.Add(Ui.Button("+ Añadir movimiento", () => ContentPicker.Show(shell, ContentSchemas.Moves, p =>
                {
                    if (moves.Contains(p)) return;
                    var l = moves.ToList(); l.Add(p); save(l);
                })));
            return box;
        }

        /// <summary>EVs or IVs with a slider per stat (and the total for EVs).</summary>
        public static VisualElement SpreadEditor(string value, bool ivs, Action<string> save)
        {
            int max = ivs ? 31 : 252;
            var v = StatNames.ParseSpread(value, ivs ? 31 : 0);
            var box = Ui.Column(2);
            var total = Ui.Text("", 0.85f, dim: true);
            void UpdateTotal() => total.text = ivs ? "" : $"Total: {v.Sum()} / 510" + (v.Sum() > 510 ? "  (¡se pasa!)" : "");
            for (int i = 0; i < 6; i++)
            {
                int k = i;
                var row = Ui.Row(6);
                row.style.alignItems = Align.Center;
                var label = Ui.Text(StatNames.Labels[i], 0.88f).NoShrink(); label.style.width = 76;
                var num = Ui.Text(v[i].ToString(), 0.88f, bold: true); num.style.width = 30;
                var slider = new Slider(0, max) { value = v[i] };
                slider.style.flexGrow = 1;
                slider.RegisterValueChangedCallback(e =>
                {
                    v[k] = Mathf.RoundToInt(e.newValue);
                    if (!ivs && v[k] > 248 && v[k] < 252) v[k] = 252;
                    num.text = v[k].ToString();
                    UpdateTotal();
                });
                // Saved when the slider is released (not on every pixel).
                slider.RegisterCallback<PointerCaptureOutEvent>(_ => save(StatNames.FormatSpread(v, ivs ? 31 : 0)));
                row.With(label, slider, num);
                box.Add(row);
            }
            UpdateTotal();
            box.Add(total);
            return box;
        }

        /// <summary>«stat: number» rows with a dropdown of the six stats (EVs a species gives...).</summary>
        public static VisualElement StatPairs(AppShell shell, string value, Action<string> save, int max = 3)
        {
            var pairs = StatNames.ParsePairs(value);
            var box = Ui.Column(3);
            for (int i = 0; i < pairs.Count; i++)
            {
                int idx = i;
                var row = Ui.Row(6);
                row.style.alignItems = Align.Center;
                row.Add(Dropdown(shell, StatNames.Label(pairs[i].stat), () => StatNames.Ids.Select((id, k) => new MenuItem(StatNames.Labels[k], () =>
                {
                    pairs[idx] = (id, pairs[idx].amount);
                    save(StatNames.FormatPairs(pairs));
                })).ToList()));
                row.Add(Ui.MiniNumber(pairs[i].amount, 0, max, n => { pairs[idx] = (pairs[idx].stat, n); save(StatNames.FormatPairs(pairs)); }, "Cantidad", 48));
                row.Add(Ui.IconButton("cerrar", () => { pairs.RemoveAt(idx); save(StatNames.FormatPairs(pairs)); }, "Quitar", iconSize: Ui.IconSize * 0.7f));
                box.Add(row);
            }
            if (pairs.Count < 6)
                box.Add(Ui.Button("+ Añadir", () =>
                {
                    var free = StatNames.Ids.FirstOrDefault(id => pairs.All(p => p.stat != id)) ?? "hp";
                    pairs.Add((free, 1));
                    save(StatNames.FormatPairs(pairs));
                }));
            return box;
        }
    }

    /// <summary>
    /// TEMPLATES for «Nuevo»: the rows of the same category in every pack (Gen1…Gen7; the newest wins), plus the classic
    /// curves. Read once per category.
    /// </summary>
    public static class ContentTemplates
    {
        private static readonly Dictionary<string, ContentTable> Cache = new Dictionary<string, ContentTable>();

        public static ContentTable For(string category)
        {
            if (Cache.TryGetValue(category, out var cached)) return cached;
            var schema = ContentSchemas.Find(category);
            var all = new ContentTable(schema);
            try
            {
                foreach (var pack in CTEditor.Project.PackInstaller.Find(AppShell.PacksRoot))
                {
                    var path = Path.Combine(pack, schema.File);
                    if (!File.Exists(path)) continue;
                    var t = ContentTable.Parse(schema, File.ReadAllText(path), null);
                    foreach (var c in t.Columns) all.EnsureColumn(c);
                    foreach (var r in t.Records)
                    {
                        var old = all.Find(t.IdOf(r));
                        if (old != null) all.Records.Remove(old);
                        all.Records.Add(r);
                    }
                }
            }
            catch (Exception) { /* no packs: no templates */ }
            if (category == ContentSchemas.Curves)
                foreach (var c in ClassicCurves.All) if (!all.Contains(c.id)) all.Records.Add(ClassicCurves.Row(c.id));
            return Cache[category] = all;
        }
    }
}
