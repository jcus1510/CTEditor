using System;
using System.Linq;
using CTEditor.Adventure.Domain.Interface;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Infrastructure.Acl
{
    /// <summary>ACL de la interfaz: MenuData e InterfaceSettingsData (Unity) → dominio puro.</summary>
    public static class InterfaceMapper
    {
        public static MenuDefinition ToDomain(MenuData d)
        {
            if (d == null) throw new ArgumentNullException(nameof(d));
            var m = new MenuDefinition(d.Id, d.DisplayName)
            {
                Title = d.Title ?? "",
                Columns = Math.Max(1, d.Columns),
                Wrap = d.Wrap,
                RememberCursor = d.RememberCursor,
                CancelCloses = d.CancelCloses,
                CancelOptionId = d.CancelOptionId ?? "",
                Anchor = d.Anchor,
            };
            foreach (var o in d.Options.Where(o => o != null))
                m.Add(new MenuOption(o.id, o.label, o.action, o.target, o.requiredFlag, o.hiddenByFlag, o.help));
            return m;
        }

        /// <summary>
        /// Ajustes → dominio. Un botón sin teclas (o sin mando) en la ficha recibe los clásicos de ese tipo: así un
        /// ajuste a medio hacer nunca deja al jugador sin poder moverse.
        /// </summary>
        public static InterfaceSettings ToDomain(InterfaceSettingsData d)
        {
            if (d == null) return new InterfaceSettings();
            var classic = ClassicInterface.Bindings();
            var b = new InputBindings { RepeatDelay = d.RepeatDelay, RepeatInterval = d.RepeatInterval };
            foreach (var button in GameButtons.All)
            {
                var keys = d.Bindings.Where(x => x != null && x.button == button).SelectMany(x => x.keys ?? new string[0])
                    .Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).ToList();
                // Por TIPO: si la ficha no dice nada del teclado (o del mando) de un botón, se usa lo clásico.
                var keyboard = keys.Where(k => !PadControls.IsPad(k)).ToList();
                var pad = keys.Where(PadControls.IsPad).ToList();
                b.Bind(button, (keyboard.Count > 0 ? keyboard : classic.Keyboard(button)).ToArray());
                b.Bind(button, (pad.Count > 0 ? pad : classic.Pad(button)).ToArray());
            }
            return new InterfaceSettings
            {
                Bindings = b,
                TextSpeed = d.TextSpeed,
                LinesPerPage = d.LinesPerPage,
                CharsPerLine = d.CharsPerLine,
                AutoAdvanceSeconds = d.AutoAdvanceSeconds,
                CancelAdvancesText = d.CancelAdvancesText,
                HoldToSpeedUp = d.HoldToSpeedUp,
                CursorSymbol = string.IsNullOrEmpty(d.CursorSymbol) ? "►" : d.CursorSymbol,
                MoreSymbol = string.IsNullOrEmpty(d.MoreSymbol) ? "▼" : d.MoreSymbol,
            };
        }
    }
}
