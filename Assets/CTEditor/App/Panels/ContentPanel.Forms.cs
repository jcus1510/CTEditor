using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using CTEditor.Content;

namespace CTEditor.App
{
    /// <summary>The right side of the data windows: the chosen piece in foldable sections, with an editor per kind of column.</summary>
    public sealed partial class ContentPanel
    {
        private string Key(string section) => _category + "_" + section;

        // ── The chosen piece ─────────────────────────────────────────────────────────────────────

        private void ShowSelected()
        {
            _right.Clear();
            if (_category == ContentSchemas.TypeChart) { Matrix(); return; }
            var r = Selected;
            var s = Schema;
            if (r == null)
            {
                var empty = Ui.EmptyState(s.Icon, Table.Records.Count == 0 ? $"Sin {s.Title.ToLowerInvariant()}" : $"Elige {s.Un} {s.Noun}",
                    Table.Records.Count == 0 ? $"El proyecto no tiene {s.Title.ToLowerInvariant()} ({s.File}). Crea {s.El} primer{(s.Feminine ? "a" : "o")} (con plantillas) o cambia de generación."
                        : "Elígela en la lista de la izquierda, o crea una nueva.",
                    $"{s.Nuevo} {s.Noun}", NewItem,
                    _category == ContentSchemas.Curves && Table.Records.Count == 0 ? "Crear las 6 clásicas" : null,
                    _category == ContentSchemas.Curves ? (Action)CreateClassicCurves : null);
                _right.Add(empty);
                return;
            }
            var box = Ui.Column(8).Pad(14, 10);
            _right.Add(box);
            var id = Table.IdOf(r);

            var head = Ui.Row(10).Wrap();
            head.style.alignItems = Align.Center;
            head.Add(Ui.Title(_category == ContentSchemas.Sets ? ContentLook.Label(Table, r) : Table.NameOf(r)));
            head.Add(Ui.Text(id, 0.85f, dim: true));
            foreach (var b in Badges(r)) head.Add(b);
            box.Add(head);

            foreach (var i in ContentChecks.Cached(C.Db, Table).Where(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase)))
                box.Add(Ui.Hint((i.Level == ContentIssueLevel.Error ? "✖ " : "⚠ ") + i.Text).Colored(i.Level == ContentIssueLevel.Error ? "error" : "aviso"));

            if (_category == ContentSchemas.Species) SpeciesForm(box, r);
            else if (_category == ContentSchemas.Sets) SetForm(box, r);
            else GenericForm(box, r);

            var uses = C.UsesOf(_category, id);
            var usesBox = Ui.Column(2);
            if (uses.Count == 0) usesBox.Add(Ui.Hint("Nadie lo usa: se puede borrar sin problemas."));
            foreach (var u in uses.Take(80)) usesBox.Add(UseRow(u));
            if (uses.Count > 80) usesBox.Add(Ui.Hint($"… y {uses.Count - 80} más (botón «¿Quién lo usa?»)."));
            box.Add(ContentWidgets.Section(_shell, Key("usos"), $"Lo usan ({uses.Count})", usesBox, open: uses.Count <= 10));
        }

        private IEnumerable<VisualElement> Badges(ContentRecord r)
        {
            foreach (var t in ContentLook.TypesOf(_category, r)) yield return ContentWidgets.TypeChip(C.Db, t);
            if (_category == ContentSchemas.Moves && r["categoria"].Length > 0) yield return ContentLook.Badge(ContentWidgets.CategoryLabel(r["categoria"]), null);
            if (_category == ContentSchemas.Abilities) yield return ContentLook.Badge(AbilityCategories.Of(r), null);
            if (Table.Columns.Contains("color") && ContentLook.Hex(r["color"]) is Color c) yield return Ui.Swatch(c, Ui.FontSize + 2);
            if (ContentChecks.IsYes(r["legendario"])) yield return ContentLook.Badge("Legendario", new Color(0.85f, 0.7f, 0.2f));
        }

        private string TypeName(string id) => C.Db.Table(ContentSchemas.Types).Find(id)?["nombre"] is string n && n.Length > 0 ? n : id;

        private string NameOf(string category, string id) => C.Db.NameOf(category, id) ?? id;

        private void Set(string column, string value) => C.SetValue(_category, _selected, column, value);

        // ── Generic form: one foldable section per group of the schema ────────────────────────────

        private void GenericForm(VisualElement box, ContentRecord r)
        {
            if (_category == ContentSchemas.Natures) box.Add(ContentWidgets.Section(_shell, Key("tabla"), "Las 25 de un vistazo", NatureTable(r)));
            if (_category == ContentSchemas.Curves) box.Add(ContentWidgets.Section(_shell, Key("grafico"), "Experiencia por nivel", CurveTable(r)));
            if (_category == ContentSchemas.Types) box.Add(ContentWidgets.Section(_shell, Key("tabla"), "Tabla de tipos", TypeSummary(r)));
            if (_category == ContentSchemas.EggGroups) box.Add(ContentWidgets.Section(_shell, Key("miembros"), "Especies del grupo", EggGroupMembers(Table.IdOf(r))));
            Groups(box, r, Schema.Columns.Where(c => c.Name != Schema.IdColumn));
        }

        private void Groups(VisualElement box, ContentRecord r, IEnumerable<ColumnSpec> columns)
        {
            foreach (var group in columns.GroupBy(c => c.Group))
            {
                var col = Ui.Column(6);
                foreach (var c in group) col.Add(Field(r, c));
                box.Add(ContentWidgets.Section(_shell, Key(group.Key), group.Key, col));
            }
            var others = Table.Columns.Where(c => Schema.Column(c) == null).ToList();
            if (others.Count > 0)
            {
                var col = Ui.Column(6);
                foreach (var c in others) col.Add(Field(r, new ColumnSpec(c, c)));
                box.Add(ContentWidgets.Section(_shell, Key("otros"), "Otros (columnas propias)", col, open: false));
            }
        }

        /// <summary>A labelled row with the editor of that column.</summary>
        private VisualElement Field(ContentRecord r, ColumnSpec col)
        {
            // The effect blocks take the whole width (their section already says what they are).
            if (col.Name == "efectos" && (_category == ContentSchemas.Items || _category == ContentSchemas.Abilities || _category == ContentSchemas.Moves)) return Editor(r, col);
            var row = Ui.Row(8);
            row.style.alignItems = Align.FlexStart;
            var label = Ui.Text(col.Label + (col.Required ? " *" : ""), 0.9f).NoShrink();
            label.style.width = 150;
            label.style.marginTop = 5;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.tooltip = string.IsNullOrEmpty(col.Help) ? col.Name : col.Help;
            row.Add(label);
            var editor = Editor(r, col);
            editor.style.flexGrow = 1;
            editor.style.flexShrink = 1;
            editor.style.minWidth = 0;
            row.Add(editor);
            return row;
        }

