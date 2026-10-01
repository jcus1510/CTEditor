using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using CTEditor.Content;
using CTEditor.GameDefinition.Domain.Abilities;
using CTEditor.GameDefinition.Domain.Conditions;
using CTEditor.GameDefinition.Domain.Effects;
using CTEditor.GameDefinition.Text;

namespace CTEditor.App
{
    /// <summary>
    /// EFFECTS BY BLOCKS, as in the Unity item and ability editors: the blocks grouped by «when» (foldable), each a card
    /// «when → what» with dropdowns of what exists in the project (types, statuses, stats, weathers, moves...), the numbers
    /// it needs, its conditions written as a sentence, chance, times per battle and «used up», and the sentence of what the
    /// engine will do. Nothing is typed as code; the text format is only what is saved in the sheet.
    /// </summary>
    public static class EffectBlocksEditor
    {
        private static readonly string[] SubjectLabels = { "Propio", "Rival" };
        private static readonly string[] CmpLabels = { "menos de", "como mucho", "exactamente", "al menos", "más de", "distinto de" };
        private static readonly string[] KindLabels =
        {
            "tiene algún estado", "tiene el estado…", "% de vida", "es del tipo…", "el clima es…",
            "amistad (0-255)", "nivel", "nivel − nivel del otro", "etapa de una estadística", "ya actuó este turno",
            "movimiento: es del tipo…", "movimiento: categoría…", "movimiento: potencia base",
            "movimiento: hace contacto", "movimiento: tiene la etiqueta…", "azar (%)",
            "recibió daño este turno", "turnos en el campo", "el otro va a atacar", "ya usó sus otros movimientos",
            "reservas (Reserva)", "eficacia del movimiento contra él", "movimiento: tiene efecto secundario", "lleva un objeto",
            "perdió su objeto", "peso (kg)", "mismo género", "género opuesto", "efecto de campo activo…", "aún puede evolucionar",
            "movimiento: es exactamente…", "es de la especie…",
        };
        private static readonly (string id, string label)[] Announce =
            { ("objeto", "El objeto del rival (Cacheo)"), ("peligro", "Si el rival tiene un movimiento peligroso (Anticipación)"), ("movimiento", "El movimiento más fuerte del rival (Alerta)") };

        /// <summary>The editor for one «efectos» cell. 'ability' = ability triggers, actions and specials (never «se gasta»).</summary>
        public static VisualElement Build(AppShell shell, string value, bool ability, Action<string> save, string key)
        {
            var box = Ui.Column(6);
            List<EffectBlock> blocks;
            try { blocks = EffectText.Parse(value); }
            catch (FormatException e)
            {
                box.Add(Ui.Hint("⚠ No se puede leer este efecto: " + e.Message).Colored("error"));
                box.Add(Ui.Hint("Corrígelo aquí (o bórralo y créalo con los bloques):"));
                box.Add(ContentWidgets.WrapBox(value, save));
                return box;
            }
            void Save() => save(EffectText.Format(blocks));
            if (blocks.Count == 0)
                box.Add(Ui.Hint("Todavía no hace nada. Añade un efecto («cuándo → qué»)."));
            var triggers = ability ? EffectText.AbilityTriggers : EffectText.AllTriggers;
            foreach (var trigger in triggers)
            {
                var indices = Enumerable.Range(0, blocks.Count).Where(i => blocks[i].Trigger == trigger).ToList();
                if (indices.Count == 0) continue;
                var cards = Ui.Column(6);
                foreach (int i in indices) cards.Add(Card(shell, blocks, i, ability, Save));
                var section = ContentWidgets.Section(shell, $"{key}_{trigger}", $"{EffectText.Label(trigger, ability)} ({indices.Count})", cards);
                section.tooltip = EffectText.Help(trigger);
                box.Add(section);
            }
            var add = Ui.Button("+ Añadir efecto…", null, Ui.ButtonKind.Primary, "Elige primero CUÁNDO y luego QUÉ hace");
            add.clicked += () =>
            {
                var r = add.worldBound;
                shell.ShowMenu(new Vector2(r.x, r.yMax + 2), AddMenu(blocks, ability, Save));
            };
            box.Add(add);
            return box;
        }

