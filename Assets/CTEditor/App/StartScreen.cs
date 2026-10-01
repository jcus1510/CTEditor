using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using CTEditor.Project;

namespace CTEditor.App
{
    /// <summary>
    /// Pantalla de inicio: crear un proyecto (nombre, carpeta, tamaño de tile — 32 px de RPG Maker XP / Essentials por
    /// defecto — y pantalla) o abrir uno (recientes o buscar la carpeta).
    /// </summary>
    public sealed class StartScreen : VisualElement
    {
        private readonly AppShell _shell;

        private string _name = "Mi juego";
        private string _parent;
        private int _tileSize = ProjectSettings.DefaultTileSize;
        private int _screenW = ProjectSettings.DefaultScreenWidth, _screenH = ProjectSettings.DefaultScreenHeight;
        private VisualElement _sizeRow, _packRow;
        private string _pack;
        private Label _destination;

        public StartScreen(AppShell shell)
        {
            _shell = shell;
            _parent = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            style.flexGrow = 1;
            // A fixed design centred in the window (like a launcher): it does not stretch with the window; if the window
            // is smaller, it scrolls.
            var scroll = Ui.Scroll(ScrollViewMode.VerticalAndHorizontal).Grow();
            scroll.contentContainer.style.flexGrow = 1;
            scroll.contentContainer.style.alignItems = Align.Center;
            scroll.contentContainer.style.justifyContent = Justify.Center;
            scroll.contentContainer.style.minHeight = Length.Percent(100);
            Add(scroll);

            var page = Ui.Column(18).Pad(24);
            page.style.width = 1040;
            page.style.flexShrink = 0;

            var head = Ui.Column(4);
            head.With(Ui.Title("CTEditor"), Ui.Hint("Crea tu juego de monstruos por turnos sin programar."));
            page.Add(head);

            var cards = Ui.Row(18);
            cards.style.alignItems = Align.Stretch;
            var newCard = NewProjectCard();
            newCard.style.width = 600;
            newCard.style.flexShrink = 0;
            cards.With(newCard, OpenCard().Grow(1f));
            page.With(cards);

            var foot = Ui.Row(12);
            foot.With(Ui.Hint("Entorno: " + _shell.Workspace.Theme.Name + " · distribución " + _shell.Workspace.Layout.Name),
                Ui.Spacer(),
                Ui.Button("Personalizar el entorno…", () => SettingsDialog.Show(_shell), Ui.ButtonKind.Flat),
                Ui.Button("Salir", () => _shell.Quit?.Invoke(), Ui.ButtonKind.Flat));
            page.Add(foot);
            scroll.Add(page);
        }

        private VisualElement NewProjectCard()
        {
            var card = Ui.Card();
            var body = Ui.Column(12);
            body.Add(Ui.Heading("Nuevo proyecto"));

            body.Add(Ui.TextBox("Nombre", _name, v => { _name = v; UpdateDestination(); }));

            var folderRow = Ui.Row(6);
            var folder = Ui.Text(_parent, dim: true).Grow();
            folder.style.overflow = Overflow.Hidden;
            folderRow.With(Ui.Text("Dentro de", dim: true), folder,
                Ui.Button("Cambiar…", () => FolderBrowser.PickFolder(_shell, "¿Dónde guardar el proyecto?", p =>
                {
                    _parent = p;
                    folder.text = p;
                    UpdateDestination();
                }, _parent)));
            body.Add(folderRow);
            _destination = Ui.Hint("");
            body.Add(_destination);

            body.Add(Ui.Text("Tamaño del tile", bold: true));
            _sizeRow = Ui.Row(6);
            _sizeRow.style.flexWrap = Wrap.Wrap;
            body.Add(_sizeRow);
            body.Add(Ui.Hint("32 px es el de RPG Maker XP y Pokémon Essentials: sus tilesets y personajes sirven tal cual. Se elige ahora y queda fijo para el proyecto."));
            BuildSizes();

            var screen = Ui.Row(12);
            screen.With(Ui.Text("Pantalla del juego", bold: true),
                Ui.NumberBox("", _screenW, 160, 3840, v => _screenW = v, "Ancho en píxeles"),
                Ui.Text("×", dim: true),
                Ui.NumberBox("", _screenH, 144, 2160, v => _screenH = v, "Alto en píxeles"));
            body.Add(screen);
            body.Add(Ui.Hint("512 × 384 es la de Essentials. El juego se amplía a pantalla completa en múltiplos exactos (píxeles nítidos)."));

            body.Add(Ui.Text("Datos iniciales", bold: true));
            _packRow = Ui.Row(6);
            _packRow.style.flexWrap = Wrap.Wrap;
            body.Add(_packRow);
            body.Add(Ui.Hint("Especies, movimientos, objetos... de una generación (se pueden cambiar o copiar después)."));
            var packs = PackInstaller.Find(AppShell.PacksRoot);
            _pack = packs.LastOrDefault();
            BuildPacks(packs);

            var create = Ui.Button("Crear proyecto", Create, Ui.ButtonKind.Primary);
            create.style.alignSelf = Align.FlexStart;
            create.style.height = Ui.FontSize + 20;
            body.Add(create);
            card.Add(body);
            UpdateDestination();
            return card;
        }

