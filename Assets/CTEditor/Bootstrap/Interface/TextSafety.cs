using System.Collections.Generic;
using System.Text;
using UnityEngine;
using TMPro;

namespace CTEditor.Bootstrap
{
    /// <summary>
    /// TEXTOS SIN «□»: la fuente por defecto de TextMeshPro (LiberationSans) no trae emojis ni algunos
    /// símbolos (▶ ◆ ₽ ⚔ ↺...). Si un texto los usa, TMP pone un cuadradito y llena la consola de avisos.
    ///
    /// Clean(...) revisa cada carácter CONTRA LA FUENTE REAL del texto:
    ///   • Si la fuente lo tiene (o su fuente de respaldo), se queda. Con TU fuente, tus símbolos se ven.
    ///   • Si no, se cambia por uno parecido que sí suele estar (▶ → ►, ◆ → •, ₽ → $...) o se quita (emojis).
    /// FixTree(...) limpia todos los textos de una parte de la escena (para escenas creadas antes).
    /// </summary>
    public static class TextSafety
    {
        // Parecidos que están en casi todas las fuentes (incluida LiberationSans).
        private static readonly Dictionary<char, string> Similar = new Dictionary<char, string>
        {
            ['▶'] = "►", ['◀'] = "◄", ['▸'] = "►", ['◂'] = "◄", ['➜'] = "→", ['➔'] = "→",
            ['◆'] = "•", ['◇'] = "◊", ['✦'] = "•", ['★'] = "*", ['☆'] = "*",
            ['✕'] = "×", ['✖'] = "×", ['✔'] = "√", ['✓'] = "√",
            ['₽'] = "$", ['⚠'] = "!", ['…'] = "...",
        };

        public static string Clean(string text, TMP_FontAsset font = null)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            if (font == null) font = TMP_Settings.defaultFontAsset;
            bool defaultFont = font == null || font.name.StartsWith("LiberationSans");
            var sb = new StringBuilder(text.Length);
            bool changed = false, inTag = false, dropped = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\uFE0F' || c == '\u200D') { changed = true; continue; } // selectores de emoji
                // Quitar un icono deja un espacio sobrante: "⚔ Luchar" → "Luchar".
                if (dropped && c == ' ') { dropped = false; continue; }
                dropped = false;
                if (c == '<') inTag = true;
                if (inTag) { sb.Append(c); if (c == '>') inTag = false; continue; }

                // Emojis y otros caracteres fuera del plano básico (dos "char"): fuera con la fuente por defecto.
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    if (defaultFont) { changed = dropped = true; i++; continue; }
                    sb.Append(c).Append(text[i + 1]); i++; continue;
                }
                if (c < 0x2000 || Has(font, c)) { sb.Append(c); continue; }

                changed = true;
                if (Similar.TryGetValue(c, out var alt) && (alt.Length > 1 || Has(font, alt[0]))) sb.Append(alt);
                else dropped = true;
            }
            return changed ? sb.ToString() : text;
        }

        // En el editor no se añaden glifos al atlas (ensuciaría el asset de la fuente); jugando, sí (fuentes dinámicas).
        private static bool Has(TMP_FontAsset font, char c) => font == null || font.HasCharacter(c, true, Application.isPlaying);

        /// <summary>Asigna un texto ya limpio para la fuente de ese componente.</summary>
        public static void Set(TMP_Text label, string text)
        {
            if (label == null) return;
            label.text = Clean(text, label.font);
        }

        /// <summary>Limpia todos los textos bajo 'root' (también los ocultos).</summary>
        public static void FixTree(Transform root)
        {
            if (root == null) return;
            foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
            {
                string clean = Clean(t.text, t.font);
                if (clean != t.text) t.text = clean;
            }
        }
    }
}
