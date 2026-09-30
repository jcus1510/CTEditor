using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using CTEditor.Adventure.Domain.Interface;

namespace CTEditor.Bootstrap
{
    /// <summary>
    /// LA CAJA DE TEXTO del juego (diálogos, avisos, mensajes de la mochila...), reutilizable:
    ///   • Reparte el texto en cajas de N líneas (Ajustes de interfaz) y lo TECLEA.
    ///   • Confirmar completa la caja; otra vez pasa a la siguiente (▼ parpadea cuando espera).
    ///   • Mantener Confirmar acelera; Cancelar también avanza si así está configurado.
    ///   • Variables: {jugador}, {dinero}... y una LÍNEA EN BLANCO fuerza caja nueva.
    ///   • Ask(...) pregunta Sí/No con el menú «si_no».
    /// Uso: yield return UiRoot.Instance.Text.Say("¡Hola, {jugador}!", variables);
    /// </summary>
    public sealed class TextBox : MonoBehaviour
    {
        private TMP_Text _text;
        private GameObject _more;   // el indicador «hay más» (puede no haber)
        private RectTransform _frame;
        private int _users;

        public bool IsBusy => _users > 0;
        /// <summary>Alto de la caja en la pantalla de referencia (para colocar menús encima).</summary>
        public float Height { get; private set; }
        /// <summary>¿Está tecleando o esperando a que se lea? (los menús de debajo no reaccionan mientras).</summary>
        public bool IsSaying => _saying > 0;
        private int _saying;

        /// <summary>Usa la caja de texto diseñada en la escena (DialogueBoxView).</summary>
        internal static TextBox Attach(DialogueBoxView view)
        {
            var box = view.GetComponent<TextBox>();
            if (box == null) box = view.gameObject.AddComponent<TextBox>();
            box._frame = (RectTransform)view.transform;
            box._text = view.Text;
            box._more = view.MoreIndicator;
            if (box._text != null) box._text.richText = true;
            box.Height = box._frame.rect.height > 1f ? box._frame.rect.height + 8f : 160f;
            UiRoot.RaiseAboveBlocker(view);
            if (box._more != null) box._more.SetActive(false);
            view.gameObject.SetActive(false);
            return box;
        }

        internal static TextBox Create(RectTransform root)
        {
            var go = new GameObject("Caja de texto", typeof(RectTransform));
            go.transform.SetParent(root, false);
            var box = go.AddComponent<TextBox>();
            box._frame = (RectTransform)go.transform;
            var inner = UiKit.FramedBox(box._frame, "Marco");
            var frameChild = (RectTransform)inner.parent;
            frameChild.anchorMin = Vector2.zero; frameChild.anchorMax = Vector2.one; frameChild.offsetMin = frameChild.offsetMax = Vector2.zero;

            var s = GameInput.Settings;
            float font = UiKit.FontSize;
            float h = s.LinesPerPage * font * 1.35f + 40f;
            UiKit.Anchor(box._frame, MenuAnchor.BottomFull, 0, h, 12f);
            box.Height = h + 12f;

            box._text = UiKit.Label(inner, "Texto", "", font, UiKit.Ink, TextAlignmentOptions.TopLeft);
            var rt = box._text.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(22, 14); rt.offsetMax = new Vector2(-50, -14);
            box._text.richText = true;

            var more = UiKit.Label(inner, "Más", s.MoreSymbol, font * 0.8f, UiKit.Highlight, TextAlignmentOptions.Center);
            box._more = more.gameObject;
            var mr = more.rectTransform;
            mr.anchorMin = mr.anchorMax = new Vector2(1, 0); mr.pivot = new Vector2(1, 0);
            mr.anchoredPosition = new Vector2(-14, 10); mr.sizeDelta = new Vector2(30, 30);
            go.SetActive(false);
            return box;
        }

        public void Hide() { if (_users <= 0) gameObject.SetActive(false); }

        /// <summary>Cierra la caja a la fuerza (al interrumpir una pantalla a medias).</summary>
        public void ForceHide()
        {
            _users = 0; _saying = 0;
            UiRoot.Unregister(this);
            gameObject.SetActive(false);
        }

