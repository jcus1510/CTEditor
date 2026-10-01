using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using CTEditor.World.Domain;

namespace CTEditor.App
{
    /// <summary>
    /// Ventana JUEGO: se juega aquí dentro mientras se edita al lado (lo que se pinta se ve al momento), o a pantalla
    /// completa. Arriba, el perfil de prueba (equipo, medallas, objetos, interruptores, hora).
    /// </summary>
    public sealed class GameWindow : VisualElement
    {
        private readonly AppShell _shell;
        private readonly VisualElement _bar, _host, _idle;

        public GameWindow(AppShell shell)
        {
            _shell = shell;
            style.flexGrow = 1;
            _bar = Ui.Row(4).Pad(6, 4).Wrap();
            _bar.style.flexShrink = 0;
            Add(_bar);
            Add(Ui.Separator());
            _host = new VisualElement().Grow();
            _host.style.backgroundColor = Color.black;
            _idle = Ui.Column(10).Fill();
            _idle.style.alignItems = Align.Center;
            _idle.style.justifyContent = Justify.Center;
            _idle.With(Icons.Element("jugar", Ui.IconSize * 3, Ui.WithAlpha(Ui.C("exito"), 0.8f)),
                Ui.Text("Juega aquí mientras editas al lado", 1.05f, bold: true).Colored("texto"),
                Ui.Hint("Lo que pintes en el mapa, el paso y los encuentros cambian al momento."),
                Ui.Button("Jugar aquí (F5)", () => _shell.StartPlay(), Ui.ButtonKind.Primary));
            _host.Add(_idle);
            Add(_host);
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                _shell.GameHost = _host;
                _shell.PlayingChanged += OnPlaying;
                _shell.ProfilesChanged += Build;
                Build();
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (_shell.GameHost == _host)
                {
                    if (_shell.PlayDocked) _shell.StopPlay();
                    _shell.GameHost = null;
                }
                _shell.PlayingChanged -= OnPlaying;
                _shell.ProfilesChanged -= Build;
            });
        }

        private void OnPlaying(bool playing) => Build();

        private void Build()
        {
            _bar.Clear();
            bool playing = _shell.IsPlaying;
            _idle.Show(!(playing && _shell.PlayDocked));
            if (!playing)
            {
                var play = Ui.Button("", () => _shell.StartPlay(), Ui.ButtonKind.Primary, "Jugar desde el inicio aquí dentro (F5)");
                play.style.flexDirection = FlexDirection.Row; play.style.alignItems = Align.Center;
                play.With(Icons.Element("jugar", Ui.IconSize * 0.8f, Color.white), Ui.Text("Jugar aquí").Colored("texto").Margin(6, 0, 0, 0));
                play.Q<Label>().style.color = Color.white;
                _bar.With(play,
                    Ui.IconButton("ajustar", FullScreen, "Jugar a pantalla completa"),
                    Ui.Button("Desde el ratón", () => _shell.RunAction("probar_aqui"), Ui.ButtonKind.Flat, "Jugar desde la casilla del ratón en el mapa (Ctrl+F5)"));
            }
            else
                _bar.With(Ui.Button("Parar (Esc)", () => _shell.StopPlay(), Ui.ButtonKind.Danger, "Volver a editar"));
            _bar.Add(Ui.Spacer());

            // The test profile: what you start with.
            var profile = _shell.ActiveProfile;
            Button pick = null;
            pick = Ui.Button(profile.Name + "  ", () =>
            {
                var r = pick.worldBound;
                var items = _shell.Profiles.Select(p => { var id = p.Id; return new MenuItem(p.Name, () => _shell.SetActiveProfile(id), isChecked: p.Id == profile.Id); }).ToList();
                items.Add(MenuItem.Separator);
                items.Add(new MenuItem("Editar perfiles…", () => ProfilesDialog.Show(_shell)));
                _shell.ShowMenu(new Vector2(r.x, r.yMax + 2), items);
            }, Ui.ButtonKind.Normal, "Perfil de prueba: con qué empiezas a jugar (equipo, medallas, objetos, interruptores, hora)");
            _bar.With(Ui.Text("Perfil", 0.85f, dim: true), pick);
        }

        /// <summary>Full screen even with this window open (it gives the game the whole application window).</summary>
        private void FullScreen()
        {
            var host = _shell.GameHost;
            _shell.GameHost = null;
            _shell.StartPlay();
            _shell.GameHost = host;
        }
    }
}
