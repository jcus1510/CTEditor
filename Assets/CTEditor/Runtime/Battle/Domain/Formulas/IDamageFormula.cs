using CTEditor.SharedKernel.Abstractions;

namespace CTEditor.Battle.Domain.Formulas
{
    /// <summary>
    /// Todo lo que una fórmula de daño necesita, YA RESUELTO por Battle, para que la fórmula sea
    /// matemática pura. Battle hace antes las decisiones "de lógica de combate" (qué stat usar según
    /// la categoría del movimiento, cuánto vale la efectividad mirando la tabla de tipos, si aplica
    /// STAB) y se las entrega masticadas. Así la fórmula se concentra en la cuenta.
    /// </summary>
    public readonly struct DamageContext
    {
        public int AttackerLevel { get; }

        /// <summary>Stat de ataque YA elegido (Ataque si es físico, Ataque Esp. si es especial).</summary>
        public int AttackStat { get; }

        /// <summary>Stat de defensa YA elegido (Defensa o Defensa Esp.), acorde a la categoría.</summary>
        public int DefenseStat { get; }

        public int MovePower { get; }

        /// <summary>Multiplicador de efectividad de tipos, calculado por Battle desde la TypeChart.</summary>
        public float TypeEffectiveness { get; }

        /// <summary>¿El tipo del movimiento coincide con un tipo del atacante? (bonus STAB).</summary>
        public bool Stab { get; }

        /// <summary>Multiplicador STAB explícito (Adaptable = 2). 0 = usar el valor por defecto según Stab (1.5/1).</summary>
        public float StabMultiplier { get; }

        /// <summary>Azar inyectado, para crítico y la variación aleatoria. Determinista bajo semilla.</summary>
        public IRng Rng { get; }

        /// <summary>Nivel de crítico del movimiento (0 = normal; mayor = más probabilidad de crítico).</summary>
        public int CritStage { get; }

        public DamageContext(
            int attackerLevel,
            int attackStat,
            int defenseStat,
            int movePower,
            float typeEffectiveness,
            bool stab,
            IRng rng,
            int critStage = 0,
            float stabMultiplier = 0f)
        {
            AttackerLevel = attackerLevel;
            AttackStat = attackStat;
            DefenseStat = defenseStat;
            MovePower = movePower;
            TypeEffectiveness = typeEffectiveness;
            Stab = stab;
            Rng = rng;
            CritStage = critStage;
            StabMultiplier = stabMultiplier;
        }
    }

    /// <summary>Resultado de la fórmula: el daño y si fue golpe crítico (para narrarlo).</summary>
    public readonly struct DamageResult
    {
        public int Damage { get; }
        public bool WasCritical { get; }
        public DamageResult(int damage, bool wasCritical)
        {
            Damage = damage;
            WasCritical = wasCritical;
        }
    }

    /// <summary>
    /// La fórmula de daño como STRATEGY (Parte D, Nivel 2): su interfaz vive en el dominio; la
    /// implementación concreta (ClassicDamageFormula) irá en Infrastructure y se selecciona por el
    /// FormulaId del Ruleset ("classic"). Un creador puede escribir otra fórmula y ofrecerla como
    /// opción sin tocar el motor. Ese es tu seam de "inventar las reglas del daño".
    /// </summary>
    public interface IDamageFormula
    {
        /// <summary>Daño final (y si fue crítico) a partir del contexto ya resuelto.</summary>
        DamageResult Compute(DamageContext context);
    }
}
