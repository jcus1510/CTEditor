using System;
using System.Collections.Generic;
using System.Linq;
using CTEditor.Project;

namespace CTEditor.Workspace
{
    /// <summary>Un panel de la aplicación (mapa, paleta de tiles, inspector...).</summary>
    public sealed class PanelInfo
    {
        public string Id { get; }
        public string Label { get; }
        public PanelInfo(string id, string label) { Id = id; Label = label; }
    }

    /// <summary>Los paneles que existen. Los ids se guardan en el archivo del entorno: no cambiarlos.</summary>
    public static class PanelCatalog
    {
        public const string Map = "mapa";
        public const string MapTree = "mapas";
        public const string Palette = "paleta";
        public const string Layers = "capas";
        public const string Inspector = "inspector";
        public const string Assets = "recursos";
        public const string Events = "eventos";
        public const string Messages = "avisos";
        public const string PixelEditor = "retoque";
        public const string Game = "juego";
        public const string Database = "base_datos";

        private static readonly List<PanelInfo> Panels = new List<PanelInfo>
        {
            new PanelInfo(Map, "Mapa"),
            new PanelInfo(MapTree, "Mapas"),
            new PanelInfo(Palette, "Tiles"),
            new PanelInfo(Layers, "Capas"),
            new PanelInfo(Inspector, "Propiedades"),
            new PanelInfo(Assets, "Recursos"),
            new PanelInfo(Events, "Eventos"),
            new PanelInfo(Messages, "Avisos"),
            new PanelInfo(PixelEditor, "Retoque"),
            new PanelInfo(Game, "Juego"),
            new PanelInfo(Database, "Base de datos"),
        };

        public static IReadOnlyList<PanelInfo> All => Panels;

        /// <summary>Un módulo nuevo añade su panel (o cambia el nombre de uno que ya existe) sin tocar esta lista.</summary>
        public static void Register(string id, string label)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("El panel necesita un id.", nameof(id));
            Panels.RemoveAll(p => p.Id == id);
            Panels.Add(new PanelInfo(id, label));
        }

