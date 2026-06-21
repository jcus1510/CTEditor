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
            bool onEntryTargetsSelf = false)
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
    }
}
