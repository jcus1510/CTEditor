using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Conditions;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Status;
using CTEditor.GameDefinition.Domain.Types;

namespace CTEditor.GameDefinition.Domain.Abilities
{
    /// <summary>Una estadística que se multiplica solo si se cumplen unas condiciones (Nado Rápido: Velocidad ×2 si llueve).</summary>
    public sealed class ConditionalStat
    {
        public StatId Stat { get; }
        public float Multiplier { get; }
        public IReadOnlyList<Condition> Conditions { get; }
        public ConditionalStat(StatId stat, float multiplier, IReadOnlyList<Condition> conditions = null)
        {
            Stat = stat;
            Multiplier = multiplier <= 0f ? 1f : multiplier;
            Conditions = conditions ?? Array.Empty<Condition>();
        }
    }

    /// <summary>Un cambio de etapa al RECIBIR un golpe que cumpla las condiciones (Armadura Frágil, Justiciero, Cobardía).</summary>
    public sealed class OnHitStat
    {
        public StatId Stat { get; }
        public int Stages { get; }
        public IReadOnlyList<Condition> Conditions { get; }
        public OnHitStat(StatId stat, int stages, IReadOnlyList<Condition> conditions = null)
        {
            Stat = stat;
            Stages = stages;
            Conditions = conditions ?? Array.Empty<Condition>();
        }
    }

    /// <summary>
    /// Los ganchos de las habilidades de la 3.ª y 4.ª generación (y las ocultas). Todo es opcional y se
    /// combina: por defecto, nada. Así cada habilidad es una ficha de datos y el autor puede inventar las suyas.
    /// Se rellena al construir la ficha (mapper) y después no se toca.
    /// </summary>
    public sealed class AbilityExtras
    {
        public static readonly AbilityExtras None = new AbilityExtras();

        // ---------------- Clima ----------------
        /// <summary>Al entrar pone este clima (Llovizna: rain). Vacío = nada.</summary>
        public string OnEntryWeather { get; set; } = "";
        /// <summary>Turnos del clima al entrar (0 = hasta que otro lo cambie, como en la 3.ª y 4.ª gen.).</summary>
        public int OnEntryWeatherTurns { get; set; }
        /// <summary>Mientras está en el campo, el clima no hace nada (Aclimatación, Bucle Aire).</summary>
        public bool SuppressesWeather { get; set; }
        /// <summary>Cambio de PS al final del turno con cada clima: + cura, − daño (Cura Lluvia, Piel Seca, Poder Solar).</summary>
        public IReadOnlyList<(string weather, float percent)> WeatherHpChanges { get; set; } = Array.Empty<(string, float)>();
        /// <summary>Climas que no le dañan ("*" = todos) (Velo Arena, Manto Níveo, Funda).</summary>
        public IReadOnlyList<string> WeatherImmunities { get; set; } = Array.Empty<string>();
        /// <summary>Con este clima se cura su estado al final del turno (Hidratación: rain).</summary>
        public string CuresStatusInWeather { get; set; } = "";
        /// <summary>Con este clima no se le pueden poner estados (Defensa Hoja: sun).</summary>
        public string StatusImmuneInWeather { get; set; } = "";
        /// <summary>Su TIPO según el clima (Predicción: rain → water...).</summary>
        public IReadOnlyList<(string weather, Id<ElementType> type)> TypeByWeather { get; set; } = Array.Empty<(string, Id<ElementType>)>();

        // ---------------- Estadísticas y precisión condicionales ----------------
        public IReadOnlyList<ConditionalStat> ConditionalStats { get; set; } = Array.Empty<ConditionalStat>();
        /// <summary>Precisión de SUS movimientos (Ojo Compuesto ×1,3; Entusiasmo ×0,8 los físicos).</summary>
        public IReadOnlyList<PowerModifier> AccuracyModifiers { get; set; } = Array.Empty<PowerModifier>();
        /// <summary>Precisión de lo que LE lanzan (Velo Arena ×0,8 con arena; Piel Milagro ×0,5 los de estado).</summary>
        public IReadOnlyList<PowerModifier> EvasionModifiers { get; set; } = Array.Empty<PowerModifier>();

