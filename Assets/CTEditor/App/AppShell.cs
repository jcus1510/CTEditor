using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using CTEditor.Project;
using CTEditor.Workspace;

namespace CTEditor.App
{
    /// <summary>Una línea del registro de avisos.</summary>
    public sealed class LogEntry
    {
        public DateTime Time;
        public string Text;
        /// <summary>Token de color: «texto», «aviso», «error», «exito».</summary>
        public string Level;
    }

    /// <summary>Una opción de menú (o un separador si Label es null).</summary>
    public sealed class MenuItem
    {
        public string Label;
        public string Shortcut;
        public Action Action;
        public bool Enabled = true;
        public bool Checked;

        public static MenuItem Separator => new MenuItem();
        public bool IsSeparator => Label == null;

        public MenuItem() { }
        public MenuItem(string label, Action action, string shortcut = null, bool enabled = true, bool isChecked = false)
        {
            Label = label; Action = action; Shortcut = shortcut; Enabled = enabled; Checked = isChecked;
        }
    }

    /// <summary>
    /// La aplicación entera en UNA ventana: pantalla de inicio o mesa de trabajo (menú, paneles acoplables, barra de
    /// estado), más las capas de diálogos, menús desplegables, arrastre y avisos. Guarda el entorno de trabajo del usuario
    /// y el proyecto abierto.
    /// </summary>
    public sealed class AppShell
    {
        public VisualElement Root { get; }
        public WorkspaceSettings Workspace { get; private set; }
        public string ProjectRoot { get; private set; }
        public ProjectSettings Project { get; private set; }
        public bool HasProject => Project != null;

        /// <summary>Cambió la escala de la interfaz (AppRoot ajusta el panel).</summary>
        public Action<float> ScaleChanged;
        /// <summary>Pantalla completa ↔ ventana (lo hace AppRoot).</summary>
        public Action ToggleFullscreen;
        public Func<bool> IsFullscreen;
        /// <summary>Cerrar la aplicación.</summary>
        public Action Quit;

        public event Action ProjectChanged;
        /// <summary>Algo cambió en «graficos/» (desde la aplicación o desde fuera).</summary>
        public event Action AssetsChanged;
        public event Action LogChanged;

        public List<LogEntry> Log { get; } = new List<LogEntry>();

        private readonly string _workspacePath;
        private VisualElement _screen, _dialogLayer, _popupLayer, _dragLayer, _toastLayer;
        private DockView _dock;
        private IVisualElementScheduledItem _saveSoon;
        private FileSystemWatcher _watcher;
        private volatile bool _assetsDirty;

        public VisualElement DragLayer => _dragLayer;

        public AppShell(VisualElement root, string workspacePath)
        {
            Root = root;
            _workspacePath = workspacePath;
            Workspace = WorkspaceSettings.Load(workspacePath);
            Ui.Theme = Workspace.Theme;
            Ui.FontSize = Workspace.FontSize;

            root.focusable = true;
            root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            root.RegisterCallback<WheelEvent>(e =>
            {
                if (!e.ctrlKey && !e.commandKey) return;
                SetScale(Workspace.UiScale + (e.delta.y < 0 ? 0.1f : -0.1f));
                e.StopPropagation();
            }, TrickleDown.TrickleDown);
            root.schedule.Execute(PollWatcher).Every(400);
            Rebuild();
        }

        // ── Screens ──────────────────────────────────────────────────────────────────────────────

        /// <summary>Rebuilds the whole interface (after a theme, font or project change).</summary>
        public void Rebuild()
        {
            Ui.Theme = Workspace.Theme;
            Ui.FontSize = Workspace.FontSize;
            Ui.InvalidateColors();
            Root.Clear();
            Root.style.backgroundColor = Ui.C("fondo");
            Root.style.fontSize = Workspace.FontSize;
            Root.style.color = Ui.C("texto");
            Root.style.flexGrow = 1;

            _screen = new VisualElement().Grow();
            Root.Add(_screen);
            _dragLayer = new VisualElement().Fill();
            _dragLayer.pickingMode = PickingMode.Ignore;
            _popupLayer = new VisualElement().Fill();
            _popupLayer.pickingMode = PickingMode.Ignore;
            _dialogLayer = new VisualElement().Fill();
            _dialogLayer.pickingMode = PickingMode.Ignore;
            _toastLayer = new VisualElement().Fill();
            _toastLayer.pickingMode = PickingMode.Ignore;
            _toastLayer.style.alignItems = Align.FlexEnd;
            _toastLayer.style.justifyContent = Justify.FlexEnd;
            _toastLayer.Pad(16, 34);
            Root.Add(_dialogLayer);
            Root.Add(_popupLayer);
            Root.Add(_dragLayer);
            Root.Add(_toastLayer);

            if (HasProject) BuildWorkbench();
            else _screen.Add(new StartScreen(this));
            Root.Focus();
        }

