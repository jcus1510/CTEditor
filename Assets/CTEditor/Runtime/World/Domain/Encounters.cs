using System;
using System.Collections.Generic;
using System.Linq;

namespace CTEditor.World.Domain
{
    /// <summary>Cuándo se comprueba un método de encuentro.</summary>
    public enum EncounterTrigger
    {
        /// <summary>Al dar un paso sobre ciertos terrenos (hierba, agua...).</summary>
        StepOnTerrain = 0,
        /// <summary>Al dar un paso en cualquier casilla de la zona (cuevas, dentro de la zona pintada).</summary>
        StepAnywhere = 1,
        /// <summary>Al usar un objeto (cañas de pescar...), mirando a ciertos terrenos si se indican.</summary>
        UseItem = 2,
        /// <summary>Al interactuar con algo (golpe cabeza en un árbol, golpe roca...).</summary>
        Interact = 3,
        /// <summary>Solo cuando lo pide un evento (lo que el autor quiera: «volando», «en el sueño»...).</summary>
        Script = 4
    }

    /// <summary>Momento del día (combinables).</summary>
    [Flags]
    public enum TimeOfDay
    {
        Any = 0,
        Morning = 1,
        Day = 2,
        Evening = 4,
        Night = 8,
        /// <summary>Las cuatro a la vez (para la interfaz; en datos se guarda como Any).</summary>
        AllDay = Morning | Day | Evening | Night
    }

    /// <summary>
    /// Una forma de encontrar monstruos: hierba, cueva, surf, buceo, cañas, golpe cabeza... o la que invente el autor
    /// («volando»). Id fijo; cada tabla de encuentros dice a qué método pertenece.
    /// </summary>
    public sealed class EncounterMethod
    {
        public string Id { get; }
        public string Label { get; set; }
        public EncounterTrigger Trigger { get; set; }
        /// <summary>Terrenos donde funciona (StepOnTerrain; en UseItem, a los que hay que mirar). Vacío = cualquiera.</summary>
        public List<int> TerrainTags { get; } = new List<int>();
        /// <summary>Objeto que lo activa (UseItem): «cana_vieja»...</summary>
        public string ItemId { get; set; } = "";
        /// <summary>Probabilidad por defecto (%) de que salga algo cada vez que se comprueba.</summary>
        public int DefaultRate { get; set; } = 10;
        /// <summary>Viene de fábrica (no se puede borrar, sí cambiar).</summary>
        public bool BuiltIn { get; set; }

        public EncounterMethod(string id, string label, EncounterTrigger trigger, int defaultRate, params int[] terrains)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("El método necesita un id.", nameof(id));
            Id = id; Label = label ?? id; Trigger = trigger; DefaultRate = defaultRate;
            TerrainTags.AddRange(terrains ?? new int[0]);
        }

