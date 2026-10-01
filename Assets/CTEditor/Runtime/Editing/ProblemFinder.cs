using System;
using System.Collections.Generic;
using System.Linq;
using CTEditor.World.Domain;

namespace CTEditor.Editing
{
    public enum ProblemLevel { Error = 0, Warning = 1, Tip = 2 }

    /// <summary>Un problema del proyecto: qué pasa, dónde (mapa y casilla, si la hay) y cómo de grave.</summary>
    public sealed class Problem
    {
        public ProblemLevel Level { get; }
        public string Text { get; }
        public string MapId { get; }
        public int X { get; } = -1;
        public int Y { get; } = -1;
        public bool HasCell => X >= 0 && Y >= 0;
        /// <summary>Where to go for problems that are not in a map («contenido:especies:pikachu»).</summary>
        public string Link { get; set; }

        public Problem(ProblemLevel level, string text, string mapId = null, int x = -1, int y = -1)
        {
            Level = level; Text = text; MapId = mapId; X = x; Y = y;
        }

        public override string ToString() => $"{Level}: {Text}" + (MapId != null ? $" ({MapId}{(HasCell ? $" {X},{Y}" : "")})" : "");
    }

    /// <summary>
    /// Revisa el proyecto entero y dice lo que falta o está mal antes de que se note al jugar: sin inicio del jugador,
    /// tilesets que ya no existen, tiles fuera de su tileset, tramos encima de otros, zonas de encuentro vacías, especies
    /// que no están en los datos... Cada problema lleva al sitio (ventana Problemas). Es solo lectura.
    /// Para añadir comprobaciones: <see cref="Register"/>.
    /// </summary>
    public static class ProblemFinder
    {
        private static readonly List<Func<MapEditorSession, IEnumerable<Problem>>> Extra = new List<Func<MapEditorSession, IEnumerable<Problem>>>();

        /// <summary>Un módulo nuevo añade sus comprobaciones (eventos, base de datos...).</summary>
        public static void Register(Func<MapEditorSession, IEnumerable<Problem>> check) => Extra.Add(check);

        public static IReadOnlyList<Problem> Check(MapEditorSession s)
        {
            var list = new List<Problem>();
            if (s == null) return list;
            var maps = s.AllMaps().ToList();
            if (maps.Count == 0)
            {
                list.Add(new Problem(ProblemLevel.Tip, "El proyecto no tiene mapas: crea el primero en la ventana Mapas."));
                return list;
            }

            var (startMap, sx, sy) = s.PlayerStart;
            var start = string.IsNullOrEmpty(startMap) ? null : maps.FirstOrDefault(m => m.Id == startMap);
            if (start == null)
                list.Add(new Problem(ProblemLevel.Error, "Falta el inicio del jugador: ponlo con la herramienta Inicio (P) en un mapa."));
            else if (!start.Contains(sx, sy))
                list.Add(new Problem(ProblemLevel.Error, $"El inicio del jugador ({sx}, {sy}) se sale de «{start.Name}».", start.Id));

            var species = new HashSet<string>((s.Species?.All() ?? new (string id, string name)[0]).Select(x => x.id), StringComparer.OrdinalIgnoreCase);
            foreach (var m in maps) list.AddRange(CheckMap(s, m, species));

            foreach (var (a, b) in s.World().Overlaps())
                list.Add(new Problem(ProblemLevel.Warning, $"«{a.Name}» y «{b.Name}» se pisan en el mundo: muévelos en la ventana Mundo.", a.Id));

            foreach (var check in Extra)
                try { list.AddRange(check(s) ?? Enumerable.Empty<Problem>()); }
                catch (Exception e) { list.Add(new Problem(ProblemLevel.Warning, "Una comprobación falló: " + e.Message)); }
            return list.OrderBy(p => p.Level).ThenBy(p => p.MapId ?? "").ToList();
        }