        private void BuildWorkbench()
        {
            var col = Ui.Column().Grow();
            col.Add(new MenuBar(this));
            _dock = new DockView(this, Workspace.Layout);
            col.Add(_dock.Grow());
            col.Add(StatusBar());
            _screen.Add(col);
        }

        private VisualElement StatusBar()
        {
            var bar = Ui.Row(14).Bg("panel").Pad(10, 0);
            bar.style.height = Workspace.FontSize + 10;
            bar.style.flexShrink = 0;
            bar.style.borderTopWidth = 1;
            bar.style.borderTopColor = Ui.C("borde");
            bar.With(
                Ui.Text("• " + Project.Name, 0.92f).Colored("exito"),
                Ui.Text($"Tile {Project.TileSize} px", 0.92f, dim: true),
                Ui.Text($"Pantalla {Project.ScreenWidth} × {Project.ScreenHeight}", 0.92f, dim: true),
                Ui.Text(ProjectRoot, 0.92f, dim: true),
                Ui.Spacer(),
                Ui.Text($"Distribución: {Workspace.Layout.Name}", 0.92f, dim: true),
                Ui.Text($"Interfaz {Mathf.RoundToInt(Workspace.UiScale * 100)} %", 0.92f, dim: true));
            return bar;
        }

        // ── Project ──────────────────────────────────────────────────────────────────────────────

        public bool OpenProject(string root)
        {
            try
            {
                if (!ProjectFile.IsProject(root))
                {
                    if (Directory.Exists(Path.Combine(root, "Data")) && Directory.Exists(Path.Combine(root, "Graphics")))
                        Warn("Esa carpeta parece un proyecto de RPG Maker XP / Essentials. Su importación llegará en la fase 9 (ver la propuesta).");
                    else
                        Warn($"En esa carpeta no hay un proyecto de CTEditor (falta «{ProjectLayout.ProjectFile}»).");
                    return false;
                }
                var settings = ProjectFile.Load(root);
                var problems = settings.Problems();
                if (problems.Count > 0) { Error(string.Join(" ", problems)); return false; }
                ProjectLayout.CreateFolders(root);
                ProjectRoot = root;
                Project = settings;
                Workspace.NoteRecentProject(root);
                SaveWorkspaceNow();
                StartWatching();
                Rebuild();
                Info($"Proyecto abierto: {settings.Name}");
                ProjectChanged?.Invoke();
                return true;
            }
            catch (Exception e)
            {
                Error("No se pudo abrir el proyecto: " + e.Message);
                return false;
            }
        }

        public bool CreateProject(string root, string name, int tileSize, int screenWidth, int screenHeight)
        {
            try
            {
                if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any() && !ProjectFile.IsProject(root))
                {
                    Warn("La carpeta ya existe y no está vacía. Elige otro nombre o una carpeta vacía.");
                    return false;
                }
                var s = ProjectFile.Create(root, name, tileSize);
                s.ScreenWidth = screenWidth;
                s.ScreenHeight = screenHeight;
                ProjectFile.Save(root, s);
                return OpenProject(root);
            }
            catch (Exception e)
            {
                Error("No se pudo crear el proyecto: " + e.Message);
                return false;
            }
        }

        public void CloseProject()
        {
            StopWatching();
            ProjectRoot = null;
            Project = null;
            Rebuild();
            ProjectChanged?.Invoke();
        }