        private void BuildSizes()
        {
            _sizeRow.Clear();
            foreach (var preset in ProjectSettings.TileSizes)
            {
                int size = preset.Size;
                var chip = Ui.Chip(preset.Label, _tileSize == size, () => { _tileSize = size; BuildSizes(); });
                chip.Margin(0, 0, 6, 6);
                _sizeRow.Add(chip);
            }
            bool custom = ProjectSettings.TileSizes.All(p => p.Size != _tileSize);
            _sizeRow.Add(Ui.NumberBox(custom ? "Otro (elegido)" : "Otro", _tileSize, 8, 256, v => { _tileSize = v; BuildSizes(); }));
        }

        private void BuildPacks(IReadOnlyList<string> packs)
        {
            _packRow.Clear();
            _packRow.Add(Ui.Chip("Ninguno", _pack == null, () => { _pack = null; BuildPacks(packs); }).Margin(0, 0, 6, 6));
            foreach (var p in packs)
            {
                var folder = p;
                _packRow.Add(Ui.Chip(Path.GetFileName(p), _pack == p, () => { _pack = folder; BuildPacks(packs); }).Margin(0, 0, 6, 6));
            }
        }

        private string Destination() => Path.Combine(_parent ?? "", Sanitize(_name));

        private void UpdateDestination()
        {
            if (_destination == null) return;
            var dest = Destination();
            bool clash = Directory.Exists(dest) && Directory.EnumerateFileSystemEntries(dest).Any();
            _destination.text = clash ? $"Ya existe una carpeta con ese nombre: {dest}" : $"Se creará en: {dest}";
            _destination.style.color = Ui.C(clash ? "aviso" : "texto_suave");
        }

        private void Create()
        {
            if (string.IsNullOrWhiteSpace(_name)) { _shell.Warn("Ponle un nombre al proyecto."); return; }
            _shell.CreateProject(Destination(), _name.Trim(), _tileSize, _screenW, _screenH, _pack);
        }

        private static string Sanitize(string name)
        {
            var bad = Path.GetInvalidFileNameChars();
            var clean = new string((name ?? "").Trim().Select(c => bad.Contains(c) ? '_' : c).ToArray());
            return clean.Length == 0 ? "Proyecto" : clean;
        }

        private VisualElement OpenCard()
        {
            var card = Ui.Card();
            var body = Ui.Column(10).Grow();
            body.Add(Ui.Heading("Abrir"));
            body.Add(Ui.Button("Buscar un proyecto…", () => FolderBrowser.PickFolder(_shell, "Abrir un proyecto (la carpeta con proyecto.json)",
                p => _shell.OpenProject(p)), Ui.ButtonKind.Primary));
            body.Add(Ui.Text("Recientes", bold: true));

            var recent = _shell.Workspace.RecentProjects;
            if (recent.Count == 0) body.Add(Ui.Hint("Aún no has abierto ningún proyecto."));
            var list = Ui.Scroll().Grow();
            list.style.maxHeight = 360;
            foreach (var path in recent.ToList())
            {
                bool exists = ProjectFile.IsProject(path);
                var row = Ui.Row(8).Pad(8, 6).Round(4);
                var texts = Ui.Column(1).Grow();
                texts.Add(Ui.Text(Path.GetFileName(path.TrimEnd('/', '\\')), bold: true));
                texts.Add(Ui.Text(exists ? path : path + " (no se encuentra)", 0.85f, dim: true));
                texts.pickingMode = PickingMode.Ignore;
                foreach (var c in texts.Children()) c.pickingMode = PickingMode.Ignore;
                row.Add(texts);
                var target = path;
                var remove = Ui.IconButton("cerrar", () =>
                {
                    _shell.Workspace.RecentProjects.Remove(target);
                    _shell.SaveWorkspaceSoon();
                    _shell.Rebuild();
                }, "Quitar de la lista");
                row.Add(remove);
                if (exists)
                {
                    row.RegisterCallback<PointerEnterEvent>(_ => row.style.backgroundColor = Ui.C("panel_alt"));
                    row.RegisterCallback<PointerLeaveEvent>(_ => row.style.backgroundColor = new Color(0, 0, 0, 0));
                    row.RegisterCallback<ClickEvent>(e => { if (e.target != remove) _shell.OpenProject(target); });
                }
                else row.style.opacity = 0.5f;
                list.Add(row);
            }
            body.Add(list);
            card.Add(body);
            return card;
        }
    }
}
