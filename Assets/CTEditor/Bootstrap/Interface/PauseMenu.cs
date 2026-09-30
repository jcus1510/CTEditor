using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CTEditor.Adventure.Domain.Interface;

namespace CTEditor.Bootstrap
{
    /// <summary>
    /// MENÚ DE PAUSA: se abre con el botón Menú (Esc/X por defecto) y usa el menú del editor cuyo id está
    /// en los Ajustes de interfaz (clásico: «pausa» → POKéDEX, POKéMON, MOCHILA, {jugador}, GUARDAR,
    /// OPCIONES, SALIR). Las opciones con marca solo salen si la partida la tiene.
    ///
    /// No se abre durante un combate ni con otro menú abierto. Las opciones «acción de pantalla» que
    /// no conoce se avisan con el evento CustomAction (para tus propias pantallas).
    /// </summary>
    public sealed class PauseMenu : MonoBehaviour
    {
        [Tooltip("Mientras este combate esté en marcha, el menú no se abre.")]
        [SerializeField] private BattleScreen battleScreen;
        [Tooltip("Id del menú a abrir. Vacío = el de los Ajustes de interfaz (clásico: «pausa»).")]
        [SerializeField] private string menuId = "";

        /// <summary>¿Está abierto ahora?</summary>
        public bool IsOpen { get; private set; }

        /// <summary>Se cierra el menú (para refrescar lo que dependa del equipo o la mochila).</summary>
        public event Action Closed;
        /// <summary>El jugador eligió una acción de pantalla que este menú no conoce (id).</summary>
        public event Action<string> CustomAction;

        public void Configure(BattleScreen battle) => battleScreen = battle;

        private string MenuId => !string.IsNullOrWhiteSpace(menuId) ? menuId
            : GameInput.Style != null && !string.IsNullOrWhiteSpace(GameInput.Style.PauseMenuId) ? GameInput.Style.PauseMenuId : ClassicInterface.PauseMenu;

        private void Update()
        {
            if (IsOpen || UiRoot.ModalOpen) return;
            if (battleScreen != null && battleScreen.IsRunning) return;
            if (!GameInput.Pressed(GameButton.Menu)) return;
            GameInput.ConsumeAll(); // que la misma tecla no cierre el menú recién abierto
            Open();
        }

        /// <summary>Abre el menú desde código (un botón en pantalla, un evento...).</summary>
        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            UiRoot.Register(this);
            StartCoroutine(SafeCoroutine.Run(Run(), OnError));
        }

        // Si una pantalla falla (contenido roto...), se avisa y se cierra todo: el jugador nunca se queda atascado.
        private void OnError(Exception e)
        {
            Debug.LogException(e);
            Cleanup();
        }

        // Si desactivan este objeto a medias (empieza un combate, cambia la escena), se cierra todo con orden.
        private void OnDisable()
        {
            if (!IsOpen) return;
            StopAllCoroutines();
            Cleanup();
        }

        private void Cleanup()
        {
            MenuStack.CloseAll();
            var box = UiRoot.Existing != null ? UiRoot.Existing.ExistingText : null;
            if (box != null) box.ForceHide();
            Finish();
        }

        private void Finish()
        {
            if (!IsOpen) return;
            IsOpen = false;
            UiRoot.Unregister(this);
            Closed?.Invoke();
        }

        private IEnumerator Run()
        {
            var root = MenuOpener.Definition(MenuId);
            if (root == null) { yield return UiRoot.Instance.Text.Say($"No existe el menú «{MenuId}»."); Finish(); yield break; }
            var stack = new List<MenuDefinition> { root };
            var save = PartyHolder.Current != null ? PartyHolder.Current.Save : null;
            while (stack.Count > 0)
            {
                var top = stack[stack.Count - 1];
                var menu = MenuOpener.Open(top.Id, top, f => save != null && save.World.HasFlag(f), null, UiContent.Variables(save));
                yield return menu.Wait();
                var r = menu.Result;
                menu.Close();
                if (r == null) { stack.RemoveAt(stack.Count - 1); continue; }
                switch (r.Action)
                {
                    case MenuActionKind.Close: stack.Clear(); break;
                    case MenuActionKind.OpenMenu:
                        var sub = MenuOpener.Definition(r.Target);
                        if (sub != null) stack.Add(sub); else yield return UiRoot.Instance.Text.Say($"(El menú «{r.Target}» no existe.)");
                        break;
                    case MenuActionKind.ScreenAction:
                        if (CustomAction != null) CustomAction(r.Target);
                        else yield return UiRoot.Instance.Text.Say($"(«{r.Label}»: acción «{r.Target}» sin pantalla todavía.)");
                        break;
                    default:
                        yield return FieldScreens.Common(r);
                        break;
                }
            }
            Finish();
        }
    }
}
