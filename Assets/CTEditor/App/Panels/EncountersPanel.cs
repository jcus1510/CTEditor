using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using CTEditor.Editing;
using CTEditor.World.Domain;

namespace CTEditor.App
{
    /// <summary>
    /// Panel ENCUENTROS del tramo abierto, como en los juegos originales:
    ///   - zonas: «todo el tramo» (la tabla general) o pintadas en el mapa (un trozo de hierba, un lago...) que mandan
    ///     sobre la general;
    ///   - por cada zona, una tabla por MÉTODO (hierba, cueva, surf, buceo, cañas, golpe cabeza... o los propios) con su
    ///     probabilidad por paso y sus especies: niveles, peso y el porcentaje real que sale;
    ///   - cada especie puede salir solo a ciertas horas o con un interruptor activo.
    /// Todo se deshace con Ctrl+Z y se guarda con el mapa.
    /// </summary>
    public sealed class EncountersPanel : VisualElement
    {
        private static readonly (TimeOfDay time, string label)[] Times =
            { (TimeOfDay.Morning, "Mañana"), (TimeOfDay.Day, "Día"), (TimeOfDay.Evening, "Tarde"), (TimeOfDay.Night, "Noche") };

        private readonly AppShell _shell;
        private MapEditorSession S => _shell.Maps;
        private readonly ScrollView _scroll;
        private TimeOfDay _viewTime = TimeOfDay.Day;