        private void OnDisable() { _saying = 0; UiRoot.Unregister(this); }

        /// <summary>Muestra un texto completo (varias cajas si hace falta) y espera a que el jugador lo lea.</summary>
        public IEnumerator Say(string text, IReadOnlyDictionary<string, string> vars = null, bool waitAtEnd = true, bool hideAfter = true)
        {
            _users++;
            _saying++;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            UiRoot.Register(this);
            try
            {
                var s = GameInput.Settings;
                var pages = TextPager.Paginate(TextSafety.Clean(TextTokens.Replace(text ?? "", vars ?? new Dictionary<string, string>()), _text.font), s.CharsPerLine, s.LinesPerPage);
                for (int p = 0; p < pages.Count; p++)
                {
                    bool last = p == pages.Count - 1;
                    var tw = new Typewriter(pages[p], s.TextSpeed);
                    if (_more != null) _more.SetActive(false);
                    while (!tw.IsDone)
                    {
                        bool fast = s.HoldToSpeedUp && (GameInput.Held(GameButton.Confirm) || GameInput.Held(GameButton.Cancel));
                        tw.Tick(Time.unscaledDeltaTime * (fast ? 3f : 1f));
                        if (GameInput.AdvancePressed()) { GameInput.Consume(GameButton.Confirm); GameInput.Consume(GameButton.Cancel); tw.Complete(); }
                        _text.text = tw.Visible;
                        yield return null;
                    }
                    _text.text = tw.Text;
                    if (last && !waitAtEnd) break;

                    // Espera: ▼ parpadea; avanza con Confirmar (o solo, si hay avance automático).
                    float waited = 0f;
                    while (true)
                    {
                        waited += Time.unscaledDeltaTime;
                        if (_more != null) _more.SetActive(Mathf.Repeat(waited, 0.8f) < 0.5f);
                        if (GameInput.AdvancePressed()) { GameInput.Consume(GameButton.Confirm); GameInput.Consume(GameButton.Cancel); break; }
                        if (s.AutoAdvanceSeconds > 0f && waited >= s.AutoAdvanceSeconds) break;
                        yield return null;
                    }
                    if (_more != null) _more.SetActive(false);
                }
            }
            finally
            {
                _users = Math.Max(0, _users - 1);
                _saying = Math.Max(0, _saying - 1);
                if (_saying == 0) UiRoot.Unregister(this);
                if (hideAfter) Hide();
            }
        }

        /// <summary>Varios mensajes seguidos (cada uno en su caja).</summary>
        public IEnumerator SayAll(IEnumerable<string> lines, IReadOnlyDictionary<string, string> vars = null)
        {
            foreach (var l in lines) if (!string.IsNullOrWhiteSpace(l)) yield return Say(l, vars, true, false);
            Hide();
        }

        /// <summary>Pregunta Sí/No (menú «si_no»; Cancelar = No). El texto queda visible mientras eliges.</summary>
        public IEnumerator Ask(string question, Action<bool> answer, IReadOnlyDictionary<string, string> vars = null)
        {
            _users++;
            yield return Say(question, vars, waitAtEnd: false, hideAfter: false);
            var menu = MenuOpener.Open(ClassicInterface.YesNo, null, null, null, vars);
            yield return menu.Wait();
            bool yes = menu.Result != null && (menu.Result.Id == ScreenActions.Yes || menu.Result.Target == ScreenActions.Yes);
            menu.Close();
            _users = Math.Max(0, _users - 1);
            Hide();
            answer?.Invoke(yes);
        }

        /// <summary>Deja un texto fijo (sin esperar), por ejemplo «¿A quién?» mientras eliges en un menú.</summary>
        public void Show(string text, IReadOnlyDictionary<string, string> vars = null)
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            if (_more != null) _more.SetActive(false);
            TextSafety.Set(_text, TextTokens.Replace(text ?? "", vars ?? new Dictionary<string, string>()));
        }
    }
}
