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
        private static readonly (TimeOfDay time, string label, string letter)[] Times =
        {
            (TimeOfDay.Morning, "Mañana", "M"), (TimeOfDay.Day, "Día", "D"), (TimeOfDay.Evening, "Tarde", "T"), (TimeOfDay.Night, "Noche", "N"),
        };

        private readonly AppShell _shell;
        private MapEditorSession S => _shell.Maps;
        private readonly ScrollView _scroll;
        /// <summary>Hour whose % is shown; null = all hours (the % of the weights).</summary>
        private TimeOfDay? _viewTime;
        /// <summary>The method (table) open in the chosen zone.</summary>
        private string _methodId;

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

        private static VisualElement Section(string title, VisualElement right = null)
        {
            var head = Ui.Row(6);
            head.style.marginTop = 4;
            head.Add(Ui.Text(title.ToUpperInvariant(), 0.78f, dim: true, bold: true).Grow());
            if (right != null) head.Add(right);
            return head;
        }

        private void Refresh()
        {
            _scroll.Clear();
            var body = Ui.Column(8).Pad(10, 8);
            _scroll.Add(body);
            var m = S?.Map;
            if (m == null) { body.Add(Ui.Hint("Abre un tramo para editar sus encuentros.")); return; }

            // Header: the section, «highlight all» and the methods.
            var top = Ui.Row(4);
            top.With(Ui.Text(m.Name, 1.05f, bold: true).Grow(),
                Ui.IconToggle(S.HighlightAllAreas ? "capas_todas" : "ver", S.HighlightAllAreas, v => S.SetHighlightAllAreas(v),
                    S.HighlightAllAreas ? "Resaltando TODAS las zonas en el mapa (clic: solo la elegida)" : "Resaltando solo la zona elegida (clic: todas)"),
                Ui.Button("Métodos…", () => MethodsDialog(), Ui.ButtonKind.Flat, "Hierba, surf, cañas... y los tuyos («volando»...)"));
            body.Add(top);
            if (S.Species == null || S.Species.All().Count == 0)
                body.Add(Ui.Text("No hay especies en datos/especies.csv: cópialas de un pack (Proyecto → Copiar datos de un pack).", 0.9f, wrap: true).Colored("aviso"));

            // Zones as a compact list (like the layers).
            Button add = null;
            add = Ui.IconButton("mas", () =>
            {
                var r = add.worldBound;
                var items = new List<MenuItem>();
                if (!m.Encounters.Any(a => a.WholeMap))
                    items.Add(new MenuItem("Todo el tramo (la tabla general)", () => S.AddArea("Todo el tramo", true)));
                items.Add(new MenuItem("Zona pintada (un trozo del mapa)…", () => _shell.Prompt("Nueva zona", "Nombre", "Zona " + (m.Encounters.Count + 1), "Crear",
                    n => { S.AddArea(n, false); _shell.ActiveEditor = "mapa"; })));
                _shell.ShowMenu(new Vector2(r.x, r.yMax + 2), items);
            }, "Añadir una zona: todo el tramo o una zona pintada en el mapa");
            body.Add(Section("Zonas", add));
            if (m.Encounters.Count == 0)
            {
                body.Add(Ui.Hint("Sin zonas: con «+» crea «Todo el tramo» (lo que sale en toda su hierba, su agua...) o una zona pintada (un trozo concreto, que manda sobre la general)."));
                return;
            }
            var list = Ui.Column(2);
            foreach (var area in m.Encounters) list.Add(AreaRow(area));
            body.Add(list);

            // Painting banner, with a clear way out.
            var activeArea = S.ActiveArea;
            if (activeArea != null && S.Tool == MapTool.EncounterPaint)
            {
                var banner = Ui.Row(6).Pad(8, 6).Round(4);
                banner.style.backgroundColor = Ui.WithAlpha(Ui.C("acento"), 0.18f);
                banner.Border(1, "acento", 4);
                banner.With(Ui.Text($"Pintando «{activeArea.Name}» en el mapa · clic derecho quita casillas", 0.88f, wrap: true).Grow(),
                    Ui.Button("Dejar de pintar", () => S.StopAreaPainting(), Ui.ButtonKind.Primary, "Vuelve al lápiz (también con Esc o B)"));
                body.Add(banner);
            }

            if (activeArea != null) Detail(body, activeArea);
        }

        private VisualElement AreaRow(EncounterArea area)
        {
            bool active = area.Id == S.ActiveAreaId;
            bool painting = active && S.Tool == MapTool.EncounterPaint;
            var row = Ui.Row(6).Pad(6, 3).Round(4);
            if (active) row.style.backgroundColor = Ui.C("seleccion");
            ColorUtility.TryParseHtmlString(area.Color, out var color);
            var sw = Ui.Swatch(color, 14);
            sw.pickingMode = PickingMode.Ignore;
            var names = Ui.Column(0).Grow();
            names.pickingMode = PickingMode.Ignore;
            var n = Ui.Text(area.Name, 0.95f, bold: active); n.pickingMode = PickingMode.Ignore;
            var info = Ui.Text((area.WholeMap ? "todo el tramo" : $"{area.Cells.Count} casillas") + $" · {area.Tables.Count} método(s)", 0.78f, dim: true);
            info.pickingMode = PickingMode.Ignore;
            names.With(n, info);
            row.With(sw, names);
            if (!area.WholeMap)
            {
                var paint = Ui.IconButton("zona", () =>
                {
                    if (painting) { S.StopAreaPainting(); return; }
                    S.SetActiveArea(area.Id);
                    _shell.ActiveEditor = "mapa";
                    S.SetTool(MapTool.EncounterPaint);
                }, painting ? "Dejar de pintar (Esc)" : "Pintar la zona en el mapa (clic derecho quita casillas)", painting);
                row.Add(paint);
            }
            row.Add(Ui.IconButton("papelera", () => _shell.Confirm("Quitar zona", $"¿Quitar «{area.Name}» y sus tablas? Se puede deshacer con Ctrl+Z.", "Quitar",
                () => S.RemoveArea(area.Id), danger: true), "Quitar la zona"));
            row.RegisterCallback<PointerUpEvent>(e => { if (e.button == 0 && (e.target == row || e.target == names)) S.SetActiveArea(area.Id); });
            row.RegisterCallback<ClickEvent>(e =>
            {
                if (e.clickCount == 2 && (e.target == row || e.target == names))
                    _shell.Prompt("Nombre de la zona", "Nombre", area.Name, "Cambiar", x => S.RenameArea(area.Id, x));
            });
            row.tooltip = "Clic: elegirla · doble clic: cambiar el nombre";
            return row;
        }

        /// <summary>The chosen zone: its methods as tabs and the table of the open one.</summary>
        private void Detail(VisualElement body, EncounterArea area)
        {
            if (_methodId == null || area.TableFor(_methodId) == null) _methodId = area.Tables.FirstOrDefault()?.MethodId;

            Button addMethod = null;
            addMethod = Ui.IconButton("mas", () =>
            {
                var r = addMethod.worldBound;
                var items = S.Methods.All.Where(x => area.TableFor(x.Id) == null)
                    .Select(x => { var id = x.Id; return new MenuItem(x.Label, () => { _methodId = id; S.AddTable(area.Id, id); }); }).ToList();
                if (items.Count == 0) items.Add(new MenuItem("Ya tiene todos los métodos", null, enabled: false));
                _shell.ShowMenu(new Vector2(r.x, r.yMax + 2), items);
            }, "Añadir un método a esta zona (hierba, surf, cañas...)");
            body.Add(Section("Métodos de «" + area.Name + "»", addMethod));
            if (area.Tables.Count == 0)
            {
                body.Add(Ui.Hint("Sin métodos: con «+» añade hierba, cueva, surf, cañas... Cada método tiene su lista de especies."));
                return;
            }
            var tabs = Ui.Row(0).Wrap();
            foreach (var t in area.Tables)
            {
                var method = S.Methods.Find(t.MethodId);
                var id = t.MethodId;
                var chip = Ui.Chip($"{method?.Label ?? id} · {t.RateWith(method)} %", id == _methodId, () => { _methodId = id; Refresh(); },
                    method != null ? TriggerHelp(method) : "Método borrado");
                chip.style.fontSize = Mathf.Round(Ui.FontSize * 0.88f);
                tabs.Add(chip.Margin(0, 0, 4, 4));
            }
            body.Add(tabs);
            var table = area.TableFor(_methodId);
            if (table != null) body.Add(TableBox(area, table));
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
            var box = Ui.Column(8).Bg("fondo").Border(1, "borde", 6).Pad(10, 8);

            var head = Ui.Row(8).Wrap();
            head.With(Ui.NumberBox("Probabilidad", table.RateWith(method), 0, 100, v => S.SetTableRate(area.Id, table.MethodId, v),
                    "Probabilidad de que salga algo en cada paso (o en cada uso, con cañas)"),
                Ui.Text(table.Rate < 0 ? "% (la del método)" : "% por paso", 0.85f, dim: true),
                Ui.Spacer(),
                Ui.Button("Pesos clásicos", () => S.ApplyClassicWeights(area.Id, table.MethodId), Ui.ButtonKind.Flat,
                    "Pone los pesos de los juegos originales en el orden de la lista: hierba 20/20/10/10/10/10/5/5/4/4/1/1 · agua 60/30/5/4/1 · cañas 70/30"),
                Ui.IconButton("papelera", () => S.RemoveTable(area.Id, table.MethodId), "Quitar este método de la zona"));
            box.Add(head);
            if (method != null) box.Add(Ui.Hint(TriggerHelp(method)));

            // Which hour the % are for: all (the weights) or one of them.
            var hours = Ui.Row(0).Wrap();
            hours.Add(Ui.Text("Ver %:", 0.85f, dim: true).Margin(0, 0, 6, 0));
            hours.Add(Ui.Chip("Todas las horas", _viewTime == null, () => { _viewTime = null; Refresh(); }, "Según los pesos, sin mirar la hora").Margin(0, 0, 3, 3));
            foreach (var (t, label, _) in Times)
            {
                var tt = t;
                hours.Add(Ui.Chip(label, _viewTime == t, () => { _viewTime = tt; Refresh(); }, "Lo que sale por la " + label.ToLowerInvariant()).Margin(0, 0, 3, 3));
            }
            box.Add(hours);

            var chances = (_viewTime.HasValue ? table.Chances(_viewTime.Value, _ => true) : table.BaseChances()).ToDictionary(c => c.slot, c => c.percent);
            for (int i = 0; i < table.Slots.Count; i++) box.Add(SlotCard(area, table, i, chances));

            var foot = Ui.Row(8);
            foot.With(Ui.Button("+ Especie", () => PickSpecies(id => S.AddSlot(area.Id, table.MethodId, new EncounterSlot(id, 2, 4, 10))),
                    Ui.ButtonKind.Primary),
                Ui.Spacer(),
                Ui.Text(table.Slots.Count == 0 ? "Sin especies: aquí no sale nada." : $"{chances.Count} de {table.Slots.Count} especies · {chances.Values.Sum():0} %", 0.82f, dim: true));
            box.Add(foot);
            return box;
        }

        /// <summary>One species: name, % with a bar, levels, weight, hours (all lit = always) and switch.</summary>
        private VisualElement SlotCard(EncounterArea area, EncounterTable table, int index, Dictionary<EncounterSlot, double> chances)
        {
            var slot = table.Slots[index];
            string aid = area.Id, mid = table.MethodId;
            var card = Ui.Column(6).Bg("panel").Border(1, "borde", 4).Pad(8, 6);

            var top = Ui.Row(8);
            var species = Ui.Button(SpeciesName(slot.SpeciesId), () => PickSpecies(id => S.UpdateSlot(aid, mid, index, x => x.SpeciesId = id)),
                Ui.ButtonKind.Normal, "Cambiar la especie");
            species.style.flexShrink = 1;
            species.style.minWidth = 90;
            species.style.unityTextAlign = TextAnchor.MiddleLeft;
            species.style.unityFontStyleAndWeight = FontStyle.Bold;
            bool shows = chances.TryGetValue(slot, out var p);
            // A bar with the real %.
            var bar = new VisualElement().Bg("fondo").Round(3);
            bar.style.height = 8;
            bar.Grow();
            bar.style.minWidth = 40;
            var fill = new VisualElement().Bg(shows ? "exito" : "borde").Round(3);
            fill.style.height = Length.Percent(100);
            fill.style.width = Length.Percent(shows ? (float)Math.Max(2, p) : 0);
            bar.Add(fill);
            var pct = Ui.Text(shows ? $"{p:0.#} %" : "no sale", 0.9f, bold: true).Colored(shows ? "exito" : "texto_suave").NoShrink();
            pct.style.minWidth = Ui.FontSize * 3.6f;
            pct.style.unityTextAlign = TextAnchor.MiddleRight;
            top.With(species, bar, pct, Ui.IconButton("papelera", () => S.RemoveSlot(aid, mid, index), "Quitar esta especie"));
            card.Add(top);

            var bottom = Ui.Row(14).Wrap();
            var levels = Ui.Row(0);
            levels.With(Ui.NumberBox("Nv.", slot.MinLevel, 1, 100, v => S.UpdateSlot(aid, mid, index, x => { x.MinLevel = v; if (x.MaxLevel < v) x.MaxLevel = v; }), "Nivel mínimo"),
                Ui.Text("–", dim: true).Margin(4, 0, 4, 0),
                Ui.NumberBox(null, slot.MaxLevel, 1, 100, v => S.UpdateSlot(aid, mid, index, x => { x.MaxLevel = v; if (x.MinLevel > v) x.MinLevel = v; }), "Nivel máximo"));
            var weight = Ui.NumberBox("Peso", slot.Weight, 0, 1000, v => S.UpdateSlot(aid, mid, index, x => x.Weight = v),
                "Frecuencia relativa: con pesos 20 y 10, la primera sale el doble");

            // Hours: all four lit = always. Clicking one turns it off (or on); at least one must stay.
            var hours = Ui.Row(2);
            var current = slot.Times == TimeOfDay.Any ? TimeOfDay.AllDay : slot.Times;
            foreach (var (t, label, letter) in Times)
            {
                var tt = t;
                bool on = (current & t) != 0;
                var chip = Ui.Chip(letter, on, () =>
                {
                    var next = current ^ tt;
                    if (next == 0) { _shell.Warn("Tiene que salir al menos a una hora."); return; }
                    S.UpdateSlot(aid, mid, index, x => x.Times = next == TimeOfDay.AllDay ? TimeOfDay.Any : next);
                }, label + (on ? ": sale (clic para quitar)" : ": no sale (clic para añadir)"));
                chip.style.width = Ui.ControlHeight - 2;
                chip.style.paddingLeft = 0; chip.style.paddingRight = 0;
                hours.Add(chip);
            }

            bool hasFlag = !string.IsNullOrEmpty(slot.RequiredFlag);
            var flag = Ui.IconButton("interruptor", () => _shell.Prompt("Interruptor necesario",
                    "Interruptor (vacío = ninguno)", slot.RequiredFlag, "Guardar", v => S.UpdateSlot(aid, mid, index, x => x.RequiredFlag = (v ?? "").Trim())),
                hasFlag ? $"Solo sale con el interruptor «{slot.RequiredFlag}» activo (clic para cambiarlo)" : "Sale sin condiciones (clic: exigir un interruptor, p. ej. «tras_la_liga»)", hasFlag);
            var flagBox = Ui.Row(4);
            flagBox.Add(flag);
            if (hasFlag) flagBox.Add(Ui.Text(slot.RequiredFlag, 0.8f, dim: true));
            bottom.With(levels, weight, hours, flagBox);
            card.Add(bottom);
            return card;
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
