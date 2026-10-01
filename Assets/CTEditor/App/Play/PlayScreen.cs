using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using CTEditor.Art.Domain;
using CTEditor.Project;
using CTEditor.World.Domain;

namespace CTEditor.App
{
    /// <summary>
    /// ▶ JUGAR dentro de la aplicación, sin compilar: el mapa con el mismo renderizador del editor, el jugador andando
    /// (OverworldSim: reglas de paso de RPG Maker XP) con su hoja de personaje, la cámara siguiéndole y la resolución del
    /// juego ampliada en múltiplos exactos (píxeles nítidos, como el juego exportado). Si una imagen cambia (retoque o
    /// programa externo) se recarga al momento. Esc vuelve al editor donde estaba; F9 abre el depurador.
    /// </summary>
    public sealed class PlayScreen : VisualElement, IDisposable
    {
        private static readonly string[] DirectionNames = { "abajo", "izquierda", "derecha", "arriba" };

        private readonly AppShell _shell;
        private readonly MapRenderer _renderer;
        private readonly OverworldSim _sim;
        private readonly Image _view;
        private readonly VisualElement _debug;
        private readonly Label _debugText;
        private readonly HashSet<KeyCode> _held = new HashSet<KeyCode>();
        private readonly List<FacingDirection> _pressedOrder = new List<FacingDirection>();
        private AtlasCache _atlases;
        private readonly Label _banner;
        private readonly System.Random _rng = new System.Random();
        private bool _encountersOn = true;
        private CharacterSprites _character;
        private SpriteRenderer _player;
        private bool _debugOn;
        private float _fps;
        private int _screenW, _screenH;

        public PlayScreen(AppShell shell, string mapId, int x, int y)
        {
            _shell = shell;
            var map = shell.Maps.Find(mapId) ?? throw new InvalidOperationException($"No existe el mapa «{mapId}».");
            var sets = shell.Maps.TilesetsOf(map);
            if (sets.IsEmpty) shell.Warn("El mapa no tiene un tileset cortado: se juega sin gráficos ni bloqueos.");
            _screenW = shell.Project.ScreenWidth;
            _screenH = shell.Project.ScreenHeight;

            _atlases = new AtlasCache(shell.ProjectRoot, shell.Maps.TilesetFor);
            _renderer = new MapRenderer("Juego", MapRenderer.PlayLayer, Color.black) { TileSize = shell.Project.TileSize };
            _renderer.Resize(_screenW, _screenH);
            _sim = new OverworldSim(map, sets, x, y) { World = shell.Maps.World(), TilesetsOf = shell.Maps.TilesetsOf };
            _sim.SectionChanged += (_, next) => { BuildSections(); ShowBanner(next.Name, 2200); };
            _sim.StepFinished += OnStep;
            BuildSections();

            var go = _renderer.CreateChild("Jugador");
            _player = go.AddComponent<SpriteRenderer>();
            _player.sortingOrder = MapRenderer.PlayerSortingOrder;
            LoadCharacter();

            // UI: the game image centred on black, scaled by a whole number.
            this.Fill();
            style.backgroundColor = Color.black;
            style.alignItems = Align.Center;
            style.justifyContent = Justify.Center;
            focusable = true;
            _view = new Image { image = _renderer.Target, scaleMode = ScaleMode.StretchToFill };
            _view.pickingMode = PickingMode.Ignore;
            Add(_view);
            RegisterCallback<GeometryChangedEvent>(_ => FitView());

            var hint = Ui.Text("Flechas o WASD: andar · Mayús o X: correr · F9: depurador · Esc: volver al editor", 0.95f);
            hint.style.color = Color.white;
            hint.style.backgroundColor = new Color(0, 0, 0, 0.6f);
            hint.Pad(10, 5).Round(4);
            hint.style.position = Position.Absolute;
            hint.style.left = 12;
            hint.style.top = 12;
            hint.pickingMode = PickingMode.Ignore;
            Add(hint);
            hint.schedule.Execute(() => hint.RemoveFromHierarchy()).ExecuteLater(5000);

            _debug = Ui.Column(4).Pad(12);
            _debug.style.position = Position.Absolute;
            _debug.style.right = 12;
            _debug.style.top = 12;
            _debug.style.backgroundColor = new Color(0, 0, 0, 0.75f);
            _debug.Round(6);
            _debugText = Ui.Text("", 0.95f, wrap: true);
            _debugText.style.color = Color.white;
            _debug.With(Ui.Text("Depurador (F9)", bold: true).Colored("acento"), _debugText,
                Ui.Check("Atravesar paredes", false, v => _sim.NoClip = v),
                Ui.Check("Encuentros salvajes", true, v => _encountersOn = v),
                Ui.Button("Volver al editor (Esc)", () => _shell.StopPlay()));
            _debug.Show(false);
            Add(_debug);

            _banner = Ui.Text("", 1.3f, bold: true);
            _banner.style.color = Color.white;
            _banner.style.backgroundColor = new Color(0.05f, 0.08f, 0.15f, 0.85f);
            _banner.Pad(18, 8).Round(6);
            _banner.style.position = Position.Absolute;
            _banner.style.top = 24;
            _banner.style.alignSelf = Align.Center;
            _banner.pickingMode = PickingMode.Ignore;
            _banner.Show(false);
            Add(_banner);
            ShowBanner(map.Name, 2200);

            RegisterCallback<KeyDownEvent>(OnKeyDown);
            RegisterCallback<KeyUpEvent>(OnKeyUp);
            RegisterCallback<BlurEvent>(_ => { _held.Clear(); _pressedOrder.Clear(); });
            shell.Ticked += Update;
            shell.AssetsChanged += ReloadGraphics;
            Update(0f);
        }

