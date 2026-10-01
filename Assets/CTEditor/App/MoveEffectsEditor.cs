using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using CTEditor.Content;
using CTEditor.GameDefinition.Domain.Conditions;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Text;

namespace CTEditor.App
{
    /// <summary>
    /// The move editors by blocks: effects (with their chance, «same roll as the previous one», target and conditions),
    /// requirements (only works if...), power changes (×N if...) and type by weather. Chosen from grouped lists; the
    /// sentence under each card says what the engine will do.
    /// </summary>
    public static class MoveEffectsEditor
    {
        private static string Name(AppShell shell, string kind, string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return "?";
            string cat = kind switch
            {
                "estado" => ContentSchemas.Statuses, "clima" => ContentSchemas.Weathers, "trampa" => ContentSchemas.Hazards, "lado" => ContentSchemas.SideEffects,
                "tipo" => ContentSchemas.Types, "habilidad" => ContentSchemas.Abilities, "movimiento" => ContentSchemas.Moves, _ => null,
            };
            if (cat == null) return id;
            var t = shell.Content.Db.Table(cat);
            var r = t.Find(id);
            return r != null ? t.NameOf(r) : id + " (no existe)";
        }

        // ── Effects ──────────────────────────────────────────────────────────────────────────────

        public static VisualElement Effects(AppShell shell, string value, Action<string> save)
        {
            var box = Ui.Column(6);
            List<ParsedEffect> effects;
            try { effects = MoveEffectText.Parse(value); }
            catch (FormatException e)
            {
                box.Add(Ui.Hint("⚠ No se puede leer: " + e.Message).Colored("error"));
                box.Add(ContentWidgets.WrapBox(value, save));
                return box;
            }
            void Save() => save(MoveEffectText.Format(effects));
            if (effects.Count == 0) box.Add(Ui.Hint("Sin efectos extra (solo hace daño, o nada si es de estado)."));
            for (int i = 0; i < effects.Count; i++) box.Add(Card(shell, effects, i, Save));
            var add = Ui.Button("+ Añadir efecto…", null, Ui.ButtonKind.Primary, "Los efectos están agrupados por tipo");
            add.clicked += () =>
            {
                var r = add.worldBound;
                shell.ShowMenu(new Vector2(r.x, r.yMax + 2), MoveEffectText.Groups.Select(g => MenuItem.Submenu(g,
                    MoveEffectText.Kinds.Where(k => k.Group == g).Select(k => new MenuItem(k.Label, () => { effects.Add(MoveEffectText.Default(k.Kind)); Save(); })).ToList())).ToList());
            };
            box.Add(add);
            return box;
        }

