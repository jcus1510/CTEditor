using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using CTEditor.Content;
using CTEditor.Workspace;

namespace CTEditor.App
{
    /// <summary>
    /// The editor of one category of content (Especies, Movimientos, Objetos...), with the same layout as the Unity
    /// editors: the actions in the window's bar (new, duplicate, delete, rename, who uses it), the list on the left (search,
    /// filters and a colour mark per row) and the chosen piece on the right, in sections, with its preview (stat bars,
    /// types...) and its uses at the bottom. Every change is safe and undoable; «How to use» is in the «i».
    /// </summary>
    public sealed class ContentPanel : VisualElement
    {
        public const string Prefix = "c_";
        public static string PanelId(string category) => Prefix + category;

        /// <summary>Piece to show when the window opens (from Problems or «go to»).</summary>
        public static readonly Dictionary<string, string> Pending = new Dictionary<string, string>();

        private readonly AppShell _shell;
        private readonly string _category;
        private readonly VisualElement _toolbar, _left;
        private readonly ScrollView _right;
        private readonly TextField _search;
        private readonly Label _count;
        private readonly VisualElement _filters;
        private ListView _list;
        private List<ContentRecord> _shown = new List<ContentRecord>();
        private string _selected;
        private string _typeFilter;
        private int _sort; // species: 0 Pokédex, 1 name, 2 total

        private ContentSession C => _shell.Content;
        private ContentTable Table => C.Db.Table(_category);
        private CategorySchema Schema => ContentSchemas.Find(_category);