        private void FitView()
        {
            float ppp = Mathf.Max(0.01f, _shell.PixelsPerPoint);
            float availW = layout.width * ppp, availH = layout.height * ppp;
            if (availW <= 0 || availH <= 0) return;
            int factor = Mathf.Max(1, Mathf.FloorToInt(Mathf.Min(availW / _screenW, availH / _screenH)));
            _view.style.width = _screenW * factor / ppp;
            _view.style.height = _screenH * factor / ppp;
        }

        // ── Graphics ─────────────────────────────────────────────────────────────────────────────

        private void LoadCharacter()
        {
            _character?.Dispose();
            _character = CharacterSprites.ForPlayer(_shell.ProjectRoot, _shell.Project.PlayerCharacter, _shell.Project.TileSize);
        }

        private void ReloadGraphics()
        {
            _shell.Maps.ReloadTilesets();
            _atlases.Clear();
            _sim.SetMap(_sim.Map, _shell.Maps.TilesetsOf(_sim.Map), _sim.Player.X, _sim.Player.Y);
            BuildSections();
            LoadCharacter();
            _shell.Info("Gráficos recargados.");
        }

        // ── Input ────────────────────────────────────────────────────────────────────────────────

        private static FacingDirection? DirectionOf(KeyCode k) => k switch
        {
            KeyCode.DownArrow or KeyCode.S => FacingDirection.Down,
            KeyCode.UpArrow or KeyCode.W => FacingDirection.Up,
            KeyCode.LeftArrow or KeyCode.A => FacingDirection.Left,
            KeyCode.RightArrow or KeyCode.D => FacingDirection.Right,
            _ => (FacingDirection?)null,
        };

        private void OnKeyDown(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.None) return;
            if (e.keyCode == KeyCode.Escape) { _shell.StopPlay(); e.StopPropagation(); return; }
            if (e.keyCode == KeyCode.F9) { ToggleDebug(); e.StopPropagation(); return; }
            _held.Add(e.keyCode);
            var d = DirectionOf(e.keyCode);
            if (d.HasValue && !_pressedOrder.Contains(d.Value)) _pressedOrder.Add(d.Value);
            e.StopPropagation();
        }

        private void OnKeyUp(KeyUpEvent e)
        {
            _held.Remove(e.keyCode);
            var d = DirectionOf(e.keyCode);
            if (d.HasValue && !_held.Any(k => DirectionOf(k) == d)) _pressedOrder.Remove(d.Value);
        }

        public void ToggleDebug()
        {
            _debugOn = !_debugOn;
            _debug.Show(_debugOn);
        }

