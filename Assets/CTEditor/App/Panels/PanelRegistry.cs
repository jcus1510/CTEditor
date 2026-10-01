using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using CTEditor.Workspace;

namespace CTEditor.App
{
    /// <summary>
    /// Crea el contenido de cada panel por su id. Es un REGISTRO: un módulo nuevo añade su panel con Register (y su nombre
    /// con PanelCatalog.Register) sin tocar este archivo. Los que aún no existen explican en qué fase llegan.
    /// </summary>
    public static class PanelRegistry
    {
        private static readonly Dictionary<string, Func<AppShell, VisualElement>> Factories = new Dictionary<string, Func<AppShell, VisualElement>>();

        static PanelRegistry()
        {
            Register(PanelCatalog.Assets, s => new AssetsPanel(s));
            Register(PanelCatalog.Messages, s => new MessagesPanel(s));
            Register(PanelCatalog.Map, s => new MapPanel(s));
            Register(PanelCatalog.MapTree, s => new MapTreePanel(s));
            Register(PanelCatalog.Palette, s => new TilesPanel(s));
            Register(PanelCatalog.Layers, s => new LayersPanel(s));
            Register(PanelCatalog.Inspector, s => new InspectorPanel(s));
            Register(PanelCatalog.PixelEditor, s => new RetouchPanel(s));
            Register(PanelCatalog.Game, s => GamePanel(s));
            Register(PanelCatalog.World, s => new WorldPanel(s));
            Register(PanelCatalog.Encounters, s => new EncountersPanel(s));
            Register(PanelCatalog.Events, _ => Placeholder("Eventos", "Eventos en lista o en grafo de nodos, con recetas y el mapa de la historia.", "Fase 7"));
            Register(PanelCatalog.Database, _ => Placeholder("Base de datos", "Especies, movimientos, objetos, habilidades, entrenadores… De momento siguen en los editores de Unity.", "Fase 10"));
            ShortcutMap.RegisterAction(new ShortcutAction("linea", "Línea (retoque)", "L", "Herramientas"));
            ShortcutMap.RegisterAction(new ShortcutAction("copiar", "Copiar la selección", "Ctrl+C", "Selección"));
            ShortcutMap.RegisterAction(new ShortcutAction("cortar", "Cortar la selección", "Ctrl+X", "Selección"));
            ShortcutMap.RegisterAction(new ShortcutAction("pegar", "Pegar", "Ctrl+V", "Selección"));
            ShortcutMap.RegisterAction(new ShortcutAction("borrar_seleccion", "Borrar la selección", "Delete", "Selección"));
        }

        public static void Register(string panelId, Func<AppShell, VisualElement> factory)
        {
            if (string.IsNullOrWhiteSpace(panelId)) throw new ArgumentException("El panel necesita un id.", nameof(panelId));
            Factories[panelId] = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public static VisualElement Create(string panelId, AppShell shell)
        {
            if (!shell.HasProject) return Placeholder(PanelCatalog.LabelOf(panelId), "Abre un proyecto.", "");
            try
            {
                return Factories.TryGetValue(panelId, out var f) ? f(shell) : Placeholder(PanelCatalog.LabelOf(panelId), "Panel desconocido.", "");
            }
            catch (Exception e)
            {
                shell.Error($"No se pudo abrir el panel «{PanelCatalog.LabelOf(panelId)}»: {e.Message}");
                return Placeholder(PanelCatalog.LabelOf(panelId), "Este panel falló al abrirse (ver Avisos).", "");
            }
        }

        private static VisualElement GamePanel(AppShell shell)
        {
            var box = Placeholder("Juego", "El juego se abre a toda la ventana, con la resolución del proyecto ampliada en píxeles exactos. Esc vuelve aquí.", "");
            box.With(Ui.Button("Jugar desde el inicio", () => shell.StartPlay(), Ui.ButtonKind.Primary),
                Ui.Button("Probar desde el ratón (mapa)", () => shell.RunAction("probar_aqui")));
            return box;
        }

        private static VisualElement Placeholder(string title, string text, string phase)
        {
            var box = Ui.Column(8).Pad(24);
            box.style.alignItems = UnityEngine.UIElements.Align.Center;
            box.style.justifyContent = Justify.Center;
            var t = Ui.Title(title);
            t.style.opacity = 0.6f;
            var h = Ui.Hint(text);
            h.style.maxWidth = 380;
            h.style.unityTextAlign = UnityEngine.TextAnchor.MiddleCenter;
            box.With(t, h);
            if (!string.IsNullOrEmpty(phase))
            {
                var chip = Ui.Text("Llega en la " + phase.ToLowerInvariant(), 0.9f).Colored("acento");
                chip.style.unityTextAlign = UnityEngine.TextAnchor.MiddleCenter;
                box.Add(chip);
            }
            return box;
        }
    }

    /// <summary>Registro de avisos de la aplicación (lo mismo que salió en los mensajes emergentes).</summary>
    public sealed class MessagesPanel : VisualElement
    {
        private readonly AppShell _shell;
        private readonly ScrollView _list;

        public MessagesPanel(AppShell shell)
        {
            _shell = shell;
            style.flexGrow = 1;
            var bar = Ui.Row(8).Pad(8, 4);
            bar.style.flexShrink = 0;
            bar.With(Ui.Text("Avisos", bold: true), Ui.Spacer(), Ui.Button("Vaciar", () => { _shell.Log.Clear(); Refresh(); }, Ui.ButtonKind.Flat));
            Add(bar);
            Add(Ui.Separator());
            _list = Ui.Scroll();
            _list.style.flexGrow = 1;
            Add(_list);
            RegisterCallback<AttachToPanelEvent>(_ => { _shell.LogChanged += Refresh; Refresh(); });
            RegisterCallback<DetachFromPanelEvent>(_ => _shell.LogChanged -= Refresh);
        }

        private void Refresh()
        {
            _list.Clear();
            if (_shell.Log.Count == 0)
            {
                _list.Add(Ui.Hint("Sin avisos.").Margin(10, 8, 10, 8));
                return;
            }
            for (int i = _shell.Log.Count - 1; i >= 0; i--)
            {
                var e = _shell.Log[i];
                var row = Ui.Row(10).Pad(10, 3);
                row.With(Ui.Text(e.Time.ToString("HH:mm:ss"), 0.9f, dim: true), Ui.Text(e.Text, wrap: true).Colored(e.Level).Grow());
                _list.Add(row);
            }
        }
    }
}
