using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using CTEditor.Workspace;

namespace CTEditor.App
{
    /// <summary>
    /// «Personalizar el entorno»: tema (y cada color), tamaño de la interfaz y de la letra, pantalla completa,
    /// distribuciones de paneles (de fábrica y guardadas) y atajos de teclado. Todo se aplica al momento y se guarda.
    /// </summary>
    public static class SettingsDialog
    {
        public enum Tab { Theme, Interface, Layouts, Shortcuts }

        private static Tab _lastTab = Tab.Theme;

        public static void Show(AppShell shell, Tab? tab = null)
        {
            if (tab.HasValue) _lastTab = tab.Value;
            var d = shell.ShowDialog("Personalizar el entorno", 64, 80);
            var tabs = Ui.Row(6);
            tabs.style.flexShrink = 0;
            var content = Ui.Scroll().Grow();
            d.Body.With(tabs, content);
            d.Buttons.Add(Ui.Button("Cerrar", () => shell.CloseDialog(d), Ui.ButtonKind.Primary));

            void Select(Tab t)
            {
                _lastTab = t;
                tabs.Clear();
                tabs.With(
                    Ui.Chip("Tema y colores", t == Tab.Theme, () => Select(Tab.Theme)),
                    Ui.Chip("Interfaz", t == Tab.Interface, () => Select(Tab.Interface)),
                    Ui.Chip("Distribuciones", t == Tab.Layouts, () => Select(Tab.Layouts)),
                    Ui.Chip("Atajos de teclado", t == Tab.Shortcuts, () => Select(Tab.Shortcuts)));
                content.Clear();
                var body = Ui.Column(10).Pad(4, 8);
                switch (t)
                {
                    case Tab.Theme: ThemeTab(shell, body, () => Reopen(shell, d)); break;
                    case Tab.Interface: InterfaceTab(shell, body, () => Reopen(shell, d)); break;
                    case Tab.Layouts: LayoutsTab(shell, body, () => Reopen(shell, d)); break;
                    case Tab.Shortcuts: ShortcutsTab(shell, body); break;
                }
                content.Add(body);
            }
            Select(_lastTab);
        }

        /// <summary>A theme or font change rebuilds the whole window: reopen the dialog on the same tab.</summary>
        private static void Reopen(AppShell shell, AppShell.Dialog d)
        {
            shell.CloseDialog(d);
            Show(shell);
        }

        private static void ThemeTab(AppShell shell, VisualElement body, System.Action reopen)
        {
            var ws = shell.Workspace;
            var presets = Ui.Row(6);
            foreach (var preset in Theme.Presets)
            {
                var t = preset();
                presets.Add(Ui.Chip(t.Name, ws.Theme.Name == t.Name, () => { shell.SetTheme(preset()); reopen(); }));
            }
            body.With(Ui.Heading("Tema"), presets,
                Ui.Hint("Cambia cualquier color escribiendo su código (#RRGGBB). Al tocar un color, el tema pasa a llamarse «Personalizado»."),
                Ui.Separator());
            foreach (var token in Theme.Tokens)
            {
                var id = token.Id;
                var row = Ui.Row(10);
                ColorUtility.TryParseHtmlString(ws.Theme.Get(id), out var c);
                var swatch = Ui.Swatch(c, 22);
                var label = Ui.Text(token.Label);
                label.style.width = 260;
                var field = Ui.TextBox(null, ws.Theme.Get(id), null, delayed: true);
                field.style.width = 120;
                field.RegisterValueChangedCallback(e =>
                {
                    var v = (e.newValue ?? "").Trim();
                    if (!v.StartsWith("#")) v = "#" + v;
                    if (!Theme.IsColor(v))
                    {
                        shell.Warn($"«{e.newValue}» no es un color. Usa #RRGGBB o #RRGGBBAA.");
                        field.SetValueWithoutNotify(ws.Theme.Get(id));
                        return;
                    }
                    var theme = ws.Theme.Clone("Personalizado");
                    theme.Set(id, v);
                    shell.SetTheme(theme);
                    reopen();
                });
                row.With(swatch, label, field);
                body.Add(row);
            }
        }

        private static void InterfaceTab(AppShell shell, VisualElement body, System.Action reopen)
        {
            var ws = shell.Workspace;
            var scaleLabel = Ui.Text($"{Mathf.RoundToInt(ws.UiScale * 100)} %", bold: true);
            var scale = Ui.SliderBox("Tamaño", ws.UiScale, WorkspaceSettings.MinScale, WorkspaceSettings.MaxScale, v =>
            {
                float snapped = Mathf.Round(v * 20f) / 20f;
                shell.SetScale(snapped);
                scaleLabel.text = $"{Mathf.RoundToInt(snapped * 100)} %";
            });
            scale.Grow();
            var scaleRow = Ui.Row(10);
            scaleRow.With(scale, scaleLabel, Ui.Button("100 %", () => { shell.SetScale(1f); reopen(); }));
            body.With(Ui.Heading("Tamaño de la interfaz"), scaleRow,
                Ui.Hint("Se suma a la escala de Windows (ppp). También con Ctrl + rueda del ratón."),
                Ui.Separator(),
                Ui.Heading("Letra"),
                Ui.NumberBox("Tamaño", ws.FontSize, 9, 24, v => { shell.SetFontSize(v); reopen(); }),
                Ui.Separator(),
                Ui.Heading("Ventana"),
                Ui.Check("Pantalla completa (F11)", shell.IsFullscreen?.Invoke() ?? true, _ => shell.ToggleFullscreen?.Invoke()),
                Ui.Hint("La aplicación ocupa una sola ventana a la resolución de tu monitor. Las ventanas interiores se ajustan arrastrando sus separadores."));
        }