        // ── Loop ─────────────────────────────────────────────────────────────────────────────────

        private void Update(float dt)
        {
            if (panel == null) return;
            FacingDirection? dir = _pressedOrder.Count > 0 ? _pressedOrder[_pressedOrder.Count - 1] : (FacingDirection?)null;
            bool run = _held.Contains(KeyCode.LeftShift) || _held.Contains(KeyCode.RightShift) || _held.Contains(KeyCode.X);
            _sim.Update(Mathf.Min(dt, 0.1f), dir, run);

            var p = _sim.Player;
            const float r = 1f;
            if (_character != null)
            {
                _player.sprite = _character.Frame(p.Facing, p.WalkFrame(_character.CycleLength));
                _player.transform.localPosition = new Vector3(p.DrawX + 0.5f, -(p.DrawY + 1f) * r, 0f);
            }

            // Camera follows the player, clamped to the map (a map smaller than the screen stays centred).
            float ppu = _renderer.PixelsPerUnit;
            float halfW = _screenW / (2f * ppu), halfH = _screenH / (2f * ppu);
            float mapW = _sim.Map.Width, mapH = _sim.Map.Height * r;
            float cx = p.DrawX + 0.5f, cy = -(p.DrawY + 0.5f) * r;
            // In the continuous world the camera follows freely (neighbours are drawn); a lone map is clamped to its edges.
            if (!(_sim.Map.InWorld && _sim.Map.Kind == MapKind.Exterior))
            {
                cx = mapW <= halfW * 2 ? mapW / 2f : Mathf.Clamp(cx, halfW, mapW - halfW);
                cy = mapH <= halfH * 2 ? -mapH / 2f : Mathf.Clamp(cy, -mapH + halfH, -halfH);
            }
            _renderer.Center = new Vector2(cx, cy);
            _renderer.UpdateCamera();

            if (_debugOn)
            {
                if (dt > 0) _fps = Mathf.Lerp(_fps, 1f / dt, 0.1f);
                int tag = Passability.TerrainAt(_sim.Map, _sim.Tileset, p.X, p.Y);
                var free = new[] { FacingDirection.Up, FacingDirection.Down, FacingDirection.Left, FacingDirection.Right }
                    .Where(d => Passability.CanMove(_sim.Map, _sim.Tileset, p.X, p.Y, d)).Select(d => DirectionNames[(int)d]);
                _debugText.text = $"Mapa: {_sim.Map.Name} ({_sim.Map.Id})\nCasilla: {p.X}, {p.Y} · mira {DirectionNames[(int)p.Facing]}\n"
                                  + $"Terreno: {_shell.Maps.Terrains.LabelOf(tag)}\nSe puede ir: {string.Join(", ", free)}\n"
                                  + $"Pasos: {p.Steps} · {Mathf.RoundToInt(_fps)} fps";
            }
        }

        /// <summary>The open section in the middle and its world neighbours around it (the camera crosses borders smoothly).</summary>
        private void BuildSections()
        {
            var m = _sim.Map;
            var views = new List<SectionView> { new SectionView { Map = m, Atlases = _atlases.For(m), Sets = _shell.Maps.TilesetsOf(m) } };
            if (m.InWorld && m.Kind == MapKind.Exterior && _sim.World != null)
                foreach (var n in _sim.World.Neighbors(m))
                    views.Add(new SectionView
                    {
                        Map = n, Atlases = _atlases.For(n), Sets = _shell.Maps.TilesetsOf(n),
                        Offset = new Vector2Int(n.WorldX - m.WorldX, n.WorldY - m.WorldY),
                    });
            _renderer.SetSections(views);
        }

        /// <summary>A step finished: check the encounter methods for that terrain, like the original games.</summary>
        private void OnStep(int x, int y, int terrain)
        {
            if (!_encountersOn || _sim.Map.Encounters.Count == 0) return;
            var now = EncounterResolver.TimeAt(DateTime.Now.Hour);
            foreach (var method in EncounterResolver.StepMethods(_shell.Maps.Methods, terrain, false))
            {
                var result = EncounterResolver.Roll(_sim.Map, x, y, method, now, _ => false, n => _rng.Next(Math.Max(1, n)));
                if (result == null) continue;
                var name = _shell.Maps.Species?.All().FirstOrDefault(s => s.id == result.SpeciesId).name ?? result.SpeciesId;
                ShowBanner($"¡Un {name} salvaje (nv. {result.Level})!   [{method.Label} · {result.Area.Name}]", 1800);
                _shell.Info($"Encuentro: {name} nv. {result.Level} ({method.Label}, {result.Area.Name}). Los combates llegan en la fase 7.");
                return;
            }
        }