        private static IList<MenuItem> AddMenu(List<EffectBlock> blocks, bool ability, Action save)
        {
            var menu = new List<MenuItem>();
            foreach (var t in ability ? EffectText.AbilityTriggers : EffectText.AllTriggers)
            {
                var trig = t;
                var items = new List<MenuItem>();
                foreach (var a in ability ? EffectText.AbilityActionsFor(t) : EffectText.ActionsFor(t))
                {
                    var act = a;
                    if (act == EffectAction.Special)
                    {
                        var specials = AbilityEffects.Specials.Where(x => x.Trigger == trig).ToList();
                        if (specials.Count > 0)
                            items.Add(MenuItem.Submenu("Especial", specials.Select(sp => new MenuItem(sp.Label, () =>
                            {
                                blocks.Add(new EffectBlock(trig, EffectAction.Special, sp.UsesAmount ? 50f : 0f, sp.Key));
                                save();
                            })).ToList()));
                        continue;
                    }
                    items.Add(new MenuItem(EffectText.Label(act), () => { blocks.Add(EffectText.Default(trig, act)); save(); }));
                }
                if (items.Count > 0) menu.Add(MenuItem.Submenu(EffectText.Label(t, ability), items));
            }
            return menu;
        }

        /// <summary>A copy of a block with some parts changed (blocks are immutable).</summary>
        private static EffectBlock With(EffectBlock b, EffectTrigger? trigger = null, EffectAction? action = null, float? amount = null, string reference = null,
            IReadOnlyList<Condition> conditions = null, BlockTarget? target = null, float? threshold = null, bool? consumes = null, float? chance = null, int? times = null) =>
            new EffectBlock(trigger ?? b.Trigger, action ?? b.Action, amount ?? b.Amount, reference ?? b.Ref, conditions ?? b.Conditions, target ?? b.Target,
                threshold ?? b.Threshold, consumes ?? b.Consumes, chance ?? b.Chance, times ?? b.MaxPerBattle);

