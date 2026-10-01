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

        private static VisualElement Section(string title, VisualElement right = null) => Ui.SectionTitle(title, right);

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
                    items.Add(new MenuItem("Todo el tramo (la tabla general)…", () => NewZoneDialog(true)));
                items.Add(new MenuItem("Zona pintada (un trozo del mapa)…", () => NewZoneDialog(false)));
                _shell.ShowMenu(new Vector2(r.x, r.yMax + 2), items);
            }, "Añadir una zona: todo el tramo o una zona pintada en el mapa");
            // Zone tools: simulator and the (optional) checks.
            var zoneTools = Ui.Row(2);
            zoneTools.With(Ui.IconButton("dado", SimulatorDialog, "Simular encuentros: anda muchos pasos y mira qué sale (en una ventana aparte)"),
                Ui.IconButton("ajustes", () => SettingsMenu(), "Ajustes de los encuentros"), add);
            body.Add(Section("Zonas", zoneTools));
            if (m.Encounters.Count == 0)
            {
                body.Add(Ui.Hint("Sin zonas: con «+» crea «Todo el tramo» (lo que sale en toda su hierba, su agua...) o una zona pintada (un trozo concreto, que manda sobre la general)."));
                return;
            }
            var list = Ui.Column(2);
            foreach (var area in m.Encounters) list.Add(AreaRow(area));
            body.Add(list);

            var activeArea = S.ActiveArea;
            if (activeArea != null) Detail(body, activeArea);
        }

        private const string TerrainCheckPref = "encuentros_aviso_terreno";
        private bool TerrainCheck => _shell.Workspace.Pref(TerrainCheckPref, "no") == "si";

        private void SettingsMenu()
        {
            var at = this.worldBound;
            _shell.ShowMenu(new Vector2(at.x + 20, at.y + 40), new List<MenuItem>
            {
                new MenuItem("Avisar de casillas pintadas sin su terreno", () =>
                {
                    _shell.Workspace.SetPref(TerrainCheckPref, TerrainCheck ? "no" : "si");
                    _shell.SaveWorkspaceSoon();
                    Refresh();
                    S.SetHighlightAllAreas(S.HighlightAllAreas); // redraw the map marks
                }, isChecked: TerrainCheck),
                new MenuItem("Métodos de encuentro…", MethodsDialog),
            });
        }

        /// <summary>A new zone: its name (painted ones) and a template to start from (in the same small dialog).</summary>
        private void NewZoneDialog(bool whole)
        {
            var m = S.Map;
            var d = _shell.ShowDialog(whole ? "Todo el tramo" : "Nueva zona pintada", 40);
            string name = whole ? "Todo el tramo" : "Zona " + (m.Encounters.Count + 1);
            var template = EncounterTemplate.BuiltIn[0];
            var cards = Ui.Column(4);
            void Fill()
            {
                cards.Clear();
                foreach (var t in EncounterTemplate.BuiltIn)
                {
                    var tt = t;
                    bool on = t == template;
                    var row = Ui.Row(10).Pad(10, 6).Round(4);
                    row.style.backgroundColor = on ? Ui.C("seleccion") : new Color(0, 0, 0, 0);
                    row.Border(1, on ? "acento" : "borde", 4);
                    var texts = Ui.Column(1).Grow();
                    texts.pickingMode = PickingMode.Ignore;
                    var n = Ui.Text(t.Name, bold: true); n.pickingMode = PickingMode.Ignore;
                    var h = Ui.Text(t.Description, 0.82f, dim: true, wrap: true); h.pickingMode = PickingMode.Ignore;
                    texts.With(n, h);
                    row.Add(texts);
                    row.RegisterCallback<PointerUpEvent>(_ => { template = tt; Fill(); });
                    cards.Add(row);
                }
            }
            Fill();
            if (!whole) d.Body.Add(Ui.TextBox("Nombre", name, v => name = v));
            d.Body.With(Ui.Text("Empezar con", 0.85f, dim: true), cards,
                Ui.Hint("Las plantillas traen especies típicas con los pesos clásicos; las que no estén en tus datos se saltan. Luego se cambia todo."));
            d.Buttons.With(Ui.Button("Cancelar", () => _shell.CloseDialog(d)), Ui.Button("Crear", () =>
            {
                _shell.CloseDialog(d);
                var area = S.AddArea(name, whole);
                if (area != null && template.Id != "vacia") S.ApplyTemplate(area.Id, template);
                if (!whole) _shell.ActiveEditor = "mapa";
            }, Ui.ButtonKind.Primary));
        }

        /// <summary>
        /// The encounter simulator in its own window: walk N steps with the chosen zone and method, at an hour, and see
        /// what comes out (how many, %, levels) and how often.
        /// </summary>
        private void SimulatorDialog()
        {
            var area = S.ActiveArea;
            if (area == null || area.Tables.Count == 0) { _shell.Warn("Elige una zona con algún método para simular."); return; }
            var d = _shell.ShowDialog("Simular encuentros · " + area.Name, 46, 74);
            string method = area.TableFor(_methodId) != null ? _methodId : area.Tables[0].MethodId;
            TimeOfDay? time = _viewTime;
            int steps = 1000, seed = 1;
            bool flags = false;
            var controls = Ui.Column(8);
            var results = Ui.Scroll().Grow();
            void Run()
            {
                controls.Clear();
                var methods = Ui.Row(0).Wrap();
                methods.Add(Ui.Text("Método", 0.82f, dim: true).Margin(0, 0, 8, 0));
                foreach (var t in area.Tables)
                {
                    var id = t.MethodId;
                    methods.Add(Small(Ui.Chip(S.Methods.Find(id)?.Label ?? id, id == method, () => { method = id; Run(); })).Margin(0, 2, 4, 2));
                }
                var hours = Ui.Row(2);
                hours.Add(Ui.Text("Hora", 0.82f, dim: true).Margin(0, 0, 8, 0));
                hours.Add(Ui.IconButton("reloj", () => { time = null; Run(); }, "Todas las horas", time == null));
                foreach (var (t, label, _) in Times) { var tt = t; hours.Add(Ui.IconButton(TimeIcon(t), () => { time = tt; Run(); }, label, time == t)); }
                var stepRow = Ui.Row(0).Wrap();
                stepRow.Add(Ui.Text("Pasos", 0.82f, dim: true).Margin(0, 0, 8, 0));
                foreach (var n in new[] { 100, 1000, 10000 }) { int nn = n; stepRow.Add(Small(Ui.Chip(n.ToString("N0"), steps == n, () => { steps = nn; Run(); })).Margin(0, 2, 4, 2)); }
                stepRow.Add(Ui.Check("Interruptores encendidos", flags, v => { flags = v; Run(); }).Margin(10, 0, 0, 0));
                stepRow.Add(Ui.Spacer());
                stepRow.Add(Ui.Button("Otra vez", () => { seed++; Run(); }, Ui.ButtonKind.Normal, "Repetir con otra suerte"));
                controls.With(methods, hours, stepRow);

                var table = area.TableFor(method);
                var m = S.Methods.Find(method);
                var r = EncounterSimulator.Run(table, table.RateWith(m), time, _ => flags, steps, seed);
                results.Clear();
                string every = r.Encounters == 0 ? "No salió nada." : $"{r.Encounters} encuentros en {steps:N0} pasos: uno cada {r.StepsPerEncounter:0.#} pasos." + (r.Doubles > 0 ? $" {r.Doubles} dobles." : "");
                results.Add(Ui.Text(every, bold: true).Margin(0, 4, 0, 8));
                foreach (var row in r.Rows())
                {
                    var line = Ui.Row(10).Pad(8, 6);
                    line.style.borderBottomWidth = 1; line.style.borderBottomColor = Ui.WithAlpha(Ui.C("borde"), 0.5f);
                    var bar = new VisualElement().Bg("panel_alt").Round(2);
                    bar.style.width = 90; bar.style.height = 6;
                    var fill = new VisualElement().Bg("exito").Round(2);
                    fill.style.height = Length.Percent(100); fill.style.width = Length.Percent((float)row.percent);
                    bar.Add(fill);
                    var name = Ui.Text(SpeciesName(row.species), bold: true).Grow();
                    var pct = Ui.Text($"{row.percent:0.#} %", 0.9f).NoShrink(); pct.style.width = 56;
                    var cnt = Ui.Text($"{row.count}×", 0.85f, dim: true).NoShrink(); cnt.style.width = 52;
                    var lv = Ui.Text(row.minLevel == row.maxLevel ? $"Nv. {row.minLevel}" : $"Nv. {row.minLevel}–{row.maxLevel}", 0.85f, dim: true).NoShrink(); lv.style.width = 80;
                    line.With(SpeciesPicture(row.species, 24), name, bar, pct, cnt, lv);
                    results.Add(line);
                }
            }
            d.Body.With(controls, Ui.Separator(), results);
            d.Buttons.With(Ui.Hint("La simulación no cambia nada: solo cuenta."), Ui.Spacer(), Ui.Button("Cerrar", () => _shell.CloseDialog(d), Ui.ButtonKind.Primary));
            Run();
        }

        private VisualElement AreaRow(EncounterArea area)
        {
            bool active = area.Id == S.ActiveAreaId;
            bool painting = active && S.Tool == MapTool.EncounterPaint;
            var row = Ui.Row(6).Pad(6, 3).Round(4);
            if (active) row.style.backgroundColor = Ui.C("seleccion");
            ColorUtility.TryParseHtmlString(area.Color, out var color);
            var sw = Ui.Swatch(color, 16);
            sw.tooltip = "Cambiar el color de la zona";
            sw.RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.button != 0) return;
                e.StopPropagation();
                ColorPicker.Show(_shell, "Color de «" + area.Name + "»", color, c => S.SetAreaColor(area.Id, "#" + ColorUtility.ToHtmlStringRGB(c)));
            });
            var names = Ui.Column(0).Grow();
            names.pickingMode = PickingMode.Ignore;
            var n = Ui.Text(area.Name, 0.95f, bold: active); n.pickingMode = PickingMode.Ignore;
            var info = Ui.Text((area.WholeMap ? "todo el tramo" : $"{area.Cells.Count} casillas") + $" · {area.Tables.Count} método(s)", 0.78f, dim: true);
            info.pickingMode = PickingMode.Ignore;
            names.With(n, info);
            if (TerrainCheck)
            {
                int off = EncounterChecks.CellsOffTerrain(S.Map, S.Tilesets, area, S.Methods).Count;
                if (off > 0)
                {
                    var warn = Ui.Text($"{off} casillas sin su terreno (marcadas en rojo en el mapa)", 0.75f).Colored("aviso");
                    warn.pickingMode = PickingMode.Ignore;
                    names.Add(warn);
                }
            }
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
            int rate = table.RateWith(method);
            var box = Ui.Column(0).Bg("fondo").Border(1, "borde", 6);

            // Settings of the method in this zone: chance per step, double battles, classic weights.
            var settings = Ui.Row(16).Wrap().Pad(10, 10);
            var rateRow = Ui.Row(6);
            rateRow.With(Ui.Text("Probabilidad por paso", 0.88f, dim: true),
                Ui.MiniNumber(rate, 0, 100, v => S.SetTableRate(area.Id, table.MethodId, v), "Probabilidad de que salga algo en cada paso (o en cada uso, con cañas)"),
                Ui.Text("%", 0.88f, dim: true));
            var doubles = Ui.Row(6);
            doubles.With(Ui.Text("Dobles", 0.88f, dim: true),
                Ui.MiniNumber(table.DoublePercent, 0, 100, v => S.SetTableDoubles(area.Id, table.MethodId, v), "% de que salgan dos a la vez (combate doble, como en la hierba oscura)"),
                Ui.Text("%", 0.88f, dim: true));
            var actions = Ui.Row(2);
            actions.With(Ui.Button("Pesos clásicos", () => S.ApplyClassicWeights(area.Id, table.MethodId), Ui.ButtonKind.Flat,
                    "Pone los pesos de los juegos originales en el orden de la lista: hierba 20/20/10/10/10/10/5/5/4/4/1/1 · agua 60/30/5/4/1 · cañas 70/30"),
                Ui.IconButton("papelera", () => S.RemoveTable(area.Id, table.MethodId), "Quitar este método de la zona"));
            settings.With(rateRow, doubles, Ui.Spacer(), actions);
            box.Add(settings);
            var explain = Ui.Text((method != null ? TriggerHelp(method) + ". " : "") + (rate > 0 ? $"De media, un encuentro cada {100.0 / rate:0.#} pasos." : "Con 0 % no sale nada."), 0.82f, dim: true, wrap: true);
            explain.Margin(10, 0, 10, 8);
            box.Add(explain);

            // Which hour the % are for: icons (all day, morning, day, evening, night).
            var hours = Ui.Row(2).Pad(10, 4);
            hours.Add(Ui.Text("Ver % de:", 0.82f, dim: true).Margin(0, 0, 6, 0));
            hours.Add(Ui.IconButton("reloj", () => { _viewTime = null; Refresh(); }, "Todas las horas (según los pesos)", _viewTime == null));
            foreach (var (t, label, _) in Times)
            {
                var tt = t;
                hours.Add(Ui.IconButton(TimeIcon(t), () => { _viewTime = tt; Refresh(); }, "Lo que sale por la " + label.ToLowerInvariant(), _viewTime == t));
            }
            hours.Add(Ui.Text(_viewTime == null ? "todas las horas" : Times.First(x => x.time == _viewTime).label.ToLowerInvariant(), 0.82f, dim: true).Margin(6, 0, 0, 0));
            box.Add(hours);

            // The species table.
            var chances = (_viewTime.HasValue ? table.Chances(_viewTime.Value, _ => true) : table.BaseChances()).ToDictionary(c => c.slot, c => c.percent);
            _slotRows.Clear();
            var head = Ui.Row(8).Pad(10, 6);
            head.style.borderTopWidth = 1; head.style.borderTopColor = Ui.C("borde");
            head.style.borderBottomWidth = 1; head.style.borderBottomColor = Ui.C("borde");
            var species = Col("Especie", 0, true);
            species.style.minWidth = SpeciesMin;
            head.With(species, Col("Nivel", LevelW), Col("Peso", WeightW), Col("Sale", OddsW), Col("Horas", HoursW), Col("", MenuW));
            box.Add(head);
            for (int i = 0; i < table.Slots.Count; i++) box.Add(SlotRow(area, table, i, chances, rate, i % 2 == 1));
            if (table.Slots.Count == 0)
                box.Add(Ui.Hint("Sin especies: aquí no sale nada. Añade la primera con «+ Especie».").Margin(12, 10, 12, 10));

            var foot = Ui.Row(8).Pad(10, 10);
            foot.With(Ui.Button("+ Especie", () => PickSpecies(id => S.AddSlot(area.Id, table.MethodId, new EncounterSlot(id, 2, 4, 10))),
                    Ui.ButtonKind.Primary),
                Ui.Spacer(),
                Ui.Text(table.Slots.Count == 0 ? "" : $"{chances.Count} de {table.Slots.Count} especies salen · {chances.Values.Sum():0} %", 0.82f, dim: true));
            box.Add(foot);
            return box;
        }

        // Column widths shared by the header and the rows (so they line up).
        private const float SpeciesMin = 104, LevelW = 88, WeightW = 42, OddsW = 74, HoursW = 92, MenuW = 24;
        private readonly List<(VisualElement row, int index)> _slotRows = new List<(VisualElement, int)>();
        private int _dragSlot = -1, _dropSlot = -1;

        private static string TimeIcon(TimeOfDay t) => t == TimeOfDay.Morning ? "manana" : t == TimeOfDay.Day ? "dia" : t == TimeOfDay.Evening ? "tarde" : "noche";

        private static VisualElement Col(string text, float width, bool grow = false)
        {
            var l = Ui.Text(text.ToUpperInvariant(), 0.72f, dim: true, bold: true);
            if (grow) l.Grow(); else { l.style.width = width; l.NoShrink(); }
            return l;
        }

        /// <summary>
        /// One species, as a table row with room to breathe: picture, name and its extras (form, item, shiny, condition),
        /// levels, weight, the real % with a bar and the steps it takes to find it, the hours (icons) and a «⋯» menu.
        /// </summary>
        private VisualElement SlotRow(EncounterArea area, EncounterTable table, int index, Dictionary<EncounterSlot, double> chances, int rate, bool alt)
        {
            var slot = table.Slots[index];
            string aid = area.Id, mid = table.MethodId;
            var row = Ui.Row(8).Pad(10, 7);
            row.style.borderTopWidth = 2; row.style.borderTopColor = new Color(0, 0, 0, 0);
            _slotRows.Add((row, index));
            if (alt) row.style.backgroundColor = Ui.WithAlpha(Ui.C("panel"), 0.5f);
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = Ui.WithAlpha(Ui.C("borde"), 0.5f);

            // Species: picture + name (click = change) + a line with its extras.
            var who = Ui.Row(6).Grow();
            who.style.minWidth = SpeciesMin;
            // Grip: drag to reorder (the order matters for «Pesos clásicos»).
            var grip = Icons.Element("asa", Mathf.Round(Ui.IconSize * 0.85f), Ui.WithAlpha(Ui.C("texto_suave"), 0.7f));
            grip.pickingMode = PickingMode.Position;
            grip.tooltip = "Arrastra para cambiar el orden";
            grip.style.marginLeft = -4;
            RegisterSlotDrag(grip, aid, mid, index);
            who.Add(grip);
            who.Add(SpeciesPicture(slot.SpeciesId, 24));
            var names = Ui.Column(1).Grow();
            var name = Ui.Button(SpeciesName(slot.SpeciesId), () => PickSpecies(id => S.UpdateSlot(aid, mid, index, x => x.SpeciesId = id)), Ui.ButtonKind.Flat, "Cambiar la especie");
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            name.style.unityTextAlign = TextAnchor.MiddleLeft;
            name.style.paddingLeft = 0;
            name.style.height = Ui.ControlHeight - 6; name.style.minHeight = Ui.ControlHeight - 6;
            name.style.alignSelf = Align.FlexStart;
            names.Add(name);
            var extras = new List<string>();
            if (slot.Form != 0) extras.Add("forma " + slot.Form);
            if (!string.IsNullOrEmpty(slot.HeldItem)) extras.Add("lleva " + slot.HeldItem);
            if (slot.ShinyOdds > 0) extras.Add($"variocolor 1/{slot.ShinyOdds}");
            if (!string.IsNullOrEmpty(slot.RequiredFlag)) extras.Add($"solo si «{slot.RequiredFlag}»");
            if (extras.Count > 0) names.Add(Ui.Text(string.Join(" · ", extras), 0.78f).Colored("acento"));
            who.Add(names);

            var levels = Ui.Row(3).NoShrink();
            levels.style.width = LevelW;
            levels.With(Ui.MiniNumber(slot.MinLevel, 1, 100, v => S.UpdateSlot(aid, mid, index, x => { x.MinLevel = v; if (x.MaxLevel < v) x.MaxLevel = v; }), "Nivel mínimo (rueda del ratón o ↑ ↓)", 38),
                Ui.Text("–", dim: true),
                Ui.MiniNumber(slot.MaxLevel, 1, 100, v => S.UpdateSlot(aid, mid, index, x => { x.MaxLevel = v; if (x.MinLevel > v) x.MinLevel = v; }), "Nivel máximo", 38));
            var weightBox = Ui.Row(0).NoShrink();
            weightBox.style.width = WeightW;
            weightBox.Add(Ui.MiniNumber(slot.Weight, 0, 1000, v => S.UpdateSlot(aid, mid, index, x => x.Weight = v), "Peso: frecuencia relativa (con 20 y 10, la primera sale el doble)", 42));

            // The real %, a bar and the average steps to find it.
            bool shows = chances.TryGetValue(slot, out var p);
            // The % with a short bar next to it, and the steps on a second, small line.
            var odds = Ui.Column(2).NoShrink();
            odds.style.width = OddsW;
            var pctRow = Ui.Row(5);
            var pct = Ui.Text(shows ? $"{p:0.#} %" : "no sale", 0.88f, bold: true).Colored(shows ? "exito" : "texto_suave");
            double steps = EncounterTable.StepsToFind(rate, shows ? p : 0);
            var bar = new VisualElement().Bg("panel_alt").Round(2);
            bar.style.height = 4;
            bar.style.width = 28;
            bar.style.flexShrink = 0;
            pctRow.With(pct, bar);
            odds.Add(pctRow);
            if (steps > 0) odds.Add(Ui.Text($"≈ {steps:0} pasos", 0.72f, dim: true));
            var fill = new VisualElement().Bg(shows ? "exito" : "borde").Round(2);
            fill.style.height = Length.Percent(100);
            fill.style.width = Length.Percent(shows ? (float)Math.Max(2, p) : 0);
            bar.Add(fill);
            odds.tooltip = shows ? $"De cada 100 encuentros con este método, unos {p:0.#} son de esta especie. Hacen falta unos {steps:0} pasos de media para verla." : "Con la hora elegida (o su condición) no sale.";

            // Hours as small icons: lit = it shows up then. All four lit = always.
            var hours = Ui.Row(2).NoShrink();
            hours.style.width = HoursW;
            var current = slot.Times == TimeOfDay.Any ? TimeOfDay.AllDay : slot.Times;
            foreach (var (t, label, _) in Times)
            {
                var tt = t;
                bool on = (current & t) != 0;
                var b = Ui.IconButton(TimeIcon(t), () =>
                {
                    var next = current ^ tt;
                    if (next == 0) { _shell.Warn("Tiene que salir al menos a una hora."); return; }
                    S.UpdateSlot(aid, mid, index, x => x.Times = next == TimeOfDay.AllDay ? TimeOfDay.Any : next);
                }, label + (on ? ": sale (clic para quitar)" : ": no sale (clic para añadir)"), false, Mathf.Round(Ui.IconSize * 0.95f));
                b.style.width = 22; b.style.height = 22; b.style.minHeight = 22;
                var icon = b.Children().FirstOrDefault();
                if (icon != null) icon.style.unityBackgroundImageTintColor = on ? Ui.C("aviso") : Ui.WithAlpha(Ui.C("texto_suave"), 0.35f);
                hours.Add(b);
            }

            Button more = null;
            more = Ui.IconButton("puntos", () =>
            {
                var r = more.worldBound;
                _shell.ShowMenu(new Vector2(r.x - 180, r.yMax + 2), new List<MenuItem>
                {
                    new MenuItem("Condición (interruptor)…", () => ConditionDialog(aid, mid, index, slot)),
                    new MenuItem("Forma…", () => _shell.Prompt("Forma", "Número de forma (0 = la normal)", slot.Form.ToString(), "Guardar",
                        v => { if (int.TryParse(v, out int f)) S.UpdateSlot(aid, mid, index, x => x.Form = Math.Max(0, f)); })),
                    new MenuItem("Objeto equipado…", () => _shell.Prompt("Objeto equipado", "Id del objeto (vacío = ninguno)", slot.HeldItem, "Guardar",
                        v => S.UpdateSlot(aid, mid, index, x => x.HeldItem = (v ?? "").Trim()))),
                    new MenuItem(slot.ShinyOdds > 0 ? $"Variocolor propio: 1/{slot.ShinyOdds}…" : $"Variocolor propio (el del juego: 1/{_shell.Project?.ShinyOdds ?? 4096})…",
                        () => _shell.Prompt("Variocolor propio de esta especie", "1 de cada… (0 = el del juego, en Proyecto → Ajustes del juego)", slot.ShinyOdds.ToString(), "Guardar",
                        v => { if (int.TryParse(v, out int o)) S.UpdateSlot(aid, mid, index, x => x.ShinyOdds = Math.Max(0, o)); })),
                    MenuItem.Separator,
                    new MenuItem("Subir", () => S.MoveSlot(aid, mid, index, -1), enabled: index > 0),
                    new MenuItem("Bajar", () => S.MoveSlot(aid, mid, index, +1), enabled: index < table.Slots.Count - 1),
                    new MenuItem("Duplicar", () => S.DuplicateSlot(aid, mid, index)),
                    new MenuItem("Quitar", () => S.RemoveSlot(aid, mid, index)),
                });
            }, "Más: condición, forma, objeto, variocolor, ordenar, duplicar, quitar");
            more.style.width = MenuW;

            row.With(who, levels, weightBox, odds, hours, more);
            return row;
        }

        private void RegisterSlotDrag(VisualElement grip, string aid, string mid, int index)
        {
            grip.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                grip.CapturePointer(e.pointerId);
                _dragSlot = _dropSlot = index;
                e.StopPropagation();
            });
            grip.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!grip.HasPointerCapture(e.pointerId)) return;
                _dropSlot = _dragSlot;
                foreach (var (row, i) in _slotRows)
                {
                    var r = row.worldBound;
                    bool over = e.position.y >= r.yMin && e.position.y < r.yMax;
                    if (over) _dropSlot = i;
                    row.style.borderTopColor = over && i != _dragSlot ? Ui.C("acento") : new Color(0, 0, 0, 0);
                }
            });
            grip.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!grip.HasPointerCapture(e.pointerId)) return;
                grip.ReleasePointer(e.pointerId);
                int from = _dragSlot, to = _dropSlot;
                _dragSlot = _dropSlot = -1;
                e.StopPropagation();
                if (to >= 0 && to != from) S.MoveSlot(aid, mid, from, to - from);
                else Refresh();
            });
        }

        /// <summary>The species picture: its battle sprite or icon if the project has one, or its initial.</summary>
        private VisualElement SpeciesPicture(string id, float size)
        {
            var box = new VisualElement().Bg("panel_alt").Round(size / 2);
            box.style.width = size; box.style.height = size;
            box.style.flexShrink = 0;
            box.style.alignItems = Align.Center;
            box.style.justifyContent = Justify.Center;
            box.style.overflow = Overflow.Hidden;
            Texture2D tex = null;
            if (!string.IsNullOrEmpty(id) && _shell.ProjectRoot != null)
                foreach (var folder in new[] { "iconos", "combate" })
                {
                    var path = System.IO.Path.Combine(_shell.ProjectRoot, CTEditor.Project.ProjectLayout.GraphicsFolder, folder, id + ".png");
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
                var n = SpeciesName(id);
                var l = Ui.Text(string.IsNullOrEmpty(n) ? "?" : n.Substring(0, 1).ToUpperInvariant(), 0.95f, bold: true);
                l.style.unityTextAlign = TextAnchor.MiddleCenter;
                l.pickingMode = PickingMode.Ignore;
                box.Add(l);
            }
            return box;
        }

        /// <summary>What a condition is, said plainly, and the switch to use.</summary>
        private void ConditionDialog(string aid, string mid, int index, EncounterSlot slot)
        {
            var d = _shell.ShowDialog("Condición de «" + SpeciesName(slot.SpeciesId) + "»", 40);
            string flag = slot.RequiredFlag ?? "";
            d.Body.With(
                Ui.Text("Un interruptor es una marca del juego que encienden los eventos: «liga_vencida» al ganar la Liga, «puente_arreglado» al terminar una misión...", wrap: true),
                Ui.Hint("Si pones uno, esta especie SOLO sale cuando ese interruptor está encendido. Así aparecen Pokémon nuevos después de la historia o de un evento (como «Activación» en Pokémon Studio). Vacío = sale siempre."),
                Ui.TextBox("Interruptor", flag, v => flag = v));
            d.Buttons.With(Ui.Button("Quitar la condición", () => { S.UpdateSlot(aid, mid, index, x => x.RequiredFlag = ""); _shell.CloseDialog(d); }, Ui.ButtonKind.Flat),
                Ui.Button("Cancelar", () => _shell.CloseDialog(d)),
                Ui.Button("Guardar", () => { S.UpdateSlot(aid, mid, index, x => x.RequiredFlag = (flag ?? "").Trim()); _shell.CloseDialog(d); }, Ui.ButtonKind.Primary));
        }

        private string SpeciesName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "(elige)";
            var found = S.Species?.All().FirstOrDefault(s => s.id == id) ?? default;
            return found.id == null ? id + " (?)" : found.name;
        }

        /// <summary>
        /// The species picker: every species of the project in Pokédex order (a virtual list: all of them, fast), with
        /// search (name, id or «#25») and filters: up to two types (exact or not), egg group, generation, alternate forms
        /// and legendaries. Double click or Intro picks.
        /// </summary>
        private void PickSpecies(Action<string> picked)
        {
            var all = S.Species?.Entries() ?? new SpeciesEntry[0];
            var types = S.Species?.Types() ?? new Dictionary<string, (string name, string color)>();
            var eggs = S.Species?.EggGroups() ?? new Dictionary<string, (string name, string color)>();
            var d = _shell.ShowDialog("Elegir especie", 54, 80);
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
                pic.Add(SpeciesPicture(sp.Id, Mathf.Round(Ui.FontSize * 2.2f)));
                var badges = e.Q("badges");
                badges.Clear();
                foreach (var t in sp.Types) badges.Add(TypeBadge(t, types));
            };
            list.style.flexGrow = 1;
            list.itemsSource = (System.Collections.IList)shown;