        public void SaveProject()
        {
            if (!HasProject) return;
            try
            {
                ProjectFile.Save(ProjectRoot, Project);
                SaveWorkspaceNow();
                Info("Guardado.");
            }
            catch (Exception e) { Error("No se pudo guardar: " + e.Message); }
        }

        public string GraphicsFolder => HasProject ? Path.Combine(ProjectRoot, ProjectLayout.GraphicsFolder) : null;

        public void NotifyAssetsChanged() => _assetsDirty = true;

        private void StartWatching()
        {
            StopWatching();
            try
            {
                _watcher = new FileSystemWatcher(GraphicsFolder) { IncludeSubdirectories = true, EnableRaisingEvents = true };
                FileSystemEventHandler dirty = (_, __) => _assetsDirty = true;
                _watcher.Changed += dirty;
                _watcher.Created += dirty;
                _watcher.Deleted += dirty;
                _watcher.Renamed += (_, __) => _assetsDirty = true;
            }
            catch (Exception)
            {
                _watcher = null; // Not supported here: «Recargar» still works.
            }
        }

        private void StopWatching()
        {
            if (_watcher == null) return;
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }

        private void PollWatcher()
        {
            if (!_assetsDirty) return;
            _assetsDirty = false;
            AssetsChanged?.Invoke();
        }

        public void Shutdown()
        {
            StopWatching();
            SaveWorkspaceNow();
        }

        // ── Workspace ────────────────────────────────────────────────────────────────────────────

        /// <summary>Saves the workspace a moment later (many small changes → one write).</summary>
        public void SaveWorkspaceSoon()
        {
            _saveSoon?.Pause();
            _saveSoon = Root.schedule.Execute(SaveWorkspaceNow);
            _saveSoon.ExecuteLater(600);
        }

        public void SaveWorkspaceNow()
        {
            try { Workspace.Save(_workspacePath); }
            catch (Exception e) { Debug.LogWarning("[CTEditor] No se pudo guardar el entorno: " + e.Message); }
        }

        public void SetLayout(DockLayout layout)
        {
            Workspace.Layout = layout;
            SaveWorkspaceSoon();
            Rebuild();
        }

        public void UseLayout(string name)
        {
            if (Workspace.UseLayout(name)) { SaveWorkspaceSoon(); Rebuild(); }
        }

        public void SetTheme(Theme theme)
        {
            Workspace.Theme = theme;
            SaveWorkspaceSoon();
            Rebuild();
        }

        public void SetScale(float scale)
        {
            Workspace.UiScale = scale;
            ScaleChanged?.Invoke(Workspace.UiScale);
            SaveWorkspaceSoon();
        }

        public void SetFontSize(int size)
        {
            Workspace.FontSize = size;
            SaveWorkspaceSoon();
            Rebuild();
        }

        // ── Messages ─────────────────────────────────────────────────────────────────────────────

        public void Info(string text) => Note(text, "texto");
        public void Success(string text) => Note(text, "exito");
        public void Warn(string text) => Note(text, "aviso");
        public void Error(string text) => Note(text, "error");

        private void Note(string text, string level)
        {
            Log.Add(new LogEntry { Time = DateTime.Now, Text = text, Level = level });
            if (Log.Count > 500) Log.RemoveAt(0);
            LogChanged?.Invoke();
            Toast(text, level);
        }

        public void Toast(string text, string level = "texto")
        {
            if (_toastLayer == null) return;
            var card = Ui.Row(8).Bg("panel_alt").Border(1, level == "texto" ? "borde" : level, 6).Pad(12, 8);
            card.style.maxWidth = 520;
            card.style.marginTop = 6;
            var bar = new VisualElement();
            bar.style.width = 4;
            bar.style.alignSelf = Align.Stretch;
            bar.style.backgroundColor = Ui.C(level == "texto" ? "acento" : level);
            bar.Round(2);
            card.With(bar, Ui.Text(text, wrap: true));
            _toastLayer.Add(card);
            card.schedule.Execute(() => card.RemoveFromHierarchy()).ExecuteLater(level == "error" ? 7000 : 3500);
        }

        // ── Dialogs ──────────────────────────────────────────────────────────────────────────────