        private static VisualElement Card(AppShell shell, List<EffectBlock> blocks, int index, bool ability, Action save)
        {
            var b = blocks[index];
            void Put(EffectBlock nb) { blocks[index] = nb; save(); }
            var card = Ui.Column(5).Pad(8, 6).Round(6);
            card.style.backgroundColor = Ui.C("panel_alt");

            // Row 1: when / what / order / delete
            var top = Ui.Row(6).Wrap();
            top.style.alignItems = Align.Center;
            var triggers = ability ? EffectText.AbilityTriggers : EffectText.AllTriggers;
            top.Add(ContentWidgets.Dropdown(shell, EffectText.Label(b.Trigger, ability), () => triggers.Select(t => new MenuItem(EffectText.Label(t, ability), () =>
                Put(With(b, trigger: t, threshold: t == EffectTrigger.LowHp ? 50f : b.Threshold)), isChecked: t == b.Trigger)).ToList(), tooltip: "Cuándo"));
            top.Add(Ui.Text("→", 1f, dim: true));
            string actionLabel = b.Action == EffectAction.Special ? (AbilityEffects.Special(b.Ref)?.Label ?? "Especial") : EffectText.Label(b.Action);
            top.Add(ContentWidgets.Dropdown(shell, actionLabel, () =>
            {
                var items = new List<MenuItem>();
                foreach (var a in ability ? EffectText.AbilityActionsFor(b.Trigger) : EffectText.ActionsFor(b.Trigger))
                {
                    var act = a;
                    if (act == EffectAction.Special)
                    {
                        var specials = AbilityEffects.Specials.Where(x => x.Trigger == b.Trigger).ToList();
                        if (specials.Count > 0)
                            items.Add(MenuItem.Submenu("Especial", specials.Select(sp => new MenuItem(sp.Label,
                                () => Put(new EffectBlock(b.Trigger, EffectAction.Special, sp.UsesAmount ? 50f : 0f, sp.Key, b.Conditions, b.Target, b.Threshold, false, b.Chance, b.MaxPerBattle)))).ToList()));
                        continue;
                    }
                    items.Add(new MenuItem(EffectText.Label(act), () =>
                    {
                        var d = EffectText.Default(b.Trigger, act);
                        Put(new EffectBlock(b.Trigger, act, d.Amount, "", b.Conditions, d.Target, b.Threshold, b.Consumes, b.Chance, b.MaxPerBattle));
                    }, isChecked: act == b.Action));
                }
                return items;
            }, tooltip: "Qué hace"));
            top.Add(Ui.Spacer());
            top.Add(Ui.IconButton("arriba", () => { if (index > 0) { blocks.RemoveAt(index); blocks.Insert(index - 1, b); save(); } }, "Subir").SetEnabledLook(index > 0));
            top.Add(Ui.IconButton("abajo", () => { if (index < blocks.Count - 1) { blocks.RemoveAt(index); blocks.Insert(index + 1, b); save(); } }, "Bajar").SetEnabledLook(index < blocks.Count - 1));
            top.Add(Ui.IconButton("papelera", () => { blocks.RemoveAt(index); save(); }, "Quitar este efecto"));
            card.Add(top);

            // Low HP threshold
            if (b.Trigger == EffectTrigger.LowHp)
                card.Add(Labelled("Con este % de PS o menos", Ui.MiniNumber(Mathf.RoundToInt(b.Threshold), 1, 100, v => Put(With(b, threshold: v)), "%", 56)));

            // Parameters of the action
            var refEditor = Reference(shell, b, ability, v => Put(With(b, reference: v)));
            if (refEditor != null) card.Add(refEditor);
            var amountKind = EffectText.AmountOf(b.Action);
            if (b.Action == EffectAction.Special) amountKind = AbilityEffects.Special(b.Ref)?.UsesAmount == true ? EffectAmountKind.Percent : EffectAmountKind.None;
            if (amountKind != EffectAmountKind.None)
                card.Add(Labelled(EffectText.AmountLabel(amountKind), ContentWidgets.NumberBox(EffectText.N(b.Amount), v =>
                {
                    if (TextNumbers.TryNumber(v, out var f)) Put(With(b, amount: f));
                })));
            if (EffectText.UsesTarget(b.Action))
                card.Add(Labelled("A quién", ContentWidgets.Dropdown(shell, b.Target == BlockTarget.Self ? (ability ? "A quien la tiene" : "A quien lo lleva") : "Al rival", () => new List<MenuItem>
                {
                    new MenuItem(ability ? "A quien la tiene" : "A quien lo lleva", () => Put(With(b, target: BlockTarget.Self))),
                    new MenuItem("Al rival", () => Put(With(b, target: BlockTarget.Other))),
                })));

            // Conditions, each as an editable sentence
            for (int c = 0; c < b.Conditions.Count; c++)
            {
                int ci = c;
                card.Add(ConditionRow(shell, b.Conditions[c], ci == 0 ? "Si" : "y", nc =>
                {
                    var list = b.Conditions.ToList();
                    if (nc == null) list.RemoveAt(ci); else list[ci] = nc;
                    Put(With(b, conditions: list));
                }));
            }

            // Options
            var opts = Ui.Row(8).Wrap();
            opts.style.alignItems = Align.Center;
            opts.Add(Ui.Button("+ Condición", () => Put(With(b, conditions: b.Conditions.Append(new Condition(ConditionKind.HpPercent, ConditionSubject.Self, Comparison.LessOrEqual, 50f)).ToList()))));
            if (!ability) opts.Add(Ui.Check("Se gasta", b.Consumes, on => Put(With(b, consumes: on))));
            opts.Add(Ui.Text("Prob. %", 0.85f, dim: true));
            opts.Add(Ui.MiniNumber(Mathf.RoundToInt(b.Chance), 0, 100, v => Put(With(b, chance: v)), "Probabilidad (100 = siempre)", 52));
            opts.Add(Ui.Text("Veces por combate", 0.85f, dim: true));
            opts.Add(Ui.MiniNumber(b.MaxPerBattle, 0, 99, v => Put(With(b, times: v)), "0 = sin límite", 48));
            card.Add(opts);

            // The sentence (what the engine will do)
            string Name(EffectRefKind k, string id) => RefName(shell, k, id);
            card.Add(Ui.Hint("→ " + EffectText.Describe(b, Name, ability)).Colored(
                (ability ? AbilityEffects.IsSupported(b) : EffectRules.IsSupported(b)) ? "texto" : "aviso"));
            return card;
        }

        private static VisualElement Labelled(string text, VisualElement editor)
        {
            var row = Ui.Row(8);
            row.style.alignItems = Align.Center;
            var l = Ui.Text(text, 0.88f, dim: true).NoShrink();
            l.style.width = 170;
            row.With(l, editor);
            return row;
        }

