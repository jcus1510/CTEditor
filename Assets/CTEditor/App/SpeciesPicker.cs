using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using CTEditor.World.Domain;

namespace CTEditor.App
{
    /// <summary>
    /// El selector de especies de toda la aplicación (encuentros, perfiles de prueba, entrenadores...): todas, en orden
    /// de Pokédex, con miniatura, tipos de color y filtros. También la miniatura y el nombre de una especie.
    /// </summary>
    public static class SpeciesPicker
    {
        public static string Name(AppShell shell, string id)
        {
            if (string.IsNullOrEmpty(id)) return "(elige)";
            var found = shell.Maps?.Species?.All().FirstOrDefault(s => s.id == id) ?? default;
            return found.id == null ? id + " (?)" : found.name;
        }

        /// <summary>
        /// The species picker: every species of the project in Pokédex order (a virtual list: all of them, fast), with
        /// search (name, id or «#25») and filters: up to two types (exact or not), egg group, generation, alternate forms
        /// and legendaries. Double click or Intro picks.
        /// </summary>
        public static void Show(AppShell shell, Action<string> picked)
        {
            var all = shell.Maps?.Species?.Entries() ?? new SpeciesEntry[0];
            var types = shell.Maps?.Species?.Types() ?? new Dictionary<string, (string name, string color)>();
            var eggs = shell.Maps?.Species?.EggGroups() ?? new Dictionary<string, (string name, string color)>();
            var d = shell.ShowDialog("Elegir especie", 54, 80);
            var filter = new SpeciesFilter();
            IReadOnlyList<SpeciesEntry> shown = all;

            var search = Ui.TextBox(null, "", null);
            search.tooltip = "Nombre, id o número («#25»)";
            var count = Ui.Text("", 0.82f, dim: true).NoShrink();
            var top = Ui.Row(10);
            top.With(search.Grow(), count);

            var filters = Ui.Column(6);
            var list = new ListView
            {
                fixedItemHeight = Mathf.Round(Ui.FontSize * 2.9f),
                selectionType = SelectionType.Single,
                makeItem = () =>
                {
                    var row = Ui.Row(10).Pad(10, 0);
                    row.style.height = Length.Percent(100);
                    var num = Ui.Text("", 0.82f, dim: true).NoShrink(); num.style.width = 42; num.name = "num";
                    var pic = new VisualElement { name = "pic" };
                    var name = Ui.Text("", bold: true).Grow(); name.name = "name";
                    var badges = Ui.Row(4).NoShrink(); badges.name = "badges";
                    row.With(num, pic, name, badges);
                    return row;
                },
            };
            list.bindItem = (e, i) =>
            {
                var sp = shown[i];
                e.Q<Label>("num").text = sp.Number > 0 ? "#" + sp.Number.ToString("000") : "";
                var nameLabel = e.Q<Label>("name");
                nameLabel.text = sp.Name + (sp.IsAlternateForm ? "  (forma)" : "") + (sp.Legendary ? "  ★" : "");
                var pic = e.Q("pic");
                pic.Clear();
                pic.Add(Picture(shell, sp.Id, Mathf.Round(Ui.FontSize * 2.2f)));
                var badges = e.Q("badges");
                badges.Clear();
                foreach (var t in sp.Types) badges.Add(TypeBadge(t, types));
            };
            list.style.flexGrow = 1;
            list.itemsSource = (System.Collections.IList)shown;
#if UNITY_2022_2_OR_NEWER
            list.itemsChosen += items => { foreach (var it in items) { shell.CloseDialog(d); picked(((SpeciesEntry)it).Id); return; } };
#else
            list.onItemsChosen += items => { foreach (var it in items) { shell.CloseDialog(d); picked(((SpeciesEntry)it).Id); return; } };
#endif

            void Apply()
            {
                filter.Text = search.value ?? "";
                shown = filter.Apply(all);
                list.itemsSource = shown.ToList();
                list.Rebuild();
                count.text = $"{shown.Count} de {all.Count}";
            }

            void BuildFilters()
            {
                filters.Clear();
                // Types: up to two; the colour of each type.
                var typeRow = Ui.Row(0).Wrap();
                typeRow.Add(Ui.Text("Tipos", 0.82f, dim: true).Margin(0, 0, 8, 0));
                foreach (var kv in types)
                {
                    var id = kv.Key;
                    bool on = filter.Types.Contains(id);
                    var chip = Ui.Chip(kv.Value.name, on, () =>
                    {
                        if (filter.Types.Contains(id)) filter.Types.Remove(id);
                        else { if (filter.Types.Count == 2) filter.Types.RemoveAt(0); filter.Types.Add(id); }
                        BuildFilters(); Apply();
                    });
                    Small(chip);
                    if (ColorUtility.TryParseHtmlString("#" + kv.Value.color, out var c))
                    {
                        chip.style.borderLeftWidth = 4; chip.style.borderLeftColor = c;
                        if (on) chip.style.backgroundColor = Ui.Mix(c, Color.black, 0.25f);
                    }
                    typeRow.Add(chip.Margin(0, 2, 3, 2));
                }
                if (types.Count == 0) typeRow.Add(Ui.Hint("(copia tipos.csv de un pack para filtrar por tipos)"));
                filters.Add(typeRow);

                var opts = Ui.Row(0).Wrap();
                var exact = Ui.Check("Exactos (solo esos tipos)", filter.ExactTypes, v => { filter.ExactTypes = v; Apply(); });
                exact.tooltip = "Con dos tipos elegidos: solo las que tienen esos dos. Con uno: solo las de un único tipo.";
                opts.Add(exact.Margin(0, 0, 12, 0));
                opts.Add(Menu("Grupo huevo", filter.EggGroup.Length == 0 ? "todos" : (eggs.TryGetValue(filter.EggGroup, out var eg) ? eg.name : filter.EggGroup),
                    new[] { ("", "todos") }.Concat(eggs.Select(kv => (kv.Key, kv.Value.name))).ToList(), v => { filter.EggGroup = v; BuildFilters(); Apply(); }));
                opts.Add(Menu("Generación", filter.Generation == 0 ? "todas" : filter.Generation + ".ª",
                    Enumerable.Range(0, 10).Select(g => (g.ToString(), g == 0 ? "todas" : g + ".ª")).ToList(), v => { filter.Generation = int.Parse(v); BuildFilters(); Apply(); }));
                opts.Add(Menu("Formas", filter.Forms == 0 ? "con formas" : filter.Forms == 1 ? "sin formas" : "solo formas",
                    new List<(string, string)> { ("0", "con formas"), ("1", "sin formas"), ("2", "solo formas alternativas") }, v => { filter.Forms = int.Parse(v); BuildFilters(); Apply(); }));
                opts.Add(Ui.Check("Solo legendarios", filter.OnlyLegendary, v => { filter.OnlyLegendary = v; Apply(); }).Margin(0, 0, 12, 0));
                opts.Add(Ui.Button("Quitar filtros", () => { filter = new SpeciesFilter { Text = search.value ?? "" }; BuildFilters(); Apply(); }, Ui.ButtonKind.Flat));
                filters.Add(opts);
            }

            VisualElement Menu(string label, string current, List<(string value, string text)> items, Action<string> chosen)
            {
                var row = Ui.Row(4).Margin(0, 2, 12, 2);
                Button b = null;
                b = Ui.Button(current + "  ", () =>
                {
                    var r = b.worldBound;
                    shell.ShowMenu(new Vector2(r.x, r.yMax + 2), items.Select(it => { var v = it.value; return new MenuItem(it.text, () => chosen(v)); }).ToList());
                }, Ui.ButtonKind.Normal, label);
                Small(b);
                row.With(Ui.Text(label, 0.82f, dim: true), b);
                return row;
            }

            search.RegisterValueChangedCallback(_ => Apply());
            search.RegisterCallback<KeyDownEvent>(e =>
            {
                if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && shown.Count > 0)
                {
                    e.StopPropagation();
                    var first = list.selectedIndex >= 0 && list.selectedIndex < shown.Count ? shown[list.selectedIndex] : shown[0];
                    shell.CloseDialog(d);
                    picked(first.Id);
                }
                else if (e.keyCode == KeyCode.DownArrow) { list.selectedIndex = Mathf.Min(list.selectedIndex + 1, shown.Count - 1); list.ScrollToItem(list.selectedIndex); e.StopPropagation(); }
                else if (e.keyCode == KeyCode.UpArrow) { list.selectedIndex = Mathf.Max(list.selectedIndex - 1, 0); list.ScrollToItem(list.selectedIndex); e.StopPropagation(); }
            }, TrickleDown.TrickleDown);