        public bool WorksOn(int terrainTag) => TerrainTags.Count == 0 || TerrainTags.Contains(terrainTag);
    }

    /// <summary>Los métodos del proyecto: los clásicos de fábrica más los que añada el autor.</summary>
    public sealed class EncounterMethodCatalog
    {
        private readonly List<EncounterMethod> _methods = new List<EncounterMethod>();

        public IReadOnlyList<EncounterMethod> All => _methods;
        public EncounterMethod Find(string id) => _methods.FirstOrDefault(m => m.Id == id);

        public void Add(EncounterMethod m)
        {
            if (m == null) throw new ArgumentNullException(nameof(m));
            _methods.RemoveAll(x => x.Id == m.Id);
            _methods.Add(m);
        }

        public bool Remove(string id)
        {
            var m = Find(id);
            if (m == null || m.BuiltIn) return false;
            return _methods.Remove(m);
        }

        /// <summary>Los métodos de los juegos originales (terrenos con los números de Essentials).</summary>
        public static EncounterMethodCatalog Classic()
        {
            var c = new EncounterMethodCatalog();
            void B(EncounterMethod m) { m.BuiltIn = true; c.Add(m); }
            B(new EncounterMethod("hierba", "Hierba", EncounterTrigger.StepOnTerrain, 10, 2, 10, 14));
            B(new EncounterMethod("hierba_alta", "Hierba alta (doble)", EncounterTrigger.StepOnTerrain, 12, 10));
            B(new EncounterMethod("cueva", "Cueva (cualquier paso)", EncounterTrigger.StepAnywhere, 8));
            B(new EncounterMethod("surf", "Surf", EncounterTrigger.StepOnTerrain, 8, 5, 6, 7, 9));
            B(new EncounterMethod("buceo", "Buceo", EncounterTrigger.StepOnTerrain, 8, 11));
            B(new EncounterMethod("cana_vieja", "Caña vieja", EncounterTrigger.UseItem, 100, 5, 6, 7, 9) { ItemId = "old_rod" });
            B(new EncounterMethod("cana_buena", "Caña buena", EncounterTrigger.UseItem, 100, 5, 6, 7, 9) { ItemId = "good_rod" });
            B(new EncounterMethod("supercana", "Supercaña", EncounterTrigger.UseItem, 100, 5, 6, 7, 9) { ItemId = "super_rod" });
            B(new EncounterMethod("golpe_cabeza", "Golpe cabeza (árboles)", EncounterTrigger.Interact, 50));
            B(new EncounterMethod("golpe_roca", "Golpe roca", EncounterTrigger.Interact, 25));
            return c;
        }
    }

    /// <summary>Una especie de una tabla: niveles, peso (frecuencia) y cuándo puede salir.</summary>
    public sealed class EncounterSlot
    {
        public string SpeciesId { get; set; }
        public int MinLevel { get; set; }
        public int MaxLevel { get; set; }
        /// <summary>Peso relativo. En las tablas clásicas son porcentajes (20, 20, 10...); da igual que no sumen 100.</summary>
        public int Weight { get; set; }
        /// <summary>Momentos del día en que sale (Any = siempre).</summary>
        public TimeOfDay Times { get; set; }
        /// <summary>Interruptor que tiene que estar activo (vacío = ninguno): «tras vencer a la liga»...</summary>
        public string RequiredFlag { get; set; } = "";

        public EncounterSlot(string speciesId, int minLevel, int maxLevel, int weight, TimeOfDay times = TimeOfDay.Any)
        {
            SpeciesId = speciesId ?? "";
            MinLevel = Math.Max(1, Math.Min(minLevel, maxLevel));
            MaxLevel = Math.Max(MinLevel, Math.Max(minLevel, maxLevel));
            Weight = Math.Max(0, weight);
            Times = times;
        }

        public bool AvailableAt(TimeOfDay now, Func<string, bool> flag) =>
            (Times == TimeOfDay.Any || (Times & now) != 0) && (string.IsNullOrEmpty(RequiredFlag) || (flag?.Invoke(RequiredFlag) ?? false));

        public EncounterSlot Clone() => new EncounterSlot(SpeciesId, MinLevel, MaxLevel, Weight, Times) { RequiredFlag = RequiredFlag };
    }

    /// <summary>Lo que sale con un método en una zona: la probabilidad de cada comprobación y las especies.</summary>
    public sealed class EncounterTable
    {
        public string MethodId { get; set; }
        /// <summary>Probabilidad (%) de que salga algo; -1 = la del método.</summary>
        public int Rate { get; set; } = -1;
        public List<EncounterSlot> Slots { get; } = new List<EncounterSlot>();

        public EncounterTable(string methodId) { MethodId = methodId; }

        public int RateWith(EncounterMethod m) => Rate >= 0 ? Rate : m?.DefaultRate ?? 10;

        /// <summary>% real de cada especie ahora (con las que no salen a esta hora o sin su interruptor quitadas).</summary>
        public IReadOnlyList<(EncounterSlot slot, double percent)> Chances(TimeOfDay now, Func<string, bool> flag)
        {
            var list = Slots.Where(s => s.Weight > 0 && s.AvailableAt(now, flag)).ToList();
            double total = list.Sum(s => s.Weight);
            return list.Select(s => (s, total <= 0 ? 0 : s.Weight * 100.0 / total)).ToList();
        }

        /// <summary>% de cada especie sin mirar la hora ni los interruptores (vista «todas las horas»).</summary>
        public IReadOnlyList<(EncounterSlot slot, double percent)> BaseChances()
        {
            var list = Slots.Where(s => s.Weight > 0).ToList();
            double total = list.Sum(s => s.Weight);
            return list.Select(s => (s, total <= 0 ? 0 : s.Weight * 100.0 / total)).ToList();
        }

        public EncounterTable Clone()
        {
            var t = new EncounterTable(MethodId) { Rate = Rate };
            t.Slots.AddRange(Slots.Select(s => s.Clone()));
            return t;
        }

        /// <summary>Pesos de la tabla clásica de hierba de los juegos originales (12 huecos).</summary>
        public static readonly int[] ClassicGrassWeights = { 20, 20, 10, 10, 10, 10, 5, 5, 4, 4, 1, 1 };
        public static readonly int[] ClassicWaterWeights = { 60, 30, 5, 4, 1 };
        public static readonly int[] ClassicRodWeights = { 70, 30 };
    }

    /// <summary>
    /// Una zona de encuentros del tramo: TODO el mapa o solo las casillas pintadas (un trozo de hierba con otras
    /// especies, el lago...). Las zonas pintadas mandan sobre la de todo el mapa para el mismo método; si varias
    /// pintadas se pisan, gana la más pequeña (la más concreta).
    /// </summary>
    public sealed class EncounterArea
    {
        private HashSet<(int x, int y)> _cells = new HashSet<(int, int)>();

        public string Id { get; }
        public string Name { get; set; }
        public bool WholeMap { get; set; }
        /// <summary>Color para verla en el editor («#RRGGBB»).</summary>
        public string Color { get; set; } = "#E0B34A";
        public List<EncounterTable> Tables { get; } = new List<EncounterTable>();

        public EncounterArea(string id, string name, bool wholeMap = false)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("La zona necesita un id.", nameof(id));
            Id = id; Name = name ?? id; WholeMap = wholeMap;
        }

        public IReadOnlyCollection<(int x, int y)> Cells => _cells;
        public int CellCount => WholeMap ? int.MaxValue : _cells.Count;
        public bool Contains(int x, int y) => WholeMap || _cells.Contains((x, y));
        public bool Paint(int x, int y) => _cells.Add((x, y));
        public bool Erase(int x, int y) => _cells.Remove((x, y));
        public void SetCells(IEnumerable<(int x, int y)> cells) => _cells = new HashSet<(int, int)>(cells);

        public EncounterTable TableFor(string methodId) => Tables.FirstOrDefault(t => t.MethodId == methodId);

        public EncounterTable GetOrAddTable(string methodId)
        {
            var t = TableFor(methodId);
            if (t == null) { t = new EncounterTable(methodId); Tables.Add(t); }
            return t;
        }

        /// <summary>Moves the painted cells (the map was resized with an anchor) and drops the ones left outside.</summary>
        public void Shift(int dx, int dy, int width, int height)
        {
            _cells = new HashSet<(int, int)>(_cells.Select(c => (c.x + dx, c.y + dy)).Where(c => c.Item1 >= 0 && c.Item2 >= 0 && c.Item1 < width && c.Item2 < height));
        }

        /// <summary>The painted cells as rectangles (row runs merged downwards): compact to save and draw.</summary>
        public List<(int x, int y, int w, int h)> ToRectangles()
        {
            var rows = _cells.GroupBy(c => c.y).OrderBy(g => g.Key);
            var runs = new List<(int x, int y, int w, int h)>();
            foreach (var row in rows)
            {
                var xs = row.Select(c => c.x).OrderBy(x => x).ToList();
                int start = xs[0], prev = xs[0];
                for (int i = 1; i <= xs.Count; i++)
                {
                    if (i < xs.Count && xs[i] == prev + 1) { prev = xs[i]; continue; }
                    runs.Add((start, row.Key, prev - start + 1, 1));
                    if (i < xs.Count) start = prev = xs[i];
                }
            }
            // Merge a run into the one just above when they have the same x and width.
            var merged = new List<(int x, int y, int w, int h)>();
            foreach (var r in runs)
            {
                int i = merged.FindIndex(m => m.x == r.x && m.w == r.w && m.y + m.h == r.y);
                if (i >= 0) merged[i] = (merged[i].x, merged[i].y, merged[i].w, merged[i].h + 1);
                else merged.Add(r);
            }
            return merged;
        }

        public void SetRectangles(IEnumerable<(int x, int y, int w, int h)> rects)
        {
            _cells.Clear();
            foreach (var (x, y, w, h) in rects)
                for (int yy = y; yy < y + h; yy++)
                for (int xx = x; xx < x + w; xx++)
                    _cells.Add((xx, yy));
        }

        public EncounterArea Clone()
        {
            var a = new EncounterArea(Id, Name, WholeMap) { Color = Color };
            a._cells = new HashSet<(int, int)>(_cells);
            a.Tables.AddRange(Tables.Select(t => t.Clone()));
            return a;
        }
    }

    /// <summary>Lo que salió: especie, nivel y de qué zona y método.</summary>
    public sealed class EncounterResult
    {
        public string SpeciesId { get; }
        public int Level { get; }
        public EncounterArea Area { get; }
        public EncounterMethod Method { get; }

        public EncounterResult(string speciesId, int level, EncounterArea area, EncounterMethod method)
        {
            SpeciesId = speciesId; Level = level; Area = area; Method = method;
        }
    }

    /// <summary>
    /// Decide si sale un monstruo salvaje y cuál, como en los juegos originales: la zona que manda en la casilla (la
    /// pintada más concreta o la de todo el mapa), su tabla para el método, la probabilidad de la tabla y un sorteo por
    /// pesos entre las especies disponibles a esa hora y con sus interruptores. El azar lo pone quien llama (tests).
    /// </summary>
    public static class EncounterResolver
    {
        /// <summary>La zona y la tabla que mandan en (x, y) para ese método (null si ninguna).</summary>
        public static (EncounterArea area, EncounterTable table)? Find(MapDefinition map, int x, int y, string methodId)
        {
            (EncounterArea, EncounterTable)? best = null;
            int bestSize = int.MaxValue;
            foreach (var a in map.Encounters)
            {
                var t = a.TableFor(methodId);
                if (t == null || t.Slots.Count == 0 || !a.Contains(x, y)) continue;
                int size = a.CellCount;
                if (best == null || size < bestSize) { best = (a, t); bestSize = size; }
            }
            return best;
        }

        /// <summary>Los métodos que se comprueban al pisar (x, y) con ese terreno.</summary>
        public static IEnumerable<EncounterMethod> StepMethods(EncounterMethodCatalog methods, int terrainTag, bool surfing)
        {
            foreach (var m in methods.All)
            {
                if (m.Trigger == EncounterTrigger.StepAnywhere) { yield return m; continue; }
                if (m.Trigger != EncounterTrigger.StepOnTerrain || terrainTag == 0 && m.TerrainTags.Count > 0) continue;
                if (m.WorksOn(terrainTag)) yield return m;
            }
        }

        /// <summary>
        /// Una comprobación. 'rng(n)' devuelve 0..n-1. Devuelve null si no sale nada (falló la probabilidad o no hay
        /// especies disponibles).
        /// </summary>
        public static EncounterResult Roll(MapDefinition map, int x, int y, EncounterMethod method, TimeOfDay now,
            Func<string, bool> flag, Func<int, int> rng)
        {
            var found = Find(map, x, y, method.Id);
            if (found == null) return null;
            var (area, table) = found.Value;
            if (rng(100) >= table.RateWith(method)) return null;
            var slots = table.Slots.Where(s => s.Weight > 0 && s.AvailableAt(now, flag)).ToList();
            int total = slots.Sum(s => s.Weight);
            if (total <= 0) return null;
            int roll = rng(total), acc = 0;
            foreach (var s in slots)
            {
                acc += s.Weight;
                if (roll >= acc) continue;
                int level = s.MinLevel + rng(s.MaxLevel - s.MinLevel + 1);
                return new EncounterResult(s.SpeciesId, level, area, method);
            }
            return null;
        }

        /// <summary>Momento del día de una hora (0-23): mañana 4-9, día 10-16, tarde 17-19, noche 20-3.</summary>
        public static TimeOfDay TimeAt(int hour)
        {
            hour = ((hour % 24) + 24) % 24;
            if (hour >= 4 && hour < 10) return TimeOfDay.Morning;
            if (hour >= 10 && hour < 17) return TimeOfDay.Day;
            if (hour >= 17 && hour < 20) return TimeOfDay.Evening;
            return TimeOfDay.Night;
        }
    }

    /// <summary>Dónde se guardan los métodos de encuentro del proyecto.</summary>
    public interface IEncounterMethodRepository
    {
        EncounterMethodCatalog Load();
        void Save(EncounterMethodCatalog catalog);
    }

    /// <summary>Las especies que existen en el proyecto (para elegir en las tablas): id y nombre.</summary>
    public interface ISpeciesDirectory
    {
        IReadOnlyList<(string id, string name)> All();
    }
}
