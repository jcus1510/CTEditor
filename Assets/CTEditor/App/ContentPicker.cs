using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using CTEditor.Content;

namespace CTEditor.App
{
    /// <summary>
    /// Chooses a piece of content (a type, a move, a species...) with a search box, FILTERS that fit the category (types
    /// and categories of moves, types of species, categories of abilities, pockets of items) and a line of information
    /// per row. «only» shows just some (the abilities of a species) with a box to see all the others.
    /// </summary>
    public static class ContentPicker
    {
        public static void Show(AppShell shell, string category, Action<string> picked, string except = null, string title = null,
            IReadOnlyCollection<string> only = null, string onlyLabel = null, ContentTable source = null)
        {
            var content = shell.Content;
            var schema = ContentSchemas.Find(category);
            if (content == null || schema == null) return;
            var db = content.Db;
            var table = source ?? db.Table(category);
            var all = table.Records.Where(r => table.IdOf(r).Length > 0 && !string.Equals(table.IdOf(r), except, StringComparison.OrdinalIgnoreCase)).ToList();
            // Built-in curves are choosable even without a row.
            if (category == ContentSchemas.Curves && source == null)
                foreach (var c in ClassicCurves.All) if (!table.Contains(c.id)) all.Add(ClassicCurves.Row(c.id));
            if (source == null) all.AddRange(ClassicContent.Records(category, table)); // statuses, weathers... the engine already has
            bool restricted = only != null && only.Count > 0;
            var d = shell.ShowDialog(title ?? $"Elegir {schema.Noun}", 46, 76);
            var search = Ui.TextBox("", "");
            search.tooltip = "Buscar por nombre o id";
            var count = Ui.Text("", 0.85f, dim: true).NoShrink();
            d.Body.Add(Ui.Row(8).With(search.Grow(), count));
            var filters = Ui.Column(4);
            d.Body.Add(filters);
            string type = null, kind = null;
            var shown = all;
            var list = new ListView
            {
                fixedItemHeight = Ui.ControlHeight + 8,
                selectionType = SelectionType.Single,
                makeItem = () =>
                {
                    var row = Ui.Row(8).Pad(8, 0);
                    row.style.height = Length.Percent(100);
                    row.style.alignItems = Align.Center;
                    var mark = new VisualElement { name = "mark" }.Round(3);
                    mark.style.width = 6; mark.style.height = Ui.FontSize + 4;
                    var name = Ui.Text("", bold: true); name.name = "name"; name.style.width = 170;
                    name.style.overflow = Overflow.Hidden; name.style.textOverflow = TextOverflow.Ellipsis;
                    var badges = Ui.Row(4).NoShrink(); badges.name = "badges";
                    var sub = Ui.Text("", 0.82f, dim: true).Grow(); sub.name = "sub";
                    var id = Ui.Text("", 0.78f, dim: true).NoShrink(); id.name = "id";
                    return row.With(mark, name, badges, sub, id);
                },
            };
            list.bindItem = (e, i) =>
            {
                if (i >= shown.Count) return;
                var r = shown[i];
                e.Q<Label>("name").text = ContentLook.Label(table, r);
                e.Q<Label>("id").text = table.IdOf(r);
                e.Q<Label>("sub").text = ContentLook.Subtitle(db, category, r);
                var c = ContentLook.Mark(db, table, r);
                e.Q("mark").style.backgroundColor = c ?? new Color(0, 0, 0, 0);
                var badges = e.Q("badges");
                badges.Clear();
                foreach (var t in ContentLook.TypesOf(category, r)) badges.Add(ContentWidgets.TypeChip(db, t));
            };
            list.style.flexGrow = 1;
            list.itemsSource = shown;
            void Choose(ContentRecord r)
            {
                shell.CloseDialog(d);
                picked(table.IdOf(r));
            }
#if UNITY_2022_2_OR_NEWER
            list.itemsChosen += items => { foreach (var it in items) { Choose((ContentRecord)it); return; } };
#else
            list.onItemsChosen += items => { foreach (var it in items) { Choose((ContentRecord)it); return; } };
#endif
            void Apply()
            {
                var q = (search.value ?? "").Trim();
                IEnumerable<ContentRecord> rows = all;
                if (restricted) rows = rows.Where(r => only.Contains(table.IdOf(r), StringComparer.OrdinalIgnoreCase));
                if (q.Length > 0) rows = rows.Where(r => ContentLook.Matches(table, r, q));
                if (type != null) rows = rows.Where(r => ContentLook.TypesOf(category, r).Contains(type));
                if (kind != null) rows = rows.Where(r => ContentLook.KindOf(category, r) == kind);
                shown = rows.ToList();
                list.itemsSource = shown;
                list.Rebuild();
                count.text = $"{shown.Count} de {all.Count}";
            }
            void BuildFilters()
            {
                filters.Clear();
                if (restricted)
                    filters.Add(Ui.Check(onlyLabel ?? "Mostrar también las que no le corresponden", false, on => { restricted = !on; Apply(); }));
                if (category == ContentSchemas.Moves || category == ContentSchemas.Species)
                {
                    var row = Ui.Row(0).Wrap();
                    foreach (var t in db.Table(ContentSchemas.Types).Ids.ToList())
                    {
                        var id = t;
                        row.Add(ContentWidgets.TypeChip(db, id, type == id, () => { type = type == id ? null : id; BuildFilters(); Apply(); }));
                    }
                    filters.Add(row);
                }
                var kinds = all.Select(r => ContentLook.KindOf(category, r)).Where(k => !string.IsNullOrEmpty(k)).Distinct().OrderBy(k => k).ToList();
                if (kinds.Count > 1 && kinds.Count <= 30)
                {
                    var row = Ui.Row(3).Wrap();
                    foreach (var k in kinds)
                    {
                        var kk = k;
                        row.Add(Ui.Chip(ContentWidgets.CategoryLabel(k), kind == kk, () => { kind = kind == kk ? null : kk; BuildFilters(); Apply(); }).Margin(0, 0, 2, 2));
                    }
                    filters.Add(row);
                }
            }
            search.RegisterValueChangedCallback(_ => Apply());
            search.RegisterCallback<KeyDownEvent>(e =>
            {
                if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && shown.Count > 0) Choose(shown[0]);
            });
            d.Body.Add(list);
            d.Buttons.With(Ui.Hint("Doble clic o Intro elige."), Ui.Spacer(), Ui.Button("Cancelar", () => shell.CloseDialog(d)));
            BuildFilters();
            Apply();
            search.schedule.Execute(() => search.Focus()).ExecuteLater(50);
        }
    }

    /// <summary>How a piece of content looks in lists: its label, its colour mark, its types, its kind and a line of information.</summary>
    public static class ContentLook
    {
        public static string Label(ContentTable t, ContentRecord r)
        {
            var name = t.NameOf(r);
            switch (t.Schema.Key)
            {
                case ContentSchemas.Species when int.TryParse(r["numero"], out var n) && n > 0: return $"#{n:000} {name}";
                case ContentSchemas.Sets:
                {
                    var sp = r["especie"].Trim();
                    return $"{(sp.Length > 0 ? char.ToUpperInvariant(sp[0]) + sp.Substring(1).Replace('_', ' ') : "?")} · {r["formato"].Trim().ToUpperInvariant()} · {r["nombre"]}";
                }
            }
            return name;
        }

        public static bool Matches(ContentTable t, ContentRecord r, string q) =>
            Label(t, r).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
            || t.IdOf(r).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
            || r["nombre_en"].IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
            || (int.TryParse(q, out var n) && r["numero"] == n.ToString());

        public static IEnumerable<string> TypesOf(string category, ContentRecord r) => category switch
        {
            ContentSchemas.Species => r["tipos"].Split('|', ',').Select(x => x.Trim()).Where(x => x.Length > 0),
            ContentSchemas.Moves => r["tipo"].Trim().Length > 0 ? new[] { r["tipo"].Trim() } : new string[0],
            _ => new string[0],
        };

        /// <summary>The «kind» used by the second row of filters: move category, item pocket, ability category, set format...</summary>
        public static string KindOf(string category, ContentRecord r) => category switch
        {
            ContentSchemas.Moves => r["categoria"].Trim().ToLowerInvariant(),
            ContentSchemas.Items => r["categoria"].Trim(),
            ContentSchemas.Abilities => AbilityCategories.Of(r),
            ContentSchemas.Sets => r["formato"].Trim().ToLowerInvariant(),
            ContentSchemas.Trainers => r["clase"].Trim(),
            _ => null,
        };

        /// <summary>A small line of information: «↑ At. esp. ↓ Ataque» for natures, «Físico · 75 · 100%» for moves...</summary>
        public static string Subtitle(ContentDatabase db, string category, ContentRecord r)
        {
            switch (category)
            {
                case ContentSchemas.Natures:
                    var up = r["sube"].Trim(); var down = r["baja"].Trim();
                    return up.Length == 0 || up == down ? "neutra" : $"↑ {StatNames.Label(up)}  ↓ {StatNames.Label(down)}";
                case ContentSchemas.Moves:
                    return $"{ContentWidgets.CategoryLabel(r["categoria"])} · pot. {(r["potencia"].Trim().Length == 0 ? "—" : r["potencia"])} · prec. {r["precision"]}";
                case ContentSchemas.Abilities: return AbilityCategories.Of(r);
                case ContentSchemas.Items: return r["categoria"] + (r["precio"].Trim().Length > 0 ? $" · {r["precio"]} ₽" : "");
                case ContentSchemas.Species:
                    int total = StatNames.Ids.Length == 6 ? new[] { "ps", "ataque", "defensa", "atq_esp", "def_esp", "velocidad" }.Sum(c => int.TryParse(r[c], out var v) ? v : 0) : 0;
                    return $"total {total}";
                case ContentSchemas.Curves: return r["forma"];
                default: return "";
            }
        }

        /// <summary>The colour of a row: the colour of its type (species, moves) or its own colour (types, egg groups, weathers...).</summary>
        public static Color? Mark(ContentDatabase db, ContentTable t, ContentRecord r)
        {
            string key = t.Schema.Key;
            if (key == ContentSchemas.Species) return TypeColor(db, r["tipos"].Split('|', ',')[0].Trim());
            if (key == ContentSchemas.Moves) return TypeColor(db, r["tipo"]);
            if (key == ContentSchemas.TypeChart) return TypeColor(db, t.IdOf(r));
            if (t.Columns.Contains("color")) return Hex(r["color"]);
            return null;
        }

        public static Color? TypeColor(ContentDatabase db, string typeId)
        {
            if (string.IsNullOrWhiteSpace(typeId)) return null;
            var type = db.Table(ContentSchemas.Types).Find(typeId.Trim());
            return type == null ? (Color?)null : Hex(type["color"]);
        }

        public static Color? Hex(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return null;
            hex = hex.Trim();
            return ColorUtility.TryParseHtmlString(hex.StartsWith("#") ? hex : "#" + hex, out var c) ? c : (Color?)null;
        }

        /// <summary>A small coloured badge with a name (a type).</summary>
        public static VisualElement Badge(string text, Color? color)
        {
            var b = Ui.Text(text, 0.82f, bold: true).Pad(7, 1).Round(4);
            var c = color ?? Ui.C("panel_alt");
            b.style.backgroundColor = c;
            b.style.color = (c.r * 0.299f + c.g * 0.587f + c.b * 0.114f) > 0.6f ? Color.black : Color.white;
            b.style.unityTextAlign = TextAnchor.MiddleCenter;
            return b;
        }
    }
}
