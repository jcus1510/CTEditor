using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using CTEditor.Art.Domain;
using CTEditor.Editing;
using CTEditor.Project;
using CTEditor.Workspace;
using CTEditor.World.Domain;

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
        /// <summary>Submenú: al pasar el ratón se abre a la derecha (Ver → Ventanas → ...).</summary>
        public IList<MenuItem> Children;

        public static MenuItem Separator => new MenuItem();
        public bool IsSeparator => Label == null;

        public MenuItem() { }
        public MenuItem(string label, Action action, string shortcut = null, bool enabled = true, bool isChecked = false)
        {
            Label = label; Action = action; Shortcut = shortcut; Enabled = enabled; Checked = isChecked;
        }

        public static MenuItem Submenu(string label, IList<MenuItem> children) => new MenuItem(label, null) { Children = children };
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

        /// <summary>Casos de uso del proyecto abierto (null sin proyecto). Los paneles los comparten.</summary>
        public MapEditorSession Maps { get; private set; }
        public PixelEditorSession Pixels { get; private set; }
        /// <summary>Qué editor reciben deshacer, rehacer y las herramientas: «mapa» o «retoque» (el último que se tocó).</summary>
        public string ActiveEditor { get; set; } = "mapa";
        /// <summary>Píxeles físicos por punto de la interfaz (escala del usuario × ppp): para texturas nítidas.</summary>
        public float PixelsPerPoint { get; set; } = 1f;
        public bool IsPlaying => _play != null;
        /// <summary>Playing inside the «Juego» window (the editor stays usable around it).</summary>
        public bool PlayDocked { get; private set; }
        /// <summary>The game has the keyboard: full screen, or docked and clicked.</summary>
        public bool PlayHasKeys => _play != null && (!PlayDocked || _play.panel?.focusController?.focusedElement == _play);
        /// <summary>Where docked play goes (the «Juego» window registers it while it is open).</summary>
        public VisualElement GameHost { get; set; }

        /// <summary>Cada fotograma (segundos desde el anterior).</summary>
        public event Action<float> Ticked;
        /// <summary>Empieza o termina el modo juego.</summary>
        public event Action<bool> PlayingChanged;

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
        private VisualElement _screen, _dialogLayer, _popupLayer, _dragLayer, _toastLayer, _tipLayer;
        private VisualElement _tipOwner;
        private IVisualElementScheduledItem _tipTimer;
        private Vector2 _tipPosition;
        private IVisualElementScheduledItem _rescale;
        private DockView _dock;
        private IVisualElementScheduledItem _saveSoon;
        private FileSystemWatcher _watcher;
        private volatile bool _assetsDirty;
        private PlayScreen _play;
        private IVisualElementScheduledItem _autosave;

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
            root.RegisterCallback<KeyUpEvent>(e => { if (e.keyCode == KeyCode.Space) SpaceHeld = false; }, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerMoveEvent>(OnTipMove, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerDownEvent>(_ => HideTip(), TrickleDown.TrickleDown);
            root.RegisterCallback<PointerLeaveEvent>(_ => HideTip());
            root.RegisterCallback<WheelEvent>(e =>
            {
                // The start screen has a fixed design: the interface scale is changed inside a project (or in Entorno).
                if (!e.ctrlKey && !e.commandKey) return;
                if (!HasProject) { e.StopPropagation(); return; }
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
            StopPlay();
            Ui.Theme = Workspace.Theme;
            Ui.FontSize = Workspace.FontSize;
            Ui.Compact = Workspace.Pref("densidad", "comoda") == "compacta";
            Ui.Animations = Workspace.Pref("animaciones", "si") == "si";
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
            _tipLayer = new VisualElement().Fill();
            _tipLayer.pickingMode = PickingMode.Ignore;
            Root.Add(_tipLayer);
            _tipOwner = null;

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
            bar.style.overflow = Overflow.Hidden;
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

        // ── Tooltips ─────────────────────────────────────────────────────────────────────────────
        // UI Toolkit only shows «tooltip» in the Unity editor; the application draws its own after a short pause.

        private void OnTipMove(PointerMoveEvent e)
        {
            _tipPosition = e.position;
            // The element really under the pointer (the deepest pickable one), then up to the first with a tooltip.
            var owner = Root.panel?.Pick(e.position) ?? e.target as VisualElement;
            while (owner != null && string.IsNullOrEmpty(owner.tooltip)) owner = owner.parent;
            if (owner == _tipOwner) return;
            HideTip();
            _tipOwner = owner;
            if (owner != null) _tipTimer = Root.schedule.Execute(ShowTip).StartingIn(450);
        }

        private void ShowTip()
        {
            if (_tipLayer == null || _tipOwner?.panel == null || string.IsNullOrEmpty(_tipOwner.tooltip)) return;
            _tipLayer.Clear();
            var tip = Ui.Text(_tipOwner.tooltip, 0.92f, wrap: true).Appear();
            tip.style.maxWidth = 360;
            tip.style.position = Position.Absolute;
            tip.Bg("panel_alt").Border(1, "borde", Ui.Radius).Pad(8, 5);
            var p = Root.WorldToLocal(_tipPosition) + new Vector2(14, 20);
            tip.style.left = p.x;
            tip.style.top = p.y;
            tip.pickingMode = PickingMode.Ignore;
            // Keep it inside the window once its size is known.
            tip.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                float maxX = Root.layout.width - tip.layout.width - 4, maxY = Root.layout.height - tip.layout.height - 4;
                if (p.x > maxX) tip.style.left = Mathf.Max(4, maxX);
                if (p.y > maxY) tip.style.top = Mathf.Max(4, p.y - tip.layout.height - 28);
            });
            _tipLayer.Add(tip);
        }

        private void HideTip()
        {
            _tipTimer?.Pause();
            _tipTimer = null;
            _tipOwner = null;
            _tipLayer?.Clear();
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
                CloseSessions();
                ProjectRoot = root;
                Project = settings;
                OpenSessions();
                Workspace.NoteRecentProject(root);
                SaveWorkspaceNow();
                StartWatching();
                StartBackups();
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

        public bool CreateProject(string root, string name, int tileSize, int screenWidth, int screenHeight, string packFolder = null)
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
                if (!string.IsNullOrEmpty(packFolder)) PackInstaller.Install(packFolder, root, overwrite: false);
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
            StopPlay();
            SaveEverything(quiet: true);
            CloseSessions();
            StopWatching();
            StopBackups();
            ProjectRoot = null;
            Project = null;
            Rebuild();
            ProjectChanged?.Invoke();
        }

        public void SaveProject() => SaveEverything(quiet: false);

        /// <summary>Guarda el proyecto, los mapas, las propiedades de tiles y la imagen que se esté retocando.</summary>
        public void SaveEverything(bool quiet)
        {
            if (!HasProject) return;
            try
            {
                ProjectFile.Save(ProjectRoot, Project);
                Maps?.Save();
                if (Pixels != null && Pixels.IsDirty) Pixels.Save();
                SaveWorkspaceNow();
                if (!quiet) Success("Guardado.");
            }
            catch (Exception e) { Error("No se pudo guardar: " + e.Message); }
        }

        private void OpenSessions()
        {
            Maps = new MapEditorSession(new JsonMapRepository(ProjectRoot), new FolderTilesetRepository(ProjectRoot),
                new JsonEncounterMethodRepository(ProjectRoot), new CsvSpeciesDirectory(ProjectRoot));
            Maps.LoadPlayerStart(Project.StartMap, Project.StartX, Project.StartY);
            Maps.Message += OnSessionMessage;
            Maps.DirtyChanged += ScheduleAutosave;
            Maps.PlayerStartChanged += OnPlayerStartChanged;
            Maps.BrushRepository = new JsonBrushRepository(ProjectRoot);
            Maps.LoadBrushes();
            Maps.PieceRepository = new JsonMapPieceRepository(ProjectRoot);
            Maps.LoadPieces();
            try { Maps.LoadTree(); }
            catch (Exception e) { Error("No se pudo leer el árbol de mapas: " + e.Message); }
            var first = !string.IsNullOrEmpty(Project.StartMap) && Maps.Tree.Contains(Project.StartMap)
                ? Project.StartMap
                : Maps.Tree.Walk().Select(w => w.entry.Id).FirstOrDefault();
            if (first != null) Maps.OpenMap(first);

            Pixels = new PixelEditorSession(new PngImageRepository());
            Pixels.Message += OnSessionMessage;
            LoadProfiles();
        }

        // ── Test profiles (A2) ───────────────────────────────────────────────────────────────────

        private ITestProfileRepository _profileRepo;
        public List<TestProfile> Profiles { get; } = new List<TestProfile>();
        public event Action ProfilesChanged;

        /// <summary>The profile «Jugar» uses (team, badges, items, switches, hour).</summary>
        public TestProfile ActiveProfile => Profiles.FirstOrDefault(p => p.Id == Workspace.Pref("perfil_prueba", "")) ?? Profiles.FirstOrDefault() ?? TestProfile.Default();

        private void LoadProfiles()
        {
            Profiles.Clear();
            _profileRepo = new JsonTestProfileRepository(ProjectRoot);
            try { Profiles.AddRange(_profileRepo.Load()); }
            catch (Exception e) { Error("No se pudieron leer los perfiles de prueba: " + e.Message); }
            if (Profiles.Count == 0) Profiles.Add(TestProfile.Default());
        }

        public void SaveProfiles()
        {
            try { _profileRepo?.Save(Profiles); }
            catch (Exception e) { Error("No se pudieron guardar los perfiles de prueba: " + e.Message); }
            ProfilesChanged?.Invoke();
        }

        public void SetActiveProfile(string id)
        {
            Workspace.SetPref("perfil_prueba", id);
            SaveWorkspaceSoon();
            ProfilesChanged?.Invoke();
            Info("Perfil de prueba: " + ActiveProfile.Name);
        }

        private void CloseSessions()
        {
            if (Maps != null)
            {
                Maps.Message -= OnSessionMessage;
                Maps.DirtyChanged -= ScheduleAutosave;
                Maps.PlayerStartChanged -= OnPlayerStartChanged;
            }
            if (Pixels != null) Pixels.Message -= OnSessionMessage;
            Maps = null;
            Pixels = null;
        }

        private void OnSessionMessage(string text, string level) => Note(text, level);

        private void OnPlayerStartChanged()
        {
            var (map, x, y) = Maps.PlayerStart;
            Project.StartMap = map;
            Project.StartX = x;
            Project.StartY = y;
            try { ProjectFile.Save(ProjectRoot, Project); }
            catch (Exception e) { Error("No se pudo guardar el inicio: " + e.Message); }
            Info($"Inicio del jugador: {map} ({x}, {y}).");
        }

        /// <summary>Maps save themselves a moment after the last change (nothing is lost if the program closes).</summary>
        private void ScheduleAutosave()
        {
            if (Maps == null || !Maps.IsDirty) return;
            _autosave?.Pause();
            _autosave = Root.schedule.Execute(() =>
            {
                try { Maps?.Save(); }
                catch (Exception e) { Error("No se pudo guardar el mapa: " + e.Message); }
            });
            _autosave.ExecuteLater(1500);
        }

        public void Tick(float dt)
        {
            Ticked?.Invoke(dt);
            ReportBackup();
        }

        // ── Version history (backups) ────────────────────────────────────────────────────────────
        // Every 10 minutes, only if something changed; at most 5 copies (ProjectBackups). Zipping runs in the background.

        public static readonly TimeSpan BackupInterval = TimeSpan.FromMinutes(10);
        private BackupSchedule _backups;
        private IVisualElementScheduledItem _backupCheck;
        private volatile bool _backupRunning;
        private string _backupDone, _backupError;
        private readonly object _backupLock = new object();

        private void StartBackups()
        {
            StopBackups();
            _backups = new BackupSchedule(BackupInterval, DateTime.Now);
            _backupCheck?.Pause();
            _backupCheck = Root.schedule.Execute(CheckBackup).Every(30000);
            if (Maps != null) Maps.DirtyChanged += NoteChangeForBackup;
            if (Pixels != null) Pixels.DirtyChanged += NoteChangeForBackup;
            AssetsChanged += NoteChangeForBackup;
        }

        private void StopBackups()
        {
            _backupCheck?.Pause();
            _backupCheck = null;
            if (Maps != null) Maps.DirtyChanged -= NoteChangeForBackup;
            if (Pixels != null) Pixels.DirtyChanged -= NoteChangeForBackup;
            AssetsChanged -= NoteChangeForBackup;
            _backups = null;
        }

        private void NoteChangeForBackup() => _backups?.NoteChange();

        private void CheckBackup()
        {
            if (_backups == null || _backupRunning || IsPlaying) return;
            if (_backups.Due(DateTime.Now)) MakeBackup("auto");
        }

        /// <summary>Saves everything and zips a copy of the project in the background.</summary>
        public void MakeBackup(string reason)
        {
            if (!HasProject || _backupRunning) return;
            SaveEverything(quiet: true);
            _backups?.Done(DateTime.Now);
            _backupRunning = true;
            string root = ProjectRoot;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var b = ProjectBackups.Create(root, reason);
                    lock (_backupLock) _backupDone = $"{(reason == "manual" ? "Copia de seguridad hecha" : "Copia automática")}: {b.Time:HH:mm} ({b.Bytes / 1024} KB)";
                }
                catch (Exception e) { lock (_backupLock) _backupError = "No se pudo hacer la copia de seguridad: " + e.Message; }
                finally { _backupRunning = false; }
            });
        }

        private void ReportBackup()
        {
            string done, error;
            lock (_backupLock) { done = _backupDone; error = _backupError; _backupDone = _backupError = null; }
            if (done != null) Note(done, "exito");
            if (error != null) Error(error);
        }

        /// <summary>Goes back to a copy: saves, restores (keeping a copy of now) and reopens the project.</summary>
        public void RestoreBackup(BackupInfo copy)
        {
            if (!HasProject || copy == null) return;
            try
            {
                SaveEverything(quiet: true);
                string root = ProjectRoot;
                CloseSessions();
                ProjectBackups.Restore(root, copy.Path);
                OpenProject(root);
                Success($"Proyecto devuelto a la copia de las {copy.Time:HH:mm} del {copy.Time:dd/MM}. Lo de antes quedó en otra copia.");
            }
            catch (Exception e) { Error("No se pudo restaurar: " + e.Message); }
        }

        /// <summary>
        /// Settings that apply to the WHOLE game (not to one map): today the shiny odds; each encounter species can still
        /// have its own. Saved in proyecto.json.
        /// </summary>
        public void GameSettingsDialog()
        {
            if (!HasProject) return;
            var d = ShowDialog("Ajustes del juego", 44);
            int shiny = Project.ShinyOdds;
            var presets = Ui.Row(0).Wrap();
            void Presets()
            {
                presets.Clear();
                foreach (var (n, label) in new[] { (8192, "1/8192 (gen. 2-5)"), (4096, "1/4096 (gen. 6 en adelante)"), (512, "1/512 (con amuleto, aprox.)") })
                {
                    int v = n;
                    presets.Add(Ui.Chip(label, shiny == v, () => { shiny = v; Presets(); }).Margin(0, 2, 4, 2));
                }
            }
            Presets();
            var custom = Ui.NumberBox("1 de cada", shiny, 1, 1000000, v => { shiny = v; Presets(); });
            d.Body.With(Ui.Heading("Variocolor"),
                Ui.Hint("Cada Pokémon salvaje tiene esta probabilidad de salir variocolor. Una especie de una tabla de encuentros puede tener la suya propia (menú «⋯» → Variocolor propio); si no, usa esta."),
                presets, custom,
                Ui.Separator(),
                Ui.Text($"Tile: {Project.TileSize} px · pantalla {Project.ScreenWidth} × {Project.ScreenHeight} (se eligen al crear el proyecto)", 0.85f, dim: true));
            d.Buttons.With(Ui.Button("Cancelar", () => CloseDialog(d)), Ui.Button("Guardar", () =>
            {
                Project.ShinyOdds = Math.Max(1, shiny);
                try { ProjectFile.Save(ProjectRoot, Project); Success("Ajustes del juego guardados."); }
                catch (Exception e) { Error("No se pudo guardar: " + e.Message); }
                CloseDialog(d);
                ProjectChanged?.Invoke();
            }, Ui.ButtonKind.Primary));
        }

        /// <summary>The list of copies: when, why and how big; restore or open the folder.</summary>
        public void HistoryDialog()
        {
            if (!HasProject) return;
            var d = ShowDialog("Historial de versiones", 46);
            var list = Ui.Column(4);
            void Fill()
            {
                list.Clear();
                var copies = ProjectBackups.List(ProjectRoot);
                if (copies.Count == 0) list.Add(Ui.Hint("Aún no hay copias. Se hace una cada 10 minutos si has cambiado algo, o ahora con «Hacer una copia»."));
                foreach (var c in copies)
                {
                    var copy = c;
                    var row = Ui.Row(10).Pad(10, 6).Bg("panel_alt").Round(4);
                    var texts = Ui.Column(1).Grow();
                    string why = c.Reason == "auto" ? "automática" : c.Reason == "manual" ? "a mano" : c.Reason == "antes_de_restaurar" ? "antes de restaurar" : c.Reason;
                    texts.With(Ui.Text($"{c.Time:dd/MM/yyyy HH:mm}", bold: true), Ui.Text($"{why} · {c.Bytes / 1024} KB", 0.85f, dim: true));
                    row.With(texts, Ui.Button("Restaurar", () => Confirm("Restaurar copia",
                        $"¿Volver el proyecto a como estaba el {c.Time:dd/MM} a las {c.Time:HH:mm}? Antes se guarda una copia de cómo está ahora.",
                        "Restaurar", () => { CloseDialog(d); RestoreBackup(copy); }), Ui.ButtonKind.Normal));
                    list.Add(row);
                }
            }
            Fill();
            d.Body.With(Ui.Hint($"Copias en «{ProjectBackups.Folder}/» del proyecto: una cada 10 minutos solo si cambiaste algo, como mucho {ProjectBackups.MaxCopies} (se borran las más viejas)."), list);
            d.Buttons.With(Ui.Button("Abrir la carpeta", () => Application.OpenURL("file://" + ProjectBackups.FolderOf(ProjectRoot)), Ui.ButtonKind.Flat),
                Ui.Button("Hacer una copia ahora", () => { MakeBackup("manual"); Root.schedule.Execute(Fill).StartingIn(1500); }, Ui.ButtonKind.Normal),
                Ui.Button("Cerrar", () => CloseDialog(d), Ui.ButtonKind.Primary));
        }

        /// <summary>Opens an image in the pixel editor (optionally focused on one tile) and shows its panel.</summary>
        public void OpenRetouch(string fullPath, PixelRect? tile = null, int tileWidth = 0, int tileHeight = 0)
        {
            if (Pixels == null) return;
            if (Pixels.IsDirty && Pixels.Path != fullPath) Pixels.Save();
            var slice = SliceFile.LoadFor(fullPath);
            int tw = tileWidth > 0 ? tileWidth : slice?.Settings.TileWidth ?? 0, th = tileHeight > 0 ? tileHeight : slice?.Settings.TileHeight ?? 0;
            bool ok = tile.HasValue ? Pixels.OpenTile(fullPath, tile.Value, tw, th) : Pixels.Open(fullPath, tw, th);
            if (!ok) return;
            ActiveEditor = "retoque";
            if (!Workspace.Layout.IsOpen(PanelCatalog.PixelEditor))
            {
                Workspace.Layout.Open(PanelCatalog.PixelEditor, PanelCatalog.Map);
                SetLayout(Workspace.Layout);
            }
            else
            {
                Workspace.Layout.Focus(PanelCatalog.PixelEditor);
                _dock?.Refresh();
            }
        }

        // ── Play ─────────────────────────────────────────────────────────────────────────────────

        /// <summary>▶ Jugar: desde el inicio del proyecto (o desde el mapa y casilla que se den).</summary>
        public void StartPlay(string mapId = null, int x = -1, int y = -1)
        {
            if (!HasProject || Maps == null || IsPlaying) return;
            if (mapId == null)
            {
                (mapId, x, y) = Maps.PlayerStart;
                if (string.IsNullOrEmpty(mapId) || !Maps.Tree.Contains(mapId))
                {
                    if (Maps.Map == null) { Warn("Crea un mapa antes de jugar (ventana Mapas → Nuevo mapa)."); return; }
                    Warn("Aún no hay inicio del jugador: se empieza en el centro del mapa abierto. Ponlo con la herramienta «Inicio».");
                    (mapId, x, y) = (Maps.Map.Id, Maps.Map.Width / 2, Maps.Map.Height / 2);
                }
            }
            CloseMenu();
            try { Maps.Save(); }
            catch (Exception e) { Error("No se pudo guardar antes de jugar: " + e.Message); }
            bool docked = GameHost?.panel != null;
            try
            {
                _play = new PlayScreen(this, mapId, x, y, docked);
            }
            catch (Exception e)
            {
                _play = null;
                Error("No se pudo empezar a jugar: " + e.Message);
                return;
            }
            PlayDocked = docked;
            if (docked) GameHost.Add(_play);
            else
            {
                _screen.Show(false);
                Root.Insert(Root.IndexOf(_screen) + 1, _play);
            }
            _play.Focus();
            PlayingChanged?.Invoke(true);
        }

        public void StopPlay()
        {
            if (_play == null) return;
            _play.Dispose();
            _play.RemoveFromHierarchy();
            _play = null;
            PlayDocked = false;
            _screen.Show(true);
            Root.Focus();
            PlayingChanged?.Invoke(false);
        }

        public string GraphicsFolder => HasProject ? Path.Combine(ProjectRoot, ProjectLayout.GraphicsFolder) : null;

        /// <summary>Where the generation packs are (inside Unity: Assets/GameContent/Packs; built app: StreamingAssets/Packs).</summary>
        public static string PacksRoot
        {
            get
            {
                var editor = Path.Combine(Application.dataPath, "GameContent", "Packs");
                return Directory.Exists(editor) ? editor : Path.Combine(Application.streamingAssetsPath, "Packs");
            }
        }

        /// <summary>Copies a pack's sheets (species, moves, items...) into datos/. 'overwrite' replaces existing ones.</summary>
        public void InstallPack(string packFolder, bool overwrite)
        {
            if (!HasProject) return;
            try
            {
                var done = PackInstaller.Install(packFolder, ProjectRoot, overwrite);
                Success(done.Count == 0
                    ? "No se copió nada (ya estaban las hojas; elige «sustituir» para cambiarlas)."
                    : $"Datos de {Path.GetFileName(packFolder)} copiados: {string.Join(", ", done)}.");
                ProjectChanged?.Invoke();
                Maps?.Species?.All();
            }
            catch (Exception e) { Error("No se pudieron copiar los datos: " + e.Message); }
        }

        /// <summary>Dialog to pick a pack and copy its data into the project.</summary>
        public void PackDialog()
        {
            var packs = PackInstaller.Find(PacksRoot);
            var d = ShowDialog("Copiar datos de un pack");
            if (packs.Count == 0) d.Body.Add(Ui.Hint("No se encuentran los packs (Assets/GameContent/Packs)."));
            bool overwrite = false;
            d.Body.Add(Ui.Hint("Copia a datos/ las hojas de la generación: especies, movimientos, objetos, habilidades, entrenadores... Las zonas de encuentros usan sus especies."));
            d.Body.Add(Ui.Check("Sustituir las hojas que ya existan", false, v => overwrite = v));
            var row = Ui.Row(6);
            row.style.flexWrap = Wrap.Wrap;
            foreach (var pack in packs)
            {
                var folder = pack;
                row.Add(Ui.Button(Path.GetFileName(pack), () => { CloseDialog(d); InstallPack(folder, overwrite); }, Ui.ButtonKind.Primary).Margin(0, 0, 6, 6));
            }
            d.Body.Add(row);
            d.Buttons.Add(Ui.Button("Cancelar", () => CloseDialog(d)));
        }

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
            Maps?.ReloadTilesets();
            AssetsChanged?.Invoke();
        }

        public void Shutdown()
        {
            StopPlay();
            SaveEverything(quiet: true);
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
            // Icons are rasterized for the scale: redraw the interface once the user stops zooming.
            _rescale?.Pause();
            _rescale = Root.schedule.Execute(() =>
            {
                if (IsPlaying) return;
                Rebuild();
                if (HasProject) Toast($"Interfaz al {Mathf.RoundToInt(Workspace.UiScale * 100)} % (Ctrl + 0: volver al 100 %)");
            }).StartingIn(700);
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

            var box = Ui.Column().Bg("panel").Border(1, "borde", 8).Appear();
            if (widthPct > 0) box.style.width = Length.Percent(widthPct);
            else { box.style.minWidth = 420; box.style.maxWidth = Length.Percent(90); }
            if (heightPct > 0) box.style.height = Length.Percent(heightPct);
            else box.style.maxHeight = Length.Percent(90);

            var header = Ui.Row(8).Bg("panel_alt").Pad(14, 10);
            header.style.borderTopLeftRadius = 8;
            header.style.borderTopRightRadius = 8;
            header.style.flexShrink = 0;
            header.With(Ui.Heading(title), Ui.Spacer(), Ui.IconButton("cerrar", () => CloseDialog(d), "Cerrar (Esc)"));
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

            var open = new List<VisualElement>(); // open lists by depth (0 = the menu, 1 = its submenu...)

            VisualElement BuildList(IList<MenuItem> entries, Vector2 at, int depth)
            {
                var list = Ui.Column().Bg("panel_alt").Border(1, "borde", 6).Pad(4).Appear();
                list.style.position = Position.Absolute;
                list.style.left = at.x;
                list.style.top = at.y;
                list.style.minWidth = 220;
                foreach (var item in entries)
                {
                    if (item.IsSeparator)
                    {
                        var sep = Ui.Separator();
                        sep.Margin(4, 4, 4, 4);
                        list.Add(sep);
                        continue;
                    }
                    var row = Ui.Row(10).Pad(10, 5).Round(4);
                    var mark = Ui.Text(item.Checked ? "•" : " ", bold: true).Colored("acento").NoShrink();
                    mark.style.width = 8;
                    row.Add(mark);
                    row.Add(Ui.Text(item.Label).Grow());
                    if (!string.IsNullOrEmpty(item.Shortcut)) row.Add(Ui.Text(item.Shortcut, 0.9f, dim: true).NoShrink());
                    if (item.Children != null) row.Add(Icons.Element("siguiente", Ui.IconSize * 0.8f, Ui.C("texto_suave")));
                    var current = item;
                    row.RegisterCallback<PointerEnterEvent>(_ =>
                    {
                        // Entering a row closes deeper submenus; a row with children opens its own.
                        while (open.Count > depth + 1) { open[open.Count - 1].RemoveFromHierarchy(); open.RemoveAt(open.Count - 1); }
                        if (!current.Enabled) return;
                        row.style.backgroundColor = Ui.C("seleccion");
                        if (current.Children != null && current.Children.Count > 0)
                        {
                            var r = row.worldBound;
                            var sub = BuildList(current.Children, Root.WorldToLocal(new Vector2(r.xMax + 2, r.y - 4)), depth + 1);
                            catcher.Add(sub);
                            open.Add(sub);
                        }
                    });
                    row.RegisterCallback<PointerLeaveEvent>(_ => row.style.backgroundColor = new Color(0, 0, 0, 0));
                    if (!current.Enabled) row.style.opacity = 0.4f;
                    else if (current.Children == null)
                    {
                        var action = current.Action;
                        row.RegisterCallback<PointerUpEvent>(_ => { CloseMenu(); onClosed?.Invoke(); action?.Invoke(); });
                    }
                    list.Add(row);
                }
                // Keep the list inside the window (a submenu that does not fit on the right opens on the left).
                list.RegisterCallback<GeometryChangedEvent>(_ =>
                {
                    var r = Root.layout;
                    if (list.layout.xMax > r.width)
                        list.style.left = depth > 0 && open.Count > depth - 1 && depth - 1 >= 0
                            ? Mathf.Max(0, open[depth - 1].layout.x - list.layout.width - 2)
                            : Mathf.Max(0, r.width - list.layout.width - 4);
                    if (list.layout.yMax > r.height) list.style.top = Mathf.Max(0, r.height - list.layout.height - 4);
                });
                return list;
            }

            var root = BuildList(items, position, 0);
            open.Add(root);
            catcher.Add(root);
            _popupLayer.Add(catcher);
            _openMenu = catcher;
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
                case "jugar": if (IsPlaying) StopPlay(); else StartPlay(); break;
                case "probar_aqui":
                    if (IsPlaying) { StopPlay(); break; }
                    if (Maps?.Map == null) { Warn("Abre un mapa para probar desde él."); break; }
                    var c = Maps.Cursor ?? (Maps.Map.Width / 2, Maps.Map.Height / 2);
                    StartPlay(Maps.Map.Id, c.x, c.y);
                    break;
                case "depurador":
                    if (IsPlaying) _play.ToggleDebug();
                    else Info("El depurador se abre durante el juego (F9 mientras juegas).");
                    break;
                case "deshacer":
                    if (ActiveEditor == "retoque" && Pixels?.Image != null) Pixels.Undo();
                    else Maps?.Undo();
                    break;
                case "rehacer":
                    if (ActiveEditor == "retoque" && Pixels?.Image != null) Pixels.Redo();
                    else Maps?.Redo();
                    break;
                case "lapiz": Tool(MapTool.Pencil, PixelTool.Pencil); break;
                case "relleno": Tool(MapTool.Fill, PixelTool.Fill); break;
                case "rectangulo": Tool(MapTool.Rectangle, PixelTool.Rectangle); break;
                case "cuentagotas": Tool(MapTool.Picker, PixelTool.Picker); break;
                case "goma": Tool(MapTool.Eraser, PixelTool.Eraser); break;
                case "copiar": if (ActiveEditor == "mapa") Maps?.Copy(); break;
                case "cortar": if (ActiveEditor == "mapa") Maps?.Cut(); break;
                case "pegar": if (ActiveEditor == "mapa" && Maps?.Clipboard != null) Maps.BeginPaste(); break;
                case "borrar_seleccion": if (ActiveEditor == "mapa") Maps?.DeleteSelection(); break;
                case "capa_siguiente": if (Maps?.Map != null) Maps.SetActiveLayer(Maps.ActiveLayer + 1); break;
                case "capa_anterior": if (Maps?.Map != null) Maps.SetActiveLayer(Maps.ActiveLayer - 1); break;
                case "buscar": CommandPalette.Show(this); break;
                case "copia": MakeBackup("manual"); break;
                case "historial": HistoryDialog(); break;
                case "seleccion": MapOnly(MapTool.Select); break;
                case "inicio": MapOnly(MapTool.PlayerStart); break;
                case "zona": MapOnly(MapTool.EncounterPaint); break;
                case "puerta": MapOnly(MapTool.Door); break;
                case "pieza_guardar": if (Maps?.Map != null) { ActiveEditor = "mapa"; Maps.SavePieceFromSelection("Pieza " + (Maps.Pieces.Count + 1)); } break;
                case "piezas": PiecesDialog.Show(this); break;
                case "pinceles": BrushesDialog.Show(this); break;
                case "seleccionar_todo": if (Maps?.Map != null) { ActiveEditor = "mapa"; Maps.SelectAll(); } break;
                case "deseleccionar": Maps?.ClearSelection(); break;
                case "nuevo_mapa": if (HasProject) MapTreePanel.NewMapDialog(this, ""); break;
                case "atajos": SettingsDialog.Show(this, SettingsDialog.Tab.Shortcuts); break;
                case "entorno": SettingsDialog.Show(this); break;
                case "capa_nueva": if (Maps?.Map != null) Maps.AddLayer(); break;
                case "capa_ver":
                    if (Maps?.Map != null && Maps.ActiveLayer < Maps.Map.Layers.Count)
                        Maps.SetLayerView(Maps.ActiveLayer, visible: !Maps.Map.Layers[Maps.ActiveLayer].Visible);
                    break;
                case "capa_bloquear":
                    if (Maps?.Map != null && Maps.ActiveLayer < Maps.Map.Layers.Count)
                        Maps.SetLayerView(Maps.ActiveLayer, locked: !Maps.Map.Layers[Maps.ActiveLayer].Locked);
                    break;
                case "capas_auto": if (Maps != null) Maps.SetAutoLayers(!Maps.AutoLayers); break;
                case "voltear_h": if (Maps?.Map != null) { ActiveEditor = "mapa"; Maps.FlipStamp(true); } break;
                case "voltear_v": if (Maps?.Map != null) { ActiveEditor = "mapa"; Maps.FlipStamp(false); } break;
                case "girar": if (Maps?.Map != null) { ActiveEditor = "mapa"; Maps.RotateStamp(true); } break;
                case "girar_izq": if (Maps?.Map != null) { ActiveEditor = "mapa"; Maps.RotateStamp(false); } break;
                case var r when r.StartsWith("sello_"):
                    if (Maps?.Map != null && int.TryParse(r.Substring("sello_".Length), out var recall)) Maps.RecallStamp(recall);
                    break;
                case var g when g.StartsWith("guardar_sello_"):
                    if (Maps?.Map != null && int.TryParse(g.Substring("guardar_sello_".Length), out var store)) Maps.StoreStamp(store);
                    break;
                case var w when w.StartsWith("ventana_"):
                    var panel = w.Substring("ventana_".Length);
                    if (PanelCatalog.Find(panel) == null || !HasProject) break;
                    Workspace.Layout.Open(panel);
                    SetLayout(Workspace.Layout);
                    break;
                default:
                    ActionRequested?.Invoke(id);
                    break;
            }
        }

        private void MapOnly(MapTool tool)
        {
            if (Maps?.Map == null) return;
            ActiveEditor = "mapa";
            Maps.SetTool(tool);
        }

        private void Tool(MapTool map, PixelTool pixel)
        {
            if (ActiveEditor == "retoque" && Pixels?.Image != null) Pixels.SetTool(pixel);
            else Maps?.SetTool(map);
        }

        /// <summary>Actions that belong to a panel (tools, grid...): the panels listen here.</summary>
        public event Action<string> ActionRequested;

        /// <summary>Space is held (Space + drag = move around the map, as in Photoshop, Tiled or GB Studio).</summary>
        public bool SpaceHeld { get; private set; }

        private Action<string> _keyCapture;

        /// <summary>
        /// The next key combination goes to onKeys instead of running an action (recording a shortcut): the keys
        /// («Ctrl+Mayús+Z»), "" for Backspace (no shortcut) or null for Esc (cancel).
        /// </summary>
        public void CaptureKeys(Action<string> onKeys) => _keyCapture = onKeys;

        private static bool IsModifierKey(KeyCode k) =>
            k == KeyCode.LeftControl || k == KeyCode.RightControl || k == KeyCode.LeftShift || k == KeyCode.RightShift ||
            k == KeyCode.LeftAlt || k == KeyCode.RightAlt || k == KeyCode.LeftCommand || k == KeyCode.RightCommand ||
            k == KeyCode.AltGr;

        private static string KeysOf(KeyDownEvent e) =>
            ShortcutMap.Normalize((e.ctrlKey || e.commandKey ? "Ctrl+" : "") + (e.altKey ? "Alt+" : "") + (e.shiftKey ? "Mayús+" : "") + e.keyCode);

        private void OnKeyDown(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.None) return;
            if (_keyCapture != null)
            {
                if (IsModifierKey(e.keyCode)) return;
                var capture = _keyCapture;
                _keyCapture = null;
                e.StopPropagation();
                capture(e.keyCode == KeyCode.Escape ? null : e.keyCode == KeyCode.Backspace ? "" : KeysOf(e));
                return;
            }
            if (PlayHasKeys && e.keyCode != KeyCode.F5 && e.keyCode != KeyCode.F9 && e.keyCode != KeyCode.F11) return;
            // Ctrl + 0: interface back to 100 % (Ctrl + wheel changes it).
            if ((e.ctrlKey || e.commandKey) && (e.keyCode == KeyCode.Alpha0 || e.keyCode == KeyCode.Keypad0) && HasProject)
            {
                SetScale(1f);
                e.StopPropagation();
                return;
            }
            // Alt + 0: the window under the pointer back to 100 % (Alt + wheel changes it).
            if (e.altKey && (e.keyCode == KeyCode.Alpha0 || e.keyCode == KeyCode.Keypad0) && _dock != null)
            {
                _dock.SetScale(_dock.HoveredPanel, 1f);
                e.StopPropagation();
                return;
            }
            if (e.keyCode == KeyCode.Escape)
            {
                if (IsMenuOpen) { CloseMenu(); e.StopPropagation(); return; }
                if (_dialogs.Count > 0) { CloseDialog(_dialogs[_dialogs.Count - 1]); e.StopPropagation(); return; }
                // Esc also ends painting a zone, then clears the selection.
                if (Maps?.Tool == MapTool.EncounterPaint) { Maps.StopAreaPainting(); e.StopPropagation(); return; }
                if (Maps?.PendingDoor != null) { Maps.CancelPendingDoor(); e.StopPropagation(); return; }
                if (Maps?.Selection != null) { Maps.ClearSelection(); e.StopPropagation(); }
                return;
            }
            bool typing = e.target is VisualElement ve && (ve is TextField || ve.GetFirstAncestorOfType<TextField>() != null);
            bool modifier = e.ctrlKey || e.commandKey || e.altKey;
            bool functionKey = e.keyCode >= KeyCode.F1 && e.keyCode <= KeyCode.F15;
            if (typing && !modifier && !functionKey) return;
            if (_dialogs.Count > 0 && !functionKey) return;

            if (e.keyCode == KeyCode.Space && !modifier) { SpaceHeld = true; return; }
            var keys = KeysOf(e);
            var action = Workspace.Shortcuts.ActionFor(keys);
            if (action == null) return;
            e.StopPropagation();
            RunAction(action);
        }
    }
}
