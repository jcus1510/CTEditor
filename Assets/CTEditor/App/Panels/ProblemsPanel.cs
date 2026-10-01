using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using CTEditor.Editing;

namespace CTEditor.App
{
    /// <summary>
    /// Ventana PROBLEMAS: lo que falta o está mal en el proyecto (<see cref="ProblemFinder"/>), siempre al día: se vuelve
    /// a revisar sola poco después de cada cambio. Clic en un problema = abrir su mapa.
    /// </summary>
    public sealed class ProblemsPanel : VisualElement
    {
        private readonly AppShell _shell;
        private MapEditorSession S => _shell.Maps;
        private readonly VisualElement _head;
        private readonly ScrollView _list;
        private IVisualElementScheduledItem _soon;

        public ProblemsPanel(AppShell shell)
        {
            _shell = shell;
            style.flexGrow = 1;
            _head = Ui.Row(8).Pad(10, 6);
            _head.style.flexShrink = 0;
            Add(_head);
            Add(Ui.Separator());
            _list = Ui.Scroll().Grow();
            Add(_list);
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                if (S == null) return;
                S.MapOpened += Soon;
                S.DirtyChanged += Soon;
                S.StructureChanged += Soon;
                S.EncountersChanged += Soon;
                S.WorldChanged += Soon;
                S.PlayerStartChanged += Soon;
                _shell.AssetsChanged += Soon;
                _shell.ContentChanged += OnContent;
                Refresh();
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (S == null) return;
                S.MapOpened -= Soon;
                S.DirtyChanged -= Soon;
                S.StructureChanged -= Soon;
                S.EncountersChanged -= Soon;
                S.WorldChanged -= Soon;
                S.PlayerStartChanged -= Soon;
                _shell.AssetsChanged -= Soon;
                _shell.ContentChanged -= OnContent;
            });
        }

        /// <summary>Checks again a moment after the last change (not on every brush stroke).</summary>
        private void Soon()
        {
            _soon?.Pause();
            _soon = schedule.Execute(Refresh);
            _soon.ExecuteLater(800);
        }

        private void Refresh()
        {
            _head.Clear();
            _list.Clear();
            if (S == null) { _list.Add(Ui.Hint("Abre un proyecto.").Margin(10, 8, 10, 8)); return; }
            var problems = ProblemFinder.Check(S);
            int errors = problems.Count(p => p.Level == ProblemLevel.Error), warnings = problems.Count(p => p.Level == ProblemLevel.Warning);
            int tips = problems.Count - errors - warnings;
            _head.With(Badge(errors, "error", "errores"), Badge(warnings, "aviso", "avisos"), Badge(tips, "texto_suave", "consejos"), Ui.Spacer(),
                Ui.Button("Revisar", Refresh, Ui.ButtonKind.Flat, "Volver a revisar ahora (se revisa sola tras cada cambio)"));
            if (problems.Count == 0)
            {
                _list.Add(Ui.EmptyState("estrella", "Todo en orden", "No hay problemas en el proyecto. Se vuelve a revisar solo después de cada cambio."));
                return;
            }
            foreach (var p in problems)
            {
                var problem = p;
                var row = Ui.Row(8).Pad(10, 5);
                row.style.alignItems = Align.FlexStart;
                string token = p.Level == ProblemLevel.Error ? "error" : p.Level == ProblemLevel.Warning ? "aviso" : "texto_suave";
                var icon = Icons.Element(p.Level == ProblemLevel.Tip ? "propiedades" : "aviso", Ui.IconSize, Ui.C(token));
                icon.style.marginTop = 1;
                var text = Ui.Text(p.Text, 0.92f, wrap: true).Grow();
                text.pickingMode = PickingMode.Ignore;
                row.With(icon, text);
                if (p.Link != null && p.Link.StartsWith("contenido:"))
                {
                    var parts = p.Link.Split(':');
                    row.tooltip = "Clic: abrir la ficha";
                    row.RegisterCallback<PointerEnterEvent>(_ => row.style.backgroundColor = Ui.C("panel_alt"));
                    row.RegisterCallback<PointerLeaveEvent>(_ => row.style.backgroundColor = new Color(0, 0, 0, 0));
                    row.RegisterCallback<PointerUpEvent>(_ => _shell.OpenContentItem(parts[1], parts.Length > 2 ? parts[2] : null));
                }
                else if (p.MapId != null)
                {
                    row.tooltip = "Clic: abrir el mapa";
                    row.RegisterCallback<PointerEnterEvent>(_ => row.style.backgroundColor = Ui.C("panel_alt"));
                    row.RegisterCallback<PointerLeaveEvent>(_ => row.style.backgroundColor = new Color(0, 0, 0, 0));
                    row.RegisterCallback<PointerUpEvent>(_ =>
                    {
                        if (S.Map?.Id != problem.MapId) S.OpenMap(problem.MapId);
                        _shell.ActiveEditor = "mapa";
                    });
                }
                _list.Add(row);
            }
        }

        private static VisualElement Badge(int n, string token, string what)
        {
            var b = Ui.Row(4);
            var dot = new VisualElement().Bg(token).Round(5);
            dot.style.width = 10; dot.style.height = 10;
            b.With(dot, Ui.Text($"{n} {what}", 0.88f, bold: n > 0, dim: n == 0));
            return b;
        }
        private void OnContent(string _) => Soon();
    }
}
