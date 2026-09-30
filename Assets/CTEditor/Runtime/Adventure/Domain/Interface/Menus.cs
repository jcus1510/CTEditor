using System;
using System.Collections.Generic;
using System.Linq;

namespace CTEditor.Adventure.Domain.Interface
{
    /// <summary>Qué hace una opción de menú al elegirla.</summary>
    public enum MenuActionKind
    {
        Close,          // cierra el menú
        OpenMenu,       // abre otro menú (Target = id del menú)
        OpenParty,      // pantalla del equipo
        OpenBag,        // mochila
        OpenSummary,    // resumen del primer miembro (o del elegido)
        OpenPokedex,    // Pokédex (próximamente)
        OpenTrainerCard,// ficha de entrenador (próximamente)
        Save,           // guardar partida (próximamente)
        Options,        // opciones (velocidad del texto...)
        ScreenAction    // acción de la pantalla que abre el menú (Target = id: "summary", "swap", "give"...)
    }

    /// <summary>Dónde aparece la caja del menú en la pantalla.</summary>
    public enum MenuAnchor { TopRight, BottomRight, BottomLeft, TopLeft, Center, BottomFull }

    /// <summary>Una opción de un menú.</summary>
    public sealed class MenuOption
    {
        public string Id { get; }
        /// <summary>Texto que se ve. Admite variables: {jugador}, {rival}, {dinero}...</summary>
        public string Label { get; }
        public MenuActionKind Action { get; }
        /// <summary>Id del menú a abrir (OpenMenu) o de la acción (Custom).</summary>
        public string Target { get; }
        /// <summary>Solo se ve si la partida tiene esta marca (vacío = siempre). P. ej. "tiene_pokedex".</summary>
        public string RequiredFlag { get; }
        /// <summary>Se oculta si la partida tiene esta marca (vacío = nunca).</summary>
        public string HiddenByFlag { get; }
        /// <summary>Texto de ayuda que se muestra abajo al pasar por la opción.</summary>
        public string Help { get; }

        public MenuOption(string id, string label, MenuActionKind action, string target = "", string requiredFlag = "", string hiddenByFlag = "", string help = "")
        {
            Id = string.IsNullOrWhiteSpace(id) ? (label ?? "").Trim().ToLowerInvariant() : id.Trim();
            Label = label ?? "";
            Action = action;
            Target = target ?? "";
            RequiredFlag = requiredFlag ?? "";
            HiddenByFlag = hiddenByFlag ?? "";
            Help = help ?? "";
        }

        public bool IsVisible(Func<string, bool> hasFlag)
        {
            hasFlag = hasFlag ?? (_ => false);
            if (!string.IsNullOrWhiteSpace(RequiredFlag) && !hasFlag(RequiredFlag.Trim())) return false;
            if (!string.IsNullOrWhiteSpace(HiddenByFlag) && hasFlag(HiddenByFlag.Trim())) return false;
            return true;
        }
    }

    /// <summary>
    /// UN MENÚ: sus opciones EN ORDEN, cuántas columnas tiene (1 = lista; 2 = rejilla como «Luchar/Mochila
    /// Pokémon/Huir») y cómo se mueve el cursor. Es solo datos: lo dibuja la capa de Unity.
    /// </summary>
    public sealed class MenuDefinition
    {
        public string Id { get; }
        public string DisplayName { get; }
        /// <summary>Título opcional encima de las opciones.</summary>
        public string Title { get; set; } = "";
        public List<MenuOption> Options { get; } = new List<MenuOption>();
        /// <summary>1 = lista vertical; 2 o más = rejilla (se rellena por filas).</summary>
        public int Columns { get; set; } = 1;
        /// <summary>Al pasar del final, ¿vuelve al principio?</summary>
        public bool Wrap { get; set; } = true;
        /// <summary>¿Recuerda dónde estaba el cursor la próxima vez que se abre?</summary>
        public bool RememberCursor { get; set; } = true;
        /// <summary>¿Cancelar cierra el menú? (en Sí/No, Cancelar = «No»).</summary>
        public bool CancelCloses { get; set; } = true;
        /// <summary>Si CancelCloses es falso, qué opción elige Cancelar (id; vacío = nada).</summary>
        public string CancelOptionId { get; set; } = "";
        public MenuAnchor Anchor { get; set; } = MenuAnchor.TopRight;

