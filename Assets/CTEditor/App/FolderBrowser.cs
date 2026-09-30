using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTEditor.App
{
    /// <summary>
    /// Explorador de carpetas y archivos dentro de la aplicación (una aplicación de Unity no tiene el diálogo nativo de
    /// Windows): unidades, accesos rápidos (Documentos, Escritorio, recientes), subir, escribir la ruta a mano y crear
    /// carpeta. Marca las carpetas que ya son proyectos de CTEditor.
    /// </summary>
    public static class FolderBrowser
    {
        private static string _last;

        public static void PickFolder(AppShell shell, string title, Action<string> onPicked, string start = null) =>
            Show(shell, title, null, onPicked, start);

        public static void PickFile(AppShell shell, string title, string extension, Action<string> onPicked, string start = null) =>
            Show(shell, title, extension, onPicked, start);

        private static void Show(AppShell shell, string title, string extension, Action<string> onPicked, string start)
        {
            bool pickFile = extension != null;
            var d = shell.ShowDialog(title, 62, 72);
            string current = FirstExisting(start, _last, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Directory.GetCurrentDirectory());
            string selectedFile = null;

            var pathField = Ui.TextBox(null, current, null, delayed: true);
            pathField.Grow();
            var up = Ui.Button("Subir", null);
            var newFolder = Ui.Button("Nueva carpeta…", null);
            var top = Ui.Row(6);
            top.style.flexShrink = 0;
            top.With(up, pathField, newFolder);
            d.Body.Add(top);

            var main = Ui.Row(10).Grow();
            main.style.alignItems = Align.Stretch;
            var places = Ui.Scroll();
            places.style.width = 200;
            places.style.flexShrink = 0;
            var list = Ui.Scroll().Grow();
            list.Bg("fondo").Border(1, "borde", 4);
            main.With(places, list);
            d.Body.Add(main);

            var status = Ui.Hint("");
            status.style.flexShrink = 0;
            d.Body.Add(status);

            var ok = Ui.Button(pickFile ? "Elegir el archivo" : "Elegir esta carpeta", null, Ui.ButtonKind.Primary);
            d.Buttons.With(Ui.Button("Cancelar", () => shell.CloseDialog(d)), ok);

            void Navigate(string path)
            {
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                {
                    status.text = "Esa carpeta no existe.";
                    status.style.color = Ui.C("error");
                    pathField.SetValueWithoutNotify(current);
                    return;
                }
                current = Path.GetFullPath(path);
                selectedFile = null;
                pathField.SetValueWithoutNotify(current);
                Fill();
            }

            void Fill()
            {
                list.Clear();
                status.style.color = Ui.C("texto_suave");
                try
                {
                    var dirs = Directory.GetDirectories(current).Where(p => !IsHidden(p)).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
                    foreach (var dir in dirs)
                    {
                        bool isProject = CTEditor.Project.ProjectFile.IsProject(dir);
                        var row = Entry(Path.GetFileName(dir) + (isProject ? "   (proyecto de CTEditor)" : "/"), isProject ? "acento" : "texto");
                        var target = dir;
                        row.RegisterCallback<ClickEvent>(e =>
                        {
                            if (e.clickCount >= 2 || !pickFile) Navigate(target);
                        });
                        list.Add(row);
                    }
                    int files = 0;
                    if (pickFile)
                    {
                        foreach (var file in Directory.GetFiles(current).Where(f => f.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                        {
                            files++;
                            var row = Entry(Path.GetFileName(file), "texto");
                            var target = file;
                            row.RegisterCallback<ClickEvent>(e =>
                            {
                                selectedFile = target;
                                foreach (var c in list.Children()) c.style.backgroundColor = new Color(0, 0, 0, 0);
                                row.style.backgroundColor = Ui.C("seleccion");
                                status.text = "Elegido: " + Path.GetFileName(target);
                                ok.SetEnabledLook(true);
                                if (e.clickCount >= 2) Accept();
                            });
                            list.Add(row);
                        }
                    }
                    status.text = pickFile
                        ? $"{dirs.Count} carpetas · {files} archivos {extension}. Haz doble clic en una carpeta para entrar."
                        : $"{dirs.Count} carpetas. Clic para entrar; «Elegir esta carpeta» usa la de arriba."
                          + (CTEditor.Project.ProjectFile.IsProject(current) ? "  Esta carpeta es un proyecto de CTEditor." : "");
                    if (list.childCount == 0) list.Add(Ui.Hint(pickFile ? "No hay carpetas ni imágenes aquí." : "No hay subcarpetas.").Margin(10, 8, 10, 8));
                }
                catch (Exception e)
                {
                    status.text = "No se puede abrir: " + e.Message;
                    status.style.color = Ui.C("error");
                }
                ok.SetEnabledLook(!pickFile || selectedFile != null);
            }

            void Accept()
            {
                var chosen = pickFile ? selectedFile : current;
                if (chosen == null) return;
                _last = pickFile ? Path.GetDirectoryName(chosen) : current;
                shell.CloseDialog(d);
                onPicked(chosen);
            }

            ok.clicked += Accept;
            up.clicked += () =>
            {
                var parent = Directory.GetParent(current);
                if (parent != null) Navigate(parent.FullName);
            };
            pathField.RegisterValueChangedCallback(e => Navigate(e.newValue));
            newFolder.clicked += () => shell.Prompt("Nueva carpeta", "Nombre", "", "Crear", name =>
            {
                if (string.IsNullOrWhiteSpace(name)) return;
                try
                {
                    var path = Path.Combine(current, name.Trim());
                    Directory.CreateDirectory(path);
                    Navigate(path);
                }
                catch (Exception e) { shell.Error("No se pudo crear la carpeta: " + e.Message); }
            });

            // Quick places.
            places.Add(Ui.Text("Accesos", 0.9f, bold: true).Margin(4, 0, 4, 4));
            foreach (var (label, path) in Places(shell))
            {
                var row = Entry(label, "texto");
                row.tooltip = path;
                var target = path;
                row.RegisterCallback<ClickEvent>(_ => Navigate(target));
                places.Add(row);
            }
            Navigate(current);
        }

        private static VisualElement Entry(string text, string token)
        {
            var row = Ui.Row(6).Pad(8, 4).Round(3);
            var label = Ui.Text(text).Colored(token);
            label.pickingMode = PickingMode.Ignore;
            row.Add(label);
            row.RegisterCallback<PointerEnterEvent>(_ => { if (row.style.backgroundColor.value != Ui.C("seleccion")) row.style.backgroundColor = Ui.C("panel_alt"); });
            row.RegisterCallback<PointerLeaveEvent>(_ => { if (row.style.backgroundColor.value != Ui.C("seleccion")) row.style.backgroundColor = new Color(0, 0, 0, 0); });
            return row;
        }

        private static IEnumerable<(string, string)> Places(AppShell shell)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IEnumerable<(string, string)> All()
            {
                yield return ("Inicio", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
                yield return ("Documentos", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
                yield return ("Escritorio", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
                if (shell.HasProject) yield return ("Proyecto abierto", shell.ProjectRoot);
                foreach (var r in shell.Workspace.RecentProjects.Take(5)) yield return ("Reciente: " + Path.GetFileName(r.TrimEnd('/', '\\')), r);
                DriveInfo[] drives;
                try { drives = DriveInfo.GetDrives(); }
                catch (Exception) { drives = new DriveInfo[0]; }
                foreach (var drive in drives)
                {
                    bool ready;
                    try { ready = drive.IsReady; }
                    catch (Exception) { ready = false; }
                    if (ready) yield return ("Unidad " + drive.Name, drive.RootDirectory.FullName);
                }
            }
            foreach (var (label, path) in All())
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path) && seen.Add(path))
                    yield return (label, path);
        }

        private static bool IsHidden(string path)
        {
            var name = Path.GetFileName(path);
            if (name.StartsWith(".") || name.StartsWith("$")) return true;
            try { return (File.GetAttributes(path) & (FileAttributes.Hidden | FileAttributes.System)) != 0; }
            catch (Exception) { return true; }
        }

        private static string FirstExisting(params string[] paths) =>
            paths.FirstOrDefault(p => !string.IsNullOrEmpty(p) && Directory.Exists(p)) ?? "/";
    }
}
