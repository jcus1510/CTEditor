using System;
using System.Collections.Generic;
using System.Linq;

namespace CTEditor.Content
{
    /// <summary>
    /// The categories of items, as the bag of the games groups them (few, not one per kind of effect). The CSV keeps the
    /// code the engine reads (Medicine, Ball...); the windows show the Spanish name. Old finer codes (Revive, Vitamin,
    /// Held...) still load: they count as the category they belong to.
    /// </summary>
    public static class ItemCategories
    {
        public static readonly string[] All = { "Medicine", "Ball", "BattleBoost", "Berry", "Machine", "Key", "Other" };

        private static readonly Dictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Medicine"] = "Medicinas", ["Ball"] = "Poké Balls", ["BattleBoost"] = "Objetos de combate", ["Berry"] = "Bayas",
            ["Machine"] = "MT y MO", ["Key"] = "Objetos clave", ["Other"] = "Objetos",
        };

        private static readonly Dictionary<string, string> Old = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Revive"] = "Medicine", ["StatusCure"] = "Medicine", ["PpRestore"] = "Medicine", ["Vitamin"] = "Medicine",
            ["Held"] = "Other", ["Evolution"] = "Other",
        };

        /// <summary>The category a stored code belongs to («Vitamin» → «Medicine»); empty stays empty.</summary>
        public static string Normalize(string code)
        {
            code = (code ?? "").Trim();
            if (code.Length == 0) return "";
            if (Old.TryGetValue(code, out var n)) return n;
            var known = All.FirstOrDefault(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase));
            return known ?? code;
        }

        public static bool IsKnown(string code) => Labels.ContainsKey(Normalize(code));

        public static string Label(string code)
        {
            var n = Normalize(code);
            return Labels.TryGetValue(n, out var l) ? l : n;
        }
    }

    /// <summary>
    /// The Spanish names of the stored values the windows show (codes stay as they are in the CSV, so the game and the
    /// translations read them): «rival» → «Un rival», «carga» → «Carga un turno», «Ball» → «Poké Balls»...
    /// </summary>
    public static class ContentLabels
    {
        private static readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Moves
            ["fisico"] = "Físico", ["especial"] = "Especial", ["estado"] = "Estado",
            ["objetivo.rival"] = "Un rival", ["objetivo.rivales"] = "Todos los rivales", ["objetivo.propio"] = "Él mismo",
            ["objetivo.todos"] = "Todos", ["objetivo.aliado"] = "Un aliado", ["objetivo.aliados"] = "Los aliados", ["objetivo.campo"] = "El campo",
            ["dos_turnos.no"] = "No", ["dos_turnos.carga"] = "Carga un turno", ["dos_turnos.recarga"] = "Recarga después",
            ["precision.nunca"] = "Nunca falla",
            ["daño_especial.ko"] = "KO directo", ["daño_especial.nivel"] = "Igual al nivel", ["daño_especial.mitad"] = "Mitad de los PS",
            ["daño_especial.venganza"] = "Venganza", ["daño_especial.esfuerzo"] = "Esfuerzo", ["daño_especial.ps_propios"] = "Sus PS",
            ["daño_especial.devolver_fisico"] = "Devuelve lo físico", ["daño_especial.devolver_especial"] = "Devuelve lo especial",
            // Trainers
            ["ia.novato"] = "Novato", ["ia.listo"] = "Listo", ["ia.experto"] = "Experto",
            // Species
            ["hembras.sin_genero"] = "Sin género",
            // Sets
            ["formato.ou"] = "OU", ["formato.uu"] = "UU", ["formato.ru"] = "RU", ["formato.nu"] = "NU", ["formato.pu"] = "PU",
            ["formato.lc"] = "LC (pequeños)", ["formato.ubers"] = "Ubers", ["formato.vgc"] = "VGC (dobles)", ["formato.doubles"] = "Dobles",
            // Yes / no
            ["si"] = "Sí", ["no"] = "No",
        };

        /// <summary>The Spanish name of a stored value of a column (or the value itself, made readable).</summary>
        public static string Of(string category, string column, string value)
        {
            var v = (value ?? "").Trim();
            if (v.Length == 0) return "";
            if (category == ContentSchemas.Items && column == "categoria") return ItemCategories.Label(v);
            if (Values.TryGetValue(column + "." + v, out var l)) return l;
            if (Values.TryGetValue(v, out l)) return l;
            int stat = StatNames.IndexOf(v);
            if (stat >= 0) return StatNames.Labels[stat];
            if (v.StartsWith("fijo:", StringComparison.OrdinalIgnoreCase)) return "Fijo: " + v.Substring(5);
            if (v.StartsWith("devolver:", StringComparison.OrdinalIgnoreCase)) return "Devuelve ×" + v.Substring(9) + "%";
            return Readable(v);
        }

        /// <summary>«medium_slow» → «Medium slow»: a code without underscores and with a capital.</summary>
        public static string Readable(string code)
        {
            var v = (code ?? "").Trim().Replace('_', ' ');
            return v.Length == 0 ? v : char.ToUpperInvariant(v[0]) + v.Substring(1);
        }
    }
}