        /// <summary>The editor of a column: lists, dropdowns, numbers and tables — never codes to type.</summary>
        private VisualElement Editor(ContentRecord r, ColumnSpec col)
        {
            var value = r[col.Name];
            string name = col.Name;
            void Save(string v) => Set(name, v);
            switch (_category + "." + col.Name)
            {
                case "especies.evs": return ContentWidgets.StatPairs(_shell, value, Save);
                case "habilidades.categoria":
                {
                    var guess = AbilityCategories.Guess(r["efectos"]);
                    var items = new List<MenuItem> { new MenuItem($"Automática ({guess})", () => Save("")) };
                    items.AddRange(AbilityCategories.All.Select(c => new MenuItem(c, () => Save(c), isChecked: value.Trim() == c)));
                    return ContentWidgets.Dropdown(_shell, value.Trim().Length > 0 ? value : $"Automática: {guess}", () => items);
                }
                case "objetos.efectos": return EffectBlocksEditor.Build(_shell, value, false, Save, Key("bloques"));
                case "habilidades.efectos": return EffectBlocksEditor.Build(_shell, value, true, Save, Key("bloques"));
                case "movimientos.efectos": return MoveEffectsEditor.Effects(_shell, value, Save);
                case "movimientos.efecto_z": return MoveEffectsEditor.Effects(_shell, value, Save);
                case "movimientos.requisitos": return MoveEffectsEditor.Conditions(_shell, value, Save, "Funciona siempre.");
                case "movimientos.potencia_mod": return MoveEffectsEditor.PowerModifiers(_shell, value, Save);
                case "movimientos.tipo_clima": return MoveEffectsEditor.TypeByWeather(_shell, value, Save);
                case "entrenadores.equipo":
                case "equipos.equipo":
                    return TeamEditor(value, Save);
            }
            switch (col.Kind)
            {
                case ColumnKind.Bool:
                    return Ui.Check("", ContentChecks.IsYes(value), on => Save(on ? "si" : "no"));
                case ColumnKind.Choice:
                {
                    var items = col.Options.Select(o => new MenuItem(ContentWidgets.CategoryLabel(o) == o ? StatNames.Label(o) : ContentWidgets.CategoryLabel(o), () => Save(o),
                        isChecked: string.Equals(value.Trim(), o, StringComparison.OrdinalIgnoreCase))).ToList();
                    items.Insert(0, new MenuItem("— (vacío)", () => Save("")));
                    var shown = value.Trim().Length == 0 ? "—" : ContentWidgets.CategoryLabel(value) == value ? StatNames.Label(value) : ContentWidgets.CategoryLabel(value);
                    return ContentWidgets.Dropdown(_shell, shown, () => items);
                }
                case ColumnKind.Color:
                {
                    var c = Ui.Row(6);
                    var swatch = Ui.Swatch(ContentLook.Hex(value) ?? new Color(0, 0, 0, 0), Ui.ControlHeight - 4);
                    swatch.RegisterCallback<ClickEvent>(_ => ColorPicker.Show(_shell, col.Label, ContentLook.Hex(value) ?? Color.gray, picked => Save(ColorUtility.ToHtmlStringRGB(picked))));
                    swatch.tooltip = "Clic: elegir el color";
                    c.With(swatch, ContentWidgets.NumberBox(value, Save, 110));
                    return c;
                }
                case ColumnKind.Ref:
                {
                    var row = Ui.Row(4);
                    row.style.alignItems = Align.Center;
                    row.Add(ContentWidgets.RefDropdown(_shell, col.Targets[0], value.Trim(), Save));
                    if (value.Trim().Length > 0)
                    {
                        row.Add(Ui.IconButton("cerrar", () => Save(""), "Quitar", iconSize: Ui.IconSize * 0.7f));
                        if (C.Db.Has(col.Targets[0], value.Trim()) && col.Targets[0] != _category)
                            row.Add(Ui.IconButton("siguiente", () => _shell.OpenContentItem(col.Targets[0], value.Trim()), "Abrir su ficha", iconSize: Ui.IconSize * 0.8f));
                    }
                    return row;
                }
                case ColumnKind.RefList:
                case ColumnKind.SlashRefs:
                    return RefListEditor(col, value, Save);
                case ColumnKind.LevelRefs:
                    return LevelEditor(col, value, Save);
                case ColumnKind.Int:
                case ColumnKind.Number:
                    return ContentWidgets.NumberBox(value, Save);
                case ColumnKind.LongText:
                    return ContentWidgets.WrapBox(value, Save, multiLine: true);
                case ColumnKind.Evolutions:
                    return EvolutionEditor(value, Save);
                case ColumnKind.Script:
                {
                    // The block editor arrives next (as in Unity); meanwhile the text and the ids it uses, clickable.
                    var c = Ui.Column(3);
                    c.Add(ContentWidgets.WrapBox(value, Save));
                    if (col.IsReference && value.Length > 0)
                    {
                        var refs = Ui.Row(0).Wrap();
                        foreach (var (rc, rid, _) in RefFormat.Extract(col, value, C.Db.Has).Distinct().Take(30)) refs.Add(RefChip(rc, rid));
                        c.Add(refs);
                    }
                    return c;
                }
                default:
                {
                    // Columns with a few known values (target, two turns, attack stat, pocket, class...): a dropdown with them.
                    var known = KnownValues(col.Name);
                    if (known != null)
                    {
                        var items = known.Select(v => new MenuItem(Pretty(v), () => Save(v), isChecked: v == value.Trim())).ToList();
                        items.Insert(0, new MenuItem("— (vacío)", () => Save("")));
                        items.Add(MenuItem.Separator);
                        items.Add(new MenuItem("Otro…", () => AskText(col.Label, value, Save)));
                        return ContentWidgets.Dropdown(_shell, value.Trim().Length == 0 ? "—" : Pretty(value.Trim()), () => items);
                    }
                    return ContentWidgets.WrapBox(value, Save);
                }
            }
        }

        private static readonly HashSet<string> FreeText = new HashSet<string>
            { "nombre", "nombre_en", "descripcion", "categoria_pokedex", "frase_inicio", "frase_derrota", "frase_victoria", "animacion", "etiquetas", "id" };

        /// <summary>The values a text column takes in this sheet, when they are few (then it is a choice, not free text).</summary>
        private List<string> KnownValues(string column)
        {
            if (FreeText.Contains(column) || (_category == ContentSchemas.Species && column == "categoria")) return null;
            var t = Table;
            if (t.Records.Count < 8) return null;
            var values = t.Records.Select(r => r[column].Trim()).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return values.Count >= 2 && values.Count <= 16 ? values.OrderBy(v => v).ToList() : null;
        }

        private static string Pretty(string v) => StatNames.IndexOf(v) >= 0 ? StatNames.Label(v) : ContentWidgets.CategoryLabel(v);