        public static PanelInfo Find(string id) => Panels.FirstOrDefault(p => p.Id == id);
        public static string LabelOf(string id) => Find(id)?.Label ?? id;
    }

    /// <summary>Horizontal = los hijos uno al lado del otro; Vertical = uno encima del otro.</summary>
    public enum SplitDirection
    {
        Horizontal = 0,
        Vertical = 1
    }

    /// <summary>Dónde se suelta un panel respecto a un grupo: a un lado (crea una división) o dentro (otra pestaña).</summary>
    public enum DockSide
    {
        Center = 0,
        Left = 1,
        Right = 2,
        Top = 3,
        Bottom = 4
    }

    /// <summary>Un trozo de la ventana: o una división en varias partes, o un grupo de pestañas.</summary>
    public abstract class DockNode
    {
        /// <summary>Fracción del espacio del padre (las de los hermanos suman 1).</summary>
        public float Size { get; set; } = 1f;
    }

    public sealed class DockSplit : DockNode
    {
        public SplitDirection Direction { get; set; }
        public List<DockNode> Children { get; } = new List<DockNode>();

        public DockSplit(SplitDirection direction, params DockNode[] children)
        {
            Direction = direction;
            Children.AddRange(children);
        }
    }

    public sealed class DockTabs : DockNode
    {
        public List<string> Panels { get; } = new List<string>();
        /// <summary>Pestaña visible (índice en Panels).</summary>
        public int Active { get; set; }

        public DockTabs(params string[] panels) { Panels.AddRange(panels); }

        public string ActivePanel => Panels.Count == 0 ? null : Panels[Math.Max(0, Math.Min(Active, Panels.Count - 1))];
    }

    /// <summary>
    /// La distribución de paneles de la ventana: un árbol de divisiones y grupos de pestañas, que el usuario cambia
    /// arrastrando (Dock), cerrando (Close), abriendo (Open) o moviendo los separadores (Resize). Cada panel aparece como
    /// mucho una vez. Tras cada cambio el árbol se ordena solo (sin grupos vacíos ni divisiones de un hijo).
    /// </summary>
    public sealed class DockLayout
    {
        public const float MinSize = 0.05f;

        public string Name { get; set; }
        public DockNode Root { get; private set; }

        public DockLayout(string name, DockNode root)
        {
            Name = name;
            Root = root ?? new DockTabs(PanelCatalog.Map);
            Normalize();
        }

        // ── Queries ──────────────────────────────────────────────────────────────────────────────

        public IEnumerable<DockTabs> AllTabs() => Walk(Root).OfType<DockTabs>();
        public IEnumerable<string> OpenPanels() => AllTabs().SelectMany(t => t.Panels);
        public bool IsOpen(string panel) => TabsOf(panel) != null;
        public DockTabs TabsOf(string panel) => AllTabs().FirstOrDefault(t => t.Panels.Contains(panel));

        private static IEnumerable<DockNode> Walk(DockNode n)
        {
            yield return n;
            if (n is DockSplit s)
                foreach (var c in s.Children)
                foreach (var x in Walk(c))
                    yield return x;
        }

        private DockSplit ParentOf(DockNode node) =>
            Walk(Root).OfType<DockSplit>().FirstOrDefault(s => s.Children.Contains(node));

        // ── Changes ──────────────────────────────────────────────────────────────────────────────

        /// <summary>Cierra un panel (si era el último de la ventana, queda el mapa).</summary>
        public void Close(string panel)
        {
            var tabs = TabsOf(panel);
            if (tabs == null) return;
            tabs.Panels.Remove(panel);
            Normalize();
        }

        /// <summary>Abre un panel (si ya está abierto, lo pone delante). Va junto a 'near' si se da; si no, al grupo más grande.</summary>
        public void Open(string panel, string near = null)
        {
            var tabs = TabsOf(panel);
            if (tabs == null)
            {
                tabs = (near != null ? TabsOf(near) : null) ?? LargestTabs();
                tabs.Panels.Add(panel);
            }
            tabs.Active = tabs.Panels.IndexOf(panel);
        }

        /// <summary>Muestra la pestaña de un panel abierto.</summary>
        public void Focus(string panel)
        {
            var tabs = TabsOf(panel);
            if (tabs != null) tabs.Active = tabs.Panels.IndexOf(panel);
        }

        /// <summary>Suelta 'panel' sobre el grupo 'target': dentro (otra pestaña) o a un lado (divide el grupo).</summary>
        public void Dock(string panel, DockTabs target, DockSide side)
        {
            if (target == null || !AllTabs().Contains(target)) throw new ArgumentException("Ese grupo no está en la ventana.", nameof(target));
            var from = TabsOf(panel);
            if (from == target && (side == DockSide.Center || target.Panels.Count == 1)) { Focus(panel); return; }
            from?.Panels.Remove(panel);

            if (side == DockSide.Center)
            {
                target.Panels.Add(panel);
                target.Active = target.Panels.Count - 1;
            }
            else
            {
                var dir = side == DockSide.Left || side == DockSide.Right ? SplitDirection.Horizontal : SplitDirection.Vertical;
                bool before = side == DockSide.Left || side == DockSide.Top;
                var fresh = new DockTabs(panel);
                var parent = ParentOf(target);
                if (parent != null && parent.Direction == dir)
                {
                    // Same direction as the parent: the new group takes half of the target's share.
                    fresh.Size = target.Size / 2f;
                    target.Size /= 2f;
                    int i = parent.Children.IndexOf(target);
                    parent.Children.Insert(before ? i : i + 1, fresh);
                }
                else
                {
                    var split = new DockSplit(dir) { Size = target.Size };
                    target.Size = 0.5f;
                    fresh.Size = 0.5f;
                    if (before) { split.Children.Add(fresh); split.Children.Add(target); }
                    else { split.Children.Add(target); split.Children.Add(fresh); }
                    Replace(target, split);
                }
            }
            Normalize();
        }

        /// <summary>Mueve el separador entre el hijo 'index' y el siguiente: 'index' pasa a medir 'size' (fracción).</summary>
        public void Resize(DockSplit split, int index, float size)
        {
            if (split == null || index < 0 || index + 1 >= split.Children.Count) return;
            var a = split.Children[index];
            var b = split.Children[index + 1];
            float pair = a.Size + b.Size;
            float s = Math.Max(MinSize, Math.Min(pair - MinSize, size));
            a.Size = s;
            b.Size = pair - s;
        }

        private DockTabs LargestTabs()
        {
            DockTabs best = null;
            float bestArea = -1;
            void Visit(DockNode n, float area)
            {
                if (n is DockTabs t) { if (area > bestArea) { best = t; bestArea = area; } return; }
                foreach (var c in ((DockSplit)n).Children) Visit(c, area * c.Size);
            }
            Visit(Root, 1f);
            return best;
        }

        private void Replace(DockNode old, DockNode now)
        {
            if (Root == old) { Root = now; return; }
            var parent = ParentOf(old);
            parent.Children[parent.Children.IndexOf(old)] = now;
        }

        /// <summary>Quita duplicados, grupos vacíos y divisiones de un hijo; junta divisiones de la misma dirección; ajusta tamaños.</summary>
        public void Normalize()
        {
            var seen = new HashSet<string>();
            foreach (var t in AllTabs().ToList())
            {
                t.Panels.RemoveAll(p => string.IsNullOrEmpty(p) || !seen.Add(p));
                t.Active = Math.Max(0, Math.Min(t.Active, t.Panels.Count - 1));
            }
            Root = Clean(Root) ?? new DockTabs(PanelCatalog.Map);
            Root.Size = 1f;
        }

        private static DockNode Clean(DockNode n)
        {
            if (n is DockTabs t) return t.Panels.Count == 0 ? null : t;
            var s = (DockSplit)n;
            var kids = new List<DockNode>();
            foreach (var c in s.Children)
            {
                var k = Clean(c);
                if (k == null) continue;
                if (k is DockSplit ks && ks.Direction == s.Direction)
                    foreach (var g in ks.Children) { g.Size *= k.Size; kids.Add(g); }
                else kids.Add(k);
            }
            if (kids.Count == 0) return null;
            if (kids.Count == 1) { kids[0].Size = s.Size; return kids[0]; }
            float total = kids.Sum(k => Math.Max(MinSize, k.Size));
            foreach (var k in kids) k.Size = Math.Max(MinSize, k.Size) / total;
            s.Children.Clear();
            s.Children.AddRange(kids);
            return s;
        }

        // ── Saving ───────────────────────────────────────────────────────────────────────────────

        public JsonObject ToJson() => new JsonObject().Set("nombre", Name).Set("raiz", NodeToJson(Root));

        public static DockLayout FromJson(JsonObject o)
        {
            var root = o?.GetObject("raiz");
            return new DockLayout(o?.GetString("nombre", "Personalizado") ?? "Personalizado", root == null ? null : NodeFromJson(root));
        }

        public DockLayout Clone(string name = null) => FromJson(ToJson().Set("nombre", name ?? Name));

        private static JsonObject NodeToJson(DockNode n)
        {
            var o = new JsonObject().Set("tamaño", Math.Round(n.Size, 4));
            if (n is DockTabs t)
                return o.Set("pestañas", t.Panels.Cast<object>().ToList()).Set("activa", t.Active);
            var s = (DockSplit)n;
            return o.Set("division", s.Direction == SplitDirection.Horizontal ? "horizontal" : "vertical")
                .Set("hijos", s.Children.Select(c => (object)NodeToJson(c)).ToList());
        }

        private static DockNode NodeFromJson(JsonObject o)
        {
            DockNode n;
            var tabs = o.GetArray("pestañas");
            if (tabs != null)
                n = new DockTabs(tabs.OfType<string>().ToArray()) { Active = o.GetInt("activa") };
            else
            {
                var s = new DockSplit(o.GetString("division") == "vertical" ? SplitDirection.Vertical : SplitDirection.Horizontal);
                foreach (var c in o.GetArray("hijos") ?? new List<object>())
                    if (c is JsonObject co) s.Children.Add(NodeFromJson(co));
                n = s;
            }
            n.Size = o.GetFloat("tamaño", 1f);
            return n;
        }

        // ── Presets ──────────────────────────────────────────────────────────────────────────────

        private static DockTabs T(float size, params string[] panels) => new DockTabs(panels) { Size = size };
        private static DockSplit S(SplitDirection d, float size, params DockNode[] kids) => new DockSplit(d, kids) { Size = size };

        /// <summary>Como RPG Maker: tiles y mapas a la izquierda, el mapa en el centro, propiedades y capas a la derecha.</summary>
        public static DockLayout Classic() => new DockLayout("Clásico (RPG Maker)",
            S(SplitDirection.Vertical, 1f,
                S(SplitDirection.Horizontal, 0.8f,
                    S(SplitDirection.Vertical, 0.22f, T(0.6f, PanelCatalog.Palette), T(0.4f, PanelCatalog.MapTree)),
                    T(0.56f, PanelCatalog.Map, PanelCatalog.Game),
                    S(SplitDirection.Vertical, 0.22f, T(0.6f, PanelCatalog.Inspector), T(0.4f, PanelCatalog.Layers))),
                T(0.2f, PanelCatalog.Messages, PanelCatalog.Assets)));

        /// <summary>El mapa lo más grande posible, con una columna estrecha de herramientas.</summary>
        public static DockLayout BigMap() => new DockLayout("Mapa grande",
            S(SplitDirection.Horizontal, 1f,
                T(0.18f, PanelCatalog.Palette, PanelCatalog.MapTree, PanelCatalog.Layers, PanelCatalog.Inspector),
                T(0.82f, PanelCatalog.Map, PanelCatalog.Game)));

        /// <summary>Para retocar gráficos: recursos, editor de píxeles y la paleta de tiles para ver el resultado.</summary>
        public static DockLayout Art() => new DockLayout("Arte",
            S(SplitDirection.Horizontal, 1f,
                T(0.2f, PanelCatalog.Assets),
                T(0.6f, PanelCatalog.PixelEditor, PanelCatalog.Map),
                T(0.2f, PanelCatalog.Palette, PanelCatalog.Inspector)));

        /// <summary>Para escribir la historia: mapas, eventos (lista o nodos) y propiedades.</summary>
        public static DockLayout Story() => new DockLayout("Historia",
            S(SplitDirection.Horizontal, 1f,
                T(0.2f, PanelCatalog.MapTree, PanelCatalog.Map),
                T(0.55f, PanelCatalog.Events),
                S(SplitDirection.Vertical, 0.25f, T(0.65f, PanelCatalog.Inspector), T(0.35f, PanelCatalog.Messages))));

        public static IReadOnlyList<Func<DockLayout>> Presets { get; } = new Func<DockLayout>[] { Classic, BigMap, Art, Story };
    }
}
