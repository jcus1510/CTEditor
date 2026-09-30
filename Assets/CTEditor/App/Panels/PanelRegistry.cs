using UnityEngine.UIElements;
using CTEditor.Workspace;

namespace CTEditor.App
{
    /// <summary>Crea el contenido de cada panel por su id. Los que aún no existen explican en qué fase llegan.</summary>
    public static class PanelRegistry
    {
        public static VisualElement Create(string panelId, AppShell shell)
        {
            switch (panelId)
            {
                case PanelCatalog.Assets: return new AssetsPanel(shell);
                case PanelCatalog.Messages: return new MessagesPanel(shell);
                case PanelCatalog.Map:
                    return Placeholder("Mapa", "Aquí se pintarán los mapas: capas ilimitadas, pincel, relleno, paso y terreno, con «Probar aquí».", "Fase 3");
                case PanelCatalog.MapTree:
                    return Placeholder("Mapas", "El árbol de mapas del juego (carpetas, orden, mapa inicial).", "Fase 3");
                case PanelCatalog.Palette:
                    return Placeholder("Tiles", "La paleta de tiles del tileset del mapa. Corta antes tus tilesets desde el panel Recursos.", "Fase 3");
                case PanelCatalog.Layers:
                    return Placeholder("Capas", "Capas del mapa: visibles, bloqueadas, opacidad y orden.", "Fase 3");
                case PanelCatalog.Inspector:
                    return Placeholder("Propiedades", "Lo que tengas seleccionado: un mapa, un tile, un NPC o un evento.", "Fase 3");
                case PanelCatalog.Events:
                    return Placeholder("Eventos", "Eventos en lista o en grafo de nodos, con recetas y el mapa de la historia.", "Fase 7");
                case PanelCatalog.PixelEditor:
                    return Placeholder("Retoque", "Editor de píxeles para corregir tiles y sprites, con modo tile y retoque desde el mapa.", "Fase 4");
                case PanelCatalog.Game:
                    return Placeholder("Juego", "El juego corriendo dentro de la aplicación, con los cambios en caliente.", "Fase 3");
                case PanelCatalog.Database:
                    return Placeholder("Base de datos", "Especies, movimientos, objetos, habilidades, entrenadores… De momento siguen en los editores de Unity.", "Fase 10");
                default:
                    return Placeholder(PanelCatalog.LabelOf(panelId), "Panel desconocido.", "");
            }
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
