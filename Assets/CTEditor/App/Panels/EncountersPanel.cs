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
            int rate = table.RateWith(method);
            var box = Ui.Column(0).Bg("fondo").Border(1, "borde", 6);

            // Settings of the method in this zone: chance per step, double battles, classic weights.
            var settings = Ui.Row(16).Wrap().Pad(12, 10);
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
            explain.Margin(12, 0, 12, 8);
            box.Add(explain);

            // Which hour the % are for.
            var hours = Ui.Row(0).Wrap().Pad(12, 4);
            hours.Add(Ui.Text("Ver % de:", 0.82f, dim: true).Margin(0, 0, 8, 0));
            void HourChip(string text, string icon, bool on, Action a, string tip)
            {
                var c = Ui.Chip(text, on, a, tip);
                c.style.fontSize = Mathf.Round(Ui.FontSize * 0.82f);
                c.style.height = Ui.ControlHeight - 8; c.style.minHeight = Ui.ControlHeight - 8;
                c.style.paddingLeft = 8; c.style.paddingRight = 8;
                hours.Add(c.Margin(0, 2, 4, 2));
            }
            HourChip("Todas las horas", null, _viewTime == null, () => { _viewTime = null; Refresh(); }, "Según los pesos, sin mirar la hora");
            foreach (var (t, label, _) in Times)
            {
                var tt = t;
                HourChip(label, null, _viewTime == t, () => { _viewTime = tt; Refresh(); }, "Lo que sale por la " + label.ToLowerInvariant());
            }
            box.Add(hours);

            // The species table.
            var chances = (_viewTime.HasValue ? table.Chances(_viewTime.Value, _ => true) : table.BaseChances()).ToDictionary(c => c.slot, c => c.percent);
            var head = Ui.Row(10).Pad(12, 6);
            head.style.borderTopWidth = 1; head.style.borderTopColor = Ui.C("borde");
            head.style.borderBottomWidth = 1; head.style.borderBottomColor = Ui.C("borde");
            head.With(Col("Especie", 0, true), Col("Nivel", 104), Col("Peso", 52), Col("Sale", 120), Col("Horas", 104), Col("", Ui.ControlHeight));
            box.Add(head);
            for (int i = 0; i < table.Slots.Count; i++) box.Add(SlotRow(area, table, i, chances, rate, i % 2 == 1));
            if (table.Slots.Count == 0)
                box.Add(Ui.Hint("Sin especies: aquí no sale nada. Añade la primera con «+ Especie».").Margin(12, 10, 12, 10));

            var foot = Ui.Row(8).Pad(12, 10);
            foot.With(Ui.Button("+ Especie", () => PickSpecies(id => S.AddSlot(area.Id, table.MethodId, new EncounterSlot(id, 2, 4, 10))),
                    Ui.ButtonKind.Primary),
                Ui.Spacer(),
                Ui.Text(table.Slots.Count == 0 ? "" : $"{chances.Count} de {table.Slots.Count} especies salen · {chances.Values.Sum():0} %", 0.82f, dim: true));
            box.Add(foot);
            return box;
        }

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
            var row = Ui.Row(10).Pad(12, 8);
            if (alt) row.style.backgroundColor = Ui.WithAlpha(Ui.C("panel"), 0.5f);
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = Ui.WithAlpha(Ui.C("borde"), 0.5f);

            // Species: picture + name (click = change) + a line with its extras.
            var who = Ui.Row(8).Grow();
            who.style.minWidth = 150;
            who.Add(SpeciesPicture(slot.SpeciesId, 30));
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

            var levels = Ui.Row(4).NoShrink();
            levels.style.width = 104;
            levels.With(Ui.MiniNumber(slot.MinLevel, 1, 100, v => S.UpdateSlot(aid, mid, index, x => { x.MinLevel = v; if (x.MaxLevel < v) x.MaxLevel = v; }), "Nivel mínimo (rueda del ratón o ↑ ↓)"),
                Ui.Text("–", dim: true),
                Ui.MiniNumber(slot.MaxLevel, 1, 100, v => S.UpdateSlot(aid, mid, index, x => { x.MaxLevel = v; if (x.MinLevel > v) x.MinLevel = v; }), "Nivel máximo"));
            var weightBox = Ui.Row(0).NoShrink();
            weightBox.style.width = 52;
            weightBox.Add(Ui.MiniNumber(slot.Weight, 0, 1000, v => S.UpdateSlot(aid, mid, index, x => x.Weight = v), "Peso: frecuencia relativa (con 20 y 10, la primera sale el doble)"));

            // The real %, a bar and the average steps to find it.
            bool shows = chances.TryGetValue(slot, out var p);
            var odds = Ui.Column(3).NoShrink();
            odds.style.width = 120;
            var pctRow = Ui.Row(6);
            var pct = Ui.Text(shows ? $"{p:0.#} %" : "no sale", 0.9f, bold: true).Colored(shows ? "exito" : "texto_suave");
            pctRow.Add(pct);
            double steps = EncounterTable.StepsToFind(rate, shows ? p : 0);
            if (steps > 0) pctRow.Add(Ui.Text($"≈ {steps:0} pasos", 0.75f, dim: true));
            var bar = new VisualElement().Bg("panel_alt").Round(2);
            bar.style.height = 4;
            var fill = new VisualElement().Bg(shows ? "exito" : "borde").Round(2);
            fill.style.height = Length.Percent(100);
            fill.style.width = Length.Percent(shows ? (float)Math.Max(2, p) : 0);
            bar.Add(fill);
            odds.With(pctRow, bar);
            odds.tooltip = shows ? $"De cada 100 encuentros con este método, unos {p:0.#} son de esta especie. Hacen falta unos {steps:0} pasos de media para verla." : "Con la hora elegida (o su condición) no sale.";

            // Hours as small icons: lit = it shows up then. All four lit = always.
            var hours = Ui.Row(2).NoShrink();
            hours.style.width = 104;
            var current = slot.Times == TimeOfDay.Any ? TimeOfDay.AllDay : slot.Times;
            foreach (var (t, label, _) in Times)
            {
                var tt = t;
                bool on = (current & t) != 0;
                var b = Ui.IconButton(t == TimeOfDay.Morning ? "manana" : t == TimeOfDay.Day ? "dia" : t == TimeOfDay.Evening ? "tarde" : "noche", () =>
                {
                    var next = current ^ tt;
                    if (next == 0) { _shell.Warn("Tiene que salir al menos a una hora."); return; }
                    S.UpdateSlot(aid, mid, index, x => x.Times = next == TimeOfDay.AllDay ? TimeOfDay.Any : next);
                }, label + (on ? ": sale (clic para quitar)" : ": no sale (clic para añadir)"), false, Mathf.Round(Ui.IconSize * 0.95f));
                b.style.width = 24; b.style.height = 24; b.style.minHeight = 24;
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
                    new MenuItem("Variocolor…", () => _shell.Prompt("Variocolor", "1 de cada… (0 = lo normal del juego)", slot.ShinyOdds.ToString(), "Guardar",
                        v => { if (int.TryParse(v, out int o)) S.UpdateSlot(aid, mid, index, x => x.ShinyOdds = Math.Max(0, o)); })),
                    MenuItem.Separator,
                    new MenuItem("Subir", () => S.MoveSlot(aid, mid, index, -1), enabled: index > 0),
                    new MenuItem("Bajar", () => S.MoveSlot(aid, mid, index, +1), enabled: index < table.Slots.Count - 1),
                    new MenuItem("Duplicar", () => S.DuplicateSlot(aid, mid, index)),
                    new MenuItem("Quitar", () => S.RemoveSlot(aid, mid, index)),
                });
            }, "Más: condición, forma, objeto, variocolor, ordenar, duplicar, quitar");

            row.With(who, levels, weightBox, odds, hours, more);
            return row;
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