        // ---------------- Daño y golpes ----------------
        /// <summary>No sufre retroceso (Cabeza Roca).</summary>
        public bool NoRecoil { get; set; }
        /// <summary>Solo le dañan los ataques: ni estados, ni clima, ni trampas, ni retroceso (Muro Mágico).</summary>
        public bool NoIndirectDamage { get; set; }
        /// <summary>No recibe golpes críticos (Armadura Batalla, Caparazón).</summary>
        public bool CritImmune { get; set; }
        /// <summary>+ índice de crítico (Afortunado).</summary>
        public int CritStageBonus { get; set; }
        /// <summary>Multiplicador extra de sus críticos (Francotirador ×1,5).</summary>
        public float CritDamageMultiplier { get; set; } = 1f;
        /// <summary>Estadísticas que el rival no le puede bajar (Vista Lince: accuracy; Corte Fuerte: attack).</summary>
        public IReadOnlyList<StatId> PreventedStatDrops { get; set; } = Array.Empty<StatId>();
        /// <summary>No retrocede (Foco Interno).</summary>
        public bool FlinchImmune { get; set; }
        /// <summary>Al retroceder, sube esta estadística (Impasible: speed +1).</summary>
        public StatId? OnFlinchStat { get; set; }
        public int OnFlinchStages { get; set; }
        /// <summary>Multiplica la probabilidad de sus efectos secundarios (Dicha ×2).</summary>
        public float SecondaryChanceMultiplier { get; set; } = 1f;
        /// <summary>Los efectos secundarios de lo que le lanzan no le afectan (Polvo Escudo).</summary>
        public bool BlocksIncomingSecondaries { get; set; }
        /// <summary>Sus movimientos pierden los efectos secundarios (Potencia Bruta; el ×1,3 va en «potencia al atacar»).</summary>
        public bool RemovesOwnSecondaries { get; set; }
        /// <summary>Probabilidad (%) de hacer retroceder con sus ataques (Hedor: 10).</summary>
        public float FlinchChanceOnAttack { get; set; }

        // ---------------- Cambios y huida ----------------
        /// <summary>No se le puede obligar a cambiar (Ventosas).</summary>
        public bool ForcedSwitchImmune { get; set; }
        /// <summary>El rival no puede cambiarse ni huir (Sombratrampa, Trampa Arena, Imán).</summary>
        public bool TrapsOpponent { get; set; }
        /// <summary>Solo atrapa a estos tipos (Imán: steel). Vacío = a todos.</summary>
        public IReadOnlyList<Id<ElementType>> TrapOnlyTypes { get; set; } = Array.Empty<Id<ElementType>>();
        /// <summary>Solo atrapa a los que están en el suelo (Trampa Arena: ni Voladores ni Levitación).</summary>
        public bool TrapOnlyGrounded { get; set; }
        /// <summary>Siempre puede huir de un salvaje (Fuga).</summary>
        public bool AlwaysEscapes { get; set; }

        // ---------------- Estados ----------------
        /// <summary>El sueño se le pasa el doble de rápido (Madrugar).</summary>
        public bool SleepAgesTwice { get; set; }
        /// <summary>Si el rival le pone quemadura, veneno o parálisis, se la devuelve (Sincronía).</summary>
        public bool SynchronizeStatus { get; set; }
        /// <summary>Estados alternativos de la reacción por contacto; se elige uno al azar (Efecto Espora).</summary>
        public IReadOnlyList<StatusId> ContactReactionStatuses { get; set; } = Array.Empty<StatusId>();
        /// <summary>% de PS máx. que pierde quien le golpea con contacto (Piel Tosca).</summary>
        public float ContactDamagePercent { get; set; }
        /// <summary>Al golpear CON CONTACTO, puede poner este estado al objetivo (Toque Tóxico).</summary>
        public StatusId? OffensiveContactStatus { get; set; }
        public float OffensiveContactChance { get; set; }
        /// <summary>% de PS máx. que pierde quien lo debilita con contacto (Resquicio).</summary>
        public float AftermathPercent { get; set; }
        /// <summary>% de anular el movimiento que le golpeó (Cuerpo Maldito).</summary>
        public float DisableOnHitChance { get; set; }
        /// <summary>Cambios de etapa al recibir golpes (Armadura Frágil, Justiciero, Cobardía).</summary>
        public IReadOnlyList<OnHitStat> OnHitStats { get; set; } = Array.Empty<OnHitStat>();
        /// <summary>Al recibir un crítico, su Ataque sube al máximo (Irascible).</summary>
        public bool CritMaxesAttack { get; set; }