        private void ShowBanner(string text, int ms)
        {
            if (_banner == null) return;
            _banner.text = text;
            _banner.Show(true);
            _banner.schedule.Execute(() => { if (_banner.text == text) _banner.Show(false); }).ExecuteLater(ms);
        }

        public void Dispose()
        {
            _shell.Ticked -= Update;
            _shell.AssetsChanged -= ReloadGraphics;
            _character?.Dispose();
            _atlases.Dispose();
            _renderer.Dispose();
        }
    }

    /// <summary>Los fotogramas de una hoja de personaje como sprites (o un marcador de color si no hay hoja).</summary>
    public sealed class CharacterSprites : IDisposable
    {
        private readonly Dictionary<(FacingDirection, int), Sprite> _frames = new Dictionary<(FacingDirection, int), Sprite>();
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        private readonly CharacterSheetLayout _layout;
        private readonly Texture2D _texture;
        private readonly PixelImage _image;
        private readonly int _ppu;

        public int CycleLength => _layout?.WalkCycle.Count ?? 0;

        private CharacterSprites(PixelImage image, CharacterSheetLayout layout, int ppu)
        {
            _image = image;
            _layout = layout;
            _ppu = ppu;
            _texture = Textures.FromImage(image);
            _owned.Add(_texture);
        }

        /// <summary>The project's player sheet, or the first character sheet sliced with a layout, or a placeholder.</summary>
        public static CharacterSprites ForPlayer(string projectRoot, string preferred, int tileWidth)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(preferred)) candidates.Add(preferred);
            try
            {
                candidates.AddRange(AssetCatalog.Scan(projectRoot).Where(a => a.Kind == AssetKind.Character && a.IsSliced).Select(a => a.RelativePath));
            }
            catch (Exception) { /* no folder: placeholder */ }
            foreach (var rel in candidates.Distinct())
            {
                try
                {
                    var path = Path.Combine(projectRoot, rel);
                    var slice = SliceFile.LoadFor(path);
                    var layout = slice?.Slice.Layout;
                    if (layout == null) continue;
                    var img = Png.Read(path);
                    if (layout.SliceFor(img.Width, img.Height) == null) continue;
                    return new CharacterSprites(img, layout, tileWidth);
                }
                catch (Exception) { /* try the next one */ }
            }
            return Placeholder(tileWidth);
        }

        private static CharacterSprites Placeholder(int size)
        {
            var img = new PixelImage(size, size);
            var fill = new Rgba32(78, 140, 247);
            var edge = new Rgba32(20, 40, 90);
            for (int y = 2; y < size - 2; y++)
            for (int x = 3; x < size - 3; x++)
                img[x, y] = x == 3 || x == size - 4 || y == 2 || y == size - 3 ? edge : fill;
            return new CharacterSprites(img, null, size);
        }

        public Sprite Frame(FacingDirection facing, int walkStep)
        {
            var key = (facing, walkStep);
            if (_frames.TryGetValue(key, out var s)) return s;
            PixelRect r = _layout == null
                ? new PixelRect(0, 0, _image.Width, _image.Height)
                : _layout.FrameRect(_image.Width, _image.Height, facing, walkStep);
            var rect = new Rect(r.X, _image.Height - r.Y - r.Height, r.Width, r.Height);
            s = Sprite.Create(_texture, rect, new Vector2(0.5f, 0f), _ppu, 0, SpriteMeshType.FullRect);
            s.hideFlags = HideFlags.DontSave;
            _owned.Add(s);
            _frames[key] = s;
            return s;
        }

        public void Dispose()
        {
            foreach (var o in _owned) if (o != null) UnityEngine.Object.Destroy(o);
            _owned.Clear();
            _frames.Clear();
        }
    }
}
