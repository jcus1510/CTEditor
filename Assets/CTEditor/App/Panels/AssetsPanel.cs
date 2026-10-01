using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using CTEditor.Art.Domain;
using CTEditor.Project;

namespace CTEditor.App
{
    /// <summary>
    /// Panel RECURSOS: las imágenes de «graficos/» agrupadas por tipo, con miniatura, tamaño y si ya están cortadas.
    /// Se actualiza solo cuando algo cambia en la carpeta (también si guardas desde Aseprite u otro programa).
    /// Desde aquí se importan imágenes y se abre el asistente de corte.
    /// </summary>
    public sealed class AssetsPanel : VisualElement
    {
        private static readonly string[] KindLabels = { "Otros", "Tilesets", "Personajes", "Combate", "Iconos", "Interfaz", "Retratos" };

        private readonly AppShell _shell;
        private readonly ScrollView _list;
        private readonly Label _summary;
        private string _filter = "";
        private AssetKind? _kind;
        private VisualElement _kindBar;
        private IReadOnlyList<AssetEntry> _all = new AssetEntry[0];
        private string _scanError;

        public AssetsPanel(AppShell shell)
        {
            _shell = shell;
            style.flexGrow = 1;

            var bar = Ui.Row(6).Pad(8, 6);
            bar.style.flexShrink = 0;
            bar.style.flexWrap = Wrap.Wrap;
            var search = Ui.TextBox(null, "", v => { _filter = v ?? ""; Render(); });
            search.tooltip = "Buscar por nombre";
            search.style.minWidth = 120;
            search.Grow();
            bar.With(search,
                Ui.Button("Importar…", ImportImage, Ui.ButtonKind.Primary, "Copiar imágenes PNG, BMP o DIB al proyecto (las BMP se convierten a PNG)"),
                Ui.Button("Carpeta…", ImportFolder, Ui.ButtonKind.Normal, "Importar todas las imágenes de una carpeta"),
                Ui.Button("Recargar", Refresh, Ui.ButtonKind.Normal, "Volver a leer la carpeta"),
                Ui.Button("Carpeta", () => Application.OpenURL("file://" + _shell.GraphicsFolder), Ui.ButtonKind.Normal, "Abrir la carpeta de gráficos"));
            Add(bar);

            _kindBar = Ui.Row(4).Pad(8, 0);
            _kindBar.style.flexWrap = Wrap.Wrap;
            _kindBar.style.flexShrink = 0;
            Add(_kindBar);

            _summary = Ui.Text("", 0.9f, dim: true);
            _summary.Margin(10, 4, 10, 4);
            Add(_summary);
            Add(Ui.Separator());

            _list = Ui.Scroll();
            _list.style.flexGrow = 1;
            Add(_list);

            // Listen only while visible: a hidden tab re-reads the folder when it comes back.
            RegisterCallback<AttachToPanelEvent>(_ => { _shell.AssetsChanged += Refresh; Refresh(); });
            RegisterCallback<DetachFromPanelEvent>(_ => _shell.AssetsChanged -= Refresh);
        }

        /// <summary>Reads the folder again and redraws.</summary>
        public void Refresh()
        {
            _scanError = null;
            try { _all = _shell.HasProject ? AssetCatalog.Scan(_shell.ProjectRoot) : new AssetEntry[0]; }
            catch (Exception e) { _all = new AssetEntry[0]; _scanError = e.Message; }
            Render();
        }

        private void BuildKindBar(IReadOnlyList<AssetEntry> all)
        {
            _kindBar.Clear();
            _kindBar.Add(Ui.Chip($"Todo ({all.Count})", _kind == null, () => { _kind = null; Render(); }));
            foreach (AssetKind k in Enum.GetValues(typeof(AssetKind)))
            {
                int n = all.Count(a => a.Kind == k);
                if (n == 0) continue;
                var kind = k;
                var chip = Ui.Chip($"{KindLabels[(int)k]} ({n})", _kind == k, () => { _kind = kind; Render(); });
                chip.style.marginLeft = 4;
                _kindBar.Add(chip);
            }
        }

