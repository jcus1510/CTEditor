using System;
using System.Collections.Generic;
using System.Linq;
using CTEditor.Art.Domain;
using CTEditor.SharedKernel.Editing;

namespace CTEditor.World.Domain
{
    /// <summary>Un tramo colocado en el mundo: su mapa y su rectángulo en tiles.</summary>
    public readonly struct WorldSection
    {
        public readonly MapDefinition Map;
        public WorldSection(MapDefinition map) { Map = map; }
        public int X => Map.WorldX;
        public int Y => Map.WorldY;
        public int Right => Map.WorldX + Map.Width;
        public int Bottom => Map.WorldY + Map.Height;
        public bool Contains(int wx, int wy) => wx >= X && wy >= Y && wx < Right && wy < Bottom;
        public bool Overlaps(WorldSection o) => X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;
    }

    /// <summary>
    /// EL MUNDO CONTINUO: los tramos exteriores colocados (cada uno sigue siendo su propio mapa, fácil de delimitar y de
    /// mover). Dos tramos que se tocan quedan conectados: se pasa de uno a otro andando, sin cargas. Detecta los que se
    /// pisan (error) y calcula los vecinos.
    /// </summary>
    public sealed class WorldLayout
    {
        private readonly List<WorldSection> _sections;

        public WorldLayout(IEnumerable<MapDefinition> maps)
        {
            _sections = maps.Where(m => m != null && m.Kind == MapKind.Exterior && m.InWorld).Select(m => new WorldSection(m)).ToList();
        }

        public IReadOnlyList<WorldSection> Sections => _sections;

        /// <summary>El tramo que ocupa esa casilla del mundo (null si ninguno).</summary>
        public MapDefinition MapAt(int wx, int wy)
        {
            foreach (var s in _sections) if (s.Contains(wx, wy)) return s.Map;
            return null;
        }

        /// <summary>Tramos que se tocan con este por un borde (o se pisan).</summary>
        public IEnumerable<MapDefinition> Neighbors(MapDefinition map)
        {
            var me = new WorldSection(map);
            foreach (var s in _sections)
            {
                if (s.Map == map) continue;
                bool touchX = s.X <= me.Right && me.X <= s.Right, touchY = s.Y <= me.Bottom && me.Y <= s.Bottom;
                bool sharesEdge = (s.Right == me.X || me.Right == s.X) && s.Y < me.Bottom && me.Y < s.Bottom
                                  || (s.Bottom == me.Y || me.Bottom == s.Y) && s.X < me.Right && me.X < s.Right;
                if (touchX && touchY && (sharesEdge || s.Overlaps(me))) yield return s.Map;
            }
        }

        /// <summary>Parejas de tramos que se pisan (no se debe: una casilla del mundo sería de dos mapas).</summary>
        public IEnumerable<(MapDefinition a, MapDefinition b)> Overlaps()
        {
            for (int i = 0; i < _sections.Count; i++)
            for (int j = i + 1; j < _sections.Count; j++)
                if (_sections[i].Overlaps(_sections[j])) yield return (_sections[i].Map, _sections[j].Map);
        }

        /// <summary>Rectángulo que abarca todo el mundo (x, y, ancho, alto). (0,0,0,0) si está vacío.</summary>
        public (int x, int y, int w, int h) Bounds
        {
            get
            {
                if (_sections.Count == 0) return (0, 0, 0, 0);
                int x0 = _sections.Min(s => s.X), y0 = _sections.Min(s => s.Y);
                return (x0, y0, _sections.Max(s => s.Right) - x0, _sections.Max(s => s.Bottom) - y0);
            }
        }

