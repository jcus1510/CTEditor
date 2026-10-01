using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using CTEditor.Content;
using CTEditor.Workspace;

namespace CTEditor.App
{
    /// <summary>
    /// ÁRBOL DE FAMILIA (the EvolutionChainWindow of Unity): on the left the families of the game; in the middle the chosen
    /// one drawn as a graph — a card per species in the colours of its types, arrows with HOW it evolves (clic = edit),
    /// battle forms as ⚔ badges on the card (megas, Modo Daruma...) and variants as outlined cards under their base
    /// (Rotom Lavado, Alola). «+ Evolución», «+ Forma» and «+ Variante» create them; on the right, the panel of what is
    /// chosen, with lists and numbers only (the data stay in the species sheet: this is another way to see and change it).
    /// </summary>
    public sealed class FamilyTreePanel : VisualElement
    {
        public const string Id = "arbol_familia";
        private const float CardWidth = 172;

        /// <summary>The species to show when the window opens (from a species sheet).</summary>
        public static string Pending;

        private enum Pick { Species, Evolution, Form }

        private readonly AppShell _shell;
        private readonly TextField _search;
        private readonly ListView _list;
        private readonly Label _count;
        private readonly VisualElement _head, _canvas, _side;
        private List<string> _roots = new List<string>();
        private readonly Dictionary<string, string> _chainLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private bool _singles;
        private string _root;
        private Pick _pick = Pick.Species;
        private string _species;
        private int _evo = -1;
        private string _form;
        private IVisualElementScheduledItem _refresh;
        private Vector2 _pointer; // where the last click was (menus open there)

        private ContentSession C => _shell.Content;
        private ContentTable T => C.Db.Table(ContentSchemas.Species);
        private FamilyGraph G => FamilyGraph.For(C.Db);

        public static void Register()
        {
            PanelCatalog.Register(Id, "Árbol de familia", "arbol");
            PanelRegistry.Register(Id, shell => shell.Content == null
                ? (VisualElement)Ui.EmptyState("arbol", "Sin datos", "Abre un proyecto.")
                : new FamilyTreePanel(shell));
            PanelHelp.Register(Id, "Las familias de especies como un grafo: evoluciones, formas de combate y variantes.",
                new[]
                {
                    "Elige una familia a la izquierda (busca por cualquiera de sus especies).",
                    "Clic en una flecha: cambia cómo evoluciona (nivel, objeto, amistad, hora, sabiendo un movimiento...).",
                    "«+ Evolución» añade una rama; «+ Forma» una forma de combate (megaevolución, por PS, por clima...).",
                    "«+ Variante» crea otra especie enlazada a esta (formas regionales, Rotom Lavado...).",
                    "Las insignias ⚔ son formas de combate: clic para cambiar sus tipos, estadísticas y cómo aparecen.",
                },
                new[]
                {
                    "Los datos siguen en la hoja de especies: esta ventana es otra forma de verlos y cambiarlos.",
                    "Los avisos (ciclos, niveles que retroceden, especies que no existen) salen arriba del árbol.",
                    "Todo se deshace con Ctrl+Z.",
                });
        }

        /// <summary>Opens the window with the family of a species.</summary>
        public static void Open(AppShell shell, string species)
        {
            Pending = species;
            shell.Workspace.Layout.Open(Id);
            shell.SetLayout(shell.Workspace.Layout);
            shell.ActiveEditor = "contenido";
            Opened?.Invoke(species);
        }

        private static event Action<string> Opened;

