using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;

namespace CTEditor.GameDefinition.Domain.Growth
{
    /// <summary>
    /// Una CURVA DE EXPERIENCIA: define cuánta XP TOTAL hace falta para SER cada nivel. Es contenido
    /// del juego (como una Species o un Move), así que un creador puede definir varias y elegir cuál
    /// usa cada especie.
    ///
    /// La representamos como una TABLA ACUMULADA por nivel (índice = nivel, valor = XP total para
    /// alcanzarlo), con el umbral del nivel 1 = 0. Es la forma más libre: un editor puede generarla a
    /// partir de una fórmula clásica (rápida/lenta/...) o afinarla a mano nivel por nivel, y el dominio
    /// solo ve la tabla. Subir de nivel es entonces trivial: "¿la XP total cruzó el umbral del siguiente?".
    /// </summary>
    public sealed class GrowthCurve
    {
        public Id<GrowthCurve> Id { get; }
        public string DisplayName { get; }
        public int MaxLevel { get; }

        // _thresholds[L] = XP total para SER nivel L. El índice 0 no se usa; _thresholds[1] = 0.
        private readonly int[] _thresholds;

        public GrowthCurve(Id<GrowthCurve> id, string displayName, IReadOnlyList<int> cumulativeXpByLevel)
        {
            if (cumulativeXpByLevel == null || cumulativeXpByLevel.Count < 2)
                throw new ArgumentException("La curva necesita umbrales al menos hasta el nivel 1.", nameof(cumulativeXpByLevel));

            Id = id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id.Value : displayName;
            MaxLevel = cumulativeXpByLevel.Count - 1;

            _thresholds = new int[cumulativeXpByLevel.Count];
            for (int i = 0; i < cumulativeXpByLevel.Count; i++)
                _thresholds[i] = cumulativeXpByLevel[i] < 0 ? 0 : cumulativeXpByLevel[i];
            _thresholds[1] = 0; // ser nivel 1 nunca cuesta XP
        }

        /// <summary>XP total necesaria para SER 'level' (recortado a [1, MaxLevel]).</summary>
        public int XpToReachLevel(int level)
        {
            if (level < 1) level = 1;
            if (level > MaxLevel) level = MaxLevel;
            return _thresholds[level];
        }

        /// <summary>XP que falta desde 'totalXp' para alcanzar 'level' (0 si ya se alcanzó).</summary>
        public int XpRemainingToLevel(int totalXp, int level)
        {
            int needed = XpToReachLevel(level) - totalXp;
            return needed > 0 ? needed : 0;
        }

        /// <summary>El mayor nivel cuyo umbral es &lt;= totalXp. (No asume monotonía: escanea todo.)</summary>
        public int LevelForXp(int totalXp)
        {
            int lvl = 1;
            for (int L = 1; L <= MaxLevel; L++)
                if (totalXp >= _thresholds[L]) lvl = L;
            return lvl;
        }
    }
}