            d.Body.With(top, filters, Ui.Separator(), list);
            d.Buttons.With(Ui.Hint("Doble clic o Intro: elegir"), Ui.Spacer(), Ui.Button("Cancelar", () => shell.CloseDialog(d)),
                Ui.Button("Elegir", () =>
                {
                    if (list.selectedIndex < 0 || list.selectedIndex >= shown.Count) { shell.Warn("Elige una especie de la lista."); return; }
                    var sp = shown[list.selectedIndex];
                    shell.CloseDialog(d);
                    picked(sp.Id);
                }, Ui.ButtonKind.Primary));
            BuildFilters();
            Apply();
            if (all.Count == 0) d.Body.Add(Ui.Hint("No hay especies: copia los datos de un pack (Proyecto → Copiar datos de un pack)."));
            search.schedule.Execute(() => search.Q(className: TextField.inputUssClassName)?.Focus()).ExecuteLater(50);
        }

        public static Button Small(Button b)
        {
            b.style.fontSize = Mathf.Round(Ui.FontSize * 0.82f);
            b.style.height = Ui.ControlHeight - 8; b.style.minHeight = Ui.ControlHeight - 8;
            b.style.paddingLeft = 8; b.style.paddingRight = 8;
            return b;
        }

        /// <summary>A small type label with its colour (from datos/tipos.csv).</summary>
        private static VisualElement TypeBadge(string typeId, IReadOnlyDictionary<string, (string name, string color)> types)
        {
            string label = types.TryGetValue(typeId, out var t) ? t.name : typeId;
            var l = Ui.Text(label, 0.72f, bold: true);
            l.style.color = Color.white;
            l.Pad(6, 1).Round(8);
            l.style.backgroundColor = ColorUtility.TryParseHtmlString("#" + (t.color ?? ""), out var c) ? Ui.Mix(c, Color.black, 0.2f) : Ui.C("panel_alt");
            return l;
        }

