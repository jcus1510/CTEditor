using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Status;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Types;

namespace CTEditor.GameDefinition.Domain.Abilities
{
    /// <summary>
    /// Una HABILIDAD: rasgo pasivo de una especie, siempre activo, hecho de datos (como un estado).
    /// Este es el LOTE 1, que reutiliza ganchos que ya existen en el combate:
    ///   - PassiveModifiers  -> EffectiveStat (igual que los estados). Ej.: "duplica el Ataque".
    ///   - StatusImmunities  -> al infligir estado. Ej.: "no puede ser paralizado".
    ///   - TypeImmunities    -> al calcular efectividad. Ej.: "inmune a tipo Tierra" (Levitación).
    /// Los lotes siguientes (al entrar, por contacto, condicionales, clima...) añadirán más ganchos
    /// SIN tirar esto: el modelo es el mismo, solo crece en hooks.
    /// </summary>
    public sealed class AbilityDefinition
    {
        public AbilityId Id { get; }
        public string DisplayName { get; }

        /// <summary>Modificadores de stat permanentes mientras el monstruo está en combate.</summary>
        public IReadOnlyList<StatPassiveModifier> PassiveModifiers { get; }

        /// <summary>Estados que esta habilidad IMPIDE (no se pueden infligir al portador).</summary>
        public IReadOnlyList<StatusId> StatusImmunities { get; }

        /// <summary>Tipos a los que el portador es INMUNE (los movimientos de ese tipo no le hacen nada).</summary>
        public IReadOnlyList<Id<ElementType>> TypeImmunities { get; }

        /// <summary>Si es inmune a un tipo, % de PS máx que CURA al recibirlo (Absorbe Agua). 0 = solo anula.</summary>
        public Percentage AbsorbImmuneHealPercent { get; }

        /// <summary>Estado que inflige al ATACANTE al recibir un golpe de CONTACTO (Estática, Cuerpo Llama). Null = ninguno.</summary>
        public StatusId? ContactReactionStatus { get; }

        /// <summary>Probabilidad de la reacción por contacto.</summary>
        public Percentage ContactReactionChance { get; }

        /// <summary>Al ENTRAR al campo, cambia una etapa de stat (Intimidación = -1 Ataque al rival). Null = nada.</summary>
        public StatId? OnEntryStat { get; }

        /// <summary>Cuántas etapas cambia al entrar (negativo baja).</summary>
        public int OnEntryStages { get; }

        /// <summary>Si true, el cambio al entrar es sobre uno mismo; si false, sobre el rival.</summary>
        public bool OnEntryTargetsSelf { get; }

        // ---- LOTE 3: modificadores CONDICIONALES ----

        /// <summary>Stat que se multiplica SOLO si el portador tiene un estado alterado (Agallas, Pies Rápidos). Null = nada.</summary>
        public StatId? StatusStatBoostStat { get; }

        /// <summary>Multiplicador del refuerzo por estado (1 = sin efecto).</summary>
        public float StatusStatBoostMultiplier { get; }

        /// <summary>Tipo de movimiento que se potencia cuando el portador ATACA con poca vida (Espesura/Torrente). Null = nada.</summary>
        public Id<ElementType>? LowHpBoostType { get; }

        /// <summary>Umbral de PS (fracción de máx) por debajo del cual se activa el refuerzo a poca vida.</summary>
        public Percentage LowHpThreshold { get; }

        /// <summary>Multiplicador de daño del refuerzo a poca vida (1 = sin efecto).</summary>
        public float LowHpBoostMultiplier { get; }

        /// <summary>Reducciones/aumentos de daño ENTRANTE por tipo (Sebo: Fuego/Hielo x0.5). Vacío = nada.</summary>
        public IReadOnlyList<TypeDamageMultiplier> IncomingTypeMultipliers { get; }

        // ---- LOTE 4: prioridad, fin de turno, al cambiar, prevención, STAB ----

        /// <summary>Multiplicador STAB explícito (Adaptable = 2). 0 = usar el clásico (1.5).</summary>
        public float StabMultiplierOverride { get; }

        /// <summary>Bonus de prioridad para movimientos de ESTADO (Vista Lince/Prankster = +1). 0 = nada.</summary>
        public int StatusMovePriorityBonus { get; }

        /// <summary>Impide que el RIVAL le baje etapas de stat (Cuerpo Puro). No afecta a las propias.</summary>
        public bool PreventsStatReduction { get; }

        /// <summary>Al CAMBIARSE (salir del campo), cura su estado alterado (Cura Natural).</summary>
        public bool CuresStatusOnSwitchOut { get; }

        /// <summary>Al CAMBIARSE, recupera este % de PS máx (Regeneración).</summary>
        public Percentage HealPercentOnSwitchOut { get; }

        /// <summary>Al FIN DE TURNO, cambia una etapa de stat propia (Impulso = +1 Velocidad). Null = nada.</summary>
        public StatId? EndOfTurnStat { get; }

        /// <summary>Etapas del cambio de fin de turno.</summary>
        public int EndOfTurnStages { get; }

        /// <summary>Al FIN DE TURNO, recupera este % de PS máx (Cura Veneno = 1/8). 0 = nada.</summary>
        public Percentage EndOfTurnHealPercent { get; }

        /// <summary>Si true, la curación de fin de turno solo ocurre si el portador TIENE estado (Cura Veneno).</summary>
        public bool EndOfTurnHealRequiresStatus { get; }

        /// <summary>Al FIN DE TURNO, probabilidad de curarse el estado alterado (Mudar/Shed Skin = 30%).</summary>
        public Percentage EndOfTurnCureStatusChance { get; }