#if UNITY_2022_2_OR_NEWER
            list.itemsChosen += items => { foreach (var it in items) { _shell.CloseDialog(d); picked(((SpeciesEntry)it).Id); return; } };
#else
            list.onItemsChosen += items => { foreach (var it in items) { _shell.CloseDialog(d); picked(((SpeciesEntry)it).Id); return; } };
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
                    _shell.ShowMenu(new Vector2(r.x, r.yMax + 2), items.Select(it => { var v = it.value; return new MenuItem(it.text, () => chosen(v)); }).ToList());
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
                    _shell.CloseDialog(d);
                    picked(first.Id);
                }
                else if (e.keyCode == KeyCode.DownArrow) { list.selectedIndex = Mathf.Min(list.selectedIndex + 1, shown.Count - 1); list.ScrollToItem(list.selectedIndex); e.StopPropagation(); }
                else if (e.keyCode == KeyCode.UpArrow) { list.selectedIndex = Mathf.Max(list.selectedIndex - 1, 0); list.ScrollToItem(list.selectedIndex); e.StopPropagation(); }
            }, TrickleDown.TrickleDown);

            d.Body.With(top, filters, Ui.Separator(), list);
            d.Buttons.With(Ui.Hint("Doble clic o Intro: elegir"), Ui.Spacer(), Ui.Button("Cancelar", () => _shell.CloseDialog(d)),
                Ui.Button("Elegir", () =>
                {
                    if (list.selectedIndex < 0 || list.selectedIndex >= shown.Count) { _shell.Warn("Elige una especie de la lista."); return; }
                    var sp = shown[list.selectedIndex];
                    _shell.CloseDialog(d);
                    picked(sp.Id);
                }, Ui.ButtonKind.Primary));
            BuildFilters();
            Apply();
            if (all.Count == 0) d.Body.Add(Ui.Hint("No hay especies: copia los datos de un pack (Proyecto → Copiar datos de un pack)."));
            search.schedule.Execute(() => search.Q(className: TextField.inputUssClassName)?.Focus()).ExecuteLater(50);
        }

        private static Button Small(Button b)
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
