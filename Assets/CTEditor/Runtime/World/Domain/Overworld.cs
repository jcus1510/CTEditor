using System;
using CTEditor.Art.Domain;

namespace CTEditor.World.Domain
{
    /// <summary>
    /// Reglas de paso como RPG Maker XP: para salir de una casilla y entrar en la siguiente se miran las capas de arriba
    /// abajo; un tile que bloquea esa dirección impide el paso; el primer tile «de suelo» (prioridad 0) que no bloquea lo
    /// permite. Los tiles con prioridad (por encima del jugador) no cuentan si no bloquean.
    /// </summary>
    public static class Passability
    {
        public static (int x, int y) Step(int x, int y, FacingDirection d) => d switch
        {
            FacingDirection.Down => (x, y + 1),
            FacingDirection.Up => (x, y - 1),
            FacingDirection.Left => (x - 1, y),
            _ => (x + 1, y),
        };

        public static FacingDirection Opposite(FacingDirection d) => d switch
        {
            FacingDirection.Down => FacingDirection.Up,
            FacingDirection.Up => FacingDirection.Down,
            FacingDirection.Left => FacingDirection.Right,
            _ => FacingDirection.Left,
        };

        public static bool CanMove(MapDefinition map, Tileset tileset, int x, int y, FacingDirection d)
        {
            var (nx, ny) = Step(x, y, d);
            if (!map.Contains(nx, ny)) return false;
            return Passable(map, tileset, x, y, PassageBlocks.Of(d)) && Passable(map, tileset, nx, ny, PassageBlocks.Of(Opposite(d)));
        }

        public static bool Passable(MapDefinition map, Tileset tileset, int x, int y, PassageBlock side)
        {
            if (!map.Contains(x, y)) return false;
            for (int i = map.Layers.Count - 1; i >= 0; i--)
            {
                int t = map.Layers[i].Get(x, y);
                if (t < 0 || tileset == null) continue;
                var p = tileset.Properties(t);
                if ((p.Blocked & side) != 0) return false;
                if (p.Priority == 0) return true;
            }
            return true;
        }

        /// <summary>Etiqueta de terreno de la casilla: la del tile más alto que tenga una (0 = ninguna).</summary>
        public static int TerrainAt(MapDefinition map, Tileset tileset, int x, int y)
        {
            if (tileset == null || !map.Contains(x, y)) return 0;
            for (int i = map.Layers.Count - 1; i >= 0; i--)
            {
                int t = map.Layers[i].Get(x, y);
                if (t < 0) continue;
                int tag = tileset.Properties(t).TerrainTag;
                if (tag != 0) return tag;
            }
            return 0;
        }

        /// <summary>¿Hay un tile «arbusto» (el personaje se ve medio hundido)?</summary>
        public static bool BushAt(MapDefinition map, Tileset tileset, int x, int y)
        {
            if (tileset == null || !map.Contains(x, y)) return false;
            for (int i = map.Layers.Count - 1; i >= 0; i--)
            {
                int t = map.Layers[i].Get(x, y);
                if (t >= 0 && tileset.Properties(t).Bush) return true;
            }
            return false;
        }
    }

    /// <summary>Alguien que anda por el mapa casilla a casilla (el jugador; luego, los NPC).</summary>
    public sealed class Walker
    {
        public int X { get; internal set; }
        public int Y { get; internal set; }
        public int FromX { get; internal set; }
        public int FromY { get; internal set; }
        public FacingDirection Facing { get; internal set; } = FacingDirection.Down;
        public bool Moving { get; internal set; }
        /// <summary>0 → 1 durante un paso.</summary>
        public float Progress { get; internal set; }
        public int Steps { get; internal set; }

        public Walker(int x, int y, FacingDirection facing = FacingDirection.Down)
        {
            X = FromX = x; Y = FromY = y; Facing = facing;
        }

