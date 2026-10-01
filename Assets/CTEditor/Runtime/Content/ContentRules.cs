using System;
using System.Collections.Generic;
using System.Linq;

namespace CTEditor.Content
{
    /// <summary>The six stats: their id in the sheets and their names for people.</summary>
    public static class StatNames
    {
        public static readonly string[] Ids = { "hp", "attack", "defense", "sp_attack", "sp_defense", "speed" };
        public static readonly string[] Labels = { "PS", "Ataque", "Defensa", "At. esp.", "Def. esp.", "Velocidad" };
        /// <summary>The short names of EV / IV spreads («252 AtqE/4 PS»).</summary>
        public static readonly string[] Short = { "PS", "Atq", "Def", "AtqE", "DefE", "Vel" };
        private static readonly string[][] Aliases =
        {
            new[] { "ps", "hp" }, new[] { "atq", "atk", "ataque", "attack" }, new[] { "def", "defensa", "defense" },
            new[] { "atqe", "spa", "spatk", "atq_esp", "sp_attack", "ate" }, new[] { "defe", "spd", "spdef", "def_esp", "sp_defense", "dfe" },
            new[] { "vel", "spe", "velocidad", "speed" },
        };

        public static int IndexOf(string name)
        {
            var n = (name ?? "").Trim().ToLowerInvariant().Replace(".", "").Replace(" ", "");
            for (int i = 0; i < Aliases.Length; i++) if (Aliases[i].Contains(n)) return i;
            return -1;
        }

        public static string Label(string id) { int i = IndexOf(id); return i < 0 ? id : Labels[i]; }

        /// <summary>«252 Atq/4 PS» → six numbers (missing = 0, or 'fill' for IVs).</summary>
        public static int[] ParseSpread(string text, int fill = 0)
        {
            var v = Enumerable.Repeat(fill, 6).ToArray();
            foreach (var part in (text ?? "").Split('/'))
            {
                var bits = part.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (bits.Length < 2 || !int.TryParse(bits[0], out var n)) continue;
                int i = IndexOf(bits[1]);
                if (i >= 0) v[i] = n;
            }
            return v;
        }

        /// <summary>Six numbers → «252 Atq/4 PS» (only the ones different from 'skip').</summary>
        public static string FormatSpread(int[] v, int skip = 0) =>
            string.Join("/", Enumerable.Range(0, 6).Where(i => v[i] != skip).Select(i => $"{v[i]} {Short[i]}"));

        /// <summary>«attack:1|speed:2» ↔ pairs.</summary>
        public static List<(string stat, int amount)> ParsePairs(string text) =>
            (text ?? "").Split('|').Select(p => p.Split(':')).Where(p => p.Length == 2 && p[0].Trim().Length > 0)
                .Select(p => (p[0].Trim(), int.TryParse(p[1].Trim(), out var n) ? n : 0)).ToList();

        public static string FormatPairs(IEnumerable<(string stat, int amount)> pairs) => string.Join("|", pairs.Select(p => $"{p.stat}:{p.amount}"));
    }

    /// <summary>Categories of abilities for filters: guessed from their effects, changeable by hand («categoria» column).</summary>
    public static class AbilityCategories
    {
        public static readonly string[] All =
            { "Clima", "Absorber", "Inmunidad", "Ignorar tipo", "Potenciar", "Defensa", "Estados", "Estadísticas", "Entrada", "Contacto", "Prioridad", "Apoyo", "Atrapar", "Otros" };

        private static readonly (string category, string[] words)[] Rules =
        {
            ("Clima", new[] { "clima", "lluvia", "sol", "arena", "granizo", "nieve" }),
            ("Absorber", new[] { "absorber", "al_absorber" }),
            ("Inmunidad", new[] { "inmune" }),
            ("Ignorar tipo", new[] { "ignora", "convierte_normal", "tipo_normal_a" }),
            ("Atrapar", new[] { "atrapa" }),
            ("Contacto", new[] { "contacto" }),
            ("Entrada", new[] { "al_entrar", "anuncia" }),
            ("Prioridad", new[] { "prioridad", "actua_primero" }),
            ("Estados", new[] { "estado" }),
            ("Defensa", new[] { "daño_recibido", "potencia_recibida", "aguanta", "sobrevive" }),
            ("Potenciar", new[] { "potencia", "stab", "daño_hecho", "critico" }),
            ("Estadísticas", new[] { "etapa", "stat", "precision", "evasion" }),
            ("Apoyo", new[] { "cura", "lado", "pantalla" }),
        };

        public static string Guess(string effects)
        {
            var t = (effects ?? "").ToLowerInvariant();
            foreach (var (category, words) in Rules) if (words.Any(w => t.Contains(w))) return category;
            return "Otros";
        }

        public static string Of(ContentRecord ability) =>
            ability["categoria"].Trim().Length > 0 ? ability["categoria"].Trim() : Guess(ability["efectos"]);
    }

    /// <summary>Rules about what a species can have: its abilities and the moves it can learn (also from earlier stages).</summary>
    public static class Legality
    {
        public static IEnumerable<string> AbilitiesOf(ContentRecord species) =>
            new[] { "habilidad", "habilidad_2", "habilidad_oculta" }.Select(c => species[c].Trim()).Where(x => x.Length > 0);

        /// <summary>Every move a species can know: level, MT, tutor, egg, also those of the ones it evolves from.</summary>
        public static HashSet<string> MovesOf(ContentTable species, string id, int depth = 0)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var r = species.Find(id);
            if (r == null || depth > 5) return set;
            foreach (var e in r["aprende"].Split('|')) { int i = e.IndexOf(':'); var m = (i >= 0 ? e.Substring(i + 1) : e).Trim(); if (m.Length > 0) set.Add(m); }
            foreach (var c in new[] { "mt", "tutor", "huevo" })
                foreach (var m in r[c].Split('|', ',')) if (m.Trim().Length > 0) set.Add(m.Trim());
            foreach (var pre in species.Records.Where(x => x["evoluciona"].Split('|').Any(e => string.Equals(e.Split('@')[0].Trim(), id, StringComparison.OrdinalIgnoreCase))))
                set.UnionWith(MovesOf(species, species.IdOf(pre), depth + 1));
            var baseForm = r["forma_de"].Trim();
            if (baseForm.Length > 0) set.UnionWith(MovesOf(species, baseForm, depth + 1));
            return set;
        }

        /// <summary>The id of a competitive set: «heatran_ou_z_move».</summary>
        public static string SetId(string species, string format, string name) => ContentIds.Normalize($"{species}_{format}_{name}");
    }

    /// <summary>The six classic growth curves as rows (to create them when the project has none).</summary>
    public static class ClassicCurves
    {
        public static readonly (string id, string name, string shape)[] All =
        {
            ("fast", "Rápida", "fast"), ("medium_fast", "Media", "medium_fast"), ("medium_slow", "Parabólica", "medium_slow"),
            ("slow", "Lenta", "slow"), ("erratic", "Errática", "erratic"), ("fluctuating", "Fluctuante", "fluctuating"),
        };

        public static ContentRecord Row(string id)
        {
            var c = All.First(x => x.id == id);
            var r = new ContentRecord();
            r["id"] = c.id; r["nombre"] = c.name; r["forma"] = c.shape; r["nivel_max"] = "100"; r["multiplicador_xp"] = "1";
            return r;
        }
    }
}