        /// <summary>Anula el daño residual de estados (Cura Veneno no recibe daño de veneno; Guardia Mágica).</summary>
        public bool NegatesStatusDamage { get; }

        public AbilityDefinition(
            AbilityId id,
            string displayName,
            IReadOnlyList<StatPassiveModifier> passiveModifiers = null,
            IReadOnlyList<StatusId> statusImmunities = null,
            IReadOnlyList<Id<ElementType>> typeImmunities = null,
            Percentage absorbImmuneHealPercent = default,
            StatusId? contactReactionStatus = null,
            Percentage contactReactionChance = default,
            StatId? onEntryStat = null,
            int onEntryStages = 0,
            bool onEntryTargetsSelf = false,
            StatId? statusStatBoostStat = null,
            float statusStatBoostMultiplier = 1f,
            Id<ElementType>? lowHpBoostType = null,
            Percentage lowHpThreshold = default,
            float lowHpBoostMultiplier = 1f,
            IReadOnlyList<TypeDamageMultiplier> incomingTypeMultipliers = null,
            float stabMultiplierOverride = 0f,
            int statusMovePriorityBonus = 0,
            bool preventsStatReduction = false,
            bool curesStatusOnSwitchOut = false,
            Percentage healPercentOnSwitchOut = default,
            StatId? endOfTurnStat = null,
            int endOfTurnStages = 0,
            Percentage endOfTurnHealPercent = default,
            bool endOfTurnHealRequiresStatus = false,
            Percentage endOfTurnCureStatusChance = default,
            bool negatesStatusDamage = false)
        {
            Id = id;
            DisplayName = displayName;
            PassiveModifiers = passiveModifiers ?? Array.Empty<StatPassiveModifier>();
            StatusImmunities = statusImmunities ?? Array.Empty<StatusId>();
            TypeImmunities = typeImmunities ?? Array.Empty<Id<ElementType>>();
            AbsorbImmuneHealPercent = absorbImmuneHealPercent;
            ContactReactionStatus = contactReactionStatus;
            ContactReactionChance = contactReactionChance;
            OnEntryStat = onEntryStat;
            OnEntryStages = onEntryStages;
            OnEntryTargetsSelf = onEntryTargetsSelf;
            StatusStatBoostStat = statusStatBoostStat;
            StatusStatBoostMultiplier = statusStatBoostMultiplier <= 0f ? 1f : statusStatBoostMultiplier;
            LowHpBoostType = lowHpBoostType;
            LowHpThreshold = lowHpThreshold;
            LowHpBoostMultiplier = lowHpBoostMultiplier <= 0f ? 1f : lowHpBoostMultiplier;
            IncomingTypeMultipliers = incomingTypeMultipliers ?? Array.Empty<TypeDamageMultiplier>();
            StabMultiplierOverride = stabMultiplierOverride;
            StatusMovePriorityBonus = statusMovePriorityBonus;
            PreventsStatReduction = preventsStatReduction;
            CuresStatusOnSwitchOut = curesStatusOnSwitchOut;
            HealPercentOnSwitchOut = healPercentOnSwitchOut;
            EndOfTurnStat = endOfTurnStat;
            EndOfTurnStages = endOfTurnStages;
            EndOfTurnHealPercent = endOfTurnHealPercent;
            EndOfTurnHealRequiresStatus = endOfTurnHealRequiresStatus;
            EndOfTurnCureStatusChance = endOfTurnCureStatusChance;
            NegatesStatusDamage = negatesStatusDamage;
        }

        /// <summary>¿Bloquea este estado?</summary>
        public bool IsImmuneToStatus(StatusId status)
        {
            foreach (var s in StatusImmunities)
                if (s == status) return true;
            return false;
        }

        /// <summary>¿Es inmune a este tipo de movimiento?</summary>
        public bool IsImmuneToType(Id<ElementType> type)
        {
            foreach (var t in TypeImmunities)
                if (t == type) return true;
            return false;
        }

        // ---- LOTE 3: cálculo de los condicionales ----

        /// <summary>Multiplicador para una stat: si hay estado y coincide la stat reforzada, aplica (Agallas).</summary>
        public float StatusStatMultiplier(StatId stat, bool isStatused)
        {
            if (isStatused && StatusStatBoostStat.HasValue && StatusStatBoostStat.Value == stat)
                return StatusStatBoostMultiplier;
            return 1f;
        }

        /// <summary>Multiplicador OFENSIVO: refuerzo a poca vida para un tipo de movimiento (Espesura/Torrente).</summary>
        public float OffensiveTypeMultiplier(Id<ElementType> moveType, int currentHp, int maxHp)
        {
            if (!LowHpBoostType.HasValue || LowHpBoostType.Value != moveType || maxHp <= 0) return 1f;
            if (currentHp <= LowHpThreshold.AsFraction * maxHp)
                return LowHpBoostMultiplier;
            return 1f;
        }

        /// <summary>Multiplicador DEFENSIVO: daño entrante por tipo (Sebo, Ignífugo). Producto de las coincidencias.</summary>
        public float IncomingTypeMultiplier(Id<ElementType> moveType)
        {
            float m = 1f;
            foreach (var entry in IncomingTypeMultipliers)
                if (entry.Type == moveType) m *= entry.Multiplier;
            return m;
        }
    }

    /// <summary>Par (tipo de movimiento, multiplicador de daño entrante). Inmutable.</summary>
    public readonly struct TypeDamageMultiplier
    {
        public Id<ElementType> Type { get; }
        public float Multiplier { get; }

        public TypeDamageMultiplier(Id<ElementType> type, float multiplier)
        {
            Type = type;
            Multiplier = multiplier <= 0f ? 1f : multiplier;
        }
    }
}