        private static VisualElement Card(AppShell shell, List<ParsedEffect> effects, int index, Action save)
        {
            var e = effects[index];
            var info = MoveEffectText.Info(e.Kind);
            var card = Ui.Column(5).Pad(8, 6).Round(6);
            card.style.backgroundColor = Ui.C("panel_alt");
            var top = Ui.Row(6).Wrap();
            top.style.alignItems = Align.Center;
            top.Add(ContentWidgets.Dropdown(shell, info.Label, () => MoveEffectText.Groups.Select(g => MenuItem.Submenu(g,
                MoveEffectText.Kinds.Where(k => k.Group == g).Select(k => new MenuItem(k.Label, () =>
                {
                    var d = MoveEffectText.Default(k.Kind);
                    d.Chance = e.Chance; d.Shared = e.Shared; d.Conditions = e.Conditions;
                    effects[index] = d; save();
                }, isChecked: k.Kind == e.Kind)).ToList())).ToList(), tooltip: "Qué hace"));
            if ((info.Fields & MoveEffectField.Target) != 0)
                top.Add(ContentWidgets.Dropdown(shell, e.Target == EffectTarget.Self ? "Al usuario" : "Al rival", () => new List<MenuItem>
                {
                    new MenuItem("Al usuario", () => { e.Target = EffectTarget.Self; save(); }), new MenuItem("Al rival", () => { e.Target = EffectTarget.Opponent; save(); }),
                }));
            top.Add(Ui.Spacer());
            top.Add(Ui.IconButton("arriba", () => { if (index > 0) { effects.RemoveAt(index); effects.Insert(index - 1, e); save(); } }, "Subir").SetEnabledLook(index > 0));
            top.Add(Ui.IconButton("abajo", () => { if (index < effects.Count - 1) { effects.RemoveAt(index); effects.Insert(index + 1, e); save(); } }, "Bajar").SetEnabledLook(index < effects.Count - 1));
            top.Add(Ui.IconButton("papelera", () => { effects.RemoveAt(index); save(); }, "Quitar este efecto"));
            card.Add(top);

            var f = info.Fields;
            var fields = Ui.Row(8).Wrap();
            fields.style.alignItems = Align.Center;
            void Label(string t) => fields.Add(Ui.Text(t, 0.85f, dim: true));
            if ((f & MoveEffectField.Status) != 0)
            {
                Label(e.Kind == MoveEffectKind.CureStatus ? "Estado (vacío = el principal)" : e.Kind == MoveEffectKind.Rampage ? "Luego queda" : "Estado");
                fields.Add(ContentWidgets.RefDropdown(shell, ContentSchemas.Statuses, e.Status ?? "", v => { e.Status = v; save(); }, e.Kind == MoveEffectKind.InflictStatus ? "Elegir…" : "Ninguno"));
                if (e.Kind != MoveEffectKind.InflictStatus && !string.IsNullOrEmpty(e.Status)) fields.Add(Ui.IconButton("cerrar", () => { e.Status = ""; save(); }, "Quitar", iconSize: Ui.IconSize * 0.7f));
            }
            if ((f & MoveEffectField.Stat) != 0)
            {
                Label("Estadística");
                fields.Add(ContentWidgets.Dropdown(shell, StatLabels.NameOf(e.Stat ?? "attack"), () => StatLabels.ClassicIds.Concat(new[] { "accuracy", "evasion" })
                    .Select(id => new MenuItem(StatLabels.NameOf(id), () => { e.Stat = id; save(); })).ToList()));
            }
            if ((f & MoveEffectField.Stages) != 0)
            {
                Label("Etapas");
                fields.Add(Ui.MiniNumber(e.Stages, -6, 6, v => { e.Stages = v; save(); }, "−6 a +6", 48));
            }
            if ((f & MoveEffectField.Amount) != 0)
            {
                Label("%");
                fields.Add(Ui.MiniNumber(Mathf.RoundToInt(e.Amount), 0, 100, v => { e.Amount = v; save(); }, "Porcentaje", 52));
            }
            if ((f & MoveEffectField.Weather) != 0)
            {
                Label("Clima");
                fields.Add(ContentWidgets.RefDropdown(shell, ContentSchemas.Weathers, e.Weather ?? "", v => { e.Weather = v; save(); }));
            }
            if ((f & MoveEffectField.WeatherTurns) != 0)
            {
                Label("Turnos (0 = los del clima)");
                fields.Add(Ui.MiniNumber(e.WeatherTurns, 0, 20, v => { e.WeatherTurns = v; save(); }, "Turnos", 48));
            }
            if ((f & MoveEffectField.Hazard) != 0)
            {
                Label(e.Kind == MoveEffectKind.ClearHazards ? "Trampa (vacío = todas)" : "Trampa");
                fields.Add(ContentWidgets.RefDropdown(shell, ContentSchemas.Hazards, e.Hazard ?? "", v => { e.Hazard = v; save(); }, e.Kind == MoveEffectKind.ClearHazards ? "Todas" : "Elegir…"));
            }
            if ((f & MoveEffectField.Side) != 0)
            {
                Label("Efecto de lado");
                fields.Add(ContentWidgets.RefDropdown(shell, ContentSchemas.SideEffects, e.Side ?? "", v => { e.Side = v; save(); }));
            }
            if ((f & MoveEffectField.Turns) != 0)
            {
                Label("Turnos (0 = los normales)");
                fields.Add(Ui.MiniNumber(e.Turns, 0, 10, v => { e.Turns = v; save(); }, "Turnos", 48));
            }
            if ((f & MoveEffectField.Type) != 0)
            {
                Label(e.Kind == MoveEffectKind.ChangeType ? "Tipo (vacío = el de su primer movimiento)" : "Tipo");
                fields.Add(ContentWidgets.RefDropdown(shell, ContentSchemas.Types, e.TypeId ?? "", v => { e.TypeId = v; save(); }));
            }
            if ((f & MoveEffectField.Ability) != 0)
            {
                Label("Habilidad");
                fields.Add(ContentWidgets.RefDropdown(shell, ContentSchemas.Abilities, e.Text ?? "", v => { e.Text = v; save(); }));
            }
            if ((f & MoveEffectField.Move) != 0)
            {
                Label("Movimiento");
                fields.Add(ContentWidgets.RefDropdown(shell, ContentSchemas.Moves, e.Text ?? "", v => { e.Text = v; save(); }));
            }
            if ((f & MoveEffectField.StatPair) != 0)
            {
                var pair = (e.Text ?? "attack,defense").Split(',');
                string a = pair[0].Trim(), b = pair.Length > 1 ? pair[1].Trim() : "defense";
                Label("Intercambia");
                fields.Add(ContentWidgets.Dropdown(shell, StatLabels.NameOf(a), () => StatLabels.ClassicIds.Select(id => new MenuItem(StatLabels.NameOf(id), () => { e.Text = id + "," + b; save(); })).ToList()));
                fields.Add(Ui.Text("y", 0.85f, dim: true));
                fields.Add(ContentWidgets.Dropdown(shell, StatLabels.NameOf(b), () => StatLabels.ClassicIds.Select(id => new MenuItem(StatLabels.NameOf(id), () => { e.Text = a + "," + id; save(); })).ToList()));
            }
            if (e.Kind == MoveEffectKind.SwitchSelf)
                fields.Add(Ui.Check("Pasa sus etapas (Relevo)", e.Stages > 0, on => { e.Stages = on ? 1 : 0; save(); }));
            if (fields.childCount > 0) card.Add(fields);

            for (int c = 0; c < e.Conditions.Count; c++)
            {
                int ci = c;
                card.Add(EffectBlocksEditor.ConditionRow(shell, e.Conditions[c], ci == 0 ? "Si" : "y", nc =>
                {
                    var list = e.Conditions.ToList();
                    if (nc == null) list.RemoveAt(ci); else list[ci] = nc;
                    e.Conditions = list;
                    save();
                }));
            }
            var opts = Ui.Row(8).Wrap();
            opts.style.alignItems = Align.Center;
            opts.Add(Ui.Button("+ Condición", () => { e.Conditions = e.Conditions.Append(new Condition(ConditionKind.HpPercent, ConditionSubject.Self, Comparison.LessOrEqual, 50f)).ToList(); save(); }));
            opts.Add(Ui.Text("Prob. %", 0.85f, dim: true));
            opts.Add(Ui.MiniNumber(Mathf.RoundToInt(e.Chance), 0, 100, v => { e.Chance = v; save(); }, "100 = siempre", 52));
            if (index > 0) opts.Add(Ui.Check("Mismo dado que el anterior", e.Shared, on => { e.Shared = on; save(); }));
            card.Add(opts);
            card.Add(Ui.Hint("→ " + MoveEffectText.Describe(e, (k, id) => Name(shell, k, id))));
            return card;
        }

