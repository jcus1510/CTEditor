using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Status;
using CTEditor.GameDefinition.Domain.Conditions;
using System;
using System.Collections.Generic;

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
        Flinch,          // hace retroceder al objetivo: pierde el turno si aún no actuó (Mordisco)
        RecoilMaxHp,     // el atacante pierde un % de SUS PS máximos (Forcejeo moderno: 25%). Va al final
                         // del enum a propósito: Unity guarda los enums como número, así no se desordenan
                         // los movimientos ya creados.
        Heal,            // cura un % de los PS máximos de QUIEN DIGA EL OBJETIVO (uno mismo o el rival)
        CureStatus,      // quita un estado (Status vacío = el principal; con id = ese estado) al objetivo
        SetWeather,      // cambia el clima (WeatherId) durante WeatherTurns turnos (0 = los de la ficha)
        SetHazard,       // coloca una capa de trampa (HazardId) en el lado de QUIEN DIGA EL OBJETIVO (normalmente el rival)
        ClearHazards,    // quita las trampas (HazardId vacío = todas) del lado de quien diga el objetivo (Giro Rápido: el propio)
        ForceSwitch,     // obliga al objetivo a cambiarse por otro al azar (Rugido); en un combate salvaje, lo termina
        // --- Cierre del combate (se añaden SIEMPRE al final: Unity guarda el número) ---
        SetSideCondition, // pone un efecto de lado (SideConditionId) en el lado de quien diga el objetivo: Reflejo, Pantalla de Luz, Neblina...
        ResetStages,      // devuelve a 0 todas las etapas de quien diga el objetivo (Niebla = uno mismo + rival)
        CritBoost,        // sube el índice de crítico del que diga el objetivo (Stages) hasta que se retire (Foco Energía)
        Substitute,       // crea un SUSTITUTO pagando Amount % de sus PS máximos (Sustituto: 25)
        DisableMove,      // anula el último movimiento del objetivo durante Turns turnos (Anulación)
        Encore,           // obliga al objetivo a repetir su último movimiento durante Turns turnos (Otra Vez)
        Rampage,          // el usuario queda encadenado 2..Turns turnos al mismo movimiento y luego sufre Status (Saña → confusión)
        Rage,             // mientras siga usándolo, cada golpe que reciba sube Stat en Stages (Furia: Ataque +1)
        CallRandomMove,   // usa un movimiento AL AZAR de todos los del juego (Metrónomo)
        CallLastMove,     // usa el último movimiento que usó el objetivo (Espejo)
        CopyLastMove,     // sustituye este movimiento por el último del objetivo hasta que se retire (Mimético)
        Transform,        // se transforma en el objetivo: tipos, estadísticas (no PS), etapas y movimientos (Transformación)
        ChangeType,       // cambia el tipo del usuario a TypeId (vacío = el tipo de su primer movimiento que no tenga ya: Conversión)
        SwitchSelf,       // el usuario se retira tras el golpe y entra otro (Ida y Vuelta). Stages > 0 = PASA etapas y sustituto (Relevo)
        Teleport,         // en combate salvaje, escapa; contra un entrenador, se retira como Ida y Vuelta (Teletransporte)
        // --- 3.ª y 4.ª generación (se añaden SIEMPRE al final) ---
        ClearSideConditions, // quita efectos de lado del objetivo (Text = ids separados por |; vacío = los que reducen daño: Demolición)
        RemoveItem,       // le quita el objeto equipado al objetivo (Desarme)
        StealItem,        // le roba el objeto al objetivo si el usuario no lleva (Ladrón, Antojo)
        SwapItems,        // intercambia los objetos (Truco, Trapicheo)
        ConsumeTargetBerry, // se come la baya del objetivo (Picoteo, Picadura)
        RestoreItem,      // recupera el objeto que gastó en este combate (Reciclaje)
        CopyAbility,      // copia la habilidad del objetivo (Imitación)
        SwapAbility,      // intercambia las habilidades (Intercambio)
        SetAbility,       // cambia la habilidad del objetivo a Text (Abatidoras: insomnia)
        DelayedHeal,      // cura Amount % de sus PS máximos dentro de Turns turnos (Deseo)
        DelayedDamage,    // golpea dentro de Turns turnos con el daño calculado ahora (Premonición, Deseo Oculto)
        Stockpile,        // +1 reserva (máx. 3) (Reserva)
        UseStockpile,     // gasta las reservas: si cura, 25/50/100 % según tenga 1/2/3 (Tragar); si no, solo las vacía (Escupir)
        TransferStatus,   // pasa su estado principal al objetivo (Psicocambio)
        RaiseRandomStat,  // sube Stages una estadística al azar (Acupresión)
        SwapOwnStats,     // intercambia dos estadísticas propias (Text = "attack,defense") (Truco Fuerza)
        SwapStages,       // intercambia etapas con el objetivo (Text = lista de stats; vacío = todas) (Cambia Fuerza/Defensa/Almas)
        CopyStages,       // copia todas las etapas del objetivo (Más Psique)
        SelfFaint,        // el usuario se debilita (Legado, Deseo Cura)
        HealNextSwitchIn, // el próximo que entre en su lado se cura del todo (Deseo Cura, Danza Lunar)
        PainSplit,        // reparte los PS a partes iguales (Divide Dolor)
        CallTeamMove,     // usa un movimiento al azar de un compañero de equipo (Ayuda)
        CallMove,         // usa el movimiento Text (Adaptación: tri_attack)
        CallTargetMove,   // usa antes el movimiento que el objetivo iba a usar, ×1,5 (Yo Primero)
        TeamCureStatus,   // cura el estado principal de TODO el equipo del usuario (Cascabel Cura, Aromaterapia)
        CallOwnMove       // usa al azar OTRO de sus movimientos (Sonámbulo)
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

        /// <summary>El efecto solo ocurre si se cumplen TODAS (vacío = siempre).</summary>
        public IReadOnlyList<Condition> Conditions { get; }

        /// <summary>Usa el MISMO dado que el efecto anterior (Poder Pasado: todo sube a la vez o nada).</summary>
        public bool SharesPreviousRoll { get; }

        public string WeatherId { get; }   // si Kind == SetWeather
        public int WeatherTurns { get; }   // si Kind == SetWeather (0 = los de la ficha del clima)
        public string HazardId { get; }    // si Kind == SetHazard / ClearHazards (vacío en ClearHazards = todas)
        /// <summary>Turnos (Anulación, Otra Vez, máximo de Saña). 0 = los clásicos de cada efecto.</summary>
        public int Turns { get; }
        /// <summary>Efecto de lado (SetSideCondition): reflect, light_screen, mist...</summary>
        public string SideConditionId { get; }
        /// <summary>Tipo al que cambia (ChangeType). Vacío = el de su primer movimiento que no tenga ya.</summary>
        public string TypeId { get; }
        /// <summary>Texto libre según el efecto: habilidad (SetAbility), movimiento (CallMove), estadísticas (SwapStages)...</summary>
        public string Text { get; }

        public MoveEffect(
            Percentage chance,
            MoveEffectKind kind,
            EffectTarget target = EffectTarget.Opponent,
            StatusId status = default,
            Percentage amount = default,
            StatId stat = default,
            int stages = 0,
            IReadOnlyList<Condition> conditions = null,
            bool sharesPreviousRoll = false,
            string weatherId = null,
            int weatherTurns = 0,
            string hazardId = null,
            int turns = 0,
            string sideConditionId = null,
            string typeId = null,
            string text = null)
        {
            Text = text ?? "";
            HazardId = hazardId ?? "";
            Turns = turns < 0 ? 0 : turns;
            SideConditionId = sideConditionId ?? "";
            TypeId = typeId ?? "";
            Conditions = conditions == null ? Array.Empty<Condition>() : new List<Condition>(conditions);
            SharesPreviousRoll = sharesPreviousRoll;
            WeatherId = weatherId ?? "";
            WeatherTurns = weatherTurns < 0 ? 0 : weatherTurns;
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