        private static IEnumerable<Problem> CheckMap(MapEditorSession s, MapDefinition m, HashSet<string> species)
        {
            // Tilesets that no longer exist, and tiles beyond the end of theirs (one problem per layer and slot).
            var sets = m.TilesetIds.Select(s.TilesetFor).ToList();
            for (int slot = 0; slot < sets.Count; slot++)
                if (sets[slot] == null && !string.IsNullOrEmpty(m.TilesetIds[slot]))
                    yield return new Problem(ProblemLevel.Error, $"«{m.Name}» usa el tileset «{m.TilesetIds[slot]}», que ya no existe o no está cortado.", m.Id);
            for (int li = 0; li < m.Layers.Count; li++)
            {
                var layer = m.Layers[li];
                var reported = new HashSet<int>();
                for (int y = 0; y < m.Height; y++)
                for (int x = 0; x < m.Width; x++)
                {
                    int c = layer.Get(x, y);
                    if (c < 0) continue;
                    int slot = MapTile.Slot(c);
                    if (reported.Contains(slot)) continue;
                    if (slot >= sets.Count)
                    {
                        reported.Add(slot);
                        yield return new Problem(ProblemLevel.Error, $"«{m.Name}», capa «{layer.Name}»: tiles de un tileset que el mapa ya no tiene.", m.Id, x, y);
                    }
                    else if (sets[slot] != null && !sets[slot].Contains(MapTile.Index(c)))
                    {
                        reported.Add(slot);
                        yield return new Problem(ProblemLevel.Warning, $"«{m.Name}», capa «{layer.Name}»: tiles fuera de «{sets[slot].Name}» (¿se cortó de nuevo más pequeño?).", m.Id, x, y);
                    }
                }
            }

            foreach (var (door, text, error) in Doors.Check(m, s.Find))
                yield return new Problem(error ? ProblemLevel.Error : ProblemLevel.Warning, text, m.Id, door.X, door.Y);

            if (m.Kind == MapKind.Exterior && !m.InWorld)
                yield return new Problem(ProblemLevel.Tip, $"«{m.Name}» es exterior pero no está en el mundo: colócalo en Propiedades o en la ventana Mundo.", m.Id);

            foreach (var area in m.Encounters)
            {
                if (!area.WholeMap && area.Cells.Count == 0)
                    yield return new Problem(ProblemLevel.Warning, $"«{m.Name}»: la zona «{area.Name}» no tiene casillas pintadas.", m.Id);
                if (area.Tables.Count == 0)
                    yield return new Problem(ProblemLevel.Tip, $"«{m.Name}»: la zona «{area.Name}» no tiene métodos (hierba, surf...).", m.Id);
                foreach (var table in area.Tables)
                {
                    var method = s.Methods.Find(table.MethodId);
                    string label = method?.Label ?? table.MethodId;
                    if (method == null)
                        yield return new Problem(ProblemLevel.Error, $"«{m.Name}», zona «{area.Name}»: el método «{table.MethodId}» ya no existe.", m.Id);
                    if (table.Slots.Count == 0)
                        yield return new Problem(ProblemLevel.Warning, $"«{m.Name}», zona «{area.Name}», {label}: sin especies (no sale nada).", m.Id);
                    else if (table.Slots.All(x => x.Weight <= 0))
                        yield return new Problem(ProblemLevel.Warning, $"«{m.Name}», zona «{area.Name}», {label}: todas las especies tienen peso 0.", m.Id);
                    foreach (var slot in table.Slots)
                        if (string.IsNullOrEmpty(slot.SpeciesId))
                            yield return new Problem(ProblemLevel.Warning, $"«{m.Name}», zona «{area.Name}», {label}: una especie sin elegir.", m.Id);
                        else if (species.Count > 0 && !species.Contains(slot.SpeciesId))
                            yield return new Problem(ProblemLevel.Error, $"«{m.Name}», zona «{area.Name}», {label}: «{slot.SpeciesId}» no está en datos/especies.csv.", m.Id);
                }
            }
        }
    }
}