        public EncountersPanel(AppShell shell)
        {
            _shell = shell;
            style.flexGrow = 1;
            _scroll = Ui.Scroll().Grow();
            Add(_scroll);
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                if (S == null) return;
                S.MapOpened += Refresh;
                S.EncountersChanged += Refresh;
                S.MethodsChanged += Refresh;
                S.SelectionChanged += Refresh;
                Refresh();
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (S == null) return;
                S.MapOpened -= Refresh;
                S.EncountersChanged -= Refresh;
                S.MethodsChanged -= Refresh;
                S.SelectionChanged -= Refresh;
            });
        }

        private void Refresh()
        {
            _scroll.Clear();
            var body = Ui.Column(10).Pad(10);
            _scroll.Add(body);
            var m = S?.Map;
            if (m == null) { body.Add(Ui.Hint("Abre un tramo para editar sus encuentros.")); return; }

            var top = Ui.Row(6);
            top.style.flexWrap = Wrap.Wrap;
            top.With(Ui.Heading("Encuentros de «" + m.Name + "»"), Ui.Spacer(),
                Ui.Button("Métodos…", () => MethodsDialog(), Ui.ButtonKind.Flat, "Hierba, surf, cañas... y los tuyos"));
            body.Add(top);
            if (S.Species == null || S.Species.All().Count == 0)
                body.Add(Ui.Text("No hay especies en datos/especies.csv: cópialas de un pack (Proyecto → Copiar datos de un pack).", wrap: true).Colored("aviso"));

            var zonesBar = Ui.Row(6);
            zonesBar.style.flexWrap = Wrap.Wrap;
            if (!m.Encounters.Any(a => a.WholeMap))
                zonesBar.Add(Ui.Button("+ Todo el tramo", () => S.AddArea("Todo el tramo", true), Ui.ButtonKind.Primary,
                    "La tabla general del tramo (lo que sale en toda su hierba, su agua...)"));
            zonesBar.Add(Ui.Button("+ Zona pintada", () => _shell.Prompt("Nueva zona", "Nombre", "Zona " + (m.Encounters.Count + 1), "Crear",
                n => { S.AddArea(n, false); _shell.ActiveEditor = "mapa"; }), Ui.ButtonKind.Normal,
                "Un trozo concreto (hierba del lago, claro del bosque...): se pinta en el mapa y manda sobre la general"));
            body.Add(zonesBar);

            if (m.Encounters.Count == 0)
            {
                body.Add(Ui.Hint("Este tramo no tiene encuentros. Empieza con «Todo el tramo» y añade un método (hierba, surf...)."));
                return;
            }
            foreach (var area in m.Encounters) body.Add(AreaCard(area));
        }

        private VisualElement AreaCard(EncounterArea area)
        {
            bool active = area.Id == S.ActiveAreaId;
            var card = Ui.Column(8).Bg(active ? "panel_alt" : "panel").Border(active ? 2 : 1, active ? "acento" : "borde", 6).Pad(10);
            ColorUtility.TryParseHtmlString(area.Color, out var color);
            var head = Ui.Row(6);
            head.style.flexWrap = Wrap.Wrap;
            var sw = Ui.Swatch(color, 16);
            head.With(sw, Ui.Text(area.Name, bold: true),
                Ui.Text(area.WholeMap ? "todo el tramo" : $"{area.Cells.Count} casillas pintadas", 0.85f, dim: true), Ui.Spacer());
            if (!active) head.Add(Ui.Button("Elegir", () => S.SetActiveArea(area.Id), Ui.ButtonKind.Flat));
            if (!area.WholeMap)
                head.Add(Ui.Button(S.Tool == MapTool.EncounterPaint && active ? "Pintando…" : "Pintar", () =>
                {
                    S.SetActiveArea(area.Id);
                    _shell.ActiveEditor = "mapa";
                    S.SetTool(MapTool.EncounterPaint);
                }, Ui.ButtonKind.Normal, "Pinta la zona en el mapa (clic derecho quita casillas)"));
            head.With(Ui.Button("Nombre", () => _shell.Prompt("Nombre de la zona", "Nombre", area.Name, "Cambiar", n => S.RenameArea(area.Id, n)), Ui.ButtonKind.Flat),
                Ui.Button("×", () => _shell.Confirm("Quitar zona", $"¿Quitar «{area.Name}» y sus tablas? Se puede deshacer con Ctrl+Z.", "Quitar",
                    () => S.RemoveArea(area.Id), danger: true), Ui.ButtonKind.Flat, "Quitar la zona"));
            card.Add(head);

            // Methods without a table yet: one click adds it.
            var add = Ui.Row(4);
            add.style.flexWrap = Wrap.Wrap;
            add.Add(Ui.Text("Añadir método:", 0.9f, dim: true).Margin(0, 0, 4, 0));
            foreach (var method in S.Methods.All.Where(x => area.TableFor(x.Id) == null))
            {
                var id = method.Id;
                add.Add(Ui.Chip("+ " + method.Label, false, () => S.AddTable(area.Id, id), TriggerHelp(method)).Margin(0, 0, 4, 4));
            }
            card.Add(add);

            var times = Ui.Row(4);
            times.Add(Ui.Text("Ver % a la hora:", 0.9f, dim: true));
            foreach (var (t, label) in Times)
            {
                var tt = t;
                times.Add(Ui.Chip(label, _viewTime == t, () => { _viewTime = tt; Refresh(); }));
            }
            if (area.Tables.Count > 0) card.Add(times);

            foreach (var table in area.Tables) card.Add(TableBox(area, table));
            return card;
        }

        private string TriggerHelp(EncounterMethod m) => m.Trigger switch
        {
            EncounterTrigger.StepOnTerrain => "Al andar sobre: " + string.Join(", ", m.TerrainTags.Select(S.Terrains.LabelOf)),
            EncounterTrigger.StepAnywhere => "Al andar por cualquier casilla de la zona",
            EncounterTrigger.UseItem => "Al usar el objeto «" + m.ItemId + "»",
            EncounterTrigger.Interact => "Al interactuar (árbol, roca...)",
            _ => "Cuando lo pida un evento",
        };

        private VisualElement TableBox(EncounterArea area, EncounterTable table)
        {
            var method = S.Methods.Find(table.MethodId);
            var box = Ui.Column(6).Bg("fondo").Border(1, "borde", 4).Pad(8);
            var head = Ui.Row(8);
            head.style.flexWrap = Wrap.Wrap;
            head.With(Ui.Text(method?.Label ?? table.MethodId + " (método borrado)", bold: true).Colored(method == null ? "error" : "texto"),
                Ui.NumberBox("Probabilidad %", table.RateWith(method), 0, 100, v => S.SetTableRate(area.Id, table.MethodId, v),
                    "Probabilidad de que salga algo en cada paso (o en cada uso, con cañas)"),
                Ui.Text(table.Rate < 0 ? "(la del método)" : "", 0.85f, dim: true),
                Ui.Spacer(),
                Ui.Button("Pesos clásicos", () => S.ApplyClassicWeights(area.Id, table.MethodId), Ui.ButtonKind.Flat,
                    "Hierba 20/20/10/10/10/10/5/5/4/4/1/1 · agua 60/30/5/4/1 · cañas 70/30, en el orden de la lista"),
                Ui.Button("×", () => S.RemoveTable(area.Id, table.MethodId), Ui.ButtonKind.Flat, "Quitar el método de esta zona"));
            box.Add(head);
            if (method != null) box.Add(Ui.Hint(TriggerHelp(method)));

            var chances = table.Chances(_viewTime, _ => true).ToDictionary(c => c.slot, c => c.percent);
            for (int i = 0; i < table.Slots.Count; i++) box.Add(SlotRow(area, table, i, chances));
            double total = chances.Values.Sum();
            var foot = Ui.Row(8);
            foot.With(Ui.Button("+ Especie", () => PickSpecies(id => S.AddSlot(area.Id, table.MethodId, new EncounterSlot(id, 2, 4, 10))),
                Ui.ButtonKind.Primary), Ui.Spacer(),
                Ui.Text(table.Slots.Count == 0 ? "Sin especies: aquí no sale nada." : $"A esa hora: {chances.Count} especies (suman {total:0} %)", 0.85f, dim: true));
            box.Add(foot);
            return box;
        }

        private VisualElement SlotRow(EncounterArea area, EncounterTable table, int index, Dictionary<EncounterSlot, double> chances)
        {
            var slot = table.Slots[index];
            string aid = area.Id, mid = table.MethodId;
            var row = Ui.Row(6).Pad(2, 2);
            row.style.flexWrap = Wrap.Wrap;
            var species = Ui.Button(SpeciesName(slot.SpeciesId), () => PickSpecies(id => S.UpdateSlot(aid, mid, index, x => x.SpeciesId = id)),
                Ui.ButtonKind.Normal, "Cambiar la especie");
            species.style.minWidth = 120;
            row.Add(species);
            row.Add(Ui.NumberBox("Nv.", slot.MinLevel, 1, 100, v => S.UpdateSlot(aid, mid, index, x => { x.MinLevel = v; if (x.MaxLevel < v) x.MaxLevel = v; })));
            row.Add(Ui.NumberBox("a", slot.MaxLevel, 1, 100, v => S.UpdateSlot(aid, mid, index, x => { x.MaxLevel = v; if (x.MinLevel > v) x.MinLevel = v; })));
            row.Add(Ui.NumberBox("Peso", slot.Weight, 0, 1000, v => S.UpdateSlot(aid, mid, index, x => x.Weight = v), "Frecuencia relativa"));
            var pct = chances.TryGetValue(slot, out var p) ? $"{p:0.#} %" : "no sale";
            row.Add(Ui.Text(pct, bold: true).Colored(chances.ContainsKey(slot) ? "exito" : "texto_suave"));
            foreach (var (t, label) in Times)
            {
                var tt = t;
                bool on = slot.Times == TimeOfDay.Any || (slot.Times & t) != 0;
                row.Add(Ui.Chip(label.Substring(0, 1), on && slot.Times != TimeOfDay.Any, () => S.UpdateSlot(aid, mid, index, x =>
                {
                    var cur = x.Times == TimeOfDay.Any ? TimeOfDay.Morning | TimeOfDay.Day | TimeOfDay.Evening | TimeOfDay.Night : x.Times;
                    cur ^= tt;
                    x.Times = cur == (TimeOfDay.Morning | TimeOfDay.Day | TimeOfDay.Evening | TimeOfDay.Night) || cur == TimeOfDay.Any ? TimeOfDay.Any : cur;
                }), label + (slot.Times == TimeOfDay.Any ? " (sale siempre; clic para limitar)" : on ? " (sale)" : " (no sale)")));
            }
            var flag = Ui.TextBox(null, slot.RequiredFlag, null, delayed: true);
            flag.tooltip = "Interruptor necesario (vacío = ninguno), p. ej. «tras_la_liga»";
            flag.style.width = 110;
            flag.RegisterValueChangedCallback(e => S.UpdateSlot(aid, mid, index, x => x.RequiredFlag = (e.newValue ?? "").Trim()));
            row.Add(flag);
            row.Add(Ui.Button("×", () => S.RemoveSlot(aid, mid, index), Ui.ButtonKind.Flat, "Quitar"));
            return row;
        }

        private string SpeciesName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "(elige)";
            var found = S.Species?.All().FirstOrDefault(s => s.id == id) ?? default;
            return found.id == null ? id + " (?)" : found.name;
        }

        /// <summary>A searchable list of the project's species.</summary>
        private void PickSpecies(Action<string> picked)
        {
            var all = S.Species?.All() ?? new (string, string)[0];
            var d = _shell.ShowDialog("Elegir especie", 40, 70);
            var list = Ui.Scroll().Grow();
            void Fill(string filter)
            {
                list.Clear();
                var f = (filter ?? "").Trim();
                foreach (var (id, name) in all.Where(s => f.Length == 0 || s.name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0
                                                                      || s.id.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0).Take(200))
                {
                    var sid = id;
                    var row = Ui.Row(8).Pad(8, 4).Round(3);
                    var l = Ui.Text(name);
                    l.pickingMode = PickingMode.Ignore;
                    var r = Ui.Text(id, 0.85f, dim: true);
                    r.pickingMode = PickingMode.Ignore;
                    row.With(l.Grow(), r);
                    row.RegisterCallback<PointerEnterEvent>(_ => row.style.backgroundColor = Ui.C("seleccion"));
                    row.RegisterCallback<PointerLeaveEvent>(_ => row.style.backgroundColor = new Color(0, 0, 0, 0));
                    row.RegisterCallback<PointerUpEvent>(_ => { _shell.CloseDialog(d); picked(sid); });
                    list.Add(row);
                }
                if (list.childCount == 0) list.Add(Ui.Hint(all.Count == 0 ? "No hay especies: copia los datos de un pack." : "Nada coincide."));
            }
            var search = Ui.TextBox("Buscar", "", Fill);
            d.Body.With(search, list);
            d.Buttons.Add(Ui.Button("Cancelar", () => _shell.CloseDialog(d)));
            Fill("");
            search.schedule.Execute(() => search.Q(className: TextField.inputUssClassName)?.Focus()).ExecuteLater(50);
        }

        // ── Methods ──────────────────────────────────────────────────────────────────────────────

        private static readonly string[] TriggerNames = { "Al pisar ciertos terrenos", "Al pisar la zona", "Al usar un objeto", "Al interactuar", "Desde un evento" };

        /// <summary>Lists the project's encounter methods; the author can change them and add new ones («volando»...).</summary>
        private void MethodsDialog()
        {
            var d = _shell.ShowDialog("Métodos de encuentro", 60, 80);
            var list = Ui.Scroll().Grow();
            void Fill()
            {
                list.Clear();
                foreach (var m in S.Methods.All)
                {
                    var method = m;
                    var row = Ui.Column(4).Bg("panel_alt").Border(1, "borde", 4).Pad(8);
                    var head = Ui.Row(6);
                    head.With(Ui.Text(m.Label, bold: true), Ui.Text(m.Id, 0.85f, dim: true), Ui.Text(m.BuiltIn ? "de fábrica" : "propio", 0.85f, dim: true).Colored(m.BuiltIn ? "texto_suave" : "acento"),
                        Ui.Spacer(), Ui.Button("Cambiar", () => EditMethod(method, Fill), Ui.ButtonKind.Flat));
                    if (!m.BuiltIn) head.Add(Ui.Button("Borrar", () => { S.RemoveMethod(method.Id); Fill(); }, Ui.ButtonKind.Flat));
                    row.With(head, Ui.Hint($"{TriggerNames[(int)m.Trigger]} · {m.DefaultRate} % · " + TriggerHelp(m)));
                    list.Add(row.Margin(0, 0, 0, 6));
                }
            }
            Fill();
            d.Body.With(Ui.Hint("Los de fábrica son los de los juegos originales (los números de terreno son los de Essentials). Crea los tuyos: «volando», «en sueños», «zona safari»..."), list);
            d.Buttons.With(Ui.Button("+ Método propio", () => EditMethod(null, Fill), Ui.ButtonKind.Primary), Ui.Button("Cerrar", () => _shell.CloseDialog(d)));
        }

        private void EditMethod(EncounterMethod existing, Action done)
        {
            var d = _shell.ShowDialog(existing == null ? "Nuevo método" : "Método «" + existing.Label + "»");
            string label = existing?.Label ?? "Volando";
            var trigger = existing?.Trigger ?? EncounterTrigger.Script;
            int rate = existing?.DefaultRate ?? 10;
            string terrains = existing == null ? "" : string.Join(", ", existing.TerrainTags);
            string item = existing?.ItemId ?? "";
            var body = Ui.Column(8);
            void Fill()
            {
                body.Clear();
                body.Add(Ui.TextBox("Nombre", label, v => label = v));
                var trig = Ui.Column(4);
                for (int i = 0; i < TriggerNames.Length; i++)
                {
                    var t = (EncounterTrigger)i;
                    var chip = Ui.Chip(TriggerNames[i], trigger == t, () => { trigger = t; Fill(); });
                    chip.style.alignSelf = Align.FlexStart;
                    trig.Add(chip);
                }
                body.With(Ui.Text("Cuándo se comprueba", bold: true), trig,
                    Ui.NumberBox("Probabilidad %", rate, 0, 100, v => rate = v));
                if (trigger == EncounterTrigger.StepOnTerrain || trigger == EncounterTrigger.UseItem)
                    body.With(Ui.TextBox("Terrenos (números)", terrains, v => terrains = v),
                        Ui.Hint(string.Join(" · ", S.Terrains.All.Where(t => t.Id != 0).Select(t => $"{t.Id} {t.Label}"))));
                if (trigger == EncounterTrigger.UseItem) body.Add(Ui.TextBox("Objeto (id)", item, v => item = v));
            }
            Fill();
            d.Body.Add(body);
            d.Buttons.With(Ui.Button("Cancelar", () => _shell.CloseDialog(d)), Ui.Button("Guardar", () =>
            {
                var id = existing?.Id ?? new MapTree().NewId(label);
                while (existing == null && S.Methods.Find(id) != null) id += "_2";
                var tags = (terrains ?? "").Split(',', ' ', ';').Select(x => int.TryParse(x.Trim(), out var n) ? n : -1).Where(n => n >= 0).ToArray();
                var m = new EncounterMethod(id, string.IsNullOrWhiteSpace(label) ? id : label.Trim(), trigger, rate, tags)
                    { ItemId = (item ?? "").Trim(), BuiltIn = existing?.BuiltIn ?? false };
                S.SaveMethod(m);
                _shell.CloseDialog(d);
                done();
            }, Ui.ButtonKind.Primary));
        }
    }
}