        public ContentPanel(AppShell shell, string category)
        {
            _shell = shell;
            _category = category;
            style.flexGrow = 1;
            _toolbar = Ui.Row(2).Pad(6, 4);
            _toolbar.style.alignItems = Align.Center;
            Add(_toolbar);
            Add(Ui.Separator());
            var body = Ui.Row(0).Grow();
            body.style.alignItems = Align.Stretch;
            _left = Ui.Column(6).Pad(6);
            _left.style.width = 250;
            _left.style.flexShrink = 0;
            _right = Ui.Scroll().Grow();
            body.With(_left, Ui.Separator(vertical: true), _right);
            Add(body);

            _search = Ui.TextBox("", "");
            _search.tooltip = "Buscar por nombre, id o número";
            _search.RegisterValueChangedCallback(_ => Refill());
            _count = Ui.Text("", 0.82f, dim: true);
            _filters = Ui.Column(4);
            _left.With(_search, _filters, _count);
            BuildList();
            RegisterCallback<PointerDownEvent>(_ => _shell.ActiveEditor = "contenido", TrickleDown.TrickleDown);
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                _shell.ContentChanged += OnChanged;
                _shell.ContentFocusRequested += OnFocus;
                if (Pending.TryGetValue(_category, out var id)) { Pending.Remove(_category); _selected = id; }
                RebuildAll();
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                _shell.ContentChanged -= OnChanged;
                _shell.ContentFocusRequested -= OnFocus;
            });
        }

        private void OnFocus(string category, string id)
        {
            if (category != _category) return;
            Pending.Remove(category);
            Select(id);
        }

        private void OnChanged(string category)
        {
            // Our sheet, or one we show things from (names of types, moves...): redraw soon, once.
            schedule.Execute(() => { if (panel != null) RebuildAll(keepScroll: true); }).ExecuteLater(10);
        }

        // ── Layout ───────────────────────────────────────────────────────────────────────────────

        private void RebuildAll(bool keepScroll = false)
        {
            if (C == null) return;
            BuildToolbar();
            BuildFilters();
            Refill();
            float scroll = _right.scrollOffset.y;
            ShowSelected();
            if (keepScroll) _right.schedule.Execute(() => _right.scrollOffset = new Vector2(0, scroll));
        }

        private void BuildToolbar()
        {
            _toolbar.Clear();
            var s = Schema;
            bool has = Selected != null;
            _toolbar.With(
                Ui.IconButton("mas", NewItem, $"{s.Nuevo} {s.Noun}"),
                Ui.IconButton("copiar", () => { var r = C.Duplicate(_category, _selected); if (r != null) Select(Table.IdOf(r)); }, "Duplicar la elegida").SetEnabledLook(has),
                Ui.IconButton("propiedades", RenameDialog, "Cambiar el id (se cambia en todo el proyecto)").SetEnabledLook(has),
                Ui.IconButton("vecinos", ShowUses, "¿Quién lo usa?").SetEnabledLook(has),
                Ui.IconButton("papelera", DeleteDialog, "Borrar (dice quién lo usa y deja sustituirlo)").SetEnabledLook(has),
                Ui.Separator(vertical: true).Margin(6, 4, 6, 4),
                Ui.IconButton("anterior", () => { C.Undo(); }, "Deshacer (Ctrl+Z)"),
                Ui.IconButton("siguiente", () => { C.Redo(); }, "Rehacer (Ctrl+Y)"),
                Ui.Spacer(),
                Ui.Text($"{Table.Records.Count} {s.Title.ToLowerInvariant()}", 0.85f, dim: true));
        }

        private void BuildFilters()
        {
            _filters.Clear();
            // By type (species and moves), as the Unity editors.
            if (_category == ContentSchemas.Species || _category == ContentSchemas.Moves)
            {
                var row = Ui.Row(3).Wrap();
                row.Add(Ui.Chip("Todos", _typeFilter == null, () => { _typeFilter = null; BuildFilters(); Refill(); }));
                foreach (var t in C.Db.Table(ContentSchemas.Types).Records)
                {
                    var id = C.Db.Table(ContentSchemas.Types).IdOf(t);
                    var chip = Ui.Chip(t["nombre"], _typeFilter == id, () => { _typeFilter = _typeFilter == id ? null : id; BuildFilters(); Refill(); });
                    chip.style.borderLeftWidth = 4;
                    chip.style.borderLeftColor = ContentLook.Hex(t["color"]) ?? Ui.C("borde");
                    chip.style.fontSize = Mathf.Round(Ui.FontSize * 0.8f);
                    row.Add(chip.Margin(0, 0, 2, 2));
                }
                _filters.Add(row);
            }
            if (_category == ContentSchemas.Species)
            {
                var sort = Ui.Row(3);
                string[] names = { "N.º", "Nombre", "Total" };
                for (int i = 0; i < names.Length; i++)
                {
                    int k = i;
                    sort.Add(Ui.Chip(names[i], _sort == k, () => { _sort = k; BuildFilters(); Refill(); }, "Ordenar por " + names[k].ToLowerInvariant()));
                }
                _filters.Add(sort);
            }
        }

        private void BuildList()
        {
            _list = new ListView
            {
                fixedItemHeight = Ui.ControlHeight + 4,
                selectionType = SelectionType.Single,
                makeItem = () =>
                {
                    var row = Ui.Row(6).Pad(6, 0);
                    row.style.height = Length.Percent(100);
                    row.style.alignItems = Align.Center;
                    var mark = new VisualElement { name = "mark" }.Round(2);
                    mark.style.width = 5; mark.style.height = Ui.FontSize + 4;
                    var name = Ui.Text("").Grow(); name.name = "name";
                    name.style.overflow = Overflow.Hidden;
                    name.style.textOverflow = TextOverflow.Ellipsis;
                    var warn = Icons.Element("aviso", Ui.IconSize * 0.8f, Ui.C("error")); warn.name = "warn";
                    return row.With(mark, name, warn);
                },
            };
            _list.bindItem = (e, i) =>
            {
                if (i >= _shown.Count) return;
                var r = _shown[i];
                var label = e.Q<Label>("name");
                label.text = ContentLook.Label(Table, r);
                e.tooltip = Table.IdOf(r);
                e.Q("mark").style.backgroundColor = ContentLook.Mark(C.Db, Table, r) ?? new Color(0, 0, 0, 0);
                e.Q("warn").Show(_withErrors.Contains(Table.IdOf(r)));
            };
            _list.style.flexGrow = 1;
#if UNITY_2022_2_OR_NEWER
            _list.selectionChanged += items => { foreach (var it in items) { Select(Table.IdOf((ContentRecord)it), fromList: true); return; } };
#else
            _list.onSelectionChange += items => { foreach (var it in items) { Select(Table.IdOf((ContentRecord)it), fromList: true); return; } };
#endif
            _left.Add(_list);
        }

        private HashSet<string> _withErrors = new HashSet<string>();

        private void Refill()
        {
            if (C == null) return;
            var t = Table;
            var q = (_search.value ?? "").Trim();
            IEnumerable<ContentRecord> rows = t.Records;
            if (q.Length > 0) rows = rows.Where(r => ContentLook.Matches(t, r, q));
            if (_typeFilter != null)
                rows = _category == ContentSchemas.Species
                    ? rows.Where(r => r["tipos"].Split('|', ',').Any(x => x.Trim() == _typeFilter))
                    : rows.Where(r => r["tipo"].Trim() == _typeFilter);
            if (_category == ContentSchemas.Species)
                rows = _sort == 1 ? rows.OrderBy(r => t.NameOf(r), StringComparer.CurrentCultureIgnoreCase)
                    : _sort == 2 ? rows.OrderByDescending(Total)
                    : rows.OrderBy(r => int.TryParse(r["numero"], out var n) && n > 0 ? n : int.MaxValue);
            _shown = rows.ToList();
            _withErrors = new HashSet<string>(ContentChecks.Check(C.Db, t).Where(i => i.Level == ContentIssueLevel.Error && i.Id != null).Select(i => i.Id), StringComparer.OrdinalIgnoreCase);
            _list.itemsSource = _shown;
            _list.Rebuild();
            _count.text = _shown.Count == t.Records.Count ? $"{t.Records.Count} en total" : $"{_shown.Count} de {t.Records.Count}";
            int index = _shown.FindIndex(r => string.Equals(t.IdOf(r), _selected, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) _list.SetSelectionWithoutNotify(new[] { index });
        }

        private static readonly string[] StatColumns = { "ps", "ataque", "defensa", "atq_esp", "def_esp", "velocidad" };
        private static int Total(ContentRecord r) => StatColumns.Sum(c => int.TryParse(r[c], out var v) ? v : 0);

        private ContentRecord Selected => _selected == null ? null : Table.Find(_selected);

        private void Select(string id, bool fromList = false)
        {
            _selected = id;
            _shell.ActiveEditor = "contenido";
            BuildToolbar();
            if (!fromList)
            {
                int index = _shown.FindIndex(r => string.Equals(Table.IdOf(r), id, StringComparison.OrdinalIgnoreCase));
                if (index < 0) { _search.SetValueWithoutNotify(""); _typeFilter = null; Refill(); index = _shown.FindIndex(r => string.Equals(Table.IdOf(r), id, StringComparison.OrdinalIgnoreCase)); }
                if (index >= 0) { _list.SetSelectionWithoutNotify(new[] { index }); _list.ScrollToItem(index); }
            }
            ShowSelected();
        }

        // ── The chosen piece ─────────────────────────────────────────────────────────────────────

        private void ShowSelected()
        {
            _right.Clear();
            var r = Selected;
            var s = Schema;
            if (r == null)
            {
                _right.Add(Ui.EmptyState(s.Icon, Table.Records.Count == 0 ? $"Sin {s.Title.ToLowerInvariant()}" : $"Elige {s.Un} {s.Noun}",
                    Table.Records.Count == 0 ? $"El proyecto no tiene {s.Title.ToLowerInvariant()} ({s.File}). Crea {s.El} primer{(s.Feminine ? "a" : "o")} o instala un pack."
                        : "Elígela en la lista de la izquierda, o crea una nueva.",
                    $"{s.Nuevo} {s.Noun}", NewItem));
                return;
            }
            var box = Ui.Column(10).Pad(14, 10);
            _right.Add(box);
            var id = Table.IdOf(r);

            // Header: name, id and the badges of its kind.
            var head = Ui.Row(10);
            head.style.alignItems = Align.Center;
            head.Add(Ui.Title(Table.NameOf(r)));
            head.Add(Ui.Text(id, 0.9f, dim: true));
            foreach (var b in Badges(r)) head.Add(b);
            box.Add(head);

            // Problems of this piece (from the checks), each with its column.
            var issues = ContentChecks.Check(C.Db, Table).Where(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var i in issues)
                box.Add(Ui.Hint((i.Level == ContentIssueLevel.Error ? "✖ " : "⚠ ") + i.Text).Colored(i.Level == ContentIssueLevel.Error ? "error" : "aviso"));

            Preview(box, r);

            // The form, in the sections of the schema; the columns it does not know go to «Otros».
            var known = s.Columns.Where(c => c.Name != s.IdColumn).ToList();
            foreach (var group in known.GroupBy(c => c.Group))
            {
                if (_category == ContentSchemas.Species && group.Key == "Estadísticas base") continue; // in the preview, as bars
                box.Add(Ui.SectionTitle(group.Key));
                var grid = Ui.Column(6);
                foreach (var col in group) grid.Add(Field(r, col));
                box.Add(grid);
            }
            var others = Table.Columns.Where(c => s.Column(c) == null).ToList();
            if (others.Count > 0)
            {
                box.Add(Ui.SectionTitle("Otros (columnas propias)"));
                foreach (var c in others) box.Add(Field(r, new ColumnSpec(c, c)));
            }

            // Who uses it.
            var uses = C.UsesOf(_category, id);
            box.Add(Ui.SectionTitle($"Lo usan ({uses.Count})"));
            if (uses.Count == 0) box.Add(Ui.Hint("Nadie lo usa: se puede borrar sin problemas."));
            foreach (var u in uses.Take(60)) box.Add(UseRow(u));
            if (uses.Count > 60) box.Add(Ui.Hint($"… y {uses.Count - 60} más."));
        }

        private IEnumerable<VisualElement> Badges(ContentRecord r)
        {
            if (_category == ContentSchemas.Species)
                foreach (var t in r["tipos"].Split('|', ',').Select(x => x.Trim()).Where(x => x.Length > 0))
                    yield return ContentLook.Badge(TypeName(t), ContentLook.TypeColor(C.Db, t));
            if (_category == ContentSchemas.Moves && r["tipo"].Length > 0)
            {
                yield return ContentLook.Badge(TypeName(r["tipo"]), ContentLook.TypeColor(C.Db, r["tipo"]));
                if (r["categoria"].Length > 0) yield return ContentLook.Badge(r["categoria"], null);
            }
            if (Table.Columns.Contains("color") && ContentLook.Hex(r["color"]) is Color c) yield return Ui.Swatch(c, Ui.FontSize + 2);
            if (ContentChecks.IsYes(r["legendario"])) yield return ContentLook.Badge("Legendario", new Color(0.85f, 0.7f, 0.2f));
        }

        private string TypeName(string id) => C.Db.Table(ContentSchemas.Types).Find(id)?["nombre"] is string n && n.Length > 0 ? n : id;

        private string NameOf(string category, string id)
        {
            var t = C.Db.Table(category);
            var r = t.Find(id);
            return r == null ? id : t.NameOf(r);
        }

        /// <summary>The preview under the header, like the Unity editors: stat bars and total, the type chart of a type...</summary>
        private void Preview(VisualElement box, ContentRecord r)
        {
            if (_category == ContentSchemas.Species)
            {
                box.Add(Ui.SectionTitle("Estadísticas base"));
                string[] labels = { "PS", "Ataque", "Defensa", "At. esp.", "Def. esp.", "Velocidad" };
                for (int i = 0; i < StatColumns.Length; i++)
                {
                    var col = StatColumns[i];
                    int v = int.TryParse(r[col], out var n) ? n : 0;
                    var row = Ui.Row(8);
                    row.style.alignItems = Align.Center;
                    var label = Ui.Text(labels[i], 0.9f).NoShrink(); label.style.width = 82;
                    var num = Ui.MiniNumber(v, 1, 255, x => C.SetValue(_category, _selected, col, x.ToString()), labels[i], 54);
                    var track = new VisualElement().Round(3).Grow();
                    track.style.height = 10;
                    track.style.backgroundColor = Ui.C("panel_alt");
                    var bar = new VisualElement().Round(3);
                    bar.style.height = Length.Percent(100);
                    bar.style.width = Length.Percent(Mathf.Clamp01(v / 255f) * 100);
                    // The usual colours: red (low) → yellow → green → cyan (very high).
                    bar.style.backgroundColor = v < 50 ? new Color(0.93f, 0.33f, 0.3f) : v < 80 ? new Color(0.96f, 0.6f, 0.25f)
                        : v < 100 ? new Color(0.95f, 0.85f, 0.3f) : v < 130 ? new Color(0.45f, 0.82f, 0.35f) : new Color(0.3f, 0.8f, 0.85f);
                    track.Add(bar);
                    row.With(label, num, track);
                    box.Add(row);
                }
                var total = Ui.Row(8);
                total.With(Ui.Text("Total", 0.9f, bold: true).NoShrink(), Ui.Text(Total(r).ToString(), 1f, bold: true));
                total.style.marginLeft = 0;
                box.Add(total);
            }
            else if (_category == ContentSchemas.Types)
            {
                // Its summary from the type chart: what it hits hard and what hits it hard.
                var chart = C.Db.Table(ContentSchemas.TypeChart);
                string id = Table.IdOf(r);
                var att = chart.Find(id);
                var strong = new List<string>(); var weak = new List<string>(); var none = new List<string>(); var hurtBy = new List<string>(); var resists = new List<string>(); var immune = new List<string>();
                foreach (var other in C.Db.Table(ContentSchemas.Types).Ids)
                {
                    if (att != null && ContentChecks.TryNumber(att[other], out var a)) { if (a > 1) strong.Add(other); else if (a == 0) none.Add(other); else if (a < 1) weak.Add(other); }
                    var row = chart.Find(other);
                    if (row != null && ContentChecks.TryNumber(row[id], out var d)) { if (d > 1) hurtBy.Add(other); else if (d == 0) immune.Add(other); else if (d < 1) resists.Add(other); }
                }
                box.Add(Ui.SectionTitle("Tabla de tipos"));
                void Line(string title, List<string> ids)
                {
                    var l = Ui.Row(4).Wrap();
                    l.Add(Ui.Text(title, 0.88f, dim: true).NoShrink().Margin(0, 0, 6, 0));
                    if (ids.Count == 0) l.Add(Ui.Text("—", 0.88f, dim: true));
                    foreach (var x in ids) l.Add(ContentLook.Badge(TypeName(x), ContentLook.TypeColor(C.Db, x)).Margin(0, 0, 2, 2));
                    box.Add(l);
                }
                Line("Muy eficaz contra", strong); Line("Poco eficaz contra", weak); Line("No afecta a", none);
                Line("Débil a", hurtBy); Line("Resiste", resists); Line("Inmune a", immune);
                box.Add(Ui.Button("Editar la tabla de tipos", () => _shell.OpenContentItem(ContentSchemas.TypeChart, id)));
            }
        }

        // ── Fields ───────────────────────────────────────────────────────────────────────────────

        private VisualElement Field(ContentRecord r, ColumnSpec col)
        {
            var row = Ui.Row(8);
            row.style.alignItems = Align.FlexStart;
            var label = Ui.Text(col.Label, 0.9f).NoShrink();
            label.style.width = 150;
            label.style.marginTop = 4;
            label.tooltip = string.IsNullOrEmpty(col.Help) ? col.Name : col.Help + $"  (columna «{col.Name}»)";
            if (col.Required) label.text += " *";
            row.Add(label);
            var value = r[col.Name];
            string cat = _category, id = _selected, name = col.Name;
            void Set(string v) => C.SetValue(cat, id, name, v);
            VisualElement editor;
            switch (col.Kind)
            {
                case ColumnKind.Bool:
                    editor = Ui.Check("", ContentChecks.IsYes(value), on => Set(on ? "si" : "no"));
                    break;
                case ColumnKind.Choice:
                {
                    var chips = Ui.Row(3).Wrap();
                    foreach (var o in col.Options)
                    {
                        var opt = o;
                        chips.Add(Ui.Chip(opt, string.Equals(value.Trim(), opt, StringComparison.OrdinalIgnoreCase), () => Set(opt)).Margin(0, 0, 2, 2));
                    }
                    editor = chips;
                    break;
                }
                case ColumnKind.Color:
                {
                    var c = Ui.Row(6);
                    var swatch = Ui.Swatch(ContentLook.Hex(value) ?? new Color(0, 0, 0, 0), Ui.ControlHeight - 4);
                    swatch.RegisterCallback<ClickEvent>(_ => ColorPicker.Show(_shell, col.Label, ContentLook.Hex(value) ?? Color.gray,
                        picked => Set(ColorUtility.ToHtmlStringRGB(picked))));
                    swatch.tooltip = "Clic: elegir el color";
                    c.With(swatch, Ui.TextBox("", value, Set, delayed: true).Grow());
                    editor = c;
                    break;
                }
                case ColumnKind.Ref:
                    editor = RefEditor(col, value, Set);
                    break;
                case ColumnKind.RefList:
                case ColumnKind.SlashRefs:
                    editor = RefListEditor(col, value, Set);
                    break;
                case ColumnKind.LevelRefs:
                    editor = LevelEditor(col, value, Set);
                    break;
                case ColumnKind.LongText:
                {
                    var t = Ui.TextBox("", value, Set, delayed: true);
                    t.multiline = true;
                    t.style.whiteSpace = WhiteSpace.Normal;
                    t.style.minHeight = Ui.ControlHeight * 2.2f;
                    editor = t;
                    break;
                }
                default:
                {
                    // Numbers, text, and the written formats (teams, evolutions, effects): a box, plus the ids it uses, clickable.
                    var col2 = Ui.Column(3);
                    col2.Add(Ui.TextBox("", value, Set, delayed: true));
                    if (col.IsReference && value.Length > 0)
                    {
                        var refs = Ui.Row(3).Wrap();
                        foreach (var (rc, rid, _) in RefFormat.Extract(col, value, C.Db.Has).Distinct().Take(40))
                            refs.Add(RefChip(rc, rid));
                        col2.Add(refs);
                    }
                    editor = col2;
                    break;
                }
            }
            editor.style.flexGrow = 1;
            row.Add(editor);
            return row;
        }

        /// <summary>A reference shown by its name: clic = open it; red when it does not exist.</summary>
        private VisualElement RefChip(string category, string id, Action remove = null)
        {
            bool exists = C.Db.Has(category, id);
            var chip = Ui.Row(2).Pad(6, 1).Round(4);
            chip.style.alignItems = Align.Center;
            chip.style.backgroundColor = category == ContentSchemas.Types ? (ContentLook.TypeColor(C.Db, id) ?? Ui.C("panel_alt")) : Ui.C("panel_alt");
            if (!exists) chip.Border(1, "error", 4);
            var label = Ui.Text(exists ? NameOf(category, id) : id + " (no existe)", 0.85f);
            if (category == ContentSchemas.Types && exists)
            {
                var c = ContentLook.TypeColor(C.Db, id) ?? Color.gray;
                label.style.color = (c.r * 0.299f + c.g * 0.587f + c.b * 0.114f) > 0.6f ? Color.black : Color.white;
            }
            chip.Add(label);
            chip.tooltip = exists ? $"{id} · clic: abrir" : "No existe: elige otro o créalo";
            if (exists) label.RegisterCallback<ClickEvent>(_ => _shell.OpenContentItem(category, id));
            if (remove != null) chip.Add(Ui.IconButton("cerrar", remove, "Quitar", iconSize: Ui.IconSize * 0.6f));
            chip.style.marginRight = 3;
            chip.style.marginBottom = 3;
            return chip;
        }

        private VisualElement RefEditor(ColumnSpec col, string value, Action<string> set)
        {
            var row = Ui.Row(4);
            row.style.alignItems = Align.Center;
            var target = col.Targets[0];
            if (value.Trim().Length > 0) row.Add(RefChip(target, value.Trim(), () => set("")));
            else row.Add(Ui.Text("—", 0.9f, dim: true));
            row.Add(Ui.Button(value.Trim().Length > 0 ? "Cambiar…" : "Elegir…", () => ContentPicker.Show(_shell, target, set)));
            return row;
        }

        private VisualElement RefListEditor(ColumnSpec col, string value, Action<string> set)
        {
            char sep = col.Kind == ColumnKind.SlashRefs ? '/' : value.Contains(',') && !value.Contains('|') ? ',' : '|';
            var items = value.Split(sep).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            var row = Ui.Row(0).Wrap();
            row.style.alignItems = Align.Center;
            foreach (var it in items)
            {
                var item = it;
                row.Add(RefChip(col.Targets[0], item, () => set(string.Join(sep.ToString(), items.Where(x => x != item)))));
            }
            row.Add(Ui.IconButton("mas", () => ContentPicker.Show(_shell, col.Targets[0], picked =>
            {
                if (!items.Contains(picked)) set(string.Join(sep.ToString(), items.Append(picked)));
            }), "Añadir"));
            return row;
        }

        /// <summary>Learnset: level and move per row, ordered by level, like the Unity table.</summary>
        private VisualElement LevelEditor(ColumnSpec col, string value, Action<string> set)
        {
            var entries = value.Split('|').Where(e => e.Trim().Length > 0).Select(e =>
            {
                int i = e.IndexOf(':');
                return (level: i > 0 && int.TryParse(e.Substring(0, i).Trim(), out var l) ? l : 1, move: (i >= 0 ? e.Substring(i + 1) : e).Trim());
            }).ToList();
            string Write(IEnumerable<(int level, string move)> list) => string.Join("|", list.OrderBy(x => x.level).Select(x => $"{x.level}:{x.move}"));
            var box = Ui.Column(2);
            for (int k = 0; k < entries.Count; k++)
            {
                int idx = k;
                var (level, move) = entries[k];
                var row = Ui.Row(6);
                row.style.alignItems = Align.Center;
                row.Add(Ui.MiniNumber(level, 0, 100, v =>
                {
                    var copy = entries.ToList();
                    copy[idx] = (v, copy[idx].move);
                    set(Write(copy));
                }, "Nivel (0 = al evolucionar)", 48));
                row.Add(RefChip(col.Targets[0], move));
                var mv = C.Db.Table(ContentSchemas.Moves).Find(move);
                if (mv != null)
                    row.Add(ContentLook.Badge(TypeName(mv["tipo"]), ContentLook.TypeColor(C.Db, mv["tipo"])).Margin(0, 0, 0, 3));
                row.Add(Ui.Spacer());
                row.Add(Ui.IconButton("cerrar", () => set(Write(entries.Where((_, j) => j != idx))), "Quitar", iconSize: Ui.IconSize * 0.7f));
                box.Add(row);
            }
            box.Add(Ui.Button("+ Añadir movimiento", () => ContentPicker.Show(_shell, col.Targets[0], picked =>
                set(Write(entries.Append((entries.Count > 0 ? entries.Max(e => e.level) : 1, picked)))))));
            return box;
        }

        private VisualElement UseRow(ContentRef u)
        {
            var row = Ui.Row(6).Pad(6, 2).Round(4);
            row.style.alignItems = Align.Center;
            row.Add(Icons.Element(u.External ? "mapa" : ContentSchemas.Find(u.OwnerCategory)?.Icon ?? "base", Ui.IconSize * 0.85f, Ui.C("texto_suave")));
            row.Add(Ui.Text(u.Where, 0.9f).Grow());
            row.RegisterCallback<PointerEnterEvent>(_ => row.style.backgroundColor = Ui.C("panel_alt"));
            row.RegisterCallback<PointerLeaveEvent>(_ => row.style.backgroundColor = new Color(0, 0, 0, 0));
            row.RegisterCallback<ClickEvent>(_ =>
            {
                if (u.External) { if (_shell.Maps?.Map?.Id != u.OwnerId) _shell.Maps?.OpenMap(u.OwnerId); _shell.ActiveEditor = "mapa"; }
                else _shell.OpenContentItem(u.OwnerCategory, u.OwnerId);
            });
            row.tooltip = "Clic: ir";
            return row;
        }

        // ── Actions ──────────────────────────────────────────────────────────────────────────────

        private void NewItem()
        {
            var s = Schema;
            var d = _shell.ShowDialog($"{s.Nuevo} {s.Noun}");
            string name = "";
            d.Body.Add(Ui.Hint($"El id se hace del nombre (sin tildes ni espacios); se puede cambiar luego en todo el proyecto."));
            var box = Ui.TextBox("Nombre", "", v => name = v);
            d.Body.Add(box);
            void Create()
            {
                if (string.IsNullOrWhiteSpace(name)) { _shell.Warn("Escribe un nombre."); return; }
                _shell.CloseDialog(d);
                var r = C.Create(_category, name.Trim());
                if (r != null) Select(Table.IdOf(r));
            }
            box.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { name = box.value; Create(); } });
            d.Buttons.With(Ui.Spacer(), Ui.Button("Cancelar", () => _shell.CloseDialog(d)), Ui.Button("Crear", Create, Ui.ButtonKind.Primary));
            box.schedule.Execute(() => box.Focus()).ExecuteLater(50);
        }

        private void RenameDialog()
        {
            var r = Selected;
            if (r == null) return;
            var old = Table.IdOf(r);
            int uses = C.UsesOf(_category, old).Count;
            var d = _shell.ShowDialog($"Cambiar el id de «{Table.NameOf(r)}»");
            string now = old;
            d.Body.Add(Ui.Hint(uses == 0 ? "Nadie lo usa todavía." : $"Se cambiará también en sus {uses} usos (equipos, aprendizajes, evoluciones, mapas...)."));
            d.Body.Add(Ui.TextBox("Id nuevo", old, v => now = v));
            d.Buttons.With(Ui.Spacer(), Ui.Button("Cancelar", () => _shell.CloseDialog(d)), Ui.Button("Cambiar", () =>
            {
                if (C.Rename(_category, old, now.Trim())) { _shell.CloseDialog(d); Select(now.Trim()); }
            }, Ui.ButtonKind.Primary));
        }

        private void ShowUses()
        {
            var r = Selected;
            if (r == null) return;
            var uses = C.UsesOf(_category, Table.IdOf(r));
            var d = _shell.ShowDialog($"¿Quién usa «{Table.NameOf(r)}»? ({uses.Count})", 40, 60);
            var list = Ui.Column(2);
            if (uses.Count == 0) list.Add(Ui.Hint("Nadie."));
            foreach (var u in uses) list.Add(UseRow(u));
            var scroll = Ui.Scroll().Grow();
            scroll.Add(list);
            d.Body.Add(scroll);
            d.Buttons.With(Ui.Spacer(), Ui.Button("Cerrar", () => _shell.CloseDialog(d), Ui.ButtonKind.Primary));
        }

        /// <summary>Deleting says who uses it and what to do: replace with another, take the uses out, or leave them.</summary>
        private void DeleteDialog()
        {
            var r = Selected;
            if (r == null) return;
            var s = Schema;
            var id = Table.IdOf(r);
            var uses = C.UsesOf(_category, id);
            if (uses.Count == 0)
            {
                C.Delete(_category, id, DeleteMode.KeepUses);
                _selected = null;
                _shell.Success($"«{Table.NameOf(r) ?? id}» borrado (está en la papelera; Ctrl+Z lo devuelve).");
                RebuildAll();
                return;
            }
            var d = _shell.ShowDialog($"Borrar «{Table.NameOf(r)}»", 46, 70);
            d.Body.Add(Ui.Hint($"Se usa en {uses.Count} sitios. ¿Qué hacemos con ellos? (Todo se puede deshacer con Ctrl+Z, y lo borrado queda en la papelera.)"));
            var scroll = Ui.Scroll().Grow();
            var list = Ui.Column(2);
            foreach (var u in uses.Take(100)) list.Add(UseRow(u));
            scroll.Add(list);
            d.Body.Add(scroll);
            bool scripts = uses.Any(u => !u.Removable);
            void Done(DeleteMode mode, string replacement = null)
            {
                if (!C.Delete(_category, id, mode, replacement)) return;
                _shell.CloseDialog(d);
                _selected = replacement;
                RebuildAll();
                _shell.Success(mode == DeleteMode.Replace ? $"Borrado; sus usos ahora apuntan a «{NameOf(_category, replacement)}»." : "Borrado.");
            }
            d.Buttons.With(
                Ui.Button($"Sustituir por otr{(s.Feminine ? "a" : "o")}…", () => ContentPicker.Show(_shell, _category, picked => Done(DeleteMode.Replace, picked), except: id,
                    title: $"¿Por cuál sustituir «{Table.NameOf(r)}»?"), Ui.ButtonKind.Primary, "Cada uso pasa a la que elijas (Pikachu → Raichu)"),
                Ui.Button("Quitar los usos", () => Done(DeleteMode.RemoveUses), Ui.ButtonKind.Normal,
                    scripts ? "Los de efectos escritos a mano no se pueden quitar solos: Problemas los señalará" : "Sale de los equipos, aprendizajes, evoluciones..."),
                Ui.Button("Dejarlos", () => Done(DeleteMode.KeepUses), Ui.ButtonKind.Normal, "Se quedan apuntando a nada; la ventana Problemas los lista"),
                Ui.Spacer(),
                Ui.Button("Cancelar", () => _shell.CloseDialog(d)));
        }

        // ── Registration ─────────────────────────────────────────────────────────────────────────

        /// <summary>One window per category («c_especies»...), its help in the «i», and the trash.</summary>
        public static void RegisterAll()
        {
            foreach (var s in ContentSchemas.All)
            {
                var schema = s;
                PanelCatalog.Register(PanelId(s.Key), s.Title, s.Icon);
                PanelRegistry.Register(PanelId(s.Key), shell => shell.Content == null
                    ? (VisualElement)Ui.EmptyState("base", "Sin datos", "Abre un proyecto.")
                    : new ContentPanel(shell, schema.Key));
                PanelHelp.Register(PanelId(s.Key), schema.Intro + $" Los datos están en «datos/{schema.File}» (se pueden abrir también con Excel).",
                    new[]
                    {
                        "Elige en la lista de la izquierda (busca por nombre, id o número; filtra por tipo).",
                        "Cambia lo que quieras a la derecha: se guarda solo en un momento. Ctrl+Z deshace.",
                        $"«+» crea {schema.Un} {schema.Noun} nuev{(schema.Feminine ? "a" : "o")}; el icono de copiar la duplica.",
                        "Borrar dice quién lo usa y deja sustituirlo por otro, quitar los usos o dejarlos.",
                        "Cambiar el id lo cambia también en todos sus usos (equipos, evoluciones, mapas...).",
                    },
                    new[]
                    {
                        "Los problemas de la ficha salen arriba en rojo; todos juntos, en la ventana Problemas.",
                        "Lo borrado va a la Papelera (Datos → Papelera) y se puede recuperar.",
                        "Las columnas que CTEditor no conoce se conservan y salen en «Otros».",
                    });
            }
            PanelCatalog.Register(TrashPanel.Id, "Papelera", "papelera");
            PanelRegistry.Register(TrashPanel.Id, shell => new TrashPanel(shell));
        }
    }

    /// <summary>PAPELERA: what was deleted from the content windows, to bring it back.</summary>
    public sealed class TrashPanel : VisualElement
    {
        public const string Id = "papelera";
        private readonly AppShell _shell;
        private readonly VisualElement _list;

        public TrashPanel(AppShell shell)
        {
            _shell = shell;
            style.flexGrow = 1;
            var bar = Ui.Row(6).Pad(8, 4);
            bar.With(Ui.Hint("Lo que se borró en las ventanas de datos. Recuperar lo devuelve con su id (si está ocupado, con otro).").Grow(),
                Ui.Button("Vaciar", () => { _shell.Content?.EmptyTrash(); Fill(); }, Ui.ButtonKind.Normal, "Borrar para siempre lo que hay aquí"));
            Add(bar);
            var scroll = Ui.Scroll().Grow();
            _list = Ui.Column(2).Pad(8, 4);
            scroll.Add(_list);
            Add(scroll);
            RegisterCallback<AttachToPanelEvent>(_ => { _shell.ContentChanged += OnChanged; Fill(); });
            RegisterCallback<DetachFromPanelEvent>(_ => _shell.ContentChanged -= OnChanged);
        }

        private void OnChanged(string _) => schedule.Execute(Fill).ExecuteLater(10);

        private void Fill()
        {
            _list.Clear();
            var c = _shell.Content;
            if (c == null || c.Trash.Count == 0) { _list.Add(Ui.EmptyState("papelera", "La papelera está vacía", "Lo que borres en las ventanas de datos aparecerá aquí.")); return; }
            foreach (var e in c.Trash.OrderByDescending(t => t.When).ToList())
            {
                var entry = e;
                var s = ContentSchemas.Find(e.Category);
                var row = Ui.Row(8).Pad(6, 3);
                row.style.alignItems = Align.Center;
                var name = e.Record[s?.NameColumn ?? "nombre"];
                row.With(Icons.Element(s?.Icon ?? "base", Ui.IconSize, Ui.C("texto_suave")),
                    Ui.Text($"{(string.IsNullOrEmpty(name) ? e.Id : name)}", bold: true),
                    Ui.Text($"{s?.Title ?? e.Category} · {e.Id} · {e.When:dd/MM HH:mm}", 0.85f, dim: true).Grow(),
                    Ui.Button("Recuperar", () =>
                    {
                        var back = c.Restore(entry);
                        if (back != null) _shell.OpenContentItem(entry.Category, back[s?.IdColumn ?? "id"]);
                    }, Ui.ButtonKind.Primary));
                _list.Add(row);
            }
        }
    }
}