        public sealed class Dialog
        {
            public VisualElement Backdrop;
            public VisualElement Body;
            public VisualElement Buttons;
            public Action OnClose;
            public void Close()
            {
                Backdrop?.RemoveFromHierarchy();
                OnClose?.Invoke();
            }
        }

        private readonly List<Dialog> _dialogs = new List<Dialog>();

        /// <summary>A modal box centered on the window. 'widthPct'/'heightPct' ≤ 0 = fit to the content.</summary>
        public Dialog ShowDialog(string title, float widthPct = 0, float heightPct = 0)
        {
            var d = new Dialog();
            var backdrop = new VisualElement().Fill();
            backdrop.style.backgroundColor = new Color(0, 0, 0, 0.55f);
            backdrop.style.alignItems = Align.Center;
            backdrop.style.justifyContent = Justify.Center;
            backdrop.pickingMode = PickingMode.Position;

            var box = Ui.Column().Bg("panel").Border(1, "borde", 8);
            if (widthPct > 0) box.style.width = Length.Percent(widthPct);
            else { box.style.minWidth = 420; box.style.maxWidth = Length.Percent(90); }
            if (heightPct > 0) box.style.height = Length.Percent(heightPct);
            else box.style.maxHeight = Length.Percent(90);

            var header = Ui.Row(8).Bg("panel_alt").Pad(14, 10);
            header.style.borderTopLeftRadius = 8;
            header.style.borderTopRightRadius = 8;
            header.style.flexShrink = 0;
            header.With(Ui.Heading(title), Ui.Spacer(), Ui.Button("×", () => CloseDialog(d), Ui.ButtonKind.Flat, "Cerrar (Esc)"));
            var body = Ui.Column(8).Pad(16).Grow();
            body.style.flexBasis = StyleKeyword.Auto;
            if (heightPct > 0) body.style.flexBasis = 0;
            var buttons = Ui.Row(8).Pad(16, 12);
            buttons.style.justifyContent = Justify.FlexEnd;
            buttons.style.flexShrink = 0;
            buttons.style.borderTopWidth = 1;
            buttons.style.borderTopColor = Ui.C("borde");
            box.Add(header);
            box.Add(body);
            box.Add(buttons);
            backdrop.Add(box);
            _dialogLayer.Add(backdrop);

            d.Backdrop = backdrop;
            d.Body = body;
            d.Buttons = buttons;
            _dialogs.Add(d);
            return d;
        }

        public void CloseDialog(Dialog d)
        {
            _dialogs.Remove(d);
            d.Close();
            Root.Focus();
        }

        public void Confirm(string title, string text, string okLabel, Action onOk, bool danger = false)
        {
            var d = ShowDialog(title);
            d.Body.Add(Ui.Text(text, wrap: true));
            d.Buttons.With(Ui.Button("Cancelar", () => CloseDialog(d)),
                Ui.Button(okLabel, () => { CloseDialog(d); onOk(); }, danger ? Ui.ButtonKind.Danger : Ui.ButtonKind.Primary));
        }