        // ---------------- Inmunidades ----------------
        /// <summary>Inmune a los KO en un golpe (Robustez).</summary>
        public bool OhkoImmune { get; set; }
        /// <summary>Con los PS al máximo, un golpe que lo debilitaría le deja con 1 PS (Robustez).</summary>
        public bool SurvivesFromFullHp { get; set; }
        /// <summary>Inmune a los movimientos con estas etiquetas (Insonorizar: sonido).</summary>
        public IReadOnlyList<string> ImmuneToMoveTags { get; set; } = Array.Empty<string>();
        /// <summary>NADIE puede usar movimientos con estas etiquetas mientras está en el campo (Humedad: explosión).</summary>
        public IReadOnlyList<string> BlocksMoveTagsForAll { get; set; } = Array.Empty<string>();
        /// <summary>Solo le afectan los golpes muy eficaces (Superguarda).</summary>
        public bool OnlySuperEffectiveHits { get; set; }
        /// <summary>Al anular un golpe por inmunidad de tipo o etiqueta, sube esta estadística (Electromotor, Pararrayos, Herbívoro).</summary>
        public StatId? OnImmuneStat { get; set; }
        public int OnImmuneStages { get; set; }
        /// <summary>Tras absorber un golpe (inmunidad de tipo), sus movimientos de ese tipo ×1,5 (Absorbe Fuego).</summary>
        public bool BoostsAbsorbedType { get; set; }

        // ---------------- Al entrar ----------------
        /// <summary>Copia la habilidad del rival (Rastro).</summary>
        public bool TraceOnEntry { get; set; }
        /// <summary>Se transforma en el rival (Impostor).</summary>
        public bool TransformOnEntry { get; set; }
        /// <summary>Sube Ataque o Atq. Esp. según la defensa más baja del rival (Descarga).</summary>
        public bool DownloadOnEntry { get; set; }
        /// <summary>Avisa de algo al entrar: "objeto" (Cacheo), "peligro" (Anticipación), "movimiento" (Alerta).</summary>
        public string AnnounceOnEntry { get; set; } = "";
        /// <summary>Las demás habilidades no funcionan mientras está en el campo (Gas Reactivo).</summary>
        public bool NeutralizingGas { get; set; }
        /// <summary>El rival no puede comer bayas (Nerviosismo).</summary>
        public bool Unnerve { get; set; }