        /// <summary>
        /// Andar desde (x, y) de 'from' hacia 'd' saliendo por su borde: el tramo y la casilla a la que se llega (false si
        /// no hay tramo al otro lado).
        /// </summary>
        public bool TryCross(MapDefinition from, int x, int y, FacingDirection d, out MapDefinition to, out int tx, out int ty)
        {
            var (nx, ny) = Passability.Step(x, y, d);
            to = null;
            tx = ty = 0;
            if (from.Contains(nx, ny) || !from.InWorld || from.Kind != MapKind.Exterior) return false;
            int wx = from.WorldX + nx, wy = from.WorldY + ny;
            to = MapAt(wx, wy);
            if (to == null || to == from) { to = null; return false; }
            tx = wx - to.WorldX;
            ty = wy - to.WorldY;
            return true;
        }

        /// <summary>
        /// Posición libre pegada a un tramo, por un lado ("arriba", "abajo", "izquierda", "derecha"), centrada con él.
        /// Si ese hueco está ocupado, se aparta hasta encontrar uno libre.
        /// </summary>
        public (int x, int y) PlaceNextTo(MapDefinition anchor, FacingDirection side, int width, int height)
        {
            int x, y;
            switch (side)
            {
                case FacingDirection.Up: x = anchor.WorldX + (anchor.Width - width) / 2; y = anchor.WorldY - height; break;
                case FacingDirection.Down: x = anchor.WorldX + (anchor.Width - width) / 2; y = anchor.WorldY + anchor.Height; break;
                case FacingDirection.Left: x = anchor.WorldX - width; y = anchor.WorldY + (anchor.Height - height) / 2; break;
                default: x = anchor.WorldX + anchor.Width; y = anchor.WorldY + (anchor.Height - height) / 2; break;
            }
            var (dx, dy) = side == FacingDirection.Up || side == FacingDirection.Down ? (1, 0) : (0, 1);
            for (int i = 0; i < 400; i++)
            {
                int k = (i + 1) / 2 * (i % 2 == 0 ? 1 : -1);
                int cx = x + dx * k, cy = y + dy * k;
                if (IsFree(cx, cy, width, height)) return (cx, cy);
            }
            return (x, y);
        }

        /// <summary>Un sitio libre a la derecha de todo el mundo (para colocar un tramo nuevo sin vecino).</summary>
        public (int x, int y) FreeSpot(int width, int height)
        {
            var (bx, by, bw, _) = Bounds;
            return _sections.Count == 0 ? (0, 0) : (bx + bw + 2, by);
        }

        public bool IsFree(int x, int y, int w, int h, MapDefinition ignore = null) =>
            _sections.All(s => s.Map == ignore || !(x < s.Right && s.X < x + w && y < s.Bottom && s.Y < y + h));
    }

    /// <summary>Mover un tramo en el mundo (deshacible).</summary>
    public sealed class MoveSectionCommand : IEditCommand
    {
        private readonly MapDefinition _map;
        private readonly int _fromX, _fromY, _toX, _toY;
        private readonly bool _wasInWorld;
        public string Label { get; }

        public MoveSectionCommand(MapDefinition map, int toX, int toY)
        {
            _map = map;
            (_fromX, _fromY, _wasInWorld) = (map.WorldX, map.WorldY, map.InWorld);
            (_toX, _toY) = (toX, toY);
            Label = "mover " + map.Name;
        }

        public void Do() { _map.WorldX = _toX; _map.WorldY = _toY; _map.InWorld = true; }
        public void Undo() { _map.WorldX = _fromX; _map.WorldY = _fromY; _map.InWorld = _wasInWorld; }
    }

    /// <summary>Una zona del mapa de la región: dónde queda un tramo en la imagen.</summary>
    public sealed class RegionMapArea
    {
        public string MapId { get; set; }
        public string Name { get; set; }
        public SectionCategory Category { get; set; }
        /// <summary>Rectángulo en píxeles de la imagen.</summary>
        public PixelRect Rect { get; set; }
    }

    /// <summary>Estilo del mapa de la región.</summary>
    public enum RegionMapStyle
    {
        /// <summary>Bloques de color por tipo de tramo (como el mapa del Pokégear).</summary>
        Schematic = 0,
        /// <summary>El mundo reducido con los colores reales de los tiles.</summary>
        Reduced = 1
    }