        public void Prompt(string title, string label, string value, string okLabel, Action<string> onOk)
        {
            var d = ShowDialog(title);
            var field = Ui.TextBox(label, value);
            d.Body.Add(field);
            void Accept() { CloseDialog(d); onOk(field.value); }
            field.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Accept(); });
            d.Buttons.With(Ui.Button("Cancelar", () => CloseDialog(d)), Ui.Button(okLabel, Accept, Ui.ButtonKind.Primary));
            field.schedule.Execute(() => field.Q(className: TextField.inputUssClassName)?.Focus()).ExecuteLater(50);
        }

        // ── Popup menus ──────────────────────────────────────────────────────────────────────────

        private VisualElement _openMenu;
        public bool IsMenuOpen => _openMenu != null;

        /// <summary>Shows a dropdown list at a window position (top-left corner). Clicking outside closes it.</summary>
        public void ShowMenu(Vector2 position, IList<MenuItem> items, Action onClosed = null, Action<Vector2> onPointerMove = null)
        {
            CloseMenu();
            var catcher = new VisualElement().Fill();
            catcher.pickingMode = PickingMode.Position;
            catcher.RegisterCallback<PointerDownEvent>(e => { if (e.target == catcher) { CloseMenu(); onClosed?.Invoke(); } });
            if (onPointerMove != null) catcher.RegisterCallback<PointerMoveEvent>(e => onPointerMove(e.position));

            var list = Ui.Column().Bg("panel_alt").Border(1, "borde", 6).Pad(4);
            list.style.position = Position.Absolute;
            list.style.left = position.x;
            list.style.top = position.y;
            list.style.minWidth = 240;
            foreach (var item in items)
            {
                if (item.IsSeparator)
                {
                    var sep = Ui.Separator();
                    sep.Margin(4, 4, 4, 4);
                    list.Add(sep);
                    continue;
                }
                var row = Ui.Row(10).Pad(10, 5).Round(4);
                row.Add(Ui.Text(item.Checked ? "•" : " ", bold: true).Colored("acento"));
                row.Add(Ui.Text(item.Label).Grow());
                if (!string.IsNullOrEmpty(item.Shortcut)) row.Add(Ui.Text(item.Shortcut, 0.9f, dim: true));
                if (!item.Enabled) row.style.opacity = 0.4f;
                else
                {
                    row.RegisterCallback<PointerEnterEvent>(_ => row.style.backgroundColor = Ui.C("seleccion"));
                    row.RegisterCallback<PointerLeaveEvent>(_ => row.style.backgroundColor = new Color(0, 0, 0, 0));
                    var action = item.Action;
                    row.RegisterCallback<PointerUpEvent>(_ => { CloseMenu(); onClosed?.Invoke(); action?.Invoke(); });
                }
                list.Add(row);
            }
            catcher.Add(list);
            _popupLayer.Add(catcher);
            _openMenu = catcher;
            // Keep the list inside the window.
            list.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                var r = Root.layout;
                if (list.layout.xMax > r.width) list.style.left = Mathf.Max(0, r.width - list.layout.width - 4);
                if (list.layout.yMax > r.height) list.style.top = Mathf.Max(0, r.height - list.layout.height - 4);
            });
        }

        public void CloseMenu()
        {
            _openMenu?.RemoveFromHierarchy();
            _openMenu = null;
        }

        // ── Shortcuts ────────────────────────────────────────────────────────────────────────────

        /// <summary>Runs an action by its id (menus, shortcuts and buttons all come here).</summary>
        public void RunAction(string id)
        {
            switch (id)
            {
                case "guardar": SaveProject(); break;
                case "pantalla_completa": ToggleFullscreen?.Invoke(); break;
                case "jugar":
                case "probar_aqui":
                case "depurador":
                    Info("Jugar llega en la fase 3, junto al editor de mapas.");
                    break;
                case "buscar": Info("La búsqueda en todo el proyecto llegará con los editores de mapas y eventos."); break;
                default:
                    ActionRequested?.Invoke(id);
                    break;
            }
        }

        /// <summary>Actions that belong to a panel (tools, grid...): the panels listen here.</summary>
        public event Action<string> ActionRequested;

        private void OnKeyDown(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.None) return;
            if (e.keyCode == KeyCode.Escape)
            {
                if (IsMenuOpen) { CloseMenu(); e.StopPropagation(); return; }
                if (_dialogs.Count > 0) { CloseDialog(_dialogs[_dialogs.Count - 1]); e.StopPropagation(); return; }
                return;
            }
            bool typing = e.target is VisualElement ve && (ve is TextField || ve.GetFirstAncestorOfType<TextField>() != null);
            bool modifier = e.ctrlKey || e.commandKey || e.altKey;
            bool functionKey = e.keyCode >= KeyCode.F1 && e.keyCode <= KeyCode.F15;
            if (typing && !modifier && !functionKey) return;
            if (_dialogs.Count > 0 && !functionKey) return;

            var keys = ShortcutMap.Normalize((e.ctrlKey || e.commandKey ? "Ctrl+" : "") + (e.altKey ? "Alt+" : "")
                                             + (e.shiftKey ? "Mayús+" : "") + e.keyCode);
            var action = Workspace.Shortcuts.ActionFor(keys);
            if (action == null) return;
            e.StopPropagation();
            RunAction(action);
        }
    }
}
