using System;
using System.Collections.Generic;
using System.Text;

namespace CTEditor.Adventure.Domain.Interface
{
    /// <summary>
    /// VARIABLES en los textos: «¡Hola, {jugador}!» → «¡Hola, Rojo!». Sin distinguir mayúsculas.
    /// Una variable que no existe se deja tal cual (así el autor ve que se equivocó al escribirla).
    /// </summary>
    public static class TextTokens
    {
        public static string Replace(string text, IReadOnlyDictionary<string, string> values)
        {
            if (string.IsNullOrEmpty(text) || values == null || values.Count == 0 || text.IndexOf('{') < 0) return text ?? "";
            var sb = new StringBuilder(text.Length + 16);
            int i = 0;
            while (i < text.Length)
            {
                int open = text.IndexOf('{', i);
                if (open < 0) { sb.Append(text, i, text.Length - i); break; }
                int close = text.IndexOf('}', open + 1);
                if (close < 0) { sb.Append(text, i, text.Length - i); break; }
                sb.Append(text, i, open - i);
                string name = text.Substring(open + 1, close - open - 1).Trim();
                string value = null;
                foreach (var kv in values)
                    if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)) { value = kv.Value; break; }
                sb.Append(value ?? text.Substring(open, close - open + 1));
                i = close + 1;
            }
            return sb.ToString();
        }

        /// <summary>Las variables que usa un texto (para avisar en el editor de las desconocidas).</summary>
        public static List<string> Find(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(text)) return list;
            int i = 0;
            while (true)
            {
                int open = text.IndexOf('{', i); if (open < 0) break;
                int close = text.IndexOf('}', open + 1); if (close < 0) break;
                string name = text.Substring(open + 1, close - open - 1).Trim();
                if (name.Length > 0 && !list.Contains(name)) list.Add(name);
                i = close + 1;
            }
            return list;
        }
    }

    /// <summary>
    /// PÁGINAS de la caja de texto (puro). Parte un texto largo en páginas de N líneas de como mucho M
    /// caracteres, cortando por palabras (como los juegos: 2 líneas por caja y ▼ para seguir).
    ///   • Una LÍNEA EN BLANCO fuerza página nueva (el autor decide dónde parar).
    ///   • Un salto de línea simple fuerza línea nueva.
    ///   • Las etiquetas de formato &lt;color=red&gt;...&lt;/color&gt; no cuentan como caracteres.
    /// </summary>
    public static class TextPager
    {
        public static List<string> Paginate(string text, int charsPerLine, int linesPerPage)
        {
            var pages = new List<string>();
            if (string.IsNullOrEmpty(text)) return pages;
            charsPerLine = Math.Max(4, charsPerLine);
            linesPerPage = Math.Max(1, linesPerPage);

            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            foreach (var block in normalized.Split(new[] { "\n\n" }, StringSplitOptions.None))
            {
                if (block.Trim().Length == 0) continue;
                var lines = new List<string>();
                foreach (var paragraph in block.Split('\n'))
                    lines.AddRange(Wrap(paragraph, charsPerLine));
                for (int i = 0; i < lines.Count; i += linesPerPage)
                    pages.Add(string.Join("\n", lines.GetRange(i, Math.Min(linesPerPage, lines.Count - i))));
            }
            return pages;
        }

        /// <summary>Corta un párrafo en líneas de como mucho 'width' caracteres visibles.</summary>
        public static List<string> Wrap(string paragraph, int width)
        {
            var lines = new List<string>();
            var current = new StringBuilder();
            int currentLen = 0;
            foreach (var word in (paragraph ?? "").Split(' '))
            {
                if (word.Length == 0) continue;
                int len = VisibleLength(word);
                // Palabra más larga que la línea: se parte a trozos.
                if (len > width && !word.Contains("<"))
                {
                    if (currentLen > 0) { lines.Add(current.ToString()); current.Clear(); currentLen = 0; }
                    for (int i = 0; i < word.Length; i += width)
                    {
                        string piece = word.Substring(i, Math.Min(width, word.Length - i));
                        if (piece.Length == width) lines.Add(piece); else { current.Append(piece); currentLen = piece.Length; }
                    }
                    continue;
                }
                int needed = currentLen == 0 ? len : currentLen + 1 + len;
                if (needed > width && currentLen > 0)
                {
                    lines.Add(current.ToString());
                    current.Clear(); currentLen = 0;
                }
                if (currentLen > 0) { current.Append(' '); currentLen++; }
                current.Append(word); currentLen += len;
            }
            if (currentLen > 0 || lines.Count == 0) lines.Add(current.ToString());
            return lines;
        }

        /// <summary>Caracteres que se VEN (sin contar etiquetas &lt;...&gt;).</summary>
        public static int VisibleLength(string s)
        {
            int n = 0; bool tag = false;
            foreach (char c in s ?? "")
            {
                if (c == '<') tag = true;
                else if (c == '>' && tag) tag = false;
                else if (!tag) n++;
            }
            return n;
        }
    }

    /// <summary>
    /// EFECTO DE TECLEO (puro): cuántos caracteres se ven según el tiempo. Confirmar a mitad lo completa
    /// de golpe. Las etiquetas de formato aparecen enteras (nunca se ve «&lt;col» a medias).
    /// </summary>
    public sealed class Typewriter
    {
        private float _progress;
        public string Text { get; }
        public float CharsPerSecond { get; }
        public int VisibleCount { get; private set; }
        public int TotalVisible { get; }
        public bool IsDone => VisibleCount >= TotalVisible;

        public Typewriter(string text, float charsPerSecond)
        {
            Text = text ?? "";
            CharsPerSecond = charsPerSecond;
            TotalVisible = TextPager.VisibleLength(Text);
            if (charsPerSecond <= 0f) VisibleCount = TotalVisible;
        }

        public void Tick(float deltaTime)
        {
            if (IsDone) return;
            _progress += Math.Max(0f, deltaTime) * CharsPerSecond;
            VisibleCount = Math.Min(TotalVisible, (int)Math.Floor(_progress));
        }

        public void Complete() => VisibleCount = TotalVisible;

        /// <summary>El texto que se ve ahora (con sus etiquetas completas).</summary>
        public string Visible
        {
            get
            {
                if (IsDone) return Text;
                var sb = new StringBuilder();
                int shown = 0; bool tag = false;
                foreach (char c in Text)
                {
                    if (c == '<') tag = true;
                    if (tag) { sb.Append(c); if (c == '>') tag = false; continue; }
                    if (shown >= VisibleCount) break;
                    sb.Append(c); shown++;
                }
                return sb.ToString();
            }
        }
    }

    /// <summary>
    /// AJUSTES DE INTERFAZ (puros): teclas, velocidad del texto y tamaño de la caja. Los colores y fuentes
    /// viven en la capa de Unity (InterfaceSettingsData), que es quien dibuja.
    /// </summary>
    public sealed class InterfaceSettings
    {
        public InputBindings Bindings { get; set; } = ClassicInterface.Bindings();
        /// <summary>Caracteres por segundo del tecleo (0 = instantáneo).</summary>
        public float TextSpeed { get; set; } = 40f;
        public int LinesPerPage { get; set; } = 2;
        public int CharsPerLine { get; set; } = 36;
        /// <summary>Si &gt; 0, el texto avanza solo tras estos segundos (además de con Confirmar).</summary>
        public float AutoAdvanceSeconds { get; set; }
        /// <summary>¿Cancelar también avanza el texto? (en los juegos clásicos, sí).</summary>
        public bool CancelAdvancesText { get; set; } = true;
        /// <summary>¿Mantener Confirmar/Cancelar acelera el texto?</summary>
        public bool HoldToSpeedUp { get; set; } = true;
        public string CursorSymbol { get; set; } = "►"; // ► y ▼ están en la fuente por defecto de TextMeshPro
        public string MoreSymbol { get; set; } = "▼";
    }
}
