using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using CTEditor.Workspace;

namespace CTEditor.App
{
    /// <summary>
    /// PALETA DE ÓRDENES (Ctrl+P): escribe lo que quieres hacer y Intro. Encuentra todas las acciones (con su atajo, así
    /// se aprenden), las ventanas, los mapas del proyecto y las distribuciones. ↑ ↓ para moverse, Intro para hacerlo,
    /// Esc para cerrar. La búsqueda está en el dominio (<see cref="CommandSearch"/>).
    /// </summary>
    public static class CommandPalette
    {
        public static void Show(AppShell shell)
        {
            var entries = Collect(shell);
            var d = shell.ShowDialog("¿Qué quieres hacer?", 44, 62);
            var search = Ui.TextBox(null, "", null);
            var list = Ui.Scroll().Grow();
            var count = Ui.Hint("");
            d.Body.With(search, list);
            d.Buttons.With(count, Ui.Spacer(), Ui.Hint("↑ ↓ elegir · Intro hacer · Esc cerrar"));

            IReadOnlyList<CommandEntry> shown = entries;
            int selected = 0;
            var rows = new List<VisualElement>();

            void Run(CommandEntry e)
            {
                shell.CloseDialog(d);
                e.Run?.Invoke();
            }

            void Highlight()
            {
                for (int i = 0; i < rows.Count; i++)
                    rows[i].style.backgroundColor = i == selected ? Ui.C("seleccion") : new Color(0, 0, 0, 0);
                if (selected >= 0 && selected < rows.Count) list.ScrollTo(rows[selected]);
            }

            void Fill(string query)
            {
                list.Clear();
                rows.Clear();
                shown = CommandSearch.Find(entries, query, 60);
                selected = 0;
                foreach (var e in shown)
                {
                    var entry = e;
                    var row = Ui.Row(10).Pad(10, 5).Round(4);
                    var label = Ui.Text(e.Label).Grow();
                    label.pickingMode = PickingMode.Ignore;
                    var cat = Ui.Text(e.Category, 0.8f, dim: true).NoShrink();
                    cat.pickingMode = PickingMode.Ignore;
                    row.With(label, cat);
                    if (e.Keys.Length > 0)
                    {
                        var keys = Ui.Text(e.Keys, 0.8f, bold: true).NoShrink();
                        keys.Bg("panel_alt").Border(1, "borde", 3).Pad(5, 1);
                        keys.pickingMode = PickingMode.Ignore;
                        row.Add(keys);
                    }
                    int index = rows.Count;
                    row.RegisterCallback<PointerEnterEvent>(_ => { selected = index; Highlight(); });
                    row.RegisterCallback<PointerUpEvent>(_ => Run(entry));
                    rows.Add(row);
                    list.Add(row);
                }
                if (shown.Count == 0) list.Add(Ui.Hint("Nada coincide. Prueba con otra palabra («capa», «rejilla», «mundo»...)."));
                count.text = $"{shown.Count} de {entries.Count}";
                Highlight();
            }

            search.RegisterValueChangedCallback(e => Fill(e.newValue));
            search.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.DownArrow) { selected = Mathf.Min(selected + 1, rows.Count - 1); Highlight(); e.StopPropagation(); }
                else if (e.keyCode == KeyCode.UpArrow) { selected = Mathf.Max(selected - 1, 0); Highlight(); e.StopPropagation(); }
                else if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && selected >= 0 && selected < shown.Count)
                {
                    e.StopPropagation();
                    Run(shown[selected]);
                }
            }, TrickleDown.TrickleDown);
            Fill("");
            search.schedule.Execute(() => search.Q(className: TextField.inputUssClassName)?.Focus()).ExecuteLater(30);
        }

        private static List<CommandEntry> Collect(AppShell shell)
        {
            var list = new List<CommandEntry>();
            var map = shell.Workspace.Shortcuts;
            foreach (var a in ShortcutMap.Actions)
            {
                if (a.Id == "buscar") continue;
                var id = a.Id;
                list.Add(new CommandEntry(a.Label, a.Category, () => shell.RunAction(id), ShortcutMap.Pretty(map.KeysFor(id)), id.Replace('_', ' ')));
            }
            if (shell.HasProject)
            {
                foreach (var p in PanelCatalog.All)
                {
                    var id = p.Id;
                    list.Add(new CommandEntry("Ventana: " + p.Label, "Ventanas", () =>
                    {
                        shell.Workspace.Layout.Open(id);
                        shell.SetLayout(shell.Workspace.Layout);
                    }, extra: "abrir mostrar " + p.Label));
                }
                if (shell.Maps != null)
                    foreach (var e in shell.Maps.Tree.Entries)
                    {
                        var id = e.Id;
                        list.Add(new CommandEntry("Abrir el mapa «" + e.Name + "»", "Mapas", () => { shell.Maps.OpenMap(id); shell.ActiveEditor = "mapa"; },
                            extra: e.Name + " " + id));
                    }
            }
            foreach (var preset in DockLayout.Presets)
            {
                var name = preset().Name;
                list.Add(new CommandEntry("Distribución: " + name, "Entorno", () => shell.UseLayout(name)));
            }
            foreach (var preset in Theme.Presets)
            {
                var t = preset;
                list.Add(new CommandEntry("Tema: " + t().Name, "Entorno", () => shell.SetTheme(t())));
            }
            list.Add(new CommandEntry("Interfaz al 100 %", "Entorno", () => shell.SetScale(1f), "Ctrl+0", "escala zoom tamaño"));
            return list;
        }
    }
}