        private static string CategoryOf(EffectRefKind k) => k switch
        {
            EffectRefKind.Type or EffectRefKind.TypeList => ContentSchemas.Types,
            EffectRefKind.Status or EffectRefKind.StatusList => ContentSchemas.Statuses,
            EffectRefKind.Weather or EffectRefKind.WeatherList => ContentSchemas.Weathers,
            EffectRefKind.Move => ContentSchemas.Moves,
            EffectRefKind.SideCondition => ContentSchemas.SideEffects,
            _ => null,
        };

        private static string RefName(AppShell shell, EffectRefKind k, string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return "?";
            if (k == EffectRefKind.Stat || k == EffectRefKind.StatList) return StatLabels.NameOf(id);
            if (k == EffectRefKind.WeatherList && id == "*") return "todos";
            if (k == EffectRefKind.Special) return AbilityEffects.Special(id)?.Label ?? id;
            var cat = CategoryOf(k);
            if (cat == null) return id;
            var t = shell.Content.Db.Table(cat);
            var r = t.Find(id);
            return r != null ? t.NameOf(r) : id + " (no existe)";
        }

        /// <summary>The id the action needs, chosen from the project's lists (single or several).</summary>
        private static VisualElement Reference(AppShell shell, EffectBlock b, bool ability, Action<string> set)
        {
            var kind = EffectText.RefOf(b.Action);
            switch (kind)
            {
                case EffectRefKind.None:
                    return null;
                case EffectRefKind.Special:
                    return Labelled("Comportamiento", ContentWidgets.Dropdown(shell, AbilityEffects.Special(b.Ref)?.Label ?? "— elige —",
                        () => AbilityEffects.Specials.Select(sp => new MenuItem(sp.Label, () => set(sp.Key), isChecked: sp.Key == b.Ref)).ToList()));
                case EffectRefKind.Announce:
                    return Labelled("Anuncia", ContentWidgets.Dropdown(shell, Announce.FirstOrDefault(a => a.id == b.Ref).label ?? "— elige —",
                        () => Announce.Select(a => new MenuItem(a.label, () => set(a.id), isChecked: a.id == b.Ref)).ToList()));
                case EffectRefKind.Stat:
                {
                    var ids = StatLabels.ClassicIds.ToList();
                    if (b.Action == EffectAction.ChangeStage) { ids.Add("accuracy"); ids.Add("evasion"); }
                    return Labelled("Estadística", ContentWidgets.Dropdown(shell, b.Ref.Length > 0 ? StatLabels.NameOf(b.Ref) : "— elige —",
                        () => ids.Select(id => new MenuItem(StatLabels.NameOf(id), () => set(id), isChecked: id == b.Ref)).ToList()));
                }
                case EffectRefKind.StatList:
                {
                    var ids = StatLabels.ClassicIds.Concat(new[] { "accuracy", "evasion" }).ToList();
                    return Labelled("Estadísticas", MultiChoice(shell, b.RefList.ToList(), ids, StatLabels.NameOf, "Todas", set));
                }
                case EffectRefKind.Form:
                case EffectRefKind.Mechanic:
                    return Labelled(kind == EffectRefKind.Form ? "Forma (id de la especie)" : "Mecánica (id)", ContentWidgets.WrapBox(b.Ref, set));
                case EffectRefKind.TagList:
                {
                    var tags = shell.Content.Db.Table(ContentSchemas.Moves).Records.SelectMany(r => r["etiquetas"].Split('|', ',')).Select(x => x.Trim())
                        .Where(x => x.Length > 0).Distinct().OrderBy(x => x).ToList();
                    return Labelled("Etiquetas", MultiChoice(shell, b.RefList.ToList(), tags, x => x, "— elige —", set));
                }
                case EffectRefKind.WeatherList:
                {
                    var t = shell.Content.Db.Table(ContentSchemas.Weathers);
                    var ids = new[] { "*" }.Concat(t.Ids).ToList();
                    return Labelled("Climas", MultiChoice(shell, b.RefList.ToList(), ids, id => RefName(shell, kind, id), "— elige —", set));
                }
                default:
                {
                    var cat = CategoryOf(kind);
                    if (cat == null) return Labelled("Valor", ContentWidgets.WrapBox(b.Ref, set));
                    bool list = EffectText.IsList(kind);
                    string label = cat == ContentSchemas.Types ? (list ? "Tipos" : "Tipo") : cat == ContentSchemas.Statuses ? (list ? "Estados" : "Estado")
                        : cat == ContentSchemas.Weathers ? "Clima" : cat == ContentSchemas.Moves ? "Movimiento" : "Efecto de campo";
                    if (!list) return Labelled(label, ContentWidgets.RefDropdown(shell, cat, b.Ref, set));
                    var current = b.RefList.ToList();
                    var row = Ui.Row(0).Wrap();
                    row.style.alignItems = Align.Center;
                    if (current.Count == 0) row.Add(Ui.Text(b.Action == EffectAction.CureStatus || b.Action == EffectAction.ImmuneToStatus ? "Cualquiera" : b.Action == EffectAction.Trap ? "Todos" : "—", 0.85f, dim: true).Margin(0, 0, 6, 0));
                    foreach (var id in current)
                    {
                        var x = id;
                        var chip = cat == ContentSchemas.Types ? ContentWidgets.TypeChip(shell.Content.Db, x) : ContentLook.Badge(RefName(shell, kind, x), null);
                        var holder = Ui.Row(0); holder.style.alignItems = Align.Center;
                        holder.With(chip, Ui.IconButton("cerrar", () => set(string.Join("|", current.Where(c => c != x))), "Quitar", iconSize: Ui.IconSize * 0.6f));
                        row.Add(holder);
                    }
                    row.Add(Ui.IconButton("mas", () => ContentPicker.Show(shell, cat, p => { if (!current.Contains(p)) set(string.Join("|", current.Append(p))); }), "Añadir"));
                    return Labelled(label, row);
                }
            }
        }