        /// <summary>Redraws with the last scan (filters only).</summary>
        private void Render()
        {
            _list.Clear();
            if (!_shell.HasProject) return;
            if (_scanError != null)
            {
                _list.Add(Ui.Text("No se pudo leer la carpeta: " + _scanError, wrap: true).Colored("error"));
                return;
            }
            var all = _all;
            BuildKindBar(all);
            var shown = all.Where(a => (_kind == null || a.Kind == _kind)
                                       && (_filter.Length == 0 || a.RelativePath.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            int sliced = all.Count(a => a.IsSliced);
            _summary.text = $"{all.Count} imágenes · {sliced} cortadas · {all.Count - sliced} sin cortar";

            if (all.Count == 0)
            {
                _list.Add(Ui.EmptyState("carpeta", "Aún no hay imágenes",
                    "Importa tilesets, personajes o sprites (PNG, BMP o DIB). Los de RPG Maker XP / Essentials sirven tal cual.",
                    "Importar imágenes…", ImportImage, "o una carpeta entera…", ImportFolder));
                return;
            }
            foreach (var group in shown.GroupBy(a => a.Kind))
            {
                var header = Ui.Text($"{KindLabels[(int)group.Key]} ({group.Count()})", 0.95f, bold: true);
                header.Margin(10, 10, 10, 4);
                _list.Add(header);
                foreach (var a in group) _list.Add(Row(a));
            }
        }

        private VisualElement Row(AssetEntry a)
        {
            var full = Path.Combine(_shell.ProjectRoot, a.RelativePath);
            var row = Ui.Row(10).Pad(8, 5);
            row.RegisterCallback<PointerEnterEvent>(_ => row.style.backgroundColor = Ui.C("panel_alt"));
            row.RegisterCallback<PointerLeaveEvent>(_ => row.style.backgroundColor = new Color(0, 0, 0, 0));

            var thumbBox = new VisualElement().Bg("fondo").Border(1, "borde", 4);
            thumbBox.style.width = 52;
            thumbBox.style.height = 52;
            thumbBox.style.flexShrink = 0;
            thumbBox.style.alignItems = Align.Center;
            thumbBox.style.justifyContent = Justify.Center;
            var tex = a.Problem == null || a.NeedsPng ? Textures.Thumbnail(full) : null;
            if (tex != null)
            {
                var img = new Image { image = tex, scaleMode = ScaleMode.ScaleToFit };
                img.style.width = 48;
                img.style.height = 48;
                thumbBox.Add(img);
            }
            else thumbBox.Add(Ui.Text("?", 1.4f, dim: true));
            row.Add(thumbBox);

            var info = Ui.Column(2).Grow();
            info.Add(Ui.Text(a.Name, bold: true));
            if (a.Problem != null) info.Add(Ui.Text(a.Problem, 0.9f).Colored("error"));
            else
            {
                string state = a.IsSliced ? "cortada" : "sin cortar";
                info.Add(Ui.Text($"{a.Width} × {a.Height} px · {state}", 0.9f, dim: !a.IsSliced).Colored(a.IsSliced ? "exito" : "texto_suave"));
            }
            info.Add(Ui.Text(a.RelativePath, 0.85f, dim: true));
            row.Add(info);

            // The kind (folder) can be changed after importing.
            if (a.Kind != AssetKind.Other || a.Problem == null)
            {
                var kindButton = Ui.Button(KindLabels[(int)a.Kind] + "  ", null, Ui.ButtonKind.Flat, "Cambiar de tipo (la mueve a otra carpeta con su corte)");
                kindButton.style.color = Ui.C("texto_suave");
                kindButton.clicked += () =>
                {
                    var r = kindButton.worldBound;
                    var items = ProjectLayout.KindFolders.Keys.Select(k => new MenuItem(KindLabels[(int)k], () => ChangeKind(a, k),
                        isChecked: k == a.Kind)).ToList();
                    _shell.ShowMenu(new Vector2(r.x, r.yMax + 2), items);
                };
                row.Add(kindButton);
            }
            if (a.NeedsPng)
                row.Add(Ui.Button("Convertir a PNG", () => ConvertToPng(full), Ui.ButtonKind.Primary,
                    "Crea «" + a.Name + ".png» al lado y quita el BMP (el proyecto trabaja en PNG)"));
            if (a.Problem == null)
            {
                var cut = Ui.Button(a.IsSliced ? "Editar corte…" : "Cortar…", () => SliceWizard.Show(_shell, full, a.Kind),
                    a.IsSliced ? Ui.ButtonKind.Normal : Ui.ButtonKind.Primary, "Asistente de corte: tamaño del tile, vacíos y repetidos");
                row.Add(cut);
                row.Add(Ui.Button("Retocar", () => _shell.OpenRetouch(full), Ui.ButtonKind.Normal, "Abrir en el editor de píxeles"));
            }
            return row;
        }

        private void ImportImage()
        {
            FolderBrowser.PickFile(_shell, "Importar una imagen (PNG, BMP o DIB)", string.Join(",", ImageFile.ImportExtensions),
                file => AskKind(new[] { file }, Path.GetFileName(file)));
        }

        private void ChangeKind(AssetEntry a, AssetKind kind)
        {
            if (kind == a.Kind) return;
            void Move()
            {
                try
                {
                    var rel = AssetMover.MoveToKind(_shell.ProjectRoot, a.RelativePath, kind);
                    _shell.Success($"«{a.Name}» ahora es de {KindLabels[(int)kind]} ({rel}).");
                    _shell.NotifyAssetsChanged();
                    Refresh();
                }
                catch (Exception e) { _shell.Error("No se pudo mover: " + e.Message); }
            }
            if (a.Kind == AssetKind.Tileset && a.IsSliced)
                _shell.Confirm("Cambiar de tipo", $"«{a.Name}» es un tileset cortado: los mapas que lo usan dejarán de encontrarlo hasta que vuelva a Tilesets. ¿Moverlo a {KindLabels[(int)kind]}?",
                    "Mover", Move);
            else Move();
        }

        private void ConvertToPng(string path)
        {
            try
            {
                var png = ImageFile.ImportAsPng(path, Path.GetDirectoryName(path));
                File.Delete(path);
                _shell.Success("Convertida: " + Path.GetFileName(png));
                _shell.NotifyAssetsChanged();
                Refresh();
            }
            catch (Exception e) { _shell.Error($"No se pudo convertir {Path.GetFileName(path)}: {e.Message}"); }
        }

        /// <summary>Imports every PNG of a folder (e.g. Graphics/Tilesets of an Essentials project) with the same kind.</summary>
        private void ImportFolder()
        {
            FolderBrowser.PickFolder(_shell, "Importar todas las imágenes de una carpeta", folder =>
            {
                var files = Directory.GetFiles(folder).Where(ImageFile.CanImport).ToArray();
                if (files.Length == 0) { _shell.Warn("En esa carpeta no hay imágenes PNG, BMP ni DIB."); return; }
                AskKind(files, $"{files.Length} imágenes de «{Path.GetFileName(folder)}»");
            });
        }

        private void AskKind(IReadOnlyList<string> files, string what)
        {
            var d = _shell.ShowDialog("Importar: ¿qué son?", 56);
            // A preview of what is being imported (up to 8 images) and a guess of their kind.
            var guesses = new List<(AssetKind kind, string reason)>();
            var previews = Ui.Row(8).Wrap();
            foreach (var file in files)
            {
                ImageFile.TryReadSize(file, out int w, out int h);
                guesses.Add(AssetKindGuesser.Guess(file, w, h));
                if (previews.childCount >= 8) continue;
                var card = Ui.Column(4).Bg("fondo").Border(1, "borde", 4).Pad(6);
                card.style.width = 148;
                card.style.marginBottom = 8;
                var box = new VisualElement();
                box.style.height = 110;
                box.style.alignItems = Align.Center;
                box.style.justifyContent = Justify.Center;
                var tex = Textures.Thumbnail(file, 220);
                if (tex != null)
                {
                    var img = new Image { image = tex, scaleMode = ScaleMode.ScaleToFit };
                    img.style.width = 132; img.style.height = 106;
                    box.Add(img);
                }
                else box.Add(Ui.Text("?", 1.4f, dim: true));
                card.With(box, Ui.Text(Path.GetFileName(file), 0.85f, bold: true), Ui.Text($"{w} × {h} px", 0.8f, dim: true));
                previews.Add(card);
            }
            if (files.Count > 8) previews.Add(Ui.Hint($"y {files.Count - 8} más…"));
            var suggested = guesses.GroupBy(g => g.kind).OrderByDescending(g => g.Count()).First();
            d.Body.Add(previews);
            d.Body.Add(Ui.Hint(what + $". Sugerido: {KindLabels[(int)suggested.Key]} ({suggested.First().reason}). Las que se puedan se cortan solas (8 columnas de 32 px, hojas de personaje 4 × 4 o 3 × 4); las demás abren el asistente. Las BMP/DIB se convierten a PNG. El tipo se puede cambiar después en Recursos."));
            var kinds = Ui.Row(6);
            kinds.style.flexWrap = Wrap.Wrap;
            foreach (var kv in ProjectLayout.KindFolders)
            {
                var kind = kv.Key;
                var folder = kv.Value;
                bool isSuggested = kind == suggested.Key;
                kinds.Add(Ui.Button(KindLabels[(int)kind] + (isSuggested ? " (sugerido)" : ""), () =>
                {
                    _shell.CloseDialog(d);
                    int done = 0, auto = 0;
                    string firstManual = null;
                    foreach (var file in files)
                    {
                        try
                        {
                            // BMP/DIB become PNG on the way in: the project always works in PNG.
                            var dest = ImageFile.ImportAsPng(file, Path.Combine(_shell.GraphicsFolder, folder));
                            done++;
                            if (kind == AssetKind.Tileset || kind == AssetKind.Character)
                            {
                                if (AutoSlice(dest, kind)) auto++;
                                else firstManual ??= dest;
                            }
                        }
                        catch (Exception e) { _shell.Error($"No se pudo importar {Path.GetFileName(file)}: {e.Message}"); }
                    }
                    _shell.Success($"Importadas {done}" + (auto > 0 ? $" · {auto} cortadas solas (puedes cambiarlo con «Editar corte…»)" : ""));
                    _shell.NotifyAssetsChanged();
                    Refresh();
                    if (firstManual != null) SliceWizard.Show(_shell, firstManual, kind);
                }, isSuggested ? Ui.ButtonKind.Primary : Ui.ButtonKind.Normal).Margin(0, 0, 6, 6));
            }
            d.Body.Add(kinds);
            d.Buttons.Add(Ui.Button("Cancelar", () => _shell.CloseDialog(d)));
        }

        /// <summary>
        /// Cuts an image without asking when its size leaves no doubt: a character sheet that fits RPG Maker XP (4 × 4) or
        /// VX/MV (3 × 4), or a tileset whose suggested tile size divides it exactly. False = it needs the wizard.
        /// </summary>
        private bool AutoSlice(string path, AssetKind kind)
        {
            if (SliceFile.LoadFor(path) != null) return true; // already cut (kept)
            if (!Png.TryReadSize(path, out int w, out int h)) return false;
            if (kind == AssetKind.Character)
            {
                var layout = CharacterSheetLayout.Detect(w, h);
                if (layout == null) return false;
                new SliceFile(layout.SliceFor(w, h), SheetKind.Character) { CharacterLayout = layout.Name }.SaveFor(path);
                return true;
            }
            int tile = _shell.Project?.TileSize ?? ProjectSettings.DefaultTileSize;
            var best = TileSizeSuggester.Suggest(w, h, tile).FirstOrDefault();
            if (best == null || w % best.Width != 0 || h % best.Height != 0) return false;
            new SliceFile(best.ToSettings(), SheetKind.Tileset).SaveFor(path);
            return true;
        }
    }
}