        /// <summary>A reference shown by its name (types in their colour): clic = open it; red when it does not exist.</summary>
        private VisualElement RefChip(string category, string id, Action remove = null)
        {
            if (category == ContentSchemas.Types && C.Db.Has(category, id))
            {
                var row = Ui.Row(0);
                row.style.alignItems = Align.Center;
                row.Add(ContentWidgets.TypeChip(C.Db, id, false, () => _shell.OpenContentItem(category, id)));
                if (remove != null) row.Add(Ui.IconButton("cerrar", remove, "Quitar", iconSize: Ui.IconSize * 0.6f));
                return row;
            }
            bool exists = C.Db.Has(category, id);
            var chip = Ui.Row(2).Pad(6, 1).Round(4);
            chip.style.alignItems = Align.Center;
            chip.style.backgroundColor = Ui.C("panel_alt");
            if (!exists) chip.Border(1, "error", 4);
            var label = Ui.Text(exists ? NameOf(category, id) : id + " (no existe)", 0.85f);
            chip.Add(label);
            var t = C.Db.Table(category).Find(id);
            var sub = t == null ? "" : ContentLook.Subtitle(C.Db, category, t);
            if (category == ContentSchemas.Natures && sub.Length > 0) chip.Add(Ui.Text(sub, 0.75f, dim: true).Margin(4, 0, 0, 0));
            chip.tooltip = exists ? $"{id} · {sub} · clic: abrir" : "No existe: elige otro o créalo";
            if (exists) label.RegisterCallback<ClickEvent>(_ => _shell.OpenContentItem(category, id));
            if (remove != null) chip.Add(Ui.IconButton("cerrar", remove, "Quitar", iconSize: Ui.IconSize * 0.6f));
            chip.style.marginRight = 3;
            chip.style.marginBottom = 3;
            return chip;
        }

