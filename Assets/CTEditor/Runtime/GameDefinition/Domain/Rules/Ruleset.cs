using System.Collections.Generic;
using System;
using CTEditor.GameDefinition.Domain.Rules.Mechanics;

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

        // --- La "genética" del juego (Lote 3). 0 = apagado: un autor puede hacer un juego sin IVs o
        //     sin EVs poniendo su tope a 0. Clásico (Gen III+): IV 0-31, EV 252 por stat y 510 en total.

        /// <summary>Valor máximo de un IV (0 = juego sin IVs). Clásico: 31.</summary>
        public int MaxIv { get; }

        /// <summary>Tope de EVs por stat (0 = juego sin EVs). Clásico: 252.</summary>
        public int MaxEvPerStat { get; }

        /// <summary>Tope de EVs totales sumando todas las stats. Clásico: 510.</summary>
        public int MaxEvTotal { get; }

        // Los tres parámetros nuevos van AL FINAL y son OPCIONALES: todas las llamadas existentes
        // (new Ruleset(6, 4, 100, formula)) siguen compilando y obtienen los valores clásicos.
        public Ruleset(int maxPartySize, int maxMovesPerMonster, int levelCap, FormulaId damageFormula,
            int maxIv = 31, int maxEvPerStat = 252, int maxEvTotal = 510,
            bool usePp = true, string struggleMoveId = "struggle",
            IReadOnlyList<int> critDenominators = null, float critMultiplier = 1.5f,
            string physicalAttackStat = "attack", string physicalDefenseStat = "defense",
            string specialAttackStat = "sp_attack", string specialDefenseStat = "sp_defense",
            AdventureRules adventure = null,
            GenerationRules generation = null, IEnumerable<MechanicDefinition> mechanics = null)
        {
            Adventure = adventure ?? AdventureRules.Classic;
            Generation = generation ?? GenerationRules.Modern;
            var mech = new List<MechanicDefinition>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (mechanics != null)
                foreach (var m in mechanics) if (m != null && seen.Add(m.Id)) mech.Add(m);
            Mechanics = mech;
            CritDenominators = critDenominators == null || critDenominators.Count == 0
                ? ModernCritTable : new List<int>(critDenominators);
            CritMultiplier = critMultiplier <= 0f ? 1.5f : critMultiplier;
            PhysicalAttackStat = string.IsNullOrWhiteSpace(physicalAttackStat) ? "attack" : physicalAttackStat;
            PhysicalDefenseStat = string.IsNullOrWhiteSpace(physicalDefenseStat) ? "defense" : physicalDefenseStat;
            SpecialAttackStat = string.IsNullOrWhiteSpace(specialAttackStat) ? "sp_attack" : specialAttackStat;
            SpecialDefenseStat = string.IsNullOrWhiteSpace(specialDefenseStat) ? "sp_defense" : specialDefenseStat;
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
            // Negativos no tienen sentido: se recortan a 0 (= apagado) en vez de lanzar.
            MaxIv = maxIv < 0 ? 0 : maxIv;
            MaxEvPerStat = maxEvPerStat < 0 ? 0 : maxEvPerStat;
            MaxEvTotal = maxEvTotal < 0 ? 0 : maxEvTotal;
            UsePp = usePp;
            StruggleMoveId = struggleMoveId ?? "";
        }

        /// <summary>Reglas de la aventura: huir, capturar, dinero, derrota, amistad, repartir experiencia.</summary>
        public AdventureRules Adventure { get; }

        /// <summary>Reglas de generación: categoría por tipo, Especial único, habilidades, objetos, naturalezas, géneros.</summary>
        public GenerationRules Generation { get; }

        /// <summary>Las MECÁNICAS ESPECIALES activas (fichas de mecánica). Vacía = ninguna. Se pueden combinar.</summary>
        public IReadOnlyList<MechanicDefinition> Mechanics { get; }

        /// <summary>¿Hay alguna mecánica activa de este tipo?</summary>
        public bool Has(MechanicKind kind) => Find(kind) != null;

        /// <summary>La primera mecánica activa de este tipo (null si no hay ninguna).</summary>
        public MechanicDefinition Find(MechanicKind kind)
        {
            foreach (var m in Mechanics) if (m.Kind == kind) return m;
            return null;
        }

        /// <summary>¿Los movimientos gastan PP? (false = usos ilimitados). Clásico: true.</summary>
        public bool UsePp { get; }

        /// <summary>Id del movimiento que se usa cuando no quedan PP (Forcejeo). Vacío = ninguno.</summary>
        public string StruggleMoveId { get; }

        // ---------------- Críticos (configurables) ----------------

        /// <summary>
        /// Tabla de críticos: posición = etapa de crítico del movimiento, valor = "1 entre N".
        /// Moderna (7ª gen.+): 24, 8, 2, 1 → etapa 0 = 1/24, etapa 3 = siempre. 0 = nunca.
        /// </summary>
        public IReadOnlyList<int> CritDenominators { get; }

        /// <summary>Multiplicador del golpe crítico. Moderno: 1,5 (2ª-5ª gen.: 2).</summary>
        public float CritMultiplier { get; }

        public static readonly IReadOnlyList<int> ModernCritTable = new[] { 24, 8, 2, 1 };

        // ---------------- Stats del daño por categoría ----------------
        // Qué stat usa cada categoría para atacar y defender (clásico: Ataque/Defensa y Atq.Esp./Def.Esp.).
        // Cada movimiento puede cambiarlas individualmente (Psicocarga, Juego Sucio...).
        public string PhysicalAttackStat { get; }
        public string PhysicalDefenseStat { get; }
        public string SpecialAttackStat { get; }
        public string SpecialDefenseStat { get; }

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
            FormulaId? damageFormula = null,
            int? maxIv = null,
            int? maxEvPerStat = null,
            int? maxEvTotal = null,
            bool? usePp = null,
            string struggleMoveId = null,
            IReadOnlyList<int> critDenominators = null,
            float? critMultiplier = null,
            AdventureRules adventure = null,
            GenerationRules generation = null,
            IEnumerable<MechanicDefinition> mechanics = null)
            => new Ruleset(
                maxPartySize ?? MaxPartySize,
                maxMovesPerMonster ?? MaxMovesPerMonster,
                levelCap ?? LevelCap,
                damageFormula ?? DamageFormula,
                maxIv ?? MaxIv,
                maxEvPerStat ?? MaxEvPerStat,
                maxEvTotal ?? MaxEvTotal,
                usePp ?? UsePp,
                struggleMoveId ?? StruggleMoveId,
                critDenominators ?? CritDenominators,
                critMultiplier ?? CritMultiplier,
                PhysicalAttackStat, PhysicalDefenseStat, SpecialAttackStat, SpecialDefenseStat,
                adventure ?? Adventure,
                generation ?? Generation,
                mechanics ?? Mechanics);
    }
}
