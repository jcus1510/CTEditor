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
    public sealed partial class ContentPanel : VisualElement
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
        // Up to two types for species (a Pokémon has two: both must match), one for moves.
        private readonly List<string> _typeFilter = new List<string>();
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
            _right = Ui.Scroll(category == ContentSchemas.TypeChart ? ScrollViewMode.VerticalAndHorizontal : ScrollViewMode.Vertical).Grow();
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
                bool pending = Pending.TryGetValue(_category, out var id);
                if (pending) { Pending.Remove(_category); _selected = id; }
                // Coming back to a tab: only rebuilt if the data changed meanwhile (keeps many tabs fast).
                if (pending || _builtVersion != C?.Db.Version) RebuildAll(keepScroll: !pending);
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

        private bool _rebuildQueued;
        private int _builtVersion = -1;

        private void OnChanged(string category)
        {
            // Our sheet, or one we show things from (names of types, moves...): redraw soon, once.
            if (_rebuildQueued) return;
            _rebuildQueued = true;
            schedule.Execute(() => { _rebuildQueued = false; if (panel != null) RebuildAll(keepScroll: true); }).ExecuteLater(30);
        }

        // ── Layout ───────────────────────────────────────────────────────────────────────────────

        private void RebuildAll(bool keepScroll = false)
        {
            if (C == null) return;
            _builtVersion = C.Db.Version;
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
                _category == ContentSchemas.Species ? Ui.IconButton("arbol", () => FamilyTreePanel.Open(_shell, _selected), "Editor del árbol de familia (evoluciones, formas y variantes)").SetEnabledLook(has) : new VisualElement(),
                Ui.Separator(vertical: true).Margin(6, 4, 6, 4),
                Ui.IconButton("anterior", () => { C.Undo(); }, "Deshacer (Ctrl+Z)"),
                Ui.IconButton("siguiente", () => { C.Redo(); }, "Rehacer (Ctrl+Y)"),
                Ui.Spacer(),
                Ui.Text($"{Table.Records.Count} {s.Title.ToLowerInvariant()}", 0.85f, dim: true));
        }

        private string _kindFilter;

        private void ToggleType(string type)
        {
            if (_typeFilter.Remove(type)) return;
            int max = _category == ContentSchemas.Species ? 2 : 1;
            while (_typeFilter.Count >= max) _typeFilter.RemoveAt(0); // the third replaces the oldest
            _typeFilter.Add(type);
        }

        private void BuildFilters()
        {
            _filters.Clear();
            // By type, each chip always in its colour (the chosen one with a white border), as the Unity editors.
            if (_category == ContentSchemas.Species || _category == ContentSchemas.Moves)
            {
                var row = Ui.Row(0).Wrap();
                foreach (var id in C.Db.Table(ContentSchemas.Types).Ids.ToList())
                {
                    var t = id;
                    row.Add(ContentWidgets.TypeChip(C.Db, t, _typeFilter.Contains(t), () => { ToggleType(t); BuildFilters(); Refill(); }));
                }
                if (_category == ContentSchemas.Species)
                    row.tooltip = "Hasta dos tipos a la vez: salen las especies que tienen los dos.";
                _filters.Add(row);
            }
            // Second row: move category, item pocket, ability category, set format, trainer class.
            var kinds = Table.Records.Select(r => ContentLook.KindOf(_category, r)).Where(k => !string.IsNullOrEmpty(k)).Distinct().OrderBy(k => k).ToList();
            if (kinds.Count > 1 && kinds.Count <= 40)
            {
                var row = Ui.Row(3).Wrap();
                foreach (var k in kinds)
                {
                    var kk = k;
                    var chip = Ui.Chip(ContentWidgets.CategoryLabel(k), _kindFilter == kk, () => { _kindFilter = _kindFilter == kk ? null : kk; BuildFilters(); Refill(); });
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
            if (_typeFilter.Count > 0) rows = rows.Where(r => { var types = ContentLook.TypesOf(_category, r).ToList(); return _typeFilter.All(types.Contains); });
            if (_kindFilter != null) rows = rows.Where(r => ContentLook.KindOf(_category, r) == _kindFilter);
            if (_category == ContentSchemas.Sets) rows = rows.OrderBy(r => ContentLook.Label(t, r), StringComparer.CurrentCultureIgnoreCase);
            if (_category == ContentSchemas.Species)
                rows = _sort == 1 ? rows.OrderBy(r => t.NameOf(r), StringComparer.CurrentCultureIgnoreCase)
                    : _sort == 2 ? rows.OrderByDescending(Total)
                    : rows.OrderBy(r => int.TryParse(r["numero"], out var n) && n > 0 ? n : int.MaxValue);
            _shown = rows.ToList();
            _withErrors = new HashSet<string>(ContentChecks.Cached(C.Db, t).Where(i => i.Level == ContentIssueLevel.Error && i.Id != null).Select(i => i.Id), StringComparer.OrdinalIgnoreCase);
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
                if (index < 0) { _search.SetValueWithoutNotify(""); _typeFilter.Clear(); Refill(); index = _shown.FindIndex(r => string.Equals(Table.IdOf(r), id, StringComparison.OrdinalIgnoreCase)); }
                if (index >= 0) { _list.SetSelectionWithoutNotify(new[] { index }); _list.ScrollToItem(index); }
            }
            ShowSelected();
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