        /// <summary>Posición para dibujar (en tiles, con decimales mientras anda).</summary>
        public float DrawX => Moving ? FromX + (X - FromX) * Progress : X;
        public float DrawY => Moving ? FromY + (Y - FromY) * Progress : Y;

        /// <summary>
        /// Índice en el ciclo de andar de la hoja (-1 = quieto). Alterna el pie en cada paso: con el ciclo de RPG Maker XP
        /// (0,1,2,3) sale 1,2 / 3,0; con el de VX (1,0,1,2) sale 0,1 / 2,1.
        /// </summary>
        public int WalkFrame(int cycleLength)
        {
            if (!Moving || cycleLength <= 0) return -1;
            int phase = (Steps % 2) * 2 + (Progress < 0.5f ? 0 : 1);
            return (phase + 1) % cycleLength;
        }

        public void Teleport(int x, int y)
        {
            X = FromX = x; Y = FromY = y;
            Moving = false;
            Progress = 0;
        }
    }

    /// <summary>
    /// El jugador andando por un mapa: cada paso es una casilla; la dirección pulsada gira y anda si se puede pasar (si no,
    /// solo gira). Correr = el doble de rápido. Sin Unity: se prueba con tests y el juego solo lo dibuja.
    /// </summary>
    public sealed class OverworldSim
    {
        public MapDefinition Map { get; private set; }
        public Tileset Tileset { get; private set; }
        public Walker Player { get; }
        /// <summary>Casillas por segundo.</summary>
        public float WalkSpeed { get; set; } = 4f;
        public float RunSpeed { get; set; } = 8f;
        /// <summary>Atravesar todo (depurador).</summary>
        public bool NoClip { get; set; }

        /// <summary>Terminó un paso en (x, y) con esa etiqueta de terreno.</summary>
        public event Action<int, int, int> StepFinished;
        /// <summary>Intentó andar hacia algo que no se puede pasar.</summary>
        public event Action<FacingDirection> Bumped;

        public OverworldSim(MapDefinition map, Tileset tileset, int x, int y, FacingDirection facing = FacingDirection.Down)
        {
            Map = map ?? throw new ArgumentNullException(nameof(map));
            Tileset = tileset;
            Player = new Walker(Math.Max(0, Math.Min(map.Width - 1, x)), Math.Max(0, Math.Min(map.Height - 1, y)), facing);
        }

        /// <summary>Cambia de mapa (teletransporte) o recarga el mismo tras editarlo.</summary>
        public void SetMap(MapDefinition map, Tileset tileset, int x, int y)
        {
            Map = map;
            Tileset = tileset;
            Player.Teleport(Math.Max(0, Math.Min(map.Width - 1, x)), Math.Max(0, Math.Min(map.Height - 1, y)));
        }

        public void Update(float dt, FacingDirection? input, bool run)
        {
            float speed = run ? RunSpeed : WalkSpeed;
            var p = Player;
            float leftover = 0f;
            if (p.Moving)
            {
                p.Progress += dt * speed;
                if (p.Progress < 1f) return;
                leftover = (p.Progress - 1f) / speed;
                p.Moving = false;
                p.Progress = 0f;
                p.FromX = p.X;
                p.FromY = p.Y;
                p.Steps++;
                StepFinished?.Invoke(p.X, p.Y, Passability.TerrainAt(Map, Tileset, p.X, p.Y));
                if (input == null) return;
            }
            if (input == null) return;
            p.Facing = input.Value;
            if (NoClip ? Map.Contains(Passability.Step(p.X, p.Y, input.Value).x, Passability.Step(p.X, p.Y, input.Value).y)
                       : Passability.CanMove(Map, Tileset, p.X, p.Y, input.Value))
            {
                var (nx, ny) = Passability.Step(p.X, p.Y, input.Value);
                p.FromX = p.X;
                p.FromY = p.Y;
                p.X = nx;
                p.Y = ny;
                p.Moving = true;
                p.Progress = Math.Min(0.99f, leftover * speed);
            }
            else Bumped?.Invoke(input.Value);
        }
    }
}