        // ── Requirements: only works if... ───────────────────────────────────────────────────────

        public static VisualElement Conditions(AppShell shell, string value, Action<string> save, string empty)
        {
            var box = Ui.Column(4);
            List<Condition> list;
            try { list = ConditionText.ParseAll(value); }
            catch (FormatException e) { box.Add(Ui.Hint("⚠ " + e.Message).Colored("error")); box.Add(ContentWidgets.WrapBox(value, save)); return box; }
            void Save() => save(ConditionText.FormatAll(list));
            if (list.Count == 0) box.Add(Ui.Hint(empty));
            for (int i = 0; i < list.Count; i++)
            {
                int ci = i;
                box.Add(EffectBlocksEditor.ConditionRow(shell, list[i], i == 0 ? "Solo si" : "y", nc => { if (nc == null) list.RemoveAt(ci); else list[ci] = nc; Save(); }));
            }
            box.Add(Ui.Button("+ Condición", () => { list.Add(new Condition(ConditionKind.HasStatus, ConditionSubject.Other)); Save(); }));
            return box;
        }

        // ── Power changes: ×N if... ──────────────────────────────────────────────────────────────

        public static VisualElement PowerModifiers(AppShell shell, string value, Action<string> save)
        {
            var box = Ui.Column(4);
            List<(float multiplier, Condition[] conditions)> mods;
            try { mods = ConditionText.ParseModifiers(value); }
            catch (FormatException e) { box.Add(Ui.Hint("⚠ " + e.Message).Colored("error")); box.Add(ContentWidgets.WrapBox(value, save)); return box; }
            void Save() => save(ConditionText.FormatModifiers(mods.Select(m => (m.multiplier, (IReadOnlyList<Condition>)m.conditions))));
            if (mods.Count == 0) box.Add(Ui.Hint("Su potencia no cambia."));
            for (int i = 0; i < mods.Count; i++)
            {
                int mi = i;
                var card = Ui.Column(3).Pad(8, 6).Round(6);
                card.style.backgroundColor = Ui.C("panel_alt");
                var top = Ui.Row(6);
                top.style.alignItems = Align.Center;
                top.Add(Ui.Text("Potencia ×", 0.9f));
                top.Add(ContentWidgets.NumberBox(EffectText.N(mods[i].multiplier), v => { if (TextNumbers.TryNumber(v, out var m)) { mods[mi] = (m, mods[mi].conditions); Save(); } }, 70));
                top.Add(Ui.Spacer());
                top.Add(Ui.IconButton("papelera", () => { mods.RemoveAt(mi); Save(); }, "Quitar"));
                card.Add(top);
                for (int c = 0; c < mods[i].conditions.Length; c++)
                {
                    int ci = c;
                    card.Add(EffectBlocksEditor.ConditionRow(shell, mods[i].conditions[c], c == 0 ? "Si" : "y", nc =>
                    {
                        var l = mods[mi].conditions.ToList();
                        if (nc == null) l.RemoveAt(ci); else l[ci] = nc;
                        mods[mi] = (mods[mi].multiplier, l.ToArray());
                        Save();
                    }));
                }
                card.Add(Ui.Button("+ Condición", () => { mods[mi] = (mods[mi].multiplier, mods[mi].conditions.Append(new Condition(ConditionKind.Weather, ConditionSubject.Self, text: "rain")).ToArray()); Save(); }));
                box.Add(card);
            }
            box.Add(Ui.Button("+ Cambio de potencia", () => { mods.Add((2f, new[] { new Condition(ConditionKind.HpPercent, ConditionSubject.Other, Comparison.LessOrEqual, 50f) })); Save(); }));
            return box;
        }

