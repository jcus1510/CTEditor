using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using CTEditor.Workspace;

namespace CTEditor.App
{
    /// <summary>
    /// Barra de menús de la mesa de trabajo: Proyecto, Ver, Entorno, Jugar y Ayuda. Con un menú abierto, pasar el ratón por
    /// otro título lo abre (como en cualquier programa de escritorio).
    /// </summary>
    public sealed class MenuBar : VisualElement
    {
        private readonly AppShell _shell;
        private readonly List<(Button button, Func<IList<MenuItem>> items)> _menus = new List<(Button, Func<IList<MenuItem>>)>();
        private Button _open;

        public MenuBar(AppShell shell)
        {
            _shell = shell;
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.flexShrink = 0;
            style.height = Ui.FontSize + 16;
            style.backgroundColor = Ui.C("panel");
            style.borderBottomWidth = 1;
            style.borderBottomColor = Ui.C("borde");
            style.paddingLeft = 6;

            var logo = Ui.Text("CTEditor", 1.05f, bold: true).Colored("acento");
            logo.style.marginRight = 12;
            logo.style.marginLeft = 4;
            Add(logo);

            AddMenu("Proyecto", ProjectItems);
            AddMenu("Ver", ViewItems);
            AddMenu("Entorno", EnvironmentItems);
            AddMenu("Jugar", PlayItems);
            AddMenu("Ayuda", HelpItems);
            Add(Ui.Spacer());
            // Save in sight (Ctrl+S): everything also saves itself, this makes it sure and says so.
            var save = Ui.IconButton("guardar", () => _shell.RunAction("guardar"), "Guardar todo (" + Keys("guardar") + "). Copia de seguridad: " + Keys("copia"));
            save.style.marginRight = 4;
            Add(save);
            var play = Ui.Button("Jugar", () => _shell.RunAction("jugar"), Ui.ButtonKind.Primary, "Jugar desde el principio (" + Keys("jugar") + ")");
            play.style.height = Ui.FontSize + 10;
            play.style.marginRight = 8;
            Add(play);
        }

        private string Keys(string action) => ShortcutMap.Pretty(_shell.Workspace.Shortcuts.KeysFor(action));

        private void AddMenu(string title, Func<IList<MenuItem>> items)
        {
            Button b = null;
            b = Ui.Button(title, () => Toggle(b, items), Ui.ButtonKind.Flat);
            b.style.height = Ui.FontSize + 10;
            b.RegisterCallback<PointerEnterEvent>(_ => { if (_open != null && _open != b) Open(b, items); });
            _menus.Add((b, items));
            Add(b);
        }

        private void Toggle(Button b, Func<IList<MenuItem>> items)
        {
            if (_open == b) { _shell.CloseMenu(); _open = null; return; }
            Open(b, items);
        }

        private void Open(Button b, Func<IList<MenuItem>> items)
        {
            _open = b;
            var r = b.worldBound;
            // The menu catcher covers the bar: hovering another title while a menu is open switches to it.
            _shell.ShowMenu(new Vector2(r.x, r.yMax + 2), items(), () => _open = null, pos =>
            {
                foreach (var (button, menuItems) in _menus)
                    if (button != _open && button.worldBound.Contains(pos)) { Open(button, menuItems); return; }
            });
        }

        private IList<MenuItem> ProjectItems()
        {
            var list = new List<MenuItem>
            {
                new MenuItem("Guardar", () => _shell.RunAction("guardar"), Keys("guardar")),
                new MenuItem("Hacer una copia de seguridad", () => _shell.RunAction("copia"), Keys("copia")),
                new MenuItem("Historial de versiones…", () => _shell.RunAction("historial"), Keys("historial")),
                new MenuItem("Abrir la carpeta del proyecto", () => Application.OpenURL("file://" + _shell.ProjectRoot)),
                new MenuItem("Copiar datos de un pack…", _shell.PackDialog),
                new MenuItem("Mapa de la región…", () => RegionMapDialog.Show(_shell)),
                MenuItem.Separator,
            };
            foreach (var recent in _shell.Workspace.RecentProjects.Where(p => p != _shell.ProjectRoot).Take(6))
            {
                var path = recent;
                list.Add(new MenuItem("Abrir reciente: " + Path.GetFileName(path.TrimEnd('/', '\\')), () => _shell.OpenProject(path),
                    enabled: Directory.Exists(path)));
            }
            if (list.Count > 3) list.Add(MenuItem.Separator);
            list.Add(new MenuItem("Cerrar el proyecto (volver al inicio)", _shell.CloseProject));
            list.Add(new MenuItem("Salir de CTEditor", () => _shell.Quit?.Invoke()));
            return list;
        }

