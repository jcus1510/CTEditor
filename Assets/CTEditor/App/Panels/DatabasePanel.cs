using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using CTEditor.Content;

namespace CTEditor.App
{
    /// <summary>
    /// BASE DE DATOS (the «Centro de contenido» of Unity): every category of game content as a card with how many there
    /// are and how many problems, grouped like the Unity menus (Criaturas, Combate, Personajes...). A click opens its window.
    /// </summary>
    public sealed class DatabasePanel : VisualElement
    {
        private static readonly (string title, string[] keys)[] Groups =
        {
            ("Criaturas", new[] { ContentSchemas.Species, ContentSchemas.Abilities, ContentSchemas.Natures, ContentSchemas.EggGroups, ContentSchemas.Curves }),
            ("Combate", new[] { ContentSchemas.Moves, ContentSchemas.Types, ContentSchemas.TypeChart, ContentSchemas.Statuses, ContentSchemas.Weathers, ContentSchemas.SideEffects, ContentSchemas.Hazards, ContentSchemas.Rules }),
            ("Objetos", new[] { ContentSchemas.Items }),
            ("Personajes", new[] { ContentSchemas.Trainers, ContentSchemas.Teams, ContentSchemas.Sets }),
        };

        private readonly AppShell _shell;
        private readonly VisualElement _box;

        public DatabasePanel(AppShell shell)
        {
            _shell = shell;
            style.flexGrow = 1;
            var scroll = Ui.Scroll().Grow();
            _box = Ui.Column(10).Pad(14);
            scroll.Add(_box);
            Add(scroll);
            RegisterCallback<AttachToPanelEvent>(_ => { _shell.ContentChanged += OnChanged; Fill(); });
            RegisterCallback<DetachFromPanelEvent>(_ => _shell.ContentChanged -= OnChanged);
        }

        private void OnChanged(string _) => schedule.Execute(Fill).ExecuteLater(300);

        private void Fill()
        {
            _box.Clear();
            var c = _shell.Content;
            if (c == null) { _box.Add(Ui.EmptyState("base", "Sin proyecto", "Abre un proyecto.")); return; }
            var issues = ContentChecks.Run(c.Db);
            _box.Add(Ui.Hint("Los datos del juego del proyecto (carpeta «datos/», también se abren con Excel). Clic en una tarjeta para editarla. " +
                             "Borrar avisa de quién lo usa; cambiar un id lo cambia en todas partes; todo se deshace con Ctrl+Z."));
            var known = Groups.SelectMany(g => g.keys).ToList();
            var others = ContentSchemas.All.Select(s => s.Key).Where(k => !known.Contains(k)).ToArray();
            foreach (var (title, keys) in Groups.Append(("Otros", others)))
            {
                if (keys.Length == 0) continue;
                _box.Add(Ui.SectionTitle(title));
                var grid = Ui.Row(8).Wrap();
                foreach (var k in keys)
                {
                    var s = ContentSchemas.Find(k);
                    if (s == null) continue;
                    int n = c.Db.Table(k).Records.Count;
                    int errors = issues.Count(i => i.Category == k && i.Level == ContentIssueLevel.Error);
                    int warnings = issues.Count(i => i.Category == k && i.Level == ContentIssueLevel.Warning);
                    var card = Ui.Card();
                    card.style.width = 200;
                    card.style.marginBottom = 8;
                    var head = Ui.Row(6);
                    head.With(Icons.Element(s.Icon, Ui.IconSize, Ui.C("acento")), Ui.Text(s.Title, bold: true).Grow());
                    card.Add(head);
                    card.Add(Ui.Text(n == 0 ? "Ninguna ficha" : $"{n} fichas", 0.88f, dim: n == 0));
                    if (errors > 0) card.Add(Ui.Text($"{errors} errores", 0.85f).Colored("error"));
                    else if (warnings > 0) card.Add(Ui.Text($"{warnings} avisos", 0.85f).Colored("aviso"));
                    else if (n > 0) card.Add(Ui.Text("Sin problemas", 0.85f).Colored("exito"));
                    card.tooltip = s.Intro;
                    var key = k;
                    card.RegisterCallback<ClickEvent>(_ => _shell.OpenContentItem(key, null));
                    card.RegisterCallback<PointerEnterEvent>(_ => card.Border(1, "acento", 6));
                    card.RegisterCallback<PointerLeaveEvent>(_ => card.Border(1, "borde", 6));
                    grid.Add(card);
                }
                _box.Add(grid);
            }
            _box.Add(Ui.Row(8).With(Ui.Button("Papelera", () => { _shell.Workspace.Layout.Open(TrashPanel.Id); _shell.SetLayout(_shell.Workspace.Layout); }),
                Ui.Button("Ver los problemas", () => _shell.RunAction("ventana_problemas"))));
        }
    }
}