        // ── Type by weather: rain → water ────────────────────────────────────────────────────────

        public static VisualElement TypeByWeather(AppShell shell, string value, Action<string> save)
        {
            var pairs = (value ?? "").Split('|').Select(p => p.Split(':')).Where(p => p.Length == 2).Select(p => (weather: p[0].Trim(), type: p[1].Trim())).ToList();
            void Save() => save(string.Join("|", pairs.Select(p => $"{p.weather}:{p.type}")));
            var box = Ui.Column(3);
            if (pairs.Count == 0) box.Add(Ui.Hint("Siempre es de su tipo."));
            for (int i = 0; i < pairs.Count; i++)
            {
                int pi = i;
                var row = Ui.Row(6);
                row.style.alignItems = Align.Center;
                row.Add(Ui.Text("Con", 0.85f, dim: true));
                row.Add(ContentWidgets.RefDropdown(shell, ContentSchemas.Weathers, pairs[i].weather, v => { pairs[pi] = (v, pairs[pi].type); Save(); }));
                row.Add(Ui.Text("es de tipo", 0.85f, dim: true));
                row.Add(ContentWidgets.RefDropdown(shell, ContentSchemas.Types, pairs[i].type, v => { pairs[pi] = (pairs[pi].weather, v); Save(); }));
                row.Add(Ui.IconButton("cerrar", () => { pairs.RemoveAt(pi); Save(); }, "Quitar", iconSize: Ui.IconSize * 0.7f));
                box.Add(row);
            }
            box.Add(Ui.Button("+ Añadir", () => { pairs.Add(("rain", "water")); Save(); }));
            return box;
        }
    }
}