        private IList<MenuItem> ViewItems()
        {
            var ws = _shell.Workspace;
            // Ver → Ventanas → (each window, ticked if open) · Ver → Distribuciones → (presets, mine, save).
            var windows = new List<MenuItem>();
            foreach (var p in PanelCatalog.All)
            {
                var id = p.Id;
                bool open = ws.Layout.IsOpen(id);
                windows.Add(new MenuItem(p.Label, () =>
                {
                    if (ws.Layout.IsOpen(id)) ws.Layout.Close(id);
                    else ws.Layout.Open(id);
                    _shell.SetLayout(ws.Layout);
                }, isChecked: open));
            }
            var layouts = new List<MenuItem>();
            foreach (var preset in DockLayout.Presets)
            {
                var name = preset().Name;
                layouts.Add(new MenuItem(name, () => _shell.UseLayout(name), isChecked: ws.Layout.Name == name));
            }
            if (ws.SavedLayouts.Count > 0) layouts.Add(MenuItem.Separator);
            foreach (var saved in ws.SavedLayouts)
            {
                var name = saved.Name;
                layouts.Add(new MenuItem("Mía: " + name, () => _shell.UseLayout(name), isChecked: ws.Layout.Name == name));
            }
            layouts.Add(MenuItem.Separator);
            var list = new List<MenuItem> { MenuItem.Submenu("Ventanas", windows), MenuItem.Submenu("Distribuciones", layouts) };
            layouts.Add(new MenuItem("Guardar esta distribución…", () => _shell.Prompt("Guardar distribución", "Nombre", ws.Layout.Name,
                "Guardar", name =>
                {
                    if (string.IsNullOrWhiteSpace(name)) return;
                    ws.SaveLayoutAs(name.Trim());
                    ws.Layout.Name = name.Trim();
                    _shell.SaveWorkspaceSoon();
                    _shell.Success($"Distribución «{name.Trim()}» guardada.");
                    _shell.Rebuild();
                })));
            list.Add(MenuItem.Separator);
            list.Add(new MenuItem("Interfaz al 100 %", () => _shell.SetScale(1f), "Ctrl + 0"));
            return list;
        }

        private IList<MenuItem> EnvironmentItems()
        {
            var ws = _shell.Workspace;
            var list = new List<MenuItem>();
            foreach (var preset in Theme.Presets)
            {
                var t = preset();
                list.Add(new MenuItem("Tema: " + t.Name, () => _shell.SetTheme(preset()), isChecked: ws.Theme.Name == t.Name));
            }
            list.Add(MenuItem.Separator);
            list.Add(new MenuItem("Interfaz más grande", () => _shell.SetScale(ws.UiScale + 0.1f), "Ctrl + rueda"));
            list.Add(new MenuItem("Interfaz más pequeña", () => _shell.SetScale(ws.UiScale - 0.1f)));
            list.Add(new MenuItem("Interfaz al 100 %", () => _shell.SetScale(1f)));
            list.Add(new MenuItem("Pantalla completa", () => _shell.RunAction("pantalla_completa"), Keys("pantalla_completa"),
                isChecked: _shell.IsFullscreen?.Invoke() ?? false));
            list.Add(MenuItem.Separator);
            list.Add(new MenuItem("Personalizar el entorno…", () => SettingsDialog.Show(_shell)));
            return list;
        }

        private IList<MenuItem> PlayItems() => new List<MenuItem>
        {
            new MenuItem("Jugar desde el principio", () => _shell.RunAction("jugar"), Keys("jugar")),
            new MenuItem("Probar desde aquí", () => _shell.RunAction("probar_aqui"), Keys("probar_aqui")),
            new MenuItem("Depurador", () => _shell.RunAction("depurador"), Keys("depurador")),
            MenuItem.Separator,
            new MenuItem("Exportar el juego (Windows)…", () => _shell.Info("La exportación llegará cuando el juego se pueda jugar (fase 3 en adelante)."), enabled: true),
        };

        private IList<MenuItem> HelpItems() => new List<MenuItem>
        {
            new MenuItem("Atajos de teclado…", () => SettingsDialog.Show(_shell, SettingsDialog.Tab.Shortcuts), Keys("atajos")),
            new MenuItem("Ver los avisos", () => { _shell.Workspace.Layout.Open(PanelCatalog.Messages); _shell.SetLayout(_shell.Workspace.Layout); }),
            new MenuItem("Acerca de CTEditor", () =>
            {
                var d = _shell.ShowDialog("Acerca de CTEditor");
                d.Body.With(Ui.Title("CTEditor"),
                    Ui.Text("Editor de juegos de monstruos por turnos, sin programar.", wrap: true),
                    Ui.Hint("Mapas, NPC, eventos por nodos, editor de píxeles y combate por bloques. Base: RPG Maker XP / Pokémon Essentials."));
                d.Buttons.Add(Ui.Button("Cerrar", () => _shell.CloseDialog(d), Ui.ButtonKind.Primary));
            }),
        };
    }
}
