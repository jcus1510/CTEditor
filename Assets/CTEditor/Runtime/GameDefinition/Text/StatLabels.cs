using System;

namespace CTEditor.GameDefinition.Text
{
    /// <summary>Nombres en español de las stats clásicas (para desplegables, tablas y gráficos).</summary>
    public static class StatLabels
    {
        public static readonly string[] ClassicIds = { "hp", "attack", "defense", "sp_attack", "sp_defense", "speed" };
        public static readonly string[] ClassicNames = { "PS", "Ataque", "Defensa", "Atq. Esp.", "Def. Esp.", "Velocidad" };

        /// <summary>Nombre legible de una stat ("attack" -> "Ataque"); si es inventada, su propio id.</summary>
        public static string NameOf(string statId)
        {
            int i = Array.IndexOf(ClassicIds, statId);
            if (i >= 0) return ClassicNames[i];
            if (statId == "accuracy") return "Precisión";
            if (statId == "evasion") return "Evasión";
            return statId;
        }
    }

}