        public FamilyTreePanel(AppShell shell)
        {
            _shell = shell;
            style.flexGrow = 1;
            style.flexDirection = FlexDirection.Row;

            var left = Ui.Column(6).Pad(8, 8);
            left.style.width = 230;
            left.style.flexShrink = 0;
            _search = Ui.TextBox("", "");
            _search.tooltip = "Buscar una familia por cualquiera de sus especies";
            _search.RegisterValueChangedCallback(_ => FillList());
            _count = Ui.Text("", 0.8f, dim: true);
            left.Add(_search);
            left.Add(Ui.Row(6).With(Ui.Check("Especies sin evolución", false, on => { _singles = on; FillList(); }), Ui.Spacer(), _count));
            _list = new ListView
            {
                fixedItemHeight = Ui.ControlHeight,
                selectionType = SelectionType.Single,
                makeItem = () =>
                {
                    var l = Ui.Text("", 0.9f).Pad(6, 0);
                    l.style.unityTextAlign = TextAnchor.MiddleLeft;
                    l.style.overflow = Overflow.Hidden;
                    l.style.textOverflow = TextOverflow.Ellipsis;
                    l.style.whiteSpace = WhiteSpace.NoWrap;
                    return l;
                },
                bindItem = (e, i) =>
                {
                    if (i >= _roots.Count) return;
                    ((Label)e).text = ChainLabel(_roots[i]);
                    e.tooltip = ChainLabel(_roots[i]);
                },
            };
            _list.style.flexGrow = 1;
            _list.itemsSource = _roots;
#if UNITY_2022_2_OR_NEWER
            _list.selectionChanged += items => { foreach (var it in items) { ShowChain((string)it); return; } };
#else
            _list.onSelectionChange += items => { foreach (var it in items) { ShowChain((string)it); return; } };
#endif
            left.Add(_list);
            Add(left);
            Add(Ui.Separator(vertical: true));

            var center = Ui.Column(0).Grow();
            center.style.minWidth = 0;
            _head = Ui.Column(4).Pad(12, 8);
            _head.style.flexShrink = 0;
            var scroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            scroll.style.flexGrow = 1;
            _canvas = Ui.Column(0).Pad(16, 12);
            scroll.Add(_canvas);
            center.With(_head, scroll);
            Add(center);
            Add(Ui.Separator(vertical: true));

            var sideScroll = Ui.Scroll();
            sideScroll.style.width = 320;
            sideScroll.style.flexShrink = 0;
            _side = Ui.Column(8).Pad(12, 10);
            sideScroll.Add(_side);
            Add(sideScroll);

            RegisterCallback<PointerDownEvent>(e => _pointer = e.position, TrickleDown.TrickleDown);
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                _shell.ContentChanged += OnChanged;
                Opened += OnOpened;
                FillList();
                var start = Pending ?? _root;
                Pending = null;
                if (start != null) OnOpened(start);
                else if (_roots.Count > 0) ShowChain(_roots[0]);
            });
            RegisterCallback<DetachFromPanelEvent>(_ => { _shell.ContentChanged -= OnChanged; Opened -= OnOpened; });
        }

        private void OnOpened(string species)
        {
            if (C == null || string.IsNullOrEmpty(species)) return;
            Pending = null;
            var root = G.RootOf(species);
            _pick = Pick.Species;
            _species = species;
            if (!_roots.Contains(root)) { _search.SetValueWithoutNotify(""); _singles |= G.Members(root).Count == 1; FillList(); }
            ShowChain(root, keepPick: true);
        }

        private void OnChanged(string category)
        {
            if (category != ContentSchemas.Species && category != ContentSchemas.Types) return;
            _refresh?.Pause();
            _refresh = schedule.Execute(() => { _chainLabels.Clear(); FillList(); Draw(); });
            _refresh.ExecuteLater(120);
        }

        // ── Families on the left ─────────────────────────────────────────────────────────────────

        private string Name(string id) => C.Db.NameOf(ContentSchemas.Species, id) ?? id;

        private string ChainLabel(string root)
        {
            if (_chainLabels.TryGetValue(root, out var l)) return l;
            var members = G.Members(root);
            l = string.Join(" → ", members.Take(4).Select(Name)) + (members.Count > 4 ? $" (+{members.Count - 4})" : "");
            return _chainLabels[root] = l;
        }

        private void FillList()
        {
            if (C == null) return;
            var q = (_search.value ?? "").Trim();
            var all = G.Roots(_singles);
            _roots = q.Length == 0 ? all : all.Where(r => G.Members(r).Any(m =>
                m.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || Name(m).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            _list.itemsSource = _roots;
            _list.Rebuild();
            _count.text = $"{_roots.Count} familias";
            int i = _root == null ? -1 : _roots.IndexOf(_root);
            if (i >= 0) _list.SetSelectionWithoutNotify(new[] { i });
        }

        private void ShowChain(string root, bool keepPick = false)
        {
            _root = root;
            if (!keepPick) { _pick = Pick.Species; _species = root; }
            int i = _roots.IndexOf(root);
            if (i >= 0) { _list.SetSelectionWithoutNotify(new[] { i }); _list.ScrollToItem(i); }
            Draw();
        }

        // ── The graph ────────────────────────────────────────────────────────────────────────────

        private void Draw()
        {
            _head.Clear();
            _canvas.Clear();
            if (C == null || _root == null || T.Find(_root) == null)
            {
                _canvas.Add(Ui.EmptyState("arbol", "Elige una familia", "A la izquierda están todas las familias del juego."));
                DrawSide();
                return;
            }
            var title = Ui.Row(8);
            title.style.alignItems = Align.Center;
            title.Add(Ui.Text("Familia de " + Name(_root), 1.2f, bold: true));
            title.Add(Ui.Text($"{G.Members(_root).Count} especies", 0.82f, dim: true));
            _head.Add(title);
            foreach (var w in G.Warnings(_root, id => C.Db.Has(ContentSchemas.Species, id)).Distinct().Take(6))
                _head.Add(Ui.Hint("⚠ " + w).Colored("aviso"));
            _canvas.Add(Tree(_root, false, new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
            DrawSide();
        }

        /// <summary>A card (with its variants under it) and, to its right, every evolution: an arrow with how, then its tree.</summary>
        private VisualElement Tree(string id, bool variant, HashSet<string> seen)
        {
            var row = Ui.Row(0);
            row.style.alignItems = Align.FlexStart;
            var col = Ui.Column(0);
            col.style.alignItems = Align.FlexStart;
            col.Add(Card(id, variant));
            bool first = seen.Add(id);
            if (first)
                foreach (var v in G.VariantsOf(id))
                {
                    var link = new VisualElement();
                    link.style.width = 0; link.style.height = 14; link.style.marginLeft = 22;
                    link.style.borderLeftWidth = 2;
                    link.style.borderLeftColor = Ui.WithAlpha(Ui.C("texto_suave"), 0.5f);
                    col.Add(link);
                    col.Add(Tree(v, true, seen));
                }
            row.Add(col);
            if (!first) return row;
            var evos = G.EvolutionsOf(id);
            if (evos.Count == 0) return row;
            var branches = Ui.Column(10);
            if (evos.Count > 1)
            {
                branches.style.borderLeftWidth = 2;
                branches.style.borderLeftColor = Ui.C("borde");
                branches.style.paddingLeft = 0;
            }
            for (int i = 0; i < evos.Count; i++)
            {
                var b = Ui.Row(0);
                b.style.alignItems = Align.FlexStart;
                b.Add(Edge(id, i, evos[i]));
                if (C.Db.Has(ContentSchemas.Species, evos[i].Target)) b.Add(Tree(evos[i].Target, false, seen));
                else b.Add(MissingCard(evos[i].Target));
                branches.Add(b);
            }
            var line = new VisualElement();
            line.style.width = 14; line.style.height = 2; line.style.marginTop = 30;
            line.style.backgroundColor = Ui.C("borde");
            row.Add(line);
            row.Add(branches);
            return row;
        }

        /// <summary>The arrow of an evolution: a line, how it evolves (clic = edit it) and the head.</summary>
        private VisualElement Edge(string from, int index, Evolution e)
        {
            var edge = Ui.Row(0);
            edge.style.alignItems = Align.Center;
            edge.style.marginTop = 18;
            var l1 = new VisualElement(); l1.style.width = 10; l1.style.height = 2; l1.style.backgroundColor = Ui.C("borde");
            bool chosen = _pick == Pick.Evolution && Same(_species, from) && _evo == index;
            var label = Ui.Button(Evolutions.Describe(e, (cat, id) => C.Db.NameOf(cat, id) ?? id), () => PickEvolution(from, index),
                chosen ? Ui.ButtonKind.Primary : Ui.ButtonKind.Normal, "Clic: cambiar cómo evoluciona");
            label.style.height = label.style.minHeight = Ui.ControlHeight - 6;
            label.style.fontSize = Ui.FontSize * 0.82f;
            label.style.maxWidth = 170;
            label.style.paddingLeft = label.style.paddingRight = 7;
            label.Round(10);
            var l2 = new VisualElement(); l2.style.width = 10; l2.style.height = 2; l2.style.backgroundColor = Ui.C("borde");
            var head = Ui.Text("▶", 0.8f, dim: true);
            head.style.marginRight = 4;
            return edge.With(l1, label, l2, head);
        }

        private VisualElement MissingCard(string id)
        {
            var card = Ui.Column(2).Pad(8, 6).Round(6);
            card.style.width = CardWidth;
            card.style.marginTop = 10;
            card.Border(1, "error", 6);
            card.Add(Ui.Text(id, 0.9f, bold: true));
            card.Add(Ui.Hint("No existe: cámbiala en su flecha."));
            return card;
        }

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        /// <summary>A species card: a stripe in its type colours, number, name, types, ⚔ forms and the add buttons.</summary>
        private VisualElement Card(string id, bool variant)
        {
            var r = T.Find(id);
            var card = Ui.Column(0).Round(6);
            card.style.width = CardWidth;
            card.style.overflow = Overflow.Hidden;
            card.style.marginTop = 6;
            bool chosen = _pick == Pick.Species && Same(_species, id);
            if (variant)
            {
                // Variants: outlined, without fill (the «dashed» cards of Unity).
                card.style.backgroundColor = Ui.WithAlpha(Ui.C("panel_alt"), 0.35f);
                card.Border(1, "texto_suave", 6);
            }
            else card.style.backgroundColor = Ui.C("panel_alt");
            if (chosen) card.Border(2, "acento", 6);

            var stripe = Ui.Row(0);
            stripe.style.height = 5;
            var types = ContentLook.TypesOf(ContentSchemas.Species, r).ToList();
            foreach (var t in types.DefaultIfEmpty(""))
            {
                var part = new VisualElement().Grow();
                part.style.backgroundColor = ContentLook.TypeColor(C.Db, t) ?? Ui.C("borde");
                stripe.Add(part);
            }
            card.Add(stripe);

            var inner = Ui.Column(3).Pad(8, 5);
            var top = Ui.Row(4);
            top.style.alignItems = Align.Center;
            if (int.TryParse(r["numero"], out var n) && n > 0) top.Add(Ui.Text($"#{n:000}", 0.75f, dim: true));
            if (variant) top.Add(Ui.Text("variante", 0.72f, dim: true));
            inner.Add(top);
            var name = Ui.Text(T.NameOf(r), 0.95f, bold: true);
            name.style.whiteSpace = WhiteSpace.Normal;
            inner.Add(name);
            var chips = Ui.Row(0);
            foreach (var t in types) chips.Add(ContentWidgets.TypeChip(C.Db, t));
            inner.Add(chips);

            var forms = SpeciesForms.ParseForms(r["formas"]);
            if (forms.Count > 0)
            {
                var badges = Ui.Row(0).Wrap();
                foreach (var f in forms)
                {
                    var fid = f.Id;
                    bool on = _pick == Pick.Form && Same(_species, id) && Same(_form, fid);
                    var b = Ui.Text("⚔ " + (f.Name.Length > 0 ? f.Name : fid), 0.72f, bold: true).Pad(5, 1).Round(4);
                    b.style.backgroundColor = on ? Ui.C("acento") : new Color(0.55f, 0.35f, 0.15f, 0.85f);
                    b.style.color = Color.white;
                    b.style.marginRight = 3; b.style.marginBottom = 3;
                    b.tooltip = "Forma de combate: clic para editarla";
                    b.RegisterCallback<ClickEvent>(e => { e.StopPropagation(); PickForm(id, fid); });
                    badges.Add(b);
                }
                inner.Add(badges);
            }

            var actions = Ui.Row(0).Wrap();
            actions.Add(Small("Abrir", "Abrir su ficha de especie", () => _shell.OpenContentItem(ContentSchemas.Species, id)));
            actions.Add(Small("+ Evolución", "Añadir una evolución (o una rama más)", () => AddEvolution(id)));
            actions.Add(Small("+ Forma", "Forma de COMBATE (cambia en mitad del combate: megaevolución, por PS, por clima...)", () => AddFormMenu(id)));
            actions.Add(Small("+ Variante", "Otra especie enlazada a esta («es forma de»): regional, Rotom Lavado...", () => AddVariant(id)));
            inner.Add(actions);
            card.Add(inner);
            card.tooltip = "Clic: ver y cambiar esta especie a la derecha";
            card.RegisterCallback<ClickEvent>(_ => { _pick = Pick.Species; _species = id; Draw(); });
            return card;
        }

        private static Button Small(string text, string tip, Action action)
        {
            var b = Ui.Button(text, null, Ui.ButtonKind.Flat, tip);
            b.clickable = new Clickable(action);
            b.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            b.style.height = b.style.minHeight = Ui.ControlHeight - 10;
            b.style.fontSize = Ui.FontSize * 0.74f;
            b.style.paddingLeft = b.style.paddingRight = 4;
            b.style.marginRight = 2;
            b.style.color = Ui.C("acento");
            return b;
        }

        // ── Adding ───────────────────────────────────────────────────────────────────────────────

        private void SetCell(string species, string column, string value) => C.SetValue(ContentSchemas.Species, species, column, value);

        private void AddEvolution(string from)
        {
            ContentPicker.Show(_shell, ContentSchemas.Species, target =>
            {
                var list = Evolutions.Parse(T.Find(from)?["evoluciona"]);
                if (list.Any(e => Same(e.Target, target))) { _shell.Warn($"{Name(from)} ya evoluciona en {Name(target)}."); return; }
                if (G.ParentOf(target) is string p) _shell.Warn($"Ojo: {Name(target)} ya es la evolución de {Name(p)}.");
                list.Add(new Evolution { Target = target, Method = "nivel", Value = "16" });
                SetCell(from, "evoluciona", Evolutions.Write(list));
                _pick = Pick.Evolution; _species = from; _evo = list.Count - 1;
            }, except: from, title: $"¿En qué evoluciona {Name(from)}?");
        }

        private void AddVariant(string baseId)
        {
            var b = T.Find(baseId);
            if (b == null) return;
            var d = _shell.ShowDialog($"Nueva variante de {Name(baseId)}");
            string name = Name(baseId) + " ";
            var box = Ui.TextBox("", name, v => name = v);
            d.Body.Add(Ui.Hint("Otra especie con sus propios tipos, estadísticas y movimientos, enlazada a esta (formas regionales, Rotom Lavado, Deoxys Ataque...). Empieza como una copia."));
            d.Body.Add(box);
            d.Buttons.With(Ui.Spacer(), Ui.Button("Cancelar", () => _shell.CloseDialog(d)), Ui.Button("Crear", () =>
            {
                _shell.CloseDialog(d);
                if (string.IsNullOrWhiteSpace(name) || name.Trim() == Name(baseId)) return;
                var template = b.Clone();
                template["nombre"] = name.Trim();
                template["forma_de"] = baseId;
                template["evoluciona"] = "";
                template["formas"] = "";
                template["cambios_forma"] = "";
                template["nombre_en"] = "";
                var created = C.Create(ContentSchemas.Species, name.Trim(), template);
                if (created != null) { _pick = Pick.Species; _species = T.IdOf(created); }
            }, Ui.ButtonKind.Primary));
            box.schedule.Execute(() => box.Focus()).ExecuteLater(50);
        }

        private string NewFormId(List<BattleForm> forms, string wanted)
        {
            var id = wanted;
            for (int i = 2; forms.Any(f => Same(f.Id, id)); i++) id = wanted + "_" + i;
            return id;
        }

        private void AddFormMenu(string species)
        {
            var r = T.Find(species);
            if (r == null) return;
            var name = T.NameOf(r);
            void Add(params (string id, string label, string change)[] forms)
            {
                var list = SpeciesForms.ParseForms(r["formas"]);
                var changes = SpeciesForms.ParseChanges(r["cambios_forma"]);
                string last = null;
                foreach (var (wanted, label, change) in forms)
                {
                    var id = NewFormId(list, wanted);
                    list.Add(new BattleForm { Id = id, Name = label, Reverts = change.Contains("ps_") || change.Contains("clima") });
                    foreach (var c in change.Split('|').Where(x => x.Length > 0)) changes.AddRange(SpeciesForms.ParseChanges(c.Replace("{id}", id)));
                    last = id;
                }
                C.SetValue(ContentSchemas.Species, species, "formas", SpeciesForms.WriteForms(list));
                C.SetValue(ContentSchemas.Species, species, "cambios_forma", SpeciesForms.WriteChanges(changes));
                _pick = Pick.Form; _species = species; _form = last;
            }
            var items = new List<MenuItem>
            {
                new MenuItem("Megaevolución", () => Add(("mega", "Mega-" + name, ">{id}:mega"))),
                new MenuItem("Mega X y Mega Y", () => Add(("mega_x", $"Mega-{name} X", ">{id}:mega"), ("mega_y", $"Mega-{name} Y", ">{id}:mega"))),
                new MenuItem("Regresión primigenia (llevando un objeto)", () => Add(("primal", name + " Primigenio", ">{id}:objeto"))),
                new MenuItem("Con poca vida (como el Modo Daruma)", () => Add(("zen", name + " (forma)", ">{id}:ps_bajo:50|{id}>:ps_desde:50"))),
                new MenuItem("Con un clima (como Castform)", () => Add(("sol", name + " (sol)", "*>{id}:clima:sun|*>:clima"))),
                new MenuItem("Al usar un movimiento (como Meloetta)", () => Add(("forma", name + " (forma)", ">{id}:movimiento;despues|{id}>:movimiento;despues"))),
                new MenuItem("Llevando un objeto (como Giratina)", () => Add(("forma", name + " (forma)", ">{id}:objeto"))),
                MenuItem.Separator,
                new MenuItem("Forma vacía (sin cambio todavía)", () => Add(("forma", name + " (forma)", ""))),
            };
            _shell.ShowMenu(_pointer, items);
        }

        // ── The panel on the right ───────────────────────────────────────────────────────────────

        private void PickEvolution(string from, int index) { _pick = Pick.Evolution; _species = from; _evo = index; Draw(); }

        private void PickForm(string species, string form) { _pick = Pick.Form; _species = species; _form = form; Draw(); }

        private void DrawSide()
        {
            _side.Clear();
            if (C == null || _species == null || T.Find(_species) == null)
            {
                _side.Add(Ui.Hint("Clic en una tarjeta, una flecha o una insignia ⚔ para verla y cambiarla aquí."));
                return;
            }
            switch (_pick)
            {
                case Pick.Evolution: EvolutionSide(); break;
                case Pick.Form: FormSide(); break;
                default: SpeciesSide(); break;
            }
        }

        private void SpeciesSide()
        {
            var r = T.Find(_species);
            _side.Add(Ui.Text(T.NameOf(r), 1.2f, bold: true));
            var chips = Ui.Row(0);
            foreach (var t in ContentLook.TypesOf(ContentSchemas.Species, r)) chips.Add(ContentWidgets.TypeChip(C.Db, t));
            _side.Add(chips);
            _side.Add(Ui.Button("Abrir su ficha", () => _shell.OpenContentItem(ContentSchemas.Species, _species)));
            if (G.ParentOf(_species) is string p) _side.Add(Ui.Hint("Evoluciona de " + Name(p) + "."));
            if (G.BaseOf(_species) is string b) _side.Add(Ui.Hint("Es una variante de " + Name(b) + "."));
            var evos = G.EvolutionsOf(_species);
            _side.Add(Ui.SectionTitle($"Evoluciones ({evos.Count})"));
            for (int i = 0; i < evos.Count; i++)
            {
                int idx = i;
                var row = Ui.Row(6);
                row.style.alignItems = Align.Center;
                row.Add(Ui.Text("→ " + Name(evos[i].Target), 0.9f, bold: true));
                row.Add(Ui.Text(Evolutions.Describe(evos[i], (c, id) => C.Db.NameOf(c, id) ?? id), 0.8f, dim: true).Grow());
                row.Add(Ui.IconButton("retocar", () => PickEvolution(_species, idx), "Cambiar cómo evoluciona", iconSize: Ui.IconSize * 0.7f));
                _side.Add(row);
            }
            _side.Add(Ui.Button("+ Evolución", () => AddEvolution(_species)));
            var forms = SpeciesForms.ParseForms(r["formas"]);
            _side.Add(Ui.SectionTitle($"Formas de combate ({forms.Count})"));
            foreach (var f in forms)
            {
                var fid = f.Id;
                _side.Add(Ui.Button("⚔ " + (f.Name.Length > 0 ? f.Name : f.Id), () => PickForm(_species, fid)));
            }
            _side.Add(Ui.Button("+ Forma", () => AddFormMenu(_species)));
            var variants = G.VariantsOf(_species);
            _side.Add(Ui.SectionTitle($"Variantes ({variants.Count})"));
            foreach (var v in variants)
            {
                var vid = v;
                _side.Add(Ui.Button("◇ " + Name(v), () => { _pick = Pick.Species; _species = vid; Draw(); }));
            }
            _side.Add(Ui.Button("+ Variante", () => AddVariant(_species)));
        }

        private static readonly (string label, string method, string value, string conditions)[] EvoTemplates =
        {
            ("Por nivel", "nivel", "16", ""),
            ("Con una piedra u objeto", "objeto", "", ""),
            ("Con amistad", "amistad", "", ""),
            ("Con amistad, de día", "amistad", "", "hora:dia"),
            ("Con amistad, de noche", "amistad", "", "hora:noche"),
            ("Al intercambiarlo", "intercambio", "", ""),
            ("Al intercambiarlo llevando un objeto", "intercambio", "metal_coat", ""),
            ("Subiendo de nivel llevando un objeto, de noche", "subir", "", "lleva:razor_claw+hora:noche"),
            ("Subiendo de nivel sabiendo un movimiento", "subir", "", "sabe:ancient_power"),
            ("Por nivel con Ataque mayor que Defensa", "nivel", "20", "stats:atq>def"),
            ("Por nivel siendo hembra", "nivel", "20", "genero:hembra"),
            ("Por nivel de noche", "nivel", "20", "hora:noche"),
        };

        private void EvolutionSide()
        {
            var r = T.Find(_species);
            var list = Evolutions.Parse(r["evoluciona"]);
            if (_evo < 0 || _evo >= list.Count) { _pick = Pick.Species; SpeciesSide(); return; }
            var e = list[_evo];
            void Save() => SetCell(_species, "evoluciona", Evolutions.Write(list));

            _side.Add(Ui.Text($"{Name(_species)} → {Name(e.Target)}", 1.1f, bold: true));
            _side.Add(Ui.Hint(Evolutions.Describe(e, (c, id) => C.Db.NameOf(c, id) ?? id)));
            _side.Add(ContentWidgets.Dropdown(_shell, "Plantillas clásicas", () => EvoTemplates.Select(t => new MenuItem(t.label, () =>
            {
                e.Method = t.method; e.Value = t.value;
                e.Conditions = Evolutions.Parse("x@1" + (t.conditions.Length > 0 ? "+" + t.conditions : ""))[0].Conditions;
                Save();
            })).ToList(), tooltip: "Rellena el método y las condiciones con un caso de los juegos (mantiene la especie)"));

            _side.Add(Field("Se convierte en", ContentWidgets.RefDropdown(_shell, ContentSchemas.Species, e.Target, p => { e.Target = p; Save(); })));
            _side.Add(Field("Cómo", ContentWidgets.Dropdown(_shell, Evolutions.MethodLabel(e.Method), () => Evolutions.Methods.Where(m => m.key != "otro").Select(m =>
                new MenuItem(m.label, () => { e.Method = m.key; e.Value = m.key == "nivel" ? "16" : ""; Save(); }, isChecked: m.key == e.Method)).ToList())));
            switch (e.Method)
            {
                case "nivel":
                    _side.Add(Field("Nivel", Ui.MiniNumber(int.TryParse(e.Value, out var l) ? l : 16, 1, 100, v => { e.Value = v.ToString(); Save(); }, "Nivel", 60)));
                    break;
                case "objeto":
                    _side.Add(Field("Objeto", ContentWidgets.RefDropdown(_shell, ContentSchemas.Items, e.Value, p => { e.Value = p; Save(); }, "Elegir objeto…")));
                    break;
                case "intercambio":
                    var item = Ui.Row(4);
                    item.Add(ContentWidgets.RefDropdown(_shell, ContentSchemas.Items, e.Value, p => { e.Value = p; Save(); }, "Sin objeto"));
                    if (e.Value.Length > 0) item.Add(Ui.IconButton("cerrar", () => { e.Value = ""; Save(); }, "Sin objeto", iconSize: Ui.IconSize * 0.7f));
                    _side.Add(Field("Llevando", item));
                    break;
                case "amistad":
                    _side.Add(Field("Amistad mínima", Ui.MiniNumber(int.TryParse(e.Value, out var f) ? f : 220, 1, 255, v => { e.Value = v == 220 ? "" : v.ToString(); Save(); }, "Amistad (220 si no se dice)", 60)));
                    break;
                case "otro":
                    _side.Add(Ui.Hint("Una condición que esta ventana no conoce: " + e.Value).Colored("aviso"));
                    break;
            }

            _side.Add(Ui.SectionTitle("Condiciones extra (todas a la vez)"));
            foreach (var c in e.Conditions.ToList())
            {
                var cond = c;
                var kind = Evolutions.ConditionKinds.FirstOrDefault(k => k.key == cond.Key);
                var row = Ui.Row(4).Wrap();
                row.style.alignItems = Align.Center;
                row.Add(ContentWidgets.Dropdown(_shell, (cond.Not ? "No: " : "") + Evolutions.ConditionLabel(cond.Key), () => new List<MenuItem>
                {
                    new MenuItem("Se tiene que cumplir", () => { cond.Not = false; Save(); }, isChecked: !cond.Not),
                    new MenuItem("NO se tiene que cumplir", () => { cond.Not = true; Save(); }, isChecked: cond.Not),
                }));
                row.Add(ValueControl(kind.key == null ? EvoValueKind.Text : kind.kind, kind.options, cond.Value, v => { cond.Value = v; Save(); }));
                row.Add(Ui.IconButton("cerrar", () => { e.Conditions.Remove(cond); Save(); }, "Quitar la condición", iconSize: Ui.IconSize * 0.7f));
                _side.Add(row);
            }
            _side.Add(ContentWidgets.Dropdown(_shell, "+ Condición", () => Evolutions.ConditionKinds.Select(k => new MenuItem(k.label, () =>
            {
                e.Conditions.Add(new EvoCondition { Key = k.key, Value = k.options?[0] ?? (k.kind == EvoValueKind.Number ? (k.key == "amistad" ? "160" : "1") : "") });
                Save();
            })).ToList()));
            _side.Add(Ui.Separator());
            _side.Add(Ui.Button("Quitar esta evolución", () =>
            {
                list.RemoveAt(_evo);
                _pick = Pick.Species;
                Save();
            }, Ui.ButtonKind.Danger));
        }

        private void FormSide()
        {
            var r = T.Find(_species);
            var forms = SpeciesForms.ParseForms(r["formas"]);
            var changes = SpeciesForms.ParseChanges(r["cambios_forma"]);
            var form = forms.FirstOrDefault(f => Same(f.Id, _form));
            if (form == null) { _pick = Pick.Species; SpeciesSide(); return; }
            void SaveForms() => SetCell(_species, "formas", SpeciesForms.WriteForms(forms));
            void SaveChanges() => SetCell(_species, "cambios_forma", SpeciesForms.WriteChanges(changes));

            _side.Add(Ui.Text("⚔ " + (form.Name.Length > 0 ? form.Name : form.Id), 1.1f, bold: true));
            _side.Add(Ui.Hint($"Forma de combate de {Name(_species)} (id «{form.Id}»). Vacío o 0 = igual que la especie."));
            _side.Add(Field("Nombre", ContentWidgets.WrapBox(form.Name, v => { form.Name = v; SaveForms(); })));
            var types = Ui.Row(4).Wrap();
            types.Add(TypePick(form.Type1, "Igual", v => { form.Type1 = v; SaveForms(); }));
            types.Add(TypePick(form.Type2, "—", v => { form.Type2 = v; SaveForms(); }));
            _side.Add(Field("Tipos", types));
            var stats = Ui.Column(3);
            string[] names = { "Ataque", "Defensa", "At. esp.", "Def. esp.", "Velocidad" };
            string[] cols = { "ataque", "defensa", "atq_esp", "def_esp", "velocidad" };
            for (int i = 0; i < 5; i++)
            {
                int k = i;
                var row = Ui.Row(6);
                row.style.alignItems = Align.Center;
                var label = Ui.Text(names[i], 0.85f); label.style.width = 80;
                row.With(label, Ui.MiniNumber(form.Stats[i], 0, 255, v => { form.Stats[k] = v; SaveForms(); }, "0 = igual", 60),
                    Ui.Text("base " + r[cols[i]], 0.78f, dim: true));
                stats.Add(row);
            }
            _side.Add(Field("Estadísticas", stats));
            var ability = Ui.Row(4);
            ability.Add(ContentWidgets.RefDropdown(_shell, ContentSchemas.Abilities, form.Ability, p => { form.Ability = p; SaveForms(); }, "Igual"));
            if (form.Ability.Length > 0) ability.Add(Ui.IconButton("cerrar", () => { form.Ability = ""; SaveForms(); }, "La misma de la especie", iconSize: Ui.IconSize * 0.7f));
            _side.Add(Field("Habilidad", ability));
            _side.Add(Ui.Check("Vuelve a la normal al retirarse", form.Reverts, on => { form.Reverts = on; SaveForms(); }));

            _side.Add(Ui.SectionTitle("Cuándo cambia"));
            var mine = changes.Where(c => Same(c.To, form.Id) || Same(c.From, form.Id)).ToList();
            if (mine.Count == 0) _side.Add(Ui.Hint("Nada la provoca todavía: añade cuándo aparece."));
            foreach (var c in mine)
            {
                var ch = c;
                var box = Ui.Column(4).Pad(8, 6).Round(5);
                box.style.backgroundColor = Ui.C("panel_alt");
                var dir = Ui.Row(4).Wrap();
                dir.style.alignItems = Align.Center;
                dir.Add(Ui.Text("De", 0.85f));
                dir.Add(FormPick(forms, ch.From, true, v => { ch.From = v; SaveChanges(); }));
                dir.Add(Ui.Text("a", 0.85f));
                dir.Add(FormPick(forms, ch.To, false, v => { ch.To = v; SaveChanges(); }));
                dir.Add(Ui.Spacer());
                dir.Add(Ui.IconButton("cerrar", () => { changes.Remove(ch); SaveChanges(); }, "Quitar este cambio", iconSize: Ui.IconSize * 0.7f));
                box.Add(dir);
                var trig = SpeciesForms.Triggers.FirstOrDefault(t => t.key == ch.Trigger);
                box.Add(ContentWidgets.Dropdown(_shell, SpeciesForms.TriggerLabel(ch.Trigger), () => SpeciesForms.Triggers.Select(t => new MenuItem(t.label, () =>
                {
                    ch.Trigger = t.key;
                    ch.Value = t.kind == EvoValueKind.Number ? "50" : t.kind == EvoValueKind.Weather ? "sun" : "";
                    SaveChanges();
                }, isChecked: t.key == ch.Trigger)).ToList()));
                if (trig.key != null && trig.kind != EvoValueKind.None)
                    box.Add(ValueControl(trig.kind, null, ch.Value, v => { ch.Value = v; SaveChanges(); }, trig.kind == EvoValueKind.Weather ? "Cualquier otro clima" : "Elegir…"));
                if (ch.Trigger == "mega")
                {
                    var knows = Ui.Row(4).Wrap();
                    knows.style.alignItems = Align.Center;
                    knows.Add(Ui.Text("o sabiendo", 0.82f, dim: true));
                    knows.Add(ContentWidgets.RefDropdown(_shell, ContentSchemas.Moves, ch.Knows, p => { ch.Knows = p; SaveChanges(); }, "Ningún movimiento"));
                    if (ch.Knows.Length > 0) knows.Add(Ui.IconButton("cerrar", () => { ch.Knows = ""; SaveChanges(); }, "Quitar", iconSize: Ui.IconSize * 0.7f));
                    box.Add(knows);
                }
                var ab = Ui.Row(4).Wrap();
                ab.style.alignItems = Align.Center;
                ab.Add(Ui.Text("Solo con la habilidad", 0.82f, dim: true));
                ab.Add(ContentWidgets.RefDropdown(_shell, ContentSchemas.Abilities, ch.Ability, p => { ch.Ability = p; SaveChanges(); }, "Cualquiera"));
                if (ch.Ability.Length > 0) ab.Add(Ui.IconButton("cerrar", () => { ch.Ability = ""; SaveChanges(); }, "Cualquiera", iconSize: Ui.IconSize * 0.7f));
                box.Add(ab);
                if (ch.Trigger == "movimiento" || ch.Trigger == "ataque")
                    box.Add(Ui.Check("Cambia después de moverse (si no, antes de golpear)", ch.After, on => { ch.After = on; SaveChanges(); }));
                _side.Add(box);
            }
            _side.Add(Ui.Button("+ Cuándo aparece", () => { changes.Add(new FormChange { From = "", To = form.Id, Trigger = "objeto" }); SaveChanges(); }));
            _side.Add(Ui.Separator());
            _side.Add(Ui.Button("Quitar esta forma", () =>
            {
                forms.Remove(form);
                changes.RemoveAll(c => Same(c.To, form.Id) || Same(c.From, form.Id));
                _pick = Pick.Species;
                SaveForms();
                SaveChanges();
            }, Ui.ButtonKind.Danger));
        }

        private VisualElement Field(string label, VisualElement editor)
        {
            var row = Ui.Column(2);
            row.Add(Ui.Text(label, 0.82f, dim: true));
            row.Add(editor);
            return row;
        }

        private VisualElement TypePick(string value, string none, Action<string> save)
        {
            var row = Ui.Row(2);
            row.style.alignItems = Align.Center;
            row.Add(ContentWidgets.RefDropdown(_shell, ContentSchemas.Types, value, save, none));
            if (!string.IsNullOrEmpty(value)) row.Add(Ui.IconButton("cerrar", () => save(""), "Quitar", iconSize: Ui.IconSize * 0.6f));
            return row;
        }

        /// <summary>A form of the species: the normal one, any (only as «from») or a battle form.</summary>
        private VisualElement FormPick(List<BattleForm> forms, string value, bool allowAny, Action<string> save)
        {
            string Label(string v) => v == SpeciesForms.AnyForm ? "cualquier forma" : v.Length == 0 ? "la normal"
                : forms.FirstOrDefault(f => Same(f.Id, v)) is BattleForm f ? (f.Name.Length > 0 ? f.Name : f.Id) : v + " (no existe)";
            return ContentWidgets.Dropdown(_shell, Label(value), () =>
            {
                var items = new List<MenuItem> { new MenuItem("La normal", () => save(""), isChecked: value.Length == 0) };
                if (allowAny) items.Add(new MenuItem("Cualquier forma", () => save(SpeciesForms.AnyForm), isChecked: value == SpeciesForms.AnyForm));
                items.AddRange(forms.Select(f => new MenuItem(f.Name.Length > 0 ? f.Name : f.Id, () => save(f.Id), isChecked: Same(f.Id, value))));
                return items;
            });
        }

        /// <summary>The control of a value: a number, a list (items, moves, types, weathers...) or a choice — never a code.</summary>
        private VisualElement ValueControl(EvoValueKind kind, string[] options, string value, Action<string> save, string none = "Elegir…")
        {
            switch (kind)
            {
                case EvoValueKind.Number:
                    return Ui.MiniNumber(int.TryParse(value, out var n) ? n : 0, 0, 255, v => save(v.ToString()), null, 60);
                case EvoValueKind.Item: return ContentWidgets.RefDropdown(_shell, ContentSchemas.Items, value, save, none);
                case EvoValueKind.Move: return ContentWidgets.RefDropdown(_shell, ContentSchemas.Moves, value, save, none);
                case EvoValueKind.Type: return ContentWidgets.RefDropdown(_shell, ContentSchemas.Types, value, save, none);
                case EvoValueKind.Species: return ContentWidgets.RefDropdown(_shell, ContentSchemas.Species, value, save, none);
                case EvoValueKind.Nature: return ContentWidgets.RefDropdown(_shell, ContentSchemas.Natures, value, save, none);
                case EvoValueKind.Weather: return ContentWidgets.RefDropdown(_shell, ContentSchemas.Weathers, value, save, none);
                case EvoValueKind.Choice:
                    return ContentWidgets.Dropdown(_shell, Evolutions.ChoiceLabel(value), () => (options ?? new string[0])
                        .Select(o => new MenuItem(Evolutions.ChoiceLabel(o), () => save(o), isChecked: o == value)).ToList());
                default:
                {
                    var box = ContentWidgets.WrapBox(value, save);
                    box.style.minWidth = 120;
                    return box;
                }
            }
        }
    }
}
