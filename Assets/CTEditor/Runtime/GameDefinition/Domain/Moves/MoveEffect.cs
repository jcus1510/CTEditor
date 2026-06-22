using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Status;

namespace CTEditor.GameDefinition.Domain.Moves
{
    /// <summary>Qué hace un efecto de movimiento. Tipado y acotado (lo clásico y editable).</summary>
    public enum MoveEffectKind
    {
        InflictStatus,   // inflige un estado (quemar, dormir...)
        Drain,           // el atacante se cura un % del DAÑO causado (absorber)
        Recoil,          // el atacante recibe un % del DAÑO causado (retroceso)
        HealSelf,        // el atacante se cura un % de SUS PS máximos (Recover, Resto)
        ChangeStatStage, // sube/baja una etapa de stat (-6..+6) de alguien (Danza Espada, Gruñido)
        Flinch           // hace retroceder al objetivo: pierde el turno si aún no actuó (Mordisco)
    }

    /// <summary>A quién afecta el efecto.</summary>
    public enum EffectTarget
    {
        Opponent, // al objetivo del movimiento (rival)
        Self      // al propio atacante
    }

    /// <summary>
    /// Un EFECTO de un movimiento, con su probabilidad y a quién afecta. Según Kind usa unos campos:
    /// - InflictStatus: Status (en Target).
    /// - Drain / Recoil: Amount como % del daño causado (siempre sobre el atacante).
    /// - HealSelf: Amount como % de los PS máximos del atacante.
    /// - ChangeStatStage: Stat + Stages (delta, p.ej. +2 o -1) en Target.
    ///
    /// Mismo mecanismo para efectos secundarios (10% de quemar) y principales (Danza Espada: +2 al
    /// ataque propio al 100%). El scripting arbitrario (Nivel 4) queda para después.
    /// </summary>
    public sealed class MoveEffect
    {
        public Percentage Chance { get; }
        public MoveEffectKind Kind { get; }
        public EffectTarget Target { get; }
        public StatusId Status { get; }   // si Kind == InflictStatus
        public Percentage Amount { get; } // si Kind == Drain / Recoil / HealSelf
        public StatId Stat { get; }       // si Kind == ChangeStatStage
        public int Stages { get; }        // si Kind == ChangeStatStage (delta, puede ser negativo)

        public MoveEffect(
            Percentage chance,
            MoveEffectKind kind,
            EffectTarget target = EffectTarget.Opponent,
            StatusId status = default,
            Percentage amount = default,
            StatId stat = default,
            int stages = 0)
        {
            Chance = chance;
            Kind = kind;
            Target = target;
            Status = status;
            Amount = amount;
            Stat = stat;
            Stages = stages;
        }
    }
}
