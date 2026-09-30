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
                Ui.Hint("La aplicación ocupa una sola ventana a la resolución de tu monitor. Los paneles se ajustan arrastrando sus separadores."));
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
            if (ws.SavedLayouts.Count == 0) body.Add(Ui.Hint("Aún no has guardado ninguna. Coloca los paneles a tu gusto y pulsa «Guardar la actual»."));
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
                Ui.Hint("Escribe el atajo como «Ctrl+Mayús+Z», «F5» o «B» y pulsa Intro. Si otra acción lo tenía, se queda sin atajo. Vacío = sin atajo."));
            var rows = Ui.Column(4);
            void Fill()
            {
                rows.Clear();
                foreach (var action in ShortcutMap.Actions)
                {
                    var id = action.Id;
                    var row = Ui.Row(10);
                    var label = Ui.Text(action.Label);
                    label.style.width = 300;
                    var field = Ui.TextBox(null, map.KeysFor(id), null, delayed: true);
                    field.style.width = 160;
                    field.RegisterValueChangedCallback(e =>
                    {
                        var clashes = map.Rebind(id, e.newValue);
                        foreach (var c in clashes)
                            shell.Warn($"«{ShortcutMap.Actions.First(a => a.Id == c).Label}» se ha quedado sin atajo.");
                        shell.SaveWorkspaceSoon();
                        Fill();
                    });
                    row.With(label, field, Ui.Text(action.DefaultKeys == map.KeysFor(id) ? "" : "(de fábrica: " + action.DefaultKeys + ")", 0.85f, dim: true));
                    rows.Add(row);
                }
            }
            Fill();
            body.With(rows, Ui.Button("Volver a los de fábrica", () => { map.ResetToDefaults(); shell.SaveWorkspaceSoon(); Fill(); }));
        }
    }
}