    /// <summary>
    /// Genera el MAPA DE LA REGIÓN a partir del mundo: una imagen (que se puede retocar) y dónde queda cada tramo (para
    /// el mapa del jugador: dónde estás, puntos de vuelo). Se adapta solo a lo que se va creando.
    /// </summary>
    public static class RegionMapBuilder
    {
        public static readonly IReadOnlyDictionary<SectionCategory, Rgba32> CategoryColors = new Dictionary<SectionCategory, Rgba32>
        {
            { SectionCategory.Town, new Rgba32(232, 96, 80) },
            { SectionCategory.City, new Rgba32(214, 70, 70) },
            { SectionCategory.Route, new Rgba32(236, 206, 120) },
            { SectionCategory.Forest, new Rgba32(70, 150, 80) },
            { SectionCategory.Cave, new Rgba32(140, 110, 90) },
            { SectionCategory.Water, new Rgba32(80, 140, 220) },
            { SectionCategory.Mountain, new Rgba32(160, 150, 140) },
            { SectionCategory.Building, new Rgba32(200, 200, 210) },
            { SectionCategory.Special, new Rgba32(180, 110, 220) },
        };

        /// <summary>
        /// 'tilesPerPixel' = cuántos tiles del mundo son un píxel (1 = un píxel por tile). 'colorAt' da el color medio de
        /// una casilla (solo para Reduced; null = se usa el del tipo de tramo). 'margin' = borde en píxeles.
        /// </summary>
        public static (PixelImage image, List<RegionMapArea> areas) Build(WorldLayout world, RegionMapStyle style, int tilesPerPixel = 1,
            Func<MapDefinition, int, int, Rgba32?> colorAt = null, int margin = 4, Rgba32? background = null)
        {
            tilesPerPixel = Math.Max(1, tilesPerPixel);
            var shown = world.Sections.Where(s => s.Map.ShowOnRegionMap).ToList();
            var (bx, by, bw, bh) = world.Bounds;
            int w = Math.Max(1, (bw + tilesPerPixel - 1) / tilesPerPixel + margin * 2);
            int h = Math.Max(1, (bh + tilesPerPixel - 1) / tilesPerPixel + margin * 2);
            var img = new PixelImage(w, h);
            var bg = background ?? new Rgba32(40, 90, 150);
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) img[x, y] = bg;
            var areas = new List<RegionMapArea>();
            foreach (var s in shown)
            {
                var m = s.Map;
                int px = (m.WorldX - bx) / tilesPerPixel + margin, py = (m.WorldY - by) / tilesPerPixel + margin;
                int pw = Math.Max(1, (m.Width + tilesPerPixel - 1) / tilesPerPixel), ph = Math.Max(1, (m.Height + tilesPerPixel - 1) / tilesPerPixel);
                var baseColor = CategoryColors.TryGetValue(m.Category, out var c) ? c : new Rgba32(200, 200, 200);
                var edge = new Rgba32((byte)(baseColor.R * 0.6), (byte)(baseColor.G * 0.6), (byte)(baseColor.B * 0.6));
                for (int y = 0; y < ph; y++)
                for (int x = 0; x < pw; x++)
                {
                    if (!img.Contains(px + x, py + y)) continue;
                    Rgba32 color = baseColor;
                    if (style == RegionMapStyle.Reduced && colorAt != null)
                        color = colorAt(m, Math.Min(m.Width - 1, x * tilesPerPixel), Math.Min(m.Height - 1, y * tilesPerPixel)) ?? baseColor;
                    else if (x == 0 || y == 0 || x == pw - 1 || y == ph - 1) color = edge;
                    img[px + x, py + y] = color;
                }
                areas.Add(new RegionMapArea { MapId = m.Id, Name = m.Name, Category = m.Category, Rect = new PixelRect(px, py, pw, ph) });
            }
            return (img, areas);
        }
    }
}
