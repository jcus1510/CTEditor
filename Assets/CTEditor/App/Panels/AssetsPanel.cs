using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
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
                Ui.Button("Importar…", ImportImage, Ui.ButtonKind.Primary, "Copiar un PNG al proyecto"),
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
                var empty = Ui.Column(8).Pad(16);
                empty.With(Ui.Heading("Aún no hay imágenes"),
                    Ui.Hint("Pulsa «Importar…» o copia tus PNG en la carpeta del proyecto:"),
                    Ui.Hint("graficos/tilesets · graficos/personajes · graficos/combate · graficos/iconos · graficos/interfaz · graficos/retratos"),
                    Ui.Hint("Los tilesets y personajes de RPG Maker XP / Essentials sirven tal cual."));
                _list.Add(empty);
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
            var tex = a.Problem == null ? Textures.Thumbnail(full) : null;
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

            if (a.Problem == null)
            {
                var cut = Ui.Button(a.IsSliced ? "Editar corte…" : "Cortar…", () => SliceWizard.Show(_shell, full, a.Kind),
                    a.IsSliced ? Ui.ButtonKind.Normal : Ui.ButtonKind.Primary, "Asistente de corte: tamaño del tile, vacíos y repetidos");
                row.Add(cut);
            }
            return row;
        }

        private void ImportImage()
        {
            FolderBrowser.PickFile(_shell, "Importar una imagen", ".png", file =>
            {
                var d = _shell.ShowDialog("¿Qué es esta imagen?");
                d.Body.Add(Ui.Hint(Path.GetFileName(file)));
                var kinds = Ui.Row(6);
                kinds.style.flexWrap = Wrap.Wrap;
                foreach (var kv in ProjectLayout.KindFolders)
                {
                    var kind = kv.Key;
                    var folder = kv.Value;
                    kinds.Add(Ui.Button(KindLabels[(int)kind], () =>
                    {
                        _shell.CloseDialog(d);
                        try
                        {
                            var dest = Path.Combine(_shell.GraphicsFolder, folder, Path.GetFileName(file));
                            if (File.Exists(dest))
                            {
                                _shell.Confirm("Ya existe", $"Ya hay una imagen «{Path.GetFileName(file)}» en {folder}. ¿Sustituirla?",
                                    "Sustituir", () => Copy(file, dest, kind), danger: true);
                                return;
                            }
                            Copy(file, dest, kind);
                        }
                        catch (Exception e) { _shell.Error("No se pudo importar: " + e.Message); }
                    }).Margin(0, 0, 6, 6));
                }
                d.Body.Add(kinds);
                d.Buttons.Add(Ui.Button("Cancelar", () => _shell.CloseDialog(d)));
            });
        }

        private void Copy(string from, string dest, AssetKind kind)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            File.Copy(from, dest, true);
            _shell.Success($"Importada: {Path.GetFileName(dest)}");
            Refresh();
            if (kind == AssetKind.Tileset || kind == AssetKind.Character) SliceWizard.Show(_shell, dest, kind);
        }
    }
}