        public MenuDefinition(string id, string displayName)
        {
            Id = id ?? "";
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Id : displayName;
        }

        public MenuDefinition Add(MenuOption o) { if (o != null) Options.Add(o); return this; }

        /// <summary>Las opciones que se ven con las marcas actuales de la partida.</summary>
        public List<MenuOption> VisibleOptions(Func<string, bool> hasFlag)
            => Options.Where(o => o.IsVisible(hasFlag)).ToList();

        /// <summary>Problemas del menú (ids repetidos, submenús sin destino...), en español.</summary>
        public List<string> Problems(Func<string, bool> menuExists = null)
        {
            var list = new List<string>();
            if (Options.Count == 0) list.Add("El menú no tiene opciones.");
            foreach (var g in Options.GroupBy(o => o.Id).Where(g => g.Count() > 1))
                list.Add($"El id de opción «{g.Key}» está repetido.");
            foreach (var o in Options)
            {
                if (string.IsNullOrWhiteSpace(o.Label)) list.Add($"La opción «{o.Id}» no tiene texto.");
                if (o.Action == MenuActionKind.OpenMenu)
                {
                    if (string.IsNullOrWhiteSpace(o.Target)) list.Add($"«{o.Label}» abre un menú, pero no dice cuál.");
                    else if (o.Target == Id) list.Add($"«{o.Label}» se abre a sí mismo.");
                    else if (menuExists != null && !menuExists(o.Target)) list.Add($"«{o.Label}» abre el menú «{o.Target}», que no existe.");
                }
                if (o.Action == MenuActionKind.ScreenAction && string.IsNullOrWhiteSpace(o.Target))
                    list.Add($"«{o.Label}» es una acción de pantalla sin id.");
            }
            if (Columns < 1) list.Add("Las columnas deben ser 1 o más.");
            if (!CancelCloses && !string.IsNullOrWhiteSpace(CancelOptionId) && Options.All(o => o.Id != CancelOptionId))
                list.Add($"Cancelar elige «{CancelOptionId}», que no es ninguna opción.");
            return list;
        }
    }

    /// <summary>
    /// EL CURSOR de un menú en rejilla (puro). Con 1 columna, Arriba/Abajo recorren la lista. Con varias,
    /// Izquierda/Derecha se mueven en la fila y Arriba/Abajo saltan de fila. Salta las opciones
    /// desactivadas y, si Wrap, al pasar del borde vuelve por el otro lado.
    /// </summary>
    public sealed class MenuCursor
    {
        private readonly Func<int, bool> _enabled;
        public int Count { get; }
        public int Columns { get; }
        public bool Wrap { get; }
        public int Index { get; private set; }
        public int Rows => Count == 0 ? 0 : (Count + Columns - 1) / Columns;

        public MenuCursor(int count, int columns = 1, bool wrap = true, Func<int, bool> enabled = null, int start = 0)
        {
            Count = Math.Max(0, count);
            Columns = Math.Max(1, columns);
            Wrap = wrap;
            _enabled = enabled ?? (_ => true);
            Index = 0;
            if (Count > 0) Set(start);
        }

        public bool IsEnabled(int i) => i >= 0 && i < Count && _enabled(i);

        /// <summary>Pone el cursor en 'i' (o en la primera opción activa a partir de ahí).</summary>
        public void Set(int i)
        {
            if (Count == 0) return;
            i = Math.Max(0, Math.Min(Count - 1, i));
            for (int k = 0; k < Count; k++)
            {
                int j = (i + k) % Count;
                if (IsEnabled(j)) { Index = j; return; }
            }
            Index = i; // ninguna activa: se queda donde se pidió
        }

