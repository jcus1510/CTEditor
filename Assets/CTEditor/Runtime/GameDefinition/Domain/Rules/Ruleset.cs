using System;

namespace CTEditor.GameDefinition.Domain.Rules
{
    /// <summary>
    /// El RULESET: el panel de perillas configurables del que todo lo demás lee sus reglas.
    /// Es el corazón de la visión "no limitarnos a Pokémon" (Parte D): sacar las reglas del código
    /// y volverlas datos que el autor ajusta.
    ///
    /// Por ahora es MÍNIMO y PLANO (tu decisión): solo las perillas que el primer sprint usa de
    /// verdad. Cada perilla nueva (naturalezas, IV/EV, tasa de encuentro...) entrará cuando llegue
    /// su sistema, no antes (J.6). Inmutable: una partida juega bajo UN ruleset que no cambia a media.
    ///
    /// Conecta con A.8 (invariantes RELATIVOS): el agregado Party no garantizará "máximo 6", sino
    /// "máximo Ruleset.MaxPartySize". El portero sigue ahí; solo lee el aforo de esta ficha. Por eso
    /// configurabilidad y orden conviven.
    /// </summary>
    public sealed class Ruleset
    {
        /// <summary>Tamaño máximo del equipo. El invariante de Party lo leerá de aquí.</summary>
        public int MaxPartySize { get; }

        /// <summary>Cuántos movimientos puede tener equipados un monstruo a la vez.</summary>
        public int MaxMovesPerMonster { get; }

        /// <summary>Nivel máximo al que puede llegar un monstruo.</summary>
        public int LevelCap { get; }

        /// <summary>Qué fórmula de daño usar, nombrada por id de texto (ver FormulaId).</summary>
        public FormulaId DamageFormula { get; }

        public Ruleset(int maxPartySize, int maxMovesPerMonster, int levelCap, FormulaId damageFormula)
        {
            if (maxPartySize < 1)
                throw new ArgumentOutOfRangeException(nameof(maxPartySize), "El equipo necesita al menos 1 hueco.");
            if (maxMovesPerMonster < 1)
                throw new ArgumentOutOfRangeException(nameof(maxMovesPerMonster), "Se necesita al menos 1 slot de movimiento.");
            if (levelCap < 1)
                throw new ArgumentOutOfRangeException(nameof(levelCap), "El nivel máximo debe ser al menos 1.");

            MaxPartySize = maxPartySize;
            MaxMovesPerMonster = maxMovesPerMonster;
            LevelCap = levelCap;
            DamageFormula = damageFormula;
        }

        /// <summary>
        /// El ruleset CLÁSICO de Pokémon, del que el autor parte por defecto (Parte D). Es un punto
        /// de partida, no una jaula: desde aquí el autor cambia lo que quiera.
        /// 'static' = se accede como Ruleset.Classic, sin tener una instancia previa.
        /// </summary>
        public static Ruleset Classic => new Ruleset(
            maxPartySize: 6,
            maxMovesPerMonster: 4,
            levelCap: 100,
            damageFormula: new FormulaId("classic"));

        /// <summary>
        /// Crea un ruleset NUEVO partiendo de este pero cambiando solo lo que indiques. No muta este
        /// (es inmutable). Hace trivial armar variantes para probar, p.ej.:
        ///     Ruleset.Classic.With(maxPartySize: 3)
        ///
        /// Cómo funciona, paso a paso:
        /// - Los parámetros son NULABLES y OPCIONALES ('int? x = null'): si no los pasas, llegan null.
        /// - El operador '??' (null-coalescing) significa "usa lo de la izquierda si NO es null; si
        ///   es null, usa lo de la derecha". Así, 'maxPartySize ?? MaxPartySize' = "el valor que me
        ///   diste, o el actual si no diste ninguno".
        /// - Al llamarlo con ARGUMENTOS CON NOMBRE (maxPartySize: 3) cambias solo esa perilla y el
        ///   resto se queda igual.
        /// </summary>
        public Ruleset With(
            int? maxPartySize = null,
            int? maxMovesPerMonster = null,
            int? levelCap = null,
            FormulaId? damageFormula = null)
            => new Ruleset(
                maxPartySize ?? MaxPartySize,
                maxMovesPerMonster ?? MaxMovesPerMonster,
                levelCap ?? LevelCap,
                damageFormula ?? DamageFormula);
    }
}