        private VisualElement RefListEditor(ColumnSpec col, string value, Action<string> set, IReadOnlyCollection<string> only = null)
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
            }, only: only), "Añadir"));
            return row;
        }

        /// <summary>Learnset: level and move per row (type, category, power), ordered by level.</summary>
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
                var row = Ui.Row(6);
                row.style.alignItems = Align.Center;
                row.Add(Ui.MiniNumber(entries[k].level, 0, 100, v =>
                {
                    var copy = entries.ToList();
                    copy[idx] = (v, copy[idx].move);
                    set(Write(copy));
                }, "Nivel (0 = al evolucionar)", 48));
                row.Add(ContentWidgets.MoveInfo(_shell, entries[k].move));
                row.Add(Ui.Spacer());
                row.Add(Ui.IconButton("cerrar", () => set(Write(entries.Where((_, j) => j != idx))), "Quitar", iconSize: Ui.IconSize * 0.7f));
                box.Add(row);
            }
            box.Add(Ui.Button("+ Añadir movimiento", () => ContentPicker.Show(_shell, col.Targets[0], picked =>
                set(Write(entries.Append((entries.Count > 0 ? entries.Max(e => e.level) : 1, picked)))))));
            return box;
        }

        // ── Species ──────────────────────────────────────────────────────────────────────────────

        private static readonly string[] StatCols = { "ps", "ataque", "defensa", "atq_esp", "def_esp", "velocidad" };

        private void SpeciesForm(VisualElement box, ContentRecord r)
        {
            box.Add(ContentWidgets.Section(_shell, Key("stats"), $"Estadísticas base · total {StatCols.Sum(c => int.TryParse(r[c], out var v) ? v : 0)}", StatBars(r)));
            var s = Schema;
            ColumnSpec[] Cols(params string[] names) => names.Select(n => s.Column(n)).Where(c => c != null).ToArray();
            void Group(string key, string title, ColumnSpec[] cols, bool open = true)
            {
                var col = Ui.Column(6);
                foreach (var c in cols) col.Add(Field(r, c));
                box.Add(ContentWidgets.Section(_shell, Key(key), title, col, open));
            }
            Group("general", "General", Cols("nombre", "nombre_en", "tipos"));
            Group("combate", "Habilidades", Cols("habilidad", "habilidad_2", "habilidad_oculta"));
            Group("crecimiento", "Crecimiento", Cols("curva", "exp_base", "ratio_captura", "evs", "stats_extra"), open: false);

            // Moves: each list in its own foldable table.
            var moves = Ui.Column(4);
            moves.Add(ContentWidgets.Section(_shell, Key("aprende"), $"Por nivel ({Count(r["aprende"])})", LevelEditor(s.Column("aprende"), r["aprende"], v => Set("aprende", v))));
            foreach (var (col, title) in new[] { ("mt", "MT / MO"), ("tutor", "Tutor"), ("huevo", "Huevo") })
            {
                var c = col;
                var list = r[c].Split('|', ',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                Func<string, VisualElement> extra = c == "huevo" ? (Func<string, VisualElement>)(m => Ui.Button("¿Quién lo pasa?", () => EggMoveParents(Table.IdOf(r), m), Ui.ButtonKind.Normal,
                    "Especies de su grupo huevo que lo saben (pueden pasarlo al criar)")) : null;
                moves.Add(ContentWidgets.Section(_shell, Key(c), $"{title} ({list.Count})",
                    ContentWidgets.MoveTable(_shell, list, l => Set(c, string.Join("|", l)), extra), open: false));
            }
            box.Add(ContentWidgets.Section(_shell, Key("movimientos"), "Movimientos", moves));

            // Breeding: egg groups (with their members), gender.
            var breed = Ui.Column(6);
            var groups = Ui.Row(0).Wrap();
            var gl = r["grupos_huevo"].Split('|', ',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            foreach (var g in gl)
            {
                var gg = g;
                var chip = RefChip(ContentSchemas.EggGroups, gg, () => Set("grupos_huevo", string.Join("|", gl.Where(x => x != gg))));
                chip.Add(Ui.IconButton("info", () => EggGroupDialog(gg), "¿Quién más está en este grupo?", iconSize: Ui.IconSize * 0.7f));
                groups.Add(chip);
            }
            if (gl.Count < 2) groups.Add(Ui.IconButton("mas", () => ContentPicker.Show(_shell, ContentSchemas.EggGroups, p => { if (!gl.Contains(p)) Set("grupos_huevo", string.Join("|", gl.Append(p))); }), "Añadir grupo huevo"));
            breed.Add(LabelRow("Grupos huevo", groups));
            breed.Add(Field(r, s.Column("hembras")));
            box.Add(ContentWidgets.Section(_shell, Key("crianza"), "Crianza", breed, open: false));

            // Evolution: the whole family and how this one evolves.
            var evo = Ui.Column(6);
            evo.Add(FamilyTree(Table.IdOf(r), compact: true));
            evo.Add(LabelRow("Evoluciona a", EvolutionEditor(r["evoluciona"], v => Set("evoluciona", v))));
            box.Add(ContentWidgets.Section(_shell, Key("evolucion"), "Evolución y familia", evo));

            Group("pokedex", "Pokédex", Cols("numero", "categoria", "altura", "peso", "color", "legendario", "descripcion"), open: false);
            Group("formas", "Formas y variantes", Cols("forma_de", "objeto_variante", "formas", "cambios_forma"), open: false);
            var others = Table.Columns.Where(c => s.Column(c) == null).ToList();
            if (others.Count > 0)
            {
                var col = Ui.Column(6);
                foreach (var c in others) col.Add(Field(r, new ColumnSpec(c, c)));
                box.Add(ContentWidgets.Section(_shell, Key("otros"), "Otros (columnas propias)", col, open: false));
            }
        }

        private static int Count(string list) => list.Split('|').Count(x => x.Trim().Length > 0);

        private VisualElement LabelRow(string text, VisualElement editor)
        {
            var row = Ui.Row(8);
            row.style.alignItems = Align.FlexStart;
            var label = Ui.Text(text, 0.9f).NoShrink();
            label.style.width = 150;
            label.style.marginTop = 5;
            editor.style.flexGrow = 1;
            editor.style.flexShrink = 1;
            row.With(label, editor);
            return row;
        }

        private VisualElement StatBars(ContentRecord r)
        {
            var box = Ui.Column(3);
            string[] labels = { "PS", "Ataque", "Defensa", "At. esp.", "Def. esp.", "Velocidad" };
            for (int i = 0; i < StatCols.Length; i++)
            {
                var col = StatCols[i];
                int v = int.TryParse(r[col], out var n) ? n : 0;
                var row = Ui.Row(8);
                row.style.alignItems = Align.Center;
                var label = Ui.Text(labels[i], 0.9f).NoShrink(); label.style.width = 82;
                var num = Ui.MiniNumber(v, 1, 255, x => Set(col, x.ToString()), labels[i], 54);
                var track = new VisualElement().Round(3).Grow();
                track.style.height = 10;
                track.style.backgroundColor = Ui.C("panel_alt");
                var bar = new VisualElement().Round(3);
                bar.style.height = Length.Percent(100);
                bar.style.width = Length.Percent(Mathf.Clamp01(v / 255f) * 100);
                bar.style.backgroundColor = v < 50 ? new Color(0.93f, 0.33f, 0.3f) : v < 80 ? new Color(0.96f, 0.6f, 0.25f)
                    : v < 100 ? new Color(0.95f, 0.85f, 0.3f) : v < 130 ? new Color(0.45f, 0.82f, 0.35f) : new Color(0.3f, 0.8f, 0.85f);
                track.Add(bar);
                row.With(label, num, track);
                box.Add(row);
            }
            return box;
        }

        private VisualElement EggGroupMembers(string group)
        {
            var t = Table.Schema.Key == ContentSchemas.Species ? Table : C.Db.Table(ContentSchemas.Species);
            var members = t.Records.Where(x => x["grupos_huevo"].Split('|', ',').Any(g => string.Equals(g.Trim(), group, StringComparison.OrdinalIgnoreCase))).ToList();
            var row = Ui.Row(0).Wrap();
            row.Add(Ui.Text($"{members.Count} especies", 0.85f, dim: true).Margin(0, 0, 8, 4));
            foreach (var m in members.Take(200)) row.Add(RefChip(ContentSchemas.Species, t.IdOf(m)));
            return row;
        }

        private void EggGroupDialog(string group)
        {
            var d = _shell.ShowDialog($"Grupo huevo «{NameOf(ContentSchemas.EggGroups, group)}»", 50, 70);
            var scroll = Ui.Scroll().Grow();
            scroll.Add(EggGroupMembers(group));
            d.Body.Add(scroll);
            d.Buttons.With(Ui.Spacer(), Ui.Button("Cerrar", () => _shell.CloseDialog(d), Ui.ButtonKind.Primary));
        }

        /// <summary>Who can pass an egg move: species that share an egg group with this one and know the move.</summary>
        private void EggMoveParents(string speciesId, string move)
        {
            var species = C.Db.Table(ContentSchemas.Species);
            var me = species.Find(speciesId);
            var groups = me == null ? new string[0] : me["grupos_huevo"].Split('|', ',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
            var parents = species.Records.Where(x => species.IdOf(x) != speciesId
                && x["grupos_huevo"].Split('|', ',').Any(g => groups.Contains(g.Trim(), StringComparer.OrdinalIgnoreCase))
                && Legality.MovesOf(species, species.IdOf(x)).Contains(move)).ToList();
            var d = _shell.ShowDialog($"¿Quién puede pasar «{NameOf(ContentSchemas.Moves, move)}» a {NameOf(ContentSchemas.Species, speciesId)}?", 50, 70);
            d.Body.Add(Ui.Hint($"Especies de sus grupos huevo ({string.Join(", ", groups.Select(g => NameOf(ContentSchemas.EggGroups, g)))}) que pueden saber el movimiento (por nivel, MT, tutor o huevo)."));
            var row = Ui.Row(0).Wrap();
            if (parents.Count == 0) row.Add(Ui.Hint("Ninguna: solo se podría heredar por otros medios.").Colored("aviso"));
            foreach (var p in parents) row.Add(RefChip(ContentSchemas.Species, species.IdOf(p)));
            var scroll = Ui.Scroll().Grow();
            scroll.Add(row);
            d.Body.Add(scroll);
            d.Buttons.With(Ui.Spacer(), Ui.Button("Cerrar", () => _shell.CloseDialog(d), Ui.ButtonKind.Primary));
        }

        // ── Evolutions: target + how, with dropdowns ──────────────────────────────────────────────

        private static readonly (string key, string label)[] EvoMethods =
        {
            ("nivel", "Al llegar al nivel"), ("objeto", "Con un objeto"), ("amistad", "Con amistad"), ("intercambio", "Al intercambiarlo"),
            ("subir", "Al subir de nivel"), ("otro", "Otra condición"),
        };

        private sealed class Evo
        {
            public string Target = "", Method = "nivel", Value = "", Extra = "";
        }

        private static List<Evo> ParseEvos(string text)
        {
            var list = new List<Evo>();
            foreach (var e in (text ?? "").Split('|'))
            {
                if (e.Trim().Length == 0) continue;
                int at = e.IndexOf('@');
                var evo = new Evo { Target = (at >= 0 ? e.Substring(0, at) : e).Trim() };
                var cond = at >= 0 ? e.Substring(at + 1).Trim() : "";
                int plus = cond.IndexOf('+');
                if (plus >= 0) { evo.Extra = cond.Substring(plus + 1); cond = cond.Substring(0, plus); }
                if (int.TryParse(cond, out _)) { evo.Method = "nivel"; evo.Value = cond; }
                else if (cond.StartsWith("objeto:")) { evo.Method = "objeto"; evo.Value = cond.Substring(7); }
                else if (cond.StartsWith("amistad")) { evo.Method = "amistad"; evo.Value = cond.Length > 8 ? cond.Substring(8) : ""; }
                else if (cond.StartsWith("intercambio")) { evo.Method = "intercambio"; evo.Value = cond.Length > 12 ? cond.Substring(12) : ""; }
                else if (cond == "subir") evo.Method = "subir";
                else { evo.Method = cond.Length == 0 ? "nivel" : "otro"; evo.Value = cond; }
                list.Add(evo);
            }
            return list;
        }

        private static string WriteEvos(IEnumerable<Evo> list) => string.Join("|", list.Select(e =>
        {
            string cond = e.Method switch
            {
                "nivel" => e.Value,
                "objeto" => "objeto:" + e.Value,
                "amistad" => e.Value.Length > 0 ? "amistad:" + e.Value : "amistad",
                "intercambio" => e.Value.Length > 0 ? "intercambio:" + e.Value : "intercambio",
                "subir" => "subir",
                _ => e.Value,
            };
            if (e.Extra.Length > 0) cond += "+" + e.Extra;
            return cond.Length > 0 ? $"{e.Target}@{cond}" : e.Target;
        }));

        private VisualElement EvolutionEditor(string value, Action<string> save)
        {
            var evos = ParseEvos(value);
            void Save() => save(WriteEvos(evos));
            var box = Ui.Column(4);
            foreach (var e in evos)
            {
                var evo = e;
                var row = Ui.Row(6).Wrap();
                row.style.alignItems = Align.Center;
                row.Add(ContentWidgets.RefDropdown(_shell, ContentSchemas.Species, evo.Target, p => { evo.Target = p; Save(); }));
                row.Add(ContentWidgets.Dropdown(_shell, EvoMethods.First(m => m.key == evo.Method).label,
                    () => EvoMethods.Select(m => new MenuItem(m.label, () => { evo.Method = m.key; evo.Value = m.key == "nivel" ? "16" : ""; Save(); }, isChecked: m.key == evo.Method)).ToList()));
                switch (evo.Method)
                {
                    case "nivel":
                        row.Add(Ui.MiniNumber(int.TryParse(evo.Value, out var l) ? l : 16, 1, 100, n => { evo.Value = n.ToString(); Save(); }, "Nivel", 52));
                        break;
                    case "objeto":
                    case "intercambio":
                        row.Add(ContentWidgets.RefDropdown(_shell, ContentSchemas.Items, evo.Value, p => { evo.Value = p; Save(); }, evo.Method == "intercambio" ? "Sin objeto" : "Elegir objeto…"));
                        if (evo.Method == "intercambio" && evo.Value.Length > 0) row.Add(Ui.IconButton("cerrar", () => { evo.Value = ""; Save(); }, "Sin objeto", iconSize: Ui.IconSize * 0.7f));
                        break;
                    case "amistad":
                        row.Add(Ui.MiniNumber(int.TryParse(evo.Value, out var f) ? f : 220, 1, 255, n => { evo.Value = n.ToString(); Save(); }, "Amistad mínima", 56));
                        break;
                    case "otro":
                        row.Add(ContentWidgets.WrapBox(evo.Value, v => { evo.Value = v; Save(); }));
                        break;
                }
                // Time of day as a dropdown (the most common extra condition).
                string hour = evo.Extra.Contains("hora:dia") ? "De día" : evo.Extra.Contains("hora:noche") ? "De noche" : "A cualquier hora";
                row.Add(ContentWidgets.Dropdown(_shell, hour, () => new List<MenuItem>
                {
                    new MenuItem("A cualquier hora", () => { evo.Extra = RemoveHour(evo.Extra); Save(); }),
                    new MenuItem("De día", () => { evo.Extra = AddPart(RemoveHour(evo.Extra), "hora:dia"); Save(); }),
                    new MenuItem("De noche", () => { evo.Extra = AddPart(RemoveHour(evo.Extra), "hora:noche"); Save(); }),
                }));
                var rest = RemoveHour(evo.Extra);
                if (rest.Length > 0) row.Add(Ui.Text("+ " + rest, 0.8f, dim: true));
                row.Add(Ui.IconButton("cerrar", () => { evos.Remove(evo); Save(); }, "Quitar esta evolución", iconSize: Ui.IconSize * 0.7f));
                box.Add(row);
            }
            box.Add(Ui.Button("+ Añadir evolución", () => ContentPicker.Show(_shell, ContentSchemas.Species, p => { evos.Add(new Evo { Target = p, Value = "16" }); Save(); })));
            return box;
        }

        private static string RemoveHour(string extra) => string.Join("+", (extra ?? "").Split('+').Where(x => x.Length > 0 && !x.StartsWith("hora:")));
        private static string AddPart(string extra, string part) => extra.Length == 0 ? part : extra + "+" + part;

        // ── Family tree: the whole family, from the first stage ──────────────────────────────────

        private string RootOf(string id)
        {
            var t = C.Db.Table(ContentSchemas.Species);
            var seen = new HashSet<string>();
            while (seen.Add(id))
            {
                var pre = t.Records.FirstOrDefault(x => ParseEvos(x["evoluciona"]).Any(e => string.Equals(e.Target, id, StringComparison.OrdinalIgnoreCase)));
                if (pre == null) break;
                id = t.IdOf(pre);
            }
            return id;
        }

        private VisualElement FamilyTree(string id, bool compact)
        {
            var t = C.Db.Table(ContentSchemas.Species);
            var root = RootOf(id);
            var box = Ui.Row(10);
            box.style.alignItems = Align.Center;
            box.style.flexWrap = Wrap.Wrap;
            box.Add(Node(t, root, id, compact, new HashSet<string>()));
            return box;
        }

        /// <summary>A card and, to its right, its evolutions (each with how), recursively; variants under it.</summary>
        private VisualElement Node(ContentTable t, string id, string current, bool compact, HashSet<string> seen)
        {
            var row = Ui.Row(8);
            row.style.alignItems = Align.Center;
            var col = Ui.Column(3);
            col.style.alignItems = Align.Center;
            var r = t.Find(id);
            var card = Ui.Column(2).Pad(8, 4).Round(6);
            card.style.alignItems = Align.Center;
            card.style.backgroundColor = string.Equals(id, current, StringComparison.OrdinalIgnoreCase) ? Ui.WithAlpha(Ui.C("acento"), 0.35f) : Ui.C("panel_alt");
            card.Add(Ui.Text(r != null ? t.NameOf(r) : id, 0.92f, bold: true));
            if (r != null)
            {
                var types = Ui.Row(2);
                foreach (var ty in ContentLook.TypesOf(ContentSchemas.Species, r)) types.Add(ContentWidgets.TypeChip(C.Db, ty));
                card.Add(types);
            }
            card.RegisterCallback<ClickEvent>(_ => { if (r != null) _shell.OpenContentItem(ContentSchemas.Species, id); });
            card.tooltip = "Clic: abrir";
            col.Add(card);
            // Variants (Rotom Lavado, formas regionales) under the card.
            if (!compact || true)
                foreach (var v in t.Records.Where(x => string.Equals(x["forma_de"].Trim(), id, StringComparison.OrdinalIgnoreCase)).Take(compact ? 4 : 30))
                {
                    var vc = Ui.Text("◇ " + t.NameOf(v), 0.8f, dim: true);
                    var vid = t.IdOf(v);
                    vc.RegisterCallback<ClickEvent>(_ => _shell.OpenContentItem(ContentSchemas.Species, vid));
                    col.Add(vc);
                }
            row.Add(col);
            if (r == null || !seen.Add(id)) return row;
            var evos = ParseEvos(r["evoluciona"]);
            if (evos.Count == 0) return row;
            var branches = Ui.Column(6);
            foreach (var e in evos)
            {
                var b = Ui.Row(6);
                b.style.alignItems = Align.Center;
                var how = Ui.Column(0);
                how.style.alignItems = Align.Center;
                how.Add(Ui.Text("→", 1.2f, dim: true));
                how.Add(Ui.Text(EvoLabel(e), 0.75f, dim: true));
                b.Add(how);
                b.Add(Node(t, e.Target, current, compact, seen));
                branches.Add(b);
            }
            row.Add(branches);
            return row;
        }

        private string EvoLabel(Evo e)
        {
            string s = e.Method switch
            {
                "nivel" => "nv. " + e.Value,
                "objeto" => NameOf(ContentSchemas.Items, e.Value),
                "amistad" => "amistad",
                "intercambio" => e.Value.Length > 0 ? "interc. + " + NameOf(ContentSchemas.Items, e.Value) : "intercambio",
                "subir" => "subir nivel",
                _ => e.Value,
            };
            if (e.Extra.Contains("hora:dia")) s += " (día)";
            if (e.Extra.Contains("hora:noche")) s += " (noche)";
            return s;
        }

        private void FamilyTreeDialog(string id)
        {
            if (id == null) return;
            var d = _shell.ShowDialog($"Árbol de familia de {NameOf(ContentSchemas.Species, id)}", 80, 70);
            var scroll = Ui.Scroll(ScrollViewMode.VerticalAndHorizontal).Grow();
            scroll.Add(FamilyTree(id, compact: false).Pad(10));
            d.Body.Add(Ui.Hint("De la primera fase a la última, con cómo evoluciona cada una; debajo de cada tarjeta, sus variantes (◇). Clic en una tarjeta: abrir su ficha."));
            d.Body.Add(scroll);
            d.Buttons.With(Ui.Spacer(), Ui.Button("Cerrar", () => _shell.CloseDialog(d), Ui.ButtonKind.Primary));
        }

        // ── Competitive sets ─────────────────────────────────────────────────────────────────────

        private void SetForm(VisualElement box, ContentRecord r)
        {
            string id = Table.IdOf(r);
            string sp = r["especie"].Trim(), fmt = r["formato"].Trim(), name = r["nombre"];
            void SetWithId(string column, string value)
            {
                string s2 = column == "especie" ? value : sp, f2 = column == "formato" ? value : fmt, n2 = column == "nombre" ? value : name;
                C.SetValueAndId(_category, id, column, value, Legality.SetId(s2, f2, n2));
                _selected = Table.Find(Legality.SetId(s2, f2, n2)) != null ? Legality.SetId(s2, f2, n2) : id;
            }
            var general = Ui.Column(6);
            general.Add(LabelRow("Especie *", ContentWidgets.RefDropdown(_shell, ContentSchemas.Species, sp, p => SetWithId("especie", p))));
            var formats = Table.Records.Select(x => x["formato"].Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            general.Add(LabelRow("Formato", ContentWidgets.Dropdown(_shell, fmt.Length > 0 ? fmt.ToUpperInvariant() : "—", () =>
            {
                var items = formats.Select(f => new MenuItem(f.ToUpperInvariant(), () => SetWithId("formato", f), isChecked: f == fmt)).ToList();
                items.Add(MenuItem.Separator);
                items.Add(new MenuItem("Otro…", () => AskText("Formato nuevo", "", v => SetWithId("formato", ContentIds.Normalize(v)))));
                return items;
            })));
            general.Add(LabelRow("Nombre del set", ContentWidgets.WrapBox(name, v => SetWithId("nombre", v))));
            general.Add(LabelRow("Puntuación", ContentWidgets.NumberBox(r["puntuacion"], v => Set("puntuacion", v))));
            general.Add(Ui.Hint($"Id: {id} (se forma solo con especie, formato y nombre)."));
            box.Add(ContentWidgets.Section(_shell, Key("general"), "General", general));

            var spRec = C.Db.Table(ContentSchemas.Species).Find(sp);
            var legal = spRec == null ? null : Legality.AbilitiesOf(spRec).ToList();
            var build = Ui.Column(6);
            build.Add(LabelRow("Objeto (a elegir)", RefListEditor(Schema.Column("objeto"), r["objeto"], v => Set("objeto", v))));
            build.Add(LabelRow("Habilidad (a elegir)", RefListEditor(Schema.Column("habilidad"), r["habilidad"], v => Set("habilidad", v), legal)));
            build.Add(LabelRow("Naturaleza (a elegir)", RefListEditor(Schema.Column("naturaleza"), r["naturaleza"], v => Set("naturaleza", v))));
            box.Add(ContentWidgets.Section(_shell, Key("build"), "Objeto, habilidad y naturaleza", build));

            box.Add(ContentWidgets.Section(_shell, Key("movs"), "Movimientos", SetMoves(r["movimientos"], v => Set("movimientos", v), sp)));
            box.Add(ContentWidgets.Section(_shell, Key("evs"), $"EVs ({(r["evs"].Trim().Length == 0 ? "ninguno" : r["evs"])})", ContentWidgets.SpreadEditor(r["evs"], false, v => Set("evs", v)), open: false));
            box.Add(ContentWidgets.Section(_shell, Key("ivs"), $"IVs ({(r["ivs"].Trim().Length == 0 ? "todos 31" : r["ivs"])})", ContentWidgets.SpreadEditor(r["ivs"], true, v => Set("ivs", v)), open: false));
        }

        /// <summary>Four move slots, each with its alternatives (the AI picks one), as a table.</summary>
        private VisualElement SetMoves(string value, Action<string> save, string species)
        {
            var slots = value.Split('/').Select(s => s.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList()).Where(s => s.Count > 0).ToList();
            void Save() => save(string.Join("/", slots.Where(s => s.Count > 0).Select(s => string.Join(",", s))));
            var learn = species.Length > 0 ? Legality.MovesOf(C.Db.Table(ContentSchemas.Species), species) : null;
            var box = Ui.Column(4);
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                var card = Ui.Column(2).Pad(6, 4).Round(4);
                card.style.backgroundColor = Ui.C("panel_alt");
                card.Add(Ui.Text($"Hueco {i + 1}" + (slot.Count > 1 ? " · una de estas" : ""), 0.8f, dim: true));
                foreach (var m in slot.ToList())
                {
                    var mv = m;
                    var row = ContentWidgets.MoveInfo(_shell, mv);
                    if (learn != null && learn.Count > 0 && !learn.Contains(mv)) row.Add(Ui.Text("no lo aprende", 0.78f).Colored("aviso"));
                    row.Add(Ui.Spacer());
                    row.Add(Ui.IconButton("cerrar", () => { slot.Remove(mv); Save(); }, "Quitar", iconSize: Ui.IconSize * 0.7f));
                    card.Add(row);
                }
                card.Add(Ui.Button("+ alternativa", () => ContentPicker.Show(_shell, ContentSchemas.Moves, p => { if (!slot.Contains(p)) { slot.Add(p); Save(); } },
                    only: learn, onlyLabel: "Mostrar también los que no aprende")));
                box.Add(card);
            }
            if (slots.Count < 4)
                box.Add(Ui.Button("+ Añadir movimiento", () => ContentPicker.Show(_shell, ContentSchemas.Moves, p => { slots.Add(new List<string> { p }); Save(); },
                    only: learn, onlyLabel: "Mostrar también los que no aprende")));
            return box;
        }

        private void AskText(string title, string value, Action<string> done)
        {
            var d = _shell.ShowDialog(title);
            string v = value;
            var box = Ui.TextBox("", value, x => v = x);
            d.Body.Add(box);
            d.Buttons.With(Ui.Spacer(), Ui.Button("Cancelar", () => _shell.CloseDialog(d)),
                Ui.Button("Aceptar", () => { _shell.CloseDialog(d); if (!string.IsNullOrWhiteSpace(v)) done(v.Trim()); }, Ui.ButtonKind.Primary));
            box.schedule.Execute(() => box.Focus()).ExecuteLater(50);
        }

        // ── Teams (trainers, ready-made teams) ───────────────────────────────────────────────────

        /// <summary>
        /// A team: each member in a foldable card (species, level, sex, item, nature with ↑/↓, ability of its species, 4 moves
        /// as a table, EVs and IVs with sliders). The same team as text (Excel) is in a closed section at the end.
        /// </summary>
        private VisualElement TeamEditor(string value, Action<string> set)
        {
            var members = TeamFormat.Parse(value);
            void Save() => set(TeamFormat.Format(members));
            var species = C.Db.Table(ContentSchemas.Species);
            var box = Ui.Column(6);
            for (int k = 0; k < members.Count; k++)
            {
                int idx = k;
                var m = members[k];
                if (m.Raw != null) { box.Add(Ui.Hint("No se entiende este miembro (se conserva tal cual): " + m.Raw).Colored("aviso")); continue; }
                var sp = species.Find(m.Species);
                var body = Ui.Column(6).Pad(6, 4);
                var top = Ui.Row(6).Wrap();
                top.style.alignItems = Align.Center;
                top.Add(ContentWidgets.RefDropdown(_shell, ContentSchemas.Species, m.Species, p => { m.Species = p; Save(); }));
                top.Add(Ui.Text("Nv.", 0.85f, dim: true));
                top.Add(Ui.MiniNumber(m.Level, 1, 100, v => { m.Level = v; Save(); }, "Nivel", 48));
                top.Add(ContentWidgets.Dropdown(_shell, m.Gender == "m" ? "♂ Macho" : m.Gender == "h" ? "♀ Hembra" : "Sexo al azar", () => new List<MenuItem>
                {
                    new MenuItem("Sexo al azar", () => { m.Gender = ""; Save(); }), new MenuItem("♂ Macho", () => { m.Gender = "m"; Save(); }), new MenuItem("♀ Hembra", () => { m.Gender = "h"; Save(); }),
                }));
                top.Add(Ui.Spacer());
                top.Add(Ui.IconButton("arriba", () => { if (idx > 0) { members.RemoveAt(idx); members.Insert(idx - 1, m); Save(); } }, "Subir").SetEnabledLook(idx > 0));
                top.Add(Ui.IconButton("abajo", () => { if (idx < members.Count - 1) { members.RemoveAt(idx); members.Insert(idx + 1, m); Save(); } }, "Bajar").SetEnabledLook(idx < members.Count - 1));
                top.Add(Ui.IconButton("papelera", () => { members.RemoveAt(idx); Save(); }, "Quitar del equipo"));
                body.Add(top);
                var legal = sp == null ? null : Legality.AbilitiesOf(sp).ToList();
                body.Add(LabelRow("Objeto", Row(ContentWidgets.RefDropdown(_shell, ContentSchemas.Items, m.Item, p => { m.Item = p; Save(); }, "Ninguno"),
                    m.Item.Length > 0 ? Ui.IconButton("cerrar", () => { m.Item = ""; Save(); }, "Sin objeto", iconSize: Ui.IconSize * 0.7f) : null)));
                body.Add(LabelRow("Naturaleza", Row(ContentWidgets.RefDropdown(_shell, ContentSchemas.Natures, m.Nature, p => { m.Nature = p; Save(); }, "Al azar"),
                    m.Nature.Length > 0 ? Ui.IconButton("cerrar", () => { m.Nature = ""; Save(); }, "Al azar", iconSize: Ui.IconSize * 0.7f) : null)));
                var ability = ContentWidgets.RefDropdown(_shell, ContentSchemas.Abilities, m.Ability, p => { m.Ability = p; Save(); }, "La de su especie", legal, "Mostrar también las que no tiene");
                bool illegal = legal != null && legal.Count > 0 && m.Ability.Length > 0 && !legal.Contains(m.Ability, StringComparer.OrdinalIgnoreCase);
                body.Add(LabelRow("Habilidad", Row(ability, m.Ability.Length > 0 ? Ui.IconButton("cerrar", () => { m.Ability = ""; Save(); }, "La de su especie", iconSize: Ui.IconSize * 0.7f) : null,
                    illegal ? Ui.Text("⚠ habilidad ilegal", 0.8f).Colored("aviso") : null)));
                var learn = Legality.MovesOf(species, m.Species);
                var movesBox = Ui.Column(2);
                if (m.Moves.Count == 0) movesBox.Add(Ui.Hint("Automáticos: los últimos que aprende por nivel."));
                for (int j = 0; j < m.Moves.Count; j++)
                {
                    int mj = j;
                    var row = ContentWidgets.MoveInfo(_shell, m.Moves[j]);
                    if (learn.Count > 0 && !learn.Contains(m.Moves[j])) row.Add(Ui.Text("no lo aprende", 0.78f).Colored("aviso"));
                    row.Add(Ui.Spacer());
                    row.Add(Ui.IconButton("cerrar", () => { m.Moves.RemoveAt(mj); Save(); }, "Quitar", iconSize: Ui.IconSize * 0.7f));
                    movesBox.Add(row);
                }
                if (m.Moves.Count < 4)
                    movesBox.Add(Ui.Button("+ Añadir movimiento", () => ContentPicker.Show(_shell, ContentSchemas.Moves, p => { if (!m.Moves.Contains(p)) { m.Moves.Add(p); Save(); } },
                        only: learn.Count > 0 ? learn : null, onlyLabel: "Mostrar también los que no aprende")));
                body.Add(ContentWidgets.Section(_shell, Key($"m{idx}_movs"), $"Movimientos ({m.Moves.Count}/4)", movesBox));
                body.Add(ContentWidgets.Section(_shell, Key($"m{idx}_evs"), "EVs", ContentWidgets.SpreadEditor(m.Evs, false, v => { m.Evs = v; Save(); }), open: false));
                body.Add(ContentWidgets.Section(_shell, Key($"m{idx}_ivs"), m.Iv >= 0 && m.Ivs.Length == 0 ? $"IVs (todos {m.Iv})" : "IVs",
                    ContentWidgets.SpreadEditor(m.Ivs.Length > 0 ? m.Ivs : m.Iv >= 0 ? string.Join("/", StatNames.Short.Select(s => $"{m.Iv} {s}")) : "", true, v => { m.Ivs = v; m.Iv = -1; Save(); }), open: false));
                string title = $"{idx + 1}. {(sp != null ? species.NameOf(sp) : m.Species)} · nv. {m.Level}" + (m.Item.Length > 0 ? " · " + NameOf(ContentSchemas.Items, m.Item) : "");
                var card = ContentWidgets.Section(_shell, Key($"m{idx}"), title, body, open: members.Count <= 2);
                card.Border(1, "borde", 6);
                box.Add(card);
            }
            if (members.Count < 6)
                box.Add(Ui.Button("+ Añadir al equipo", () => ContentPicker.Show(_shell, ContentSchemas.Species, p =>
                {
                    members.Add(new TeamMember { Species = p, Level = members.Count > 0 ? members.Max(x => x.Level) : 5 });
                    Save();
                })));
            box.Add(ContentWidgets.Section(_shell, Key("equipo_texto"), "Como texto (Excel)", ContentWidgets.WrapBox(value, set), open: false));
            return box;
        }

        private static VisualElement Row(params VisualElement[] items)
        {
            var r = Ui.Row(4);
            r.style.alignItems = Align.Center;
            foreach (var i in items) if (i != null) r.Add(i);
            return r;
        }

        // ── Type summary, natures, curves ─────────────────────────────────────────────────────────

        private VisualElement TypeSummary(ContentRecord r)
        {
            var box = Ui.Column(3);
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
            void Line(string title, List<string> ids)
            {
                var l = Ui.Row(0).Wrap();
                var t = Ui.Text(title, 0.88f, dim: true).NoShrink(); t.style.width = 130;
                l.Add(t);
                if (ids.Count == 0) l.Add(Ui.Text("—", 0.88f, dim: true));
                foreach (var x in ids) l.Add(ContentWidgets.TypeChip(C.Db, x));
                box.Add(l);
            }
            Line("Muy eficaz contra", strong); Line("Poco eficaz contra", weak); Line("No afecta a", none);
            Line("Débil a", hurtBy); Line("Resiste", resists); Line("Inmune a", immune);
            box.Add(Ui.Button("Editar la tabla de tipos", () => _shell.OpenContentItem(ContentSchemas.TypeChart, id)));
            return box;
        }

        private VisualElement NatureTable(ContentRecord chosen)
        {
            var box = Ui.Column(0);
            NatureGrid(box, chosen);
            return box;
        }

        private VisualElement CurveTable(ContentRecord r)
        {
            var box = Ui.Column(4);
            CurveChart(box, r);
            return box;
        }

        private void CreateClassicCurves()
        {
            foreach (var c in ClassicCurves.All)
                if (!Table.Contains(c.id)) C.Create(ContentSchemas.Curves, c.id, ClassicCurves.Row(c.id));
            _shell.Success("Creadas las 6 curvas clásicas (Rápida, Media, Parabólica, Lenta, Errática y Fluctuante).");
        }

        // ── New: empty or from a template ────────────────────────────────────────────────────────

        private void NewItem()
        {
            var s = Schema;
            var templates = ContentTemplates.For(_category);
            var d = _shell.ShowDialog($"{s.Nuevo} {s.Noun}");
            string name = "";
            d.Body.Add(Ui.Hint(templates.Records.Count > 0
                ? $"Empieza vací{(s.Feminine ? "a" : "o")} o desde una plantilla de los packs ({templates.Records.Count}): se copia todo y luego lo cambias. El id se hace del nombre."
                : "El id se hace del nombre (sin tildes ni espacios); se puede cambiar luego en todo el proyecto."));
            var box = Ui.TextBox("Nombre", "", v => name = v);
            d.Body.Add(box);
            void Create(ContentRecord template)
            {
                var n = string.IsNullOrWhiteSpace(name) ? (template != null ? templates.NameOf(template) : "") : name.Trim();
                if (string.IsNullOrWhiteSpace(n)) { _shell.Warn("Escribe un nombre."); return; }
                _shell.CloseDialog(d);
                if (template != null && string.IsNullOrWhiteSpace(name)) n = templates.IdOf(template);
                var r = C.Create(_category, n, template);
                if (r != null && template != null && !string.IsNullOrWhiteSpace(name) && Schema.NameColumn != Schema.IdColumn) C.SetValue(_category, Table.IdOf(r), Schema.NameColumn, name.Trim());
                if (r != null) Select(Table.IdOf(r));
            }
            box.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { name = box.value; Create(null); } });
            d.Buttons.With(
                templates.Records.Count > 0 ? Ui.Button("Desde una plantilla…", () => ContentPicker.Show(_shell, _category, id => Create(templates.Find(id)),
                    title: $"Plantilla de {s.Noun}", source: templates), Ui.ButtonKind.Normal, "Copiar una de los packs (con filtros)") : new VisualElement(),
                Ui.Spacer(), Ui.Button("Cancelar", () => _shell.CloseDialog(d)), Ui.Button("Crear vací" + (s.Feminine ? "a" : "o"), () => Create(null), Ui.ButtonKind.Primary));
            box.schedule.Execute(() => box.Focus()).ExecuteLater(50);
        }
    }
}