        /// <summary>Mueve el cursor. Devuelve true si cambió de opción.</summary>
        public bool Move(Direction d)
        {
            if (Count <= 1 || d == Direction.None) return false;
            int before = Index;
            int row = Index / Columns, col = Index % Columns;

            if (d == Direction.Left || d == Direction.Right)
            {
                if (Columns == 1) return false;
                int step = d == Direction.Right ? 1 : -1;
                int rowStart = row * Columns, rowLen = Math.Min(Columns, Count - rowStart);
                for (int k = 1; k < rowLen; k++)
                {
                    int c = col + step * k;
                    if (!Wrap && (c < 0 || c >= rowLen)) break;
                    c = ((c % rowLen) + rowLen) % rowLen;
                    int j = rowStart + c;
                    if (IsEnabled(j)) { Index = j; break; }
                }
            }
            else
            {
                int step = d == Direction.Down ? 1 : -1;
                for (int k = 1; k < Rows + 1; k++)
                {
                    int r = row + step * k;
                    if (!Wrap && (r < 0 || r >= Rows)) break;
                    r = ((r % Rows) + Rows) % Rows;
                    if (r == row) break;
                    // En una fila más corta (la última), se va a la última columna que exista.
                    int j = Math.Min(r * Columns + col, Count - 1);
                    if (IsEnabled(j)) { Index = j; break; }
                    // Si esa está desactivada, prueba otras de la misma fila.
                    int rowStart = r * Columns, rowLen = Math.Min(Columns, Count - rowStart);
                    bool found = false;
                    for (int c = 0; c < rowLen && !found; c++)
                        if (IsEnabled(rowStart + c)) { Index = rowStart + c; found = true; }
                    if (found) break;
                }
            }
            return Index != before;
        }
    }

    /// <summary>
    /// NAVEGACIÓN POR POSICIÓN (pura): para pantallas diseñadas a mano (los botones del combate, el
    /// Laboratorio), donde no hay filas y columnas. Con las flechas se va al botón más cercano EN ESA
    /// DIRECCIÓN, como con la cruceta de una consola. Coordenadas con Y hacia ARRIBA (como Unity).
    /// </summary>
    public static class SpatialNavigator
    {
        /// <summary>Índice del siguiente elemento en esa dirección, o 'current' si no hay ninguno.</summary>
        public static int Next(int current, Direction d, IReadOnlyList<(float x, float y)> positions, Func<int, bool> enabled = null, bool wrap = true)
        {
            if (positions == null || positions.Count == 0 || d == Direction.None) return current;
            enabled = enabled ?? (_ => true);
            if (current < 0 || current >= positions.Count)
                return FirstEnabled(positions, enabled, current);

            var (dx, dy) = Vector(d);
            var from = positions[current];
            int best = -1; float bestScore = float.MaxValue;
            for (int i = 0; i < positions.Count; i++)
            {
                if (i == current || !enabled(i)) continue;
                float vx = positions[i].x - from.x, vy = positions[i].y - from.y;
                float along = vx * dx + vy * dy;            // cuánto avanza en la dirección pedida
                if (along <= 0.5f) continue;                // no está en esa dirección
                float across = Math.Abs(vx * dy - vy * dx); // cuánto se desvía hacia un lado
                if (across > along * 2.5f) continue;        // demasiado de lado (más de ~68°)
                float score = along + across * 2f;          // preferimos lo recto a lo cercano en diagonal
                if (score < bestScore) { bestScore = score; best = i; }
            }
            if (best >= 0) return best;
            if (!wrap) return current;

            // Vuelta: el más alejado en la dirección CONTRARIA que esté alineado (como salir por el otro lado).
            float bestFar = float.MinValue;
            for (int i = 0; i < positions.Count; i++)
            {
                if (i == current || !enabled(i)) continue;
                float vx = positions[i].x - from.x, vy = positions[i].y - from.y;
                float back = -(vx * dx + vy * dy);
                float across = Math.Abs(vx * dy - vy * dx);
                if (back <= 0.5f || across > 30f + back * 0.25f) continue;
                float score = back - across * 2f;
                if (score > bestFar) { bestFar = score; best = i; }
            }
            return best >= 0 ? best : current;
        }

        private static int FirstEnabled(IReadOnlyList<(float x, float y)> positions, Func<int, bool> enabled, int fallback)
        {
            // El de más arriba a la izquierda (orden de lectura).
            int best = -1;
            for (int i = 0; i < positions.Count; i++)
            {
                if (!enabled(i)) continue;
                if (best < 0 || positions[i].y > positions[best].y + 1f ||
                    (Math.Abs(positions[i].y - positions[best].y) <= 1f && positions[i].x < positions[best].x)) best = i;
            }
            return best >= 0 ? best : fallback;
        }

        private static (float dx, float dy) Vector(Direction d)
        {
            switch (d)
            {
                case Direction.Up: return (0, 1);
                case Direction.Down: return (0, -1);
                case Direction.Left: return (-1, 0);
                default: return (1, 0);
            }
        }
    }
}