        /// <summary>The species picture: its battle sprite or icon if the project has one, or its initial.</summary>
        public static VisualElement Picture(AppShell shell, string id, float size)
        {
            var box = new VisualElement().Bg("panel_alt").Round(size / 2);
            box.style.width = size; box.style.height = size;
            box.style.flexShrink = 0;
            box.style.alignItems = Align.Center;
            box.style.justifyContent = Justify.Center;
            box.style.overflow = Overflow.Hidden;
            Texture2D tex = null;
            if (!string.IsNullOrEmpty(id) && shell.ProjectRoot != null)
                foreach (var folder in new[] { "iconos", "combate" })
                {
                    var path = System.IO.Path.Combine(shell.ProjectRoot, CTEditor.Project.ProjectLayout.GraphicsFolder, folder, id + ".png");
                    if (System.IO.File.Exists(path) && (tex = Textures.Thumbnail(path, 96)) != null) break;
                }
            if (tex != null)
            {
                var img = new Image { image = tex, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                img.style.width = size; img.style.height = size;
                box.Add(img);
            }
            else
            {
                var n = Name(shell, id);
                var l = Ui.Text(string.IsNullOrEmpty(n) ? "?" : n.Substring(0, 1).ToUpperInvariant(), 0.95f, bold: true);
                l.style.unityTextAlign = TextAnchor.MiddleCenter;
                l.pickingMode = PickingMode.Ignore;
                box.Add(l);
            }
            return box;
        }

    }
}
