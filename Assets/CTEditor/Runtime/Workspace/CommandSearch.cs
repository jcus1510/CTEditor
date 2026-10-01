using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CTEditor.Workspace
{
    /// <summary>Algo que se puede hacer desde la paleta de órdenes (Ctrl+P): una acción, abrir una ventana, un mapa...</summary>
    public sealed class CommandEntry
    {
        public string Label { get; }
        /// <summary>Grupo que se ve a la derecha («Herramientas», «Mapa», «Ventana»...).</summary>
        public string Category { get; }
        /// <summary>Atajo para mostrar (ya bonito: «Ctrl+S»), o vacío.</summary>
        public string Keys { get; }
        /// <summary>Otras palabras que también lo encuentran («grid» para la rejilla...).</summary>
        public string Extra { get; }
        public Action Run { get; }

        public CommandEntry(string label, string category, Action run, string keys = "", string extra = "")
        {
            Label = label ?? ""; Category = category ?? ""; Run = run; Keys = keys ?? ""; Extra = extra ?? "";
        }
    }

    /// <summary>
    /// Busca en la paleta de órdenes sin tener que escribir exacto: no distingue mayúsculas ni tildes («rejilla» =
    /// «Rejílla»), vale escribir las iniciales o letras salteadas en orden («cdeg» → «Capa de encima... gu...»), y puntúa
    /// mejor lo que empieza igual, las palabras enteras y lo más corto.
    /// </summary>
    public static class CommandSearch
    {
        public static string Fold(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var d = s.Normalize(NormalizationForm.FormD);
            var b = new StringBuilder(d.Length);
            foreach (var c in d)
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) b.Append(char.ToLowerInvariant(c));
            return b.ToString();
        }

        /// <summary>Puntuación (más = mejor) o -1 si no encaja.</summary>
        public static int Score(string query, string text)
        {
            var q = Fold(query).Trim();
            var t = Fold(text);
            if (q.Length == 0) return 1;
            if (t.Length == 0) return -1;
            int at = t.IndexOf(q, StringComparison.Ordinal);
            if (at == 0) return 1000 - t.Length;                                   // starts with it
            if (at > 0 && !char.IsLetterOrDigit(t[at - 1])) return 800 - t.Length;  // a whole word inside
            if (at > 0) return 600 - t.Length;                                       // inside a word
            // Every word of the query somewhere (any order).
            var words = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 1 && words.All(w => t.Contains(w))) return 500 - t.Length;
            // Letters in order (initials or skipped letters), better when they start words.
            int score = 300, ti = 0;
            foreach (var c in q)
            {
                if (c == ' ') continue;
                int found = t.IndexOf(c, ti);
                if (found < 0) return -1;
                bool wordStart = found == 0 || !char.IsLetterOrDigit(t[found - 1]);
                score += wordStart ? 6 : -(found - ti);
                ti = found + 1;
            }
            return Math.Max(1, score - t.Length);
        }

        /// <summary>The best matches, best first (all of them, in order, with an empty query).</summary>
        public static IReadOnlyList<CommandEntry> Find(IEnumerable<CommandEntry> entries, string query, int max = 50)
        {
            return entries.Select((e, i) => (e, i, s: Math.Max(Score(query, e.Label), Score(query, e.Extra) - 50)))
                .Where(x => x.s >= 0)
                .OrderByDescending(x => x.s).ThenBy(x => x.i)
                .Take(max).Select(x => x.e).ToList();
        }
    }
}
