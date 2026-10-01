using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using CTEditor.Art.Domain;
using CTEditor.Editing;
using CTEditor.Project;
using CTEditor.World.Domain;

namespace CTEditor.App
{
    /// <summary>
    /// Panel PROPIEDADES del mapa abierto: nombre, tamaño (con ancla), tileset, música, bici, exterior; el inicio del
    /// jugador y su personaje. Todo pasa por la sesión (se deshace con Ctrl+Z y se guarda solo).
    /// </summary>
    public sealed class InspectorPanel : VisualElement
    {
        private readonly AppShell _shell;
        private MapEditorSession S => _shell.Maps;
        private readonly ScrollView _scroll;
        private float _anchorX, _anchorY;
        private int _newW, _newH;

        public InspectorPanel(AppShell shell)
        {
            _shell = shell;
            style.flexGrow = 1;
            _scroll = Ui.Scroll().Grow();
            Add(_scroll);
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                if (S == null) return;
                S.MapOpened += Refresh;
                S.StructureChanged += Refresh;
                S.PlayerStartChanged += Refresh;
                S.WorldChanged += Refresh;
                _shell.AssetsChanged += Refresh;
                Refresh();
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (S == null) return;
                S.MapOpened -= Refresh;
                S.StructureChanged -= Refresh;
                S.PlayerStartChanged -= Refresh;
                S.WorldChanged -= Refresh;
                _shell.AssetsChanged -= Refresh;
            });
        }

        private static VisualElement Section(string title)
        {
            var box = Ui.Column(8).Pad(12, 10);
            box.Add(Ui.Heading(title));
            return box;
        }

        private void Refresh()
        {
            _scroll.Clear();
            var body = Ui.Column(0);
            _scroll.Add(body);
            var m = S?.Map;
            if (m == null)
            {
                body.Add(Ui.Hint("Abre un mapa para ver sus propiedades.").Margin(12, 10, 12, 10));
                body.Add(PlayerSection());
                return;
            }
            _newW = m.Width;
            _newH = m.Height;

            var map = Section("Mapa");
            map.With(Ui.TextBox("Nombre", m.Name, v => S.RenameMap(m.Id, v), delayed: true),
                Ui.Text("Id: " + m.Id, 0.9f, dim: true));
            body.Add(map);
            body.Add(Ui.Separator());

            var size = Section("Tamaño");
            var wh = Ui.Row(10);
            wh.With(Ui.NumberBox("Ancho", m.Width, 1, MapDefinition.MaxSize, v => _newW = v), Ui.NumberBox("Alto", m.Height, 1, MapDefinition.MaxSize, v => _newH = v));
            var ax = Ui.Row(4);
            ax.With(Ui.Text("Añadir o quitar por:", 0.9f, dim: true),
                Ui.Chip("derecha", _anchorX == 0f, () => { _anchorX = 0f; Refresh(); }),
                Ui.Chip("los dos lados", _anchorX == 0.5f, () => { _anchorX = 0.5f; Refresh(); }),
                Ui.Chip("izquierda", _anchorX == 1f, () => { _anchorX = 1f; Refresh(); }));
            var ay = Ui.Row(4);
            ay.With(Ui.Text("y por:", 0.9f, dim: true),
                Ui.Chip("abajo", _anchorY == 0f, () => { _anchorY = 0f; Refresh(); }),
                Ui.Chip("los dos lados", _anchorY == 0.5f, () => { _anchorY = 0.5f; Refresh(); }),
                Ui.Chip("arriba", _anchorY == 1f, () => { _anchorY = 1f; Refresh(); }));
            size.With(wh, ax, ay, Ui.Button("Aplicar el tamaño", () => S.ResizeMap(_newW, _newH, _anchorX, _anchorY), Ui.ButtonKind.Primary));
            body.Add(size);
            body.Add(Ui.Separator());

            // The section: kind, category and its place in the continuous world.
            var sec = Section("Tramo");
            var kinds = Ui.Row(4);
            kinds.With(Ui.Chip("Exterior", m.Kind == MapKind.Exterior, () => S.SetMapProperties(kind: MapKind.Exterior)),
                Ui.Chip("Interior", m.Kind == MapKind.Interior, () => S.SetMapProperties(kind: MapKind.Interior)));
            var cats = Ui.Row(4);
            cats.style.flexWrap = Wrap.Wrap;
            for (int i = 0; i < WorldPanel.CategoryNames.Length; i++)
            {
                var c = (SectionCategory)i;
                cats.Add(Ui.Chip(WorldPanel.CategoryNames[i], m.Category == c, () => S.SetMapProperties(category: c)).Margin(0, 0, 4, 4));
            }
            sec.With(kinds, cats, Ui.Check("Sale en el mapa de la región", m.ShowOnRegionMap, v => S.SetMapProperties(showOnRegionMap: v)));
            if (m.Kind == MapKind.Exterior)
            {
                if (m.InWorld)
                {
                    int wx = m.WorldX, wy = m.WorldY;
                    var pos = Ui.Row(8);
                    pos.With(Ui.NumberBox("Mundo X", wx, -100000, 100000, v => wx = v), Ui.NumberBox("Y", wy, -100000, 100000, v => wy = v));
                    var acts = Ui.Row(6);
                    acts.With(Ui.Button("Mover", () => S.MoveSection(m.Id, wx, wy), Ui.ButtonKind.Primary, "También se puede arrastrar en el panel Mundo"),
                        Ui.Button("Quitar del mundo", () => S.RemoveFromWorld(m.Id), Ui.ButtonKind.Flat));
                    sec.With(pos, acts);
                }
                else sec.Add(Ui.Button("Colocar en el mundo", () =>
                {
                    var (x, y) = S.World().FreeSpot(m.Width, m.Height);
                    S.MoveSection(m.Id, x, y);
                }, Ui.ButtonKind.Primary, "Luego se puede arrastrar en el panel Mundo"));
            }
            body.Add(sec);
            body.Add(Ui.Separator());

            // Tilesets of the map: the first is the main one; Tiles can add more (each cell remembers its tileset).
            var ts = Section("Tilesets del mapa");
            var list = Ui.Column(4);
            for (int i = 0; i < m.TilesetIds.Count; i++)
            {
                int slot = i;
                var t = S.TilesetFor(m.TilesetIds[i]);
                var row = Ui.Row(6);
                row.With(Ui.Text((i == 0 ? "Principal: " : $"{i + 1}. ") + (t?.Name ?? m.TilesetIds[i] + " (no está: ¿se borró o no está cortado?)"))
                    .Colored(t == null ? "aviso" : "texto").Grow());
                if (i > 0 && i == m.TilesetIds.Count - 1) row.Add(Ui.Button("Quitar", () => S.RemoveTilesetFromMap(slot), Ui.ButtonKind.Flat, "Solo si ningún tile lo usa"));
                list.Add(row);
            }
            var available = S.AvailableTilesets();
            if (available.Count == 0) list.Add(Ui.Hint("No hay tilesets cortados. Corta una imagen de graficos/tilesets en Recursos."));
            else
            {
                list.Add(Ui.Text("Cambiar el principal:", 0.9f, dim: true));
                var chips = Ui.Row(4);
                chips.style.flexWrap = Wrap.Wrap;
                foreach (var t in available)
                {
                    var id = t;
                    chips.Add(Ui.Chip(S.TilesetFor(t)?.Name ?? t, m.TilesetId == t, () => S.SetMapTileset(id), t).Margin(0, 0, 4, 4));
                }
                list.Add(chips);
                list.Add(Ui.Hint("Para usar varios tilesets a la vez, añádelos en el panel Tiles con «+ Tileset»."));
            }
            ts.Add(list);
            body.Add(ts);
            body.Add(Ui.Separator());

            var props = Section("Ambiente");
            props.With(Ui.TextBox("Música", m.Music, v => S.SetMapProperties(music: v ?? ""), delayed: true),
                Ui.Hint("Nombre del archivo en audio/musica (el sonido llegará con el motor del juego)."),
                Ui.TextBox("Clima", m.Weather, v => S.SetMapProperties(weather: (v ?? "").Trim()), delayed: true),
                Ui.Hint("Id del clima (lluvia, nieve...) de la base de datos; vacío = despejado."),
                Ui.Check("Se puede usar la bici", m.Bicycle, v => S.SetMapProperties(bicycle: v)),
                Ui.Check("Exterior (se puede volar; afecta a la hora del día)", m.Outdoor, v => S.SetMapProperties(outdoor: v)));
            body.Add(props);
            body.Add(Ui.Separator());
            body.Add(PlayerSection());
        }

        private VisualElement PlayerSection()
        {
            var sec = Section("Jugador");
            if (S == null) return sec;
            var (map, x, y) = S.PlayerStart;
            string where = string.IsNullOrEmpty(map) ? "sin poner" : $"{S.Tree.Find(map)?.Name ?? map} ({x}, {y})";
            sec.With(Ui.Text("Inicio: " + where),
                Ui.Button("Colocar el inicio en el mapa", () => { _shell.ActiveEditor = "mapa"; S.SetTool(MapTool.PlayerStart); }, Ui.ButtonKind.Normal,
                    "Herramienta «Inicio»: haz clic en la casilla del mapa"),
                Ui.Button("Jugar desde el inicio", () => _shell.StartPlay(), Ui.ButtonKind.Primary));

            sec.Add(Ui.Text("Personaje", bold: true));
            var chars = Ui.Column(4);
            var sheets = AssetCatalog.Scan(_shell.ProjectRoot).Where(a => a.Kind == AssetKind.Character && a.IsSliced).ToList();
            if (sheets.Count == 0) chars.Add(Ui.Hint("Corta una hoja de personaje (graficos/personajes) en Recursos. Mientras, se usa un cuadrado de color."));
            string current = _shell.Project.PlayerCharacter;
            foreach (var a in sheets)
            {
                var rel = a.RelativePath;
                bool isCurrent = current == rel || (string.IsNullOrEmpty(current) && a == sheets[0]);
                var chip = Ui.Chip(a.Name, isCurrent, () =>
                {
                    _shell.Project.PlayerCharacter = rel;
                    try { ProjectFile.Save(_shell.ProjectRoot, _shell.Project); }
                    catch (System.Exception e) { _shell.Error("No se pudo guardar: " + e.Message); }
                    Refresh();
                }, rel);
                chip.style.alignSelf = Align.FlexStart;
                chars.Add(chip);
            }
            sec.Add(chars);
            return sec;
        }
    }
}