        private static void LayoutsTab(AppShell shell, VisualElement body, System.Action reopen)
        {
            var ws = shell.Workspace;
            body.Add(Ui.Heading("Distribuciones de fábrica"));
            foreach (var preset in DockLayout.Presets)
            {
                var l = preset();
                var name = l.Name;
                var row = Ui.Row(10);
                row.With(Ui.Text(name, bold: ws.Layout.Name == name).Grow(),
                    Ui.Text(string.Join(", ", l.OpenPanels().Select(PanelCatalog.LabelOf)), 0.85f, dim: true).Grow(2),
                    Ui.Button("Usar", () => { shell.UseLayout(name); reopen(); }));
                body.Add(row);
            }
            body.Add(Ui.Separator());
            body.Add(Ui.Heading("Mis distribuciones"));
            if (ws.SavedLayouts.Count == 0) body.Add(Ui.Hint("Aún no has guardado ninguna. Coloca las ventanas a tu gusto y pulsa «Guardar la actual»."));
            foreach (var saved in ws.SavedLayouts.ToList())
            {
                var name = saved.Name;
                var row = Ui.Row(10);
                row.With(Ui.Text(name, bold: ws.Layout.Name == name).Grow(),
                    Ui.Button("Usar", () => { shell.UseLayout(name); reopen(); }),
                    Ui.Button("Borrar", () =>
                    {
                        ws.SavedLayouts.RemoveAll(x => x.Name == name);
                        shell.SaveWorkspaceSoon();
                        reopen();
                    }, Ui.ButtonKind.Flat));
                body.Add(row);
            }
            body.Add(Ui.Button("Guardar la actual…", () => shell.Prompt("Guardar distribución", "Nombre", ws.Layout.Name, "Guardar", name =>
            {
                if (string.IsNullOrWhiteSpace(name)) return;
                ws.SaveLayoutAs(name.Trim());
                ws.Layout.Name = name.Trim();
                shell.SaveWorkspaceSoon();
                reopen();
            }), Ui.ButtonKind.Primary));
        }

        private static void ShortcutsTab(AppShell shell, VisualElement body)
        {
            var map = shell.Workspace.Shortcuts;
            body.With(Ui.Heading("Atajos de teclado"),
                Ui.Hint("Haz clic en un atajo y pulsa la nueva combinación (Esc cancela, Retroceso lo deja sin atajo). Si otra acción la tenía, se queda sin atajo. También: Espacio + arrastrar mueve el mapa; Ctrl + rueda, la escala de toda la interfaz; Alt + rueda, la de la ventana bajo el ratón (Ctrl + 0 / Alt + 0 las devuelven al 100 %)."));
            string filter = "";
            var search = Ui.TextBox(null, "", null);
            search.tooltip = "Buscar una acción o un atajo";
            search.style.maxWidth = 320;
            body.Add(search);
            var rows = Ui.Column(2);
            void Fill()
            {
                rows.Clear();
                var actions = ShortcutMap.Actions.Where(a => filter.Length == 0
                    || a.Label.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                    || ShortcutMap.Pretty(map.KeysFor(a.Id)).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                var order = ShortcutMap.Categories.ToList();
                foreach (var group in actions.GroupBy(a => a.Category)
                             .OrderBy(g => order.IndexOf(g.Key) < 0 ? 99 : order.IndexOf(g.Key)).ThenBy(g => g.Key))
                {
                    rows.Add(Ui.Text(group.Key, 0.95f, bold: true).Colored("acento").Margin(0, 10, 0, 2));
                    foreach (var action in group)
                    {
                        var id = action.Id;
                        var row = Ui.Row(10).Pad(6, 2).Round(4);
                        row.RegisterCallback<PointerEnterEvent>(_ => row.style.backgroundColor = Ui.C("panel_alt"));
                        row.RegisterCallback<PointerLeaveEvent>(_ => row.style.backgroundColor = new Color(0, 0, 0, 0));
                        var label = Ui.Text(action.Label).Grow();
                        var keys = map.KeysFor(id);
                        var key = Ui.Button(keys.Length > 0 ? ShortcutMap.Pretty(keys) : "—", null, Ui.ButtonKind.Normal,
                            "Clic y pulsa la nueva combinación");
                        key.style.minWidth = 140;
                        key.clicked += () =>
                        {
                            key.text = "Pulsa el atajo…";
                            key.style.borderBottomColor = Ui.C("acento");
                            shell.CaptureKeys(k =>
                            {
                                if (k != null)
                                {
                                    foreach (var c in map.Rebind(id, k))
                                        shell.Warn($"«{ShortcutMap.Actions.First(a => a.Id == c).Label}» se ha quedado sin atajo.");
                                    shell.SaveWorkspaceSoon();
                                }
                                Fill();
                            });
                        };
                        bool changed = ShortcutMap.Normalize(action.DefaultKeys) != keys;
                        var reset = Ui.IconButton("anterior", () => { map.Rebind(id, action.DefaultKeys); shell.SaveWorkspaceSoon(); Fill(); },
                            "Volver al de fábrica: " + ShortcutMap.Pretty(ShortcutMap.Normalize(action.DefaultKeys)));
                        reset.style.visibility = changed ? Visibility.Visible : Visibility.Hidden;
                        row.With(label, key, reset);
                        rows.Add(row);
                    }
                }
                if (actions.Count == 0) rows.Add(Ui.Hint("Ninguna acción coincide."));
            }
            search.RegisterValueChangedCallback(e => { filter = (e.newValue ?? "").Trim(); Fill(); });
            Fill();
            body.With(rows, Ui.Button("Volver todos a los de fábrica", () => { map.ResetToDefaults(); shell.SaveWorkspaceSoon(); Fill(); }).Margin(0, 10, 0, 0));
        }
    }
}