        // ---------------- Otros ----------------
        /// <summary>Su tipo pasa a ser el del ataque que le golpea (Cambio Color).</summary>
        public bool ColorChange { get; set; }
        /// <summary>Su tipo pasa a ser el del movimiento que va a usar (Mutatipo).</summary>
        public bool Protean { get; set; }
        /// <summary>El rival gasta 1 PP más (Presión).</summary>
        public bool Pressure { get; set; }
        /// <summary>Actúa un turno sí y otro no (Ausente).</summary>
        public bool Truant { get; set; }
        /// <summary>Ignora las etapas del rival (Ignorante).</summary>
        public bool IgnoresStages { get; set; }
        /// <summary>Multiplica sus cambios de etapa (Simple ×2).</summary>
        public int StageMultiplier { get; set; } = 1;
        /// <summary>Sus cambios de etapa se invierten (Respondón).</summary>
        public bool InvertsStages { get; set; }
        /// <summary>Quien le drena PS los pierde en vez de ganarlos (Lodo Líquido).</summary>
        public bool LiquidOoze { get; set; }
        /// <summary>Todos sus movimientos son de tipo Normal (Normalidad).</summary>
        public bool NormalizeMoves { get; set; }
        /// <summary>Sus movimientos de estos tipos alcanzan a quien sería inmune (Intrépido: normal, fighting).</summary>
        public IReadOnlyList<Id<ElementType>> IgnoresImmunityFor { get; set; } = Array.Empty<Id<ElementType>>();
        /// <summary>Ignora las habilidades del rival al atacar (Rompemoldes).</summary>
        public bool MoldBreaker { get; set; }
        /// <summary>Sus golpes múltiples siempre dan el máximo (Encadenado).</summary>
        public bool SkillLink { get; set; }
        /// <summary>% de PS que pierde el rival DORMIDO cada turno (Mal Sueño).</summary>
        public float BadDreamsPercent { get; set; }
        /// <summary>Sus movimientos y los que le lanzan nunca fallan (Indefenso).</summary>
        public bool NoGuard { get; set; }
        /// <summary>Actúa el último dentro de su prioridad (Rezagado).</summary>
        public bool MovesLast { get; set; }
        /// <summary>Su objeto equipado no hace nada (Zoquete).</summary>
        public bool Klutz { get; set; }
        /// <summary>Come las bayas antes: con este % de PS (Gula: 50). 0 = el de la baya.</summary>
        public int BerryThresholdPercent { get; set; }
        /// <summary>No le pueden quitar el objeto (Viscosidad).</summary>
        public bool StickyHold { get; set; }
        /// <summary>Roba el objeto de quien le golpea con contacto (Hurto).</summary>
        public bool Pickpocket { get; set; }
        /// <summary>Al debilitar a un rival, sube esta estadística (Autoestima: attack +1).</summary>
        public StatId? OnKoStat { get; set; }
        public int OnKoStages { get; set; }
        /// <summary>Si el rival le baja una estadística, sube esta (Competitivo: attack +2).</summary>
        public StatId? OnStatDroppedStat { get; set; }
        public int OnStatDroppedStages { get; set; }
        /// <summary>% de recuperar la baya gastada al final del turno (Cosecha: 50; con sol, 100).</summary>
        public float HarvestChance { get; set; }
        /// <summary>Cada turno sube mucho una estadística al azar y baja otra (Veleta).</summary>
        public bool Moody { get; set; }
        /// <summary>Atraviesa Reflejo, Pantalla de Luz y sustitutos (Allanamiento).</summary>
        public bool Infiltrator { get; set; }
        /// <summary>Devuelve los movimientos de estado que le lanzan (Espejo Mágico).</summary>
        public bool MagicBounce { get; set; }
        /// <summary>Multiplica su peso (Metal Pesado ×2, Metal Liviano ×0,5).</summary>
        public float WeightMultiplier { get; set; } = 1f;

        // --- 5.ª y 6.ª generación ---
        /// <summary>Tipo cuyos movimientos ganan prioridad (Alas Vendaval: flying).</summary>
        public string PriorityType { get; set; } = "";
        public int PriorityTypeBonus { get; set; }
        /// <summary>Quien le golpea con contacto pierde etapas de esta estadística (Baba: speed -1).</summary>
        public StatId? ContactStatDrop { get; set; }
        public int ContactStatDropStages { get; set; }
        /// <summary>Al golpear roba el objeto del rival si no lleva nada (Prestidigitador).</summary>
        public bool StealOnHit { get; set; }
        /// <summary>Quien le golpea con contacto recibe esta habilidad (Momia).</summary>
        public bool SpreadsAbilityOnContact { get; set; }
        /// <summary>Sus movimientos Normales pasan a ser de este tipo y ×ConvertBoost (Piel Feérica: fairy ×1,3).</summary>
        public string ConvertNormalTo { get; set; } = "";
        public float ConvertBoost { get; set; } = 1f;
        /// <summary>Al comer una baya recupera además este % de PS (Carrillo: 33).</summary>
        public float BerryBonusHealPercent { get; set; }
    }
}