        /// <summary>Several values chosen from a short list (chips with ×, «+» opens the list).</summary>
        private static VisualElement MultiChoice(AppShell shell, List<string> current, List<string> options, Func<string, string> label, string empty, Action<string> set)
        {
            var row = Ui.Row(3).Wrap();
            row.style.alignItems = Align.Center;
            if (current.Count == 0) row.Add(Ui.Text(empty, 0.85f, dim: true).Margin(0, 0, 6, 0));
            foreach (var c in current)
            {
                var x = c;
                row.Add(Ui.Chip(label(x) + "  ×", true, () => set(string.Join("|", current.Where(v => v != x)))).Margin(0, 0, 2, 2));
            }
            var add = Ui.IconButton("mas", null, "Añadir");
            add.clicked += () =>
            {
                var r = add.worldBound;
                shell.ShowMenu(new Vector2(r.x, r.yMax + 2), options.Where(o => !current.Contains(o)).Select(o => new MenuItem(label(o), () => set(string.Join("|", current.Append(o))))).ToList());
            };
            row.Add(add);
            return row;
        }

        /// <summary>A condition as a sentence to edit: [of whom] [what is asked] [NO], then its value, then the sentence.</summary>
        private static VisualElement ConditionRow(AppShell shell, Condition c, string prefix, Action<Condition> put)
        {
            var box = Ui.Column(3).Pad(6, 4).Round(4).Border(1, "borde", 4);
            Condition Make(ConditionKind? kind = null, ConditionSubject? subject = null, Comparison? cmp = null, float? number = null, string text = null, bool? negate = null) =>
                new Condition(kind ?? c.Kind, subject ?? c.Subject, cmp ?? c.Comparison, number ?? c.Number, text ?? c.Text, negate ?? c.Negate);
            var row1 = Ui.Row(6).Wrap();
            row1.style.alignItems = Align.Center;
            row1.Add(Ui.Text(prefix, 0.9f, bold: true));
            if (Condition.UsesSubject(c.Kind))
                row1.Add(ContentWidgets.Dropdown(shell, SubjectLabels[(int)c.Subject], () => new List<MenuItem>
                {
                    new MenuItem(SubjectLabels[0], () => put(Make(subject: ConditionSubject.Self))), new MenuItem(SubjectLabels[1], () => put(Make(subject: ConditionSubject.Other))),
                }));
            row1.Add(ContentWidgets.Dropdown(shell, (int)c.Kind < KindLabels.Length ? KindLabels[(int)c.Kind] : c.Kind.ToString(),
                () => Enumerable.Range(0, KindLabels.Length).Select(i => new MenuItem(KindLabels[i], () => put(new Condition((ConditionKind)i, c.Subject, c.Comparison, c.Number, "", c.Negate)), isChecked: i == (int)c.Kind)).ToList()));
            row1.Add(Ui.Check("NO", c.Negate, on => put(Make(negate: on))));
            row1.Add(Ui.Spacer());
            row1.Add(Ui.IconButton("cerrar", () => put(null), "Quitar la condición", iconSize: Ui.IconSize * 0.7f));
            box.Add(row1);

            // The value
            var row2 = Ui.Row(6).Wrap();
            row2.style.alignItems = Align.Center;
            VisualElement CmpDropdown() => ContentWidgets.Dropdown(shell, CmpLabels[(int)c.Comparison],
                () => Enumerable.Range(0, CmpLabels.Length).Select(i => new MenuItem(CmpLabels[i], () => put(Make(cmp: (Comparison)i)))).ToList());
            VisualElement Number() => ContentWidgets.NumberBox(EffectText.N(c.Number), v => { if (TextNumbers.TryNumber(v, out var f)) put(Make(number: f)); }, 80);
            switch (c.Kind)
            {
                case ConditionKind.StatStage:
                    row2.Add(ContentWidgets.Dropdown(shell, c.Text.Length > 0 ? StatLabels.NameOf(c.Text) : "— estadística —",
                        () => StatLabels.ClassicIds.Concat(new[] { "accuracy", "evasion" }).Select(id => new MenuItem(StatLabels.NameOf(id), () => put(Make(text: id)))).ToList()));
                    row2.With(CmpDropdown(), Number());
                    break;
                case ConditionKind.RandomChance:
                    row2.With(Ui.Text("Probabilidad %", 0.85f, dim: true), Number());
                    break;
                case ConditionKind.MoveCategory:
                    row2.Add(ContentWidgets.Dropdown(shell, ConditionText.CategoryName(c.Text), () => new[] { ("Physical", "Físico"), ("Special", "Especial"), ("Status", "Estado") }
                        .Select(x => new MenuItem(x.Item2, () => put(Make(text: x.Item1)))).ToList()));
                    break;
                case ConditionKind.HasStatus: row2.Add(ContentWidgets.RefDropdown(shell, ContentSchemas.Statuses, c.Text, v => put(Make(text: v)))); break;
                case ConditionKind.IsType: case ConditionKind.MoveType: row2.Add(ContentWidgets.RefDropdown(shell, ContentSchemas.Types, c.Text, v => put(Make(text: v)))); break;
                case ConditionKind.Weather: row2.Add(ContentWidgets.RefDropdown(shell, ContentSchemas.Weathers, c.Text, v => put(Make(text: v)))); break;
                case ConditionKind.FieldCondition: row2.Add(ContentWidgets.RefDropdown(shell, ContentSchemas.SideEffects, c.Text, v => put(Make(text: v)))); break;
                case ConditionKind.MoveIs: row2.Add(ContentWidgets.RefDropdown(shell, ContentSchemas.Moves, c.Text, v => put(Make(text: v)))); break;
                case ConditionKind.IsSpecies: row2.Add(ContentWidgets.RefDropdown(shell, ContentSchemas.Species, c.Text, v => put(Make(text: v)))); break;
                case ConditionKind.MoveHasTag:
                {
                    var tags = shell.Content.Db.Table(ContentSchemas.Moves).Records.SelectMany(r => r["etiquetas"].Split('|', ',')).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().OrderBy(x => x).ToList();
                    row2.Add(ContentWidgets.Dropdown(shell, c.Text.Length > 0 ? c.Text : "— etiqueta —", () => tags.Select(t => new MenuItem(t, () => put(Make(text: t)))).ToList()));
                    break;
                }
                default:
                    if (Condition.UsesNumber(c.Kind)) row2.With(CmpDropdown(), Number());
                    else row2.Add(Ui.Text("(no necesita valor)", 0.8f, dim: true));
                    break;
            }
            box.Add(row2);
            bool missing = Condition.UsesText(c.Kind) && string.IsNullOrWhiteSpace(c.Text);
            var sentence = ConditionText.Describe(c);
            box.Add(Ui.Hint(missing ? "⚠ Falta elegir el valor" : "→ Si " + (sentence.Length > 0 ? char.ToLowerInvariant(sentence[0]) + sentence.Substring(1) : "")).Colored(missing ? "aviso" : "exito"));
            return box;
        }
    }
}
