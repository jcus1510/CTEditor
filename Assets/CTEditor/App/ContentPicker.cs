using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using CTEditor.Content;

namespace CTEditor.App
{
    /// <summary>Chooses a piece of content of a category (a type, a move, a species...) with a search box and a virtual list.</summary>
    public static class ContentPicker
    {
        public static void Show(AppShell shell, string category, Action<string> picked, string except = null, string title = null)
        {
            var content = shell.Content;
            var schema = ContentSchemas.Find(category);
            if (content == null || schema == null) return;
            var table = content.Db.Table(category);
            var all = table.Records.Where(r => table.IdOf(r).Length > 0 && !string.Equals(table.IdOf(r), except, StringComparison.OrdinalIgnoreCase)).ToList();
            var d = shell.ShowDialog(title ?? $"Elegir {schema.Noun}", 40, 70);
            var search = Ui.TextBox("", "");
            search.tooltip = "Buscar por nombre o id";
            var count = Ui.Text("", 0.85f, dim: true).NoShrink();
            d.Body.Add(Ui.Row(8).With(search.Grow(), count));
            var shown = all;
            var list = new ListView
            {
                fixedItemHeight = Ui.ControlHeight + 6,
                selectionType = SelectionType.Single,
                makeItem = () =>
                {
                    var row = Ui.Row(8).Pad(8, 0);
                    row.style.height = Length.Percent(100);
                    row.style.alignItems = Align.Center;
                    var mark = new VisualElement { name = "mark" }.Round(3);
                    mark.style.width = 6; mark.style.height = Ui.FontSize + 4;
                    var name = Ui.Text("").Grow(); name.name = "name";
                    var id = Ui.Text("", 0.82f, dim: true).NoShrink(); id.name = "id";
                    return row.With(mark, name, id);
                },
            };
            list.bindItem = (e, i) =>
            {
                var r = shown[i];
                e.Q<Label>("name").text = ContentLook.Label(table, r);
                e.Q<Label>("id").text = table.IdOf(r);
                var c = ContentLook.Mark(content.Db, table, r);
                e.Q("mark").style.backgroundColor = c ?? new Color(0, 0, 0, 0);
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
                shown = q.Length == 0 ? all : all.Where(r => ContentLook.Matches(table, r, q)).ToList();
                list.itemsSource = shown;
                list.Rebuild();
                count.text = $"{shown.Count} de {all.Count}";
            }
            search.RegisterValueChangedCallback(_ => Apply());
            search.RegisterCallback<KeyDownEvent>(e =>
            {
                if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && shown.Count > 0) Choose(shown[0]);
            });
            d.Body.Add(list);
            d.Buttons.With(Ui.Hint("Doble clic o Intro elige."), Ui.Spacer(), Ui.Button("Cancelar", () => shell.CloseDialog(d)));
            Apply();
            search.schedule.Execute(() => search.Focus()).ExecuteLater(50);
        }
    }

    /// <summary>How a piece of content looks in lists: its label («#025 Pikachu») and its colour mark (its type, its colour).</summary>
    public static class ContentLook
    {
        public static string Label(ContentTable t, ContentRecord r)
        {
            var name = t.NameOf(r);
            if (t.Schema.Key == ContentSchemas.Species && int.TryParse(r["numero"], out var n) && n > 0) return $"#{n:000} {name}";
            return name;
        }

        public static bool Matches(ContentTable t, ContentRecord r, string q) =>
            t.NameOf(r).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
            || t.IdOf(r).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
            || r["nombre_en"].IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
            || (int.TryParse(q, out var n) && r["numero"] == n.ToString());

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
            return b;
        }
    }
}
