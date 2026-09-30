using System.Collections.Generic;
using UnityEditor;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Conditions;
using CTEditor.GameDefinition.Infrastructure.Catalog;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// Biblioteca de MOVIMIENTOS-PLANTILLA: uno por cada mecánica del motor (daño simple, efecto
    /// secundario, drenaje, retroceso, recarga, carga, prioridad, crítico alto, multigolpe, retroceso de
    /// turno, bajar/subir stats, estado principal, curación, Forcejeo). Valores oficiales modernos.
    ///
    /// Sirven para dos cosas: tener movimientos listos, y ENSEÑAR cómo se arma cada mecánica para que el
    /// autor invente las suyas combinando los mismos campos.
    /// </summary>
    public static class ClassicMovePresets
    {
        public sealed class Effect
        {
            public MoveEffectKind Kind; public EffectTarget Target = EffectTarget.Opponent; public float Chance = 100;
            public string Status; public float Amount; public string Stat; public int Stages;
            public Condition[] Conditions = new Condition[0]; public bool Shared; public string Weather; public string Hazard;
            public int Turns; public string Side; public string TypeId;
        }

        /// <summary>Etiqueta que Metrónomo evita (el motor también evita a los que llaman o copian movimientos).</summary>
        public const string NoMetronome = "no_metronomo";

        public sealed class Preset
        {
            public string Group, Id, Name, Type, Summary;
            public MoveCategory Category; public int Power; public float? Accuracy; public int Pp;
            public int Priority; public int MinHits = 1, MaxHits = 1; public int Crit;
            public TwoTurnKind TwoTurn = TwoTurnKind.None; public bool Contact;
            public FixedDamageKind Fixed = FixedDamageKind.None; public int FixedAmount;
            public bool RespectsImmunity;
            // Lote A: potencia avanzada, stats del daño y etiquetas.
            public List<(float, Condition[])> PowerMods = new List<(float, Condition[])>();
            public string Formula = "", AtkStat = "", DefStat = "";
            public bool FromTarget;
            public string[] Tags = new string[0];
            public List<Effect> Effects = new List<Effect>();
        }

        private static Preset P(string group, string id, string name, string type, MoveCategory cat, int power, float? acc, int pp, string summary)
            => new Preset { Group = group, Id = id, Name = name, Type = type, Category = cat, Power = power, Accuracy = acc, Pp = pp, Summary = summary };

        private static Preset With(this Preset p, System.Action<Preset> edit) { edit(p); return p; }
        private static Effect E(MoveEffectKind k, float chance = 100) => new Effect { Kind = k, Chance = chance };

        public static readonly Preset[] All =
        {
            P("Daño", "tackle", "Placaje", "normal", MoveCategory.Physical, 40, 100, 35, "Daño físico simple con contacto.")
                .With(p => p.Contact = true),
            P("Daño", "water_gun", "Pistola Agua", "water", MoveCategory.Special, 40, 100, 25, "Daño especial simple."),
            P("Efecto secundario", "ember", "Ascuas", "fire", MoveCategory.Special, 40, 100, 25, "Daño + 10% de quemar.")
                .With(p => p.Effects.Add(new Effect { Kind = MoveEffectKind.InflictStatus, Chance = 10, Status = "burn" })),
            P("Efecto secundario", "thunder_shock", "Impactrueno", "electric", MoveCategory.Special, 40, 100, 30, "Daño + 10% de paralizar.")
                .With(p => p.Effects.Add(new Effect { Kind = MoveEffectKind.InflictStatus, Chance = 10, Status = "paralysis" })),
            P("Efecto secundario", "bite", "Mordisco", "dark", MoveCategory.Physical, 60, 100, 25, "Daño + 30% de hacer retroceder.")
                .With(p => { p.Contact = true; p.Effects.Add(E(MoveEffectKind.Flinch, 30)); }),
            P("Drenaje / retroceso", "mega_drain", "Megaagotar", "grass", MoveCategory.Special, 40, 100, 15, "Recupera el 50% del daño causado.")
                .With(p => p.Effects.Add(new Effect { Kind = MoveEffectKind.Drain, Target = EffectTarget.Self, Amount = 50 })),
            P("Drenaje / retroceso", "double_edge", "Doble Filo", "normal", MoveCategory.Physical, 120, 100, 15, "Mucho daño, pero el usuario recibe 1/3 del daño causado.")
                .With(p => { p.Contact = true; p.Effects.Add(new Effect { Kind = MoveEffectKind.Recoil, Target = EffectTarget.Self, Amount = 33 }); }),
            P("Dos turnos", "hyper_beam", "Hiperrayo", "normal", MoveCategory.Special, 150, 90, 5, "Golpe enorme; el turno siguiente debe recargar.")
                .With(p => p.TwoTurn = TwoTurnKind.Recharge),
            P("Dos turnos", "fly", "Vuelo", "flying", MoveCategory.Physical, 90, 95, 15, "Turno 1 se eleva; turno 2 golpea.")
                .With(p => { p.TwoTurn = TwoTurnKind.Charge; p.Contact = true; }),
            P("Prioridad / crítico / multigolpe", "quick_attack", "Ataque Rápido", "normal", MoveCategory.Physical, 40, 100, 30, "Prioridad +1: casi siempre golpea primero.")
                .With(p => { p.Priority = 1; p.Contact = true; }),
            P("Prioridad / crítico / multigolpe", "slash", "Cuchillada", "normal", MoveCategory.Physical, 70, 100, 20, "Probabilidad de crítico alta.")
                .With(p => { p.Crit = 1; p.Contact = true; }),
            P("Prioridad / crítico / multigolpe", "double_kick", "Doble Patada", "fighting", MoveCategory.Physical, 30, 100, 30, "Golpea exactamente 2 veces.")
                .With(p => { p.MinHits = 2; p.MaxHits = 2; p.Contact = true; }),
            P("Prioridad / crítico / multigolpe", "fury_attack", "Ataque Furia", "normal", MoveCategory.Physical, 15, 85, 20, "Golpea de 2 a 5 veces.")
                .With(p => { p.MinHits = 2; p.MaxHits = 5; p.Contact = true; }),
            P("Estadísticas", "growl", "Gruñido", "normal", MoveCategory.Status, 0, 100, 40, "Baja 1 etapa el Ataque del rival.")
                .With(p => p.Effects.Add(new Effect { Kind = MoveEffectKind.ChangeStatStage, Stat = "attack", Stages = -1 })),
            P("Estadísticas", "swords_dance", "Danza Espada", "normal", MoveCategory.Status, 0, null, 20, "Sube 2 etapas su propio Ataque.")
                .With(p => p.Effects.Add(new Effect { Kind = MoveEffectKind.ChangeStatStage, Target = EffectTarget.Self, Stat = "attack", Stages = 2 })),
            P("Estado principal", "toxic", "Tóxico", "poison", MoveCategory.Status, 0, 90, 10, "Envenena gravemente (daño creciente cada turno).")
                .With(p => p.Effects.Add(new Effect { Kind = MoveEffectKind.InflictStatus, Status = "toxic" })),
            P("Estado principal", "thunder_wave", "Onda Trueno", "electric", MoveCategory.Status, 0, 90, 20, "Paraliza al rival; no afecta a los inmunes por tipo (Tierra).")
                .With(p => { p.RespectsImmunity = true; p.Effects.Add(new Effect { Kind = MoveEffectKind.InflictStatus, Status = "paralysis" }); }),
            P("Estado principal", "sleep_powder", "Somnífero", "grass", MoveCategory.Status, 0, 75, 15, "Duerme al rival.")
                .With(p => p.Effects.Add(new Effect { Kind = MoveEffectKind.InflictStatus, Status = "sleep" })),
            P("Curación", "recover", "Recuperación", "normal", MoveCategory.Status, 0, null, 5, "Recupera la mitad de sus PS máximos.")
                .With(p => p.Effects.Add(new Effect { Kind = MoveEffectKind.Heal, Target = EffectTarget.Self, Amount = 50 })),
            P("Daño especial", "sonic_boom", "Bomba Sónica", "normal", MoveCategory.Special, 0, 90, 20, "Quita siempre 20 PS (ignora las estadísticas; un Fantasma es inmune).")
                .With(p => { p.Fixed = FixedDamageKind.Fixed; p.FixedAmount = 20; }),
            P("Daño especial", "seismic_toss", "Sísmico", "fighting", MoveCategory.Physical, 0, 100, 20, "Quita tantos PS como el nivel del usuario.")
                .With(p => { p.Fixed = FixedDamageKind.UserLevel; p.Contact = true; }),
            P("Daño especial", "super_fang", "Superdiente", "normal", MoveCategory.Physical, 0, 90, 10, "Quita la mitad de los PS actuales del rival.")
                .With(p => { p.Fixed = FixedDamageKind.HalfTargetHp; p.Contact = true; }),
            P("Daño especial", "guillotine", "Guillotina", "normal", MoveCategory.Physical, 0, 30, 5, "KO directo si acierta; falla contra rivales de más nivel.")
                .With(p => { p.Fixed = FixedDamageKind.OneHitKo; p.Contact = true; }),
            P("Estadísticas", "sand_attack", "Ataque Arena", "ground", MoveCategory.Status, 0, 100, 15, "Baja 1 etapa la precisión del rival.")
                .With(p => p.Effects.Add(new Effect { Kind = MoveEffectKind.ChangeStatStage, Stat = "accuracy", Stages = -1 })),
            P("Estadísticas", "double_team", "Doble Equipo", "normal", MoveCategory.Status, 0, null, 15, "Sube 1 etapa su propia evasión.")
                .With(p => p.Effects.Add(new Effect { Kind = MoveEffectKind.ChangeStatStage, Target = EffectTarget.Self, Stat = "evasion", Stages = 1 })),
            // ---------------- Lote B: estados volátiles ----------------
            P("Volátiles", "confuse_ray", "Rayo Confuso", "ghost", MoveCategory.Status, 0, 100, 10, "Confunde al rival (se suma a su estado principal).")
                .With(p => p.Effects.Add(new Effect { Kind = MoveEffectKind.InflictStatus, Status = "confusion" })),
            P("Volátiles", "wrap", "Constricción", "normal", MoveCategory.Physical, 15, 90, 20, "Golpea y atrapa al rival 4-5 turnos: no puede huir ni cambiarse y pierde 1/8 de PS cada turno.")
                .With(p => { p.Contact = true; p.Effects.Add(new Effect { Kind = MoveEffectKind.InflictStatus, Status = "trapped" }); }),
            P("Volátiles", "leech_seed", "Drenadoras", "grass", MoveCategory.Status, 0, 90, 10, "Planta una semilla: cada turno roba 1/8 de PS al rival y te cura. Planta es inmune.")
                .With(p => p.Effects.Add(new Effect { Kind = MoveEffectKind.InflictStatus, Status = "leech_seed" })),
            P("Volátiles", "protect", "Protección", "normal", MoveCategory.Status, 0, null, 10, "Prioridad +4. Bloquea los ataques del rival este turno; seguido, cada vez es más difícil.")
                .With(p => { p.Priority = 4; p.Effects.Add(new Effect { Kind = MoveEffectKind.InflictStatus, Target = EffectTarget.Self, Status = "protect" }); }),
            P("Volátiles", "endure", "Aguante", "normal", MoveCategory.Status, 0, null, 10, "Prioridad +4. Este turno aguanta cualquier golpe con 1 PS.")
                .With(p => { p.Priority = 4; p.Effects.Add(new Effect { Kind = MoveEffectKind.InflictStatus, Target = EffectTarget.Self, Status = "endure" }); }),

            // ---------------- Lote A: clima ----------------
            P("Clima", "rain_dance", "Danza Lluvia", "water", MoveCategory.Status, 0, null, 5, "Hace llover 5 turnos: Agua ×1,5 y Fuego ×0,5.")
                .With(p => p.Effects.Add(new Effect { Kind = MoveEffectKind.SetWeather, Target = EffectTarget.Self, Weather = "rain" })),
            P("Clima", "sunny_day", "Día Soleado", "fire", MoveCategory.Status, 0, null, 5, "Sol intenso 5 turnos: Fuego ×1,5 y Agua ×0,5.")
                .With(p => p.Effects.Add(new Effect { Kind = MoveEffectKind.SetWeather, Target = EffectTarget.Self, Weather = "sun" })),
            P("Clima", "sandstorm", "Tormenta Arena", "rock", MoveCategory.Status, 0, null, 10, "Tormenta de arena 5 turnos: daña 1/16 cada turno salvo a Roca, Tierra y Acero.")
                .With(p => p.Effects.Add(new Effect { Kind = MoveEffectKind.SetWeather, Target = EffectTarget.Self, Weather = "sandstorm" })),

            // ---------------- Lote A: potencia con condiciones ----------------
            P("Potencia condicional", "facade", "Fachada", "normal", MoveCategory.Physical, 70, 100, 20, "Potencia ×2 si el usuario tiene un estado alterado.")
                .With(p => { p.Contact = true; p.PowerMods.Add((2f, new[] { new Condition(ConditionKind.HasAnyStatus, ConditionSubject.Self) })); }),
            P("Potencia condicional", "hex", "Infortunio", "ghost", MoveCategory.Special, 65, 100, 10, "Potencia ×2 si el rival tiene un estado alterado.")
                .With(p => p.PowerMods.Add((2f, new[] { new Condition(ConditionKind.HasAnyStatus, ConditionSubject.Other) }))),
            P("Potencia condicional", "venoshock", "Carga Tóxica", "poison", MoveCategory.Special, 65, 100, 10, "Potencia ×2 si el rival está envenenado (normal o gravemente).")
                .With(p =>
                {
                    p.PowerMods.Add((2f, new[] { new Condition(ConditionKind.HasStatus, ConditionSubject.Other, text: "poison") }));
                    p.PowerMods.Add((2f, new[] { new Condition(ConditionKind.HasStatus, ConditionSubject.Other, text: "toxic") }));
                }),
            P("Potencia condicional", "payback", "Vendetta", "dark", MoveCategory.Physical, 50, 100, 10, "Potencia ×2 si el rival ya actuó este turno.")
                .With(p => { p.Contact = true; p.PowerMods.Add((2f, new[] { new Condition(ConditionKind.AlreadyActed, ConditionSubject.Other) })); }),
            P("Potencia por fórmula", "eruption", "Estallido", "fire", MoveCategory.Special, 150, 100, 5, "Potencia = 150 × % de vida del usuario (más fuerte cuanto más sano).")
                .With(p => p.Formula = "max(1, floor(150 * vida / 100))"),
            P("Potencia por fórmula", "return", "Retribución", "normal", MoveCategory.Physical, 102, 100, 20, "Potencia = amistad / 2,5 (hasta 102).")
                .With(p => { p.Contact = true; p.Formula = "max(1, floor(amistad / 2.5))"; }),

            // ---------------- Lote A: stats del daño, etiquetas, dado compartido, curas ----------------
            P("Estadísticas del daño", "psyshock", "Psicocarga", "psychic", MoveCategory.Special, 80, 100, 10, "Especial, pero golpea la DEFENSA física del rival.")
                .With(p => p.DefStat = "defense"),
            P("Estadísticas del daño", "foul_play", "Juego Sucio", "dark", MoveCategory.Physical, 95, 100, 15, "Usa el ATAQUE del rival en vez del propio.")
                .With(p => { p.Contact = true; p.FromTarget = true; }),
            P("Etiquetas", "fire_punch", "Puño Fuego", "fire", MoveCategory.Physical, 75, 100, 15, "10% de quemar. Etiqueta 'puño' (la potencia Puño Férreo).")
                .With(p => { p.Contact = true; p.Tags = new[] { "puño" }; p.Effects.Add(new Effect { Kind = MoveEffectKind.InflictStatus, Chance = 10, Status = "burn" }); }),
            P("Estadísticas", "ancient_power", "Poder Pasado", "rock", MoveCategory.Special, 60, 100, 5, "10% de subir TODAS sus estadísticas a la vez (un solo dado).")
                .With(p =>
                {
                    bool first = true;
                    foreach (var st in new[] { "attack", "defense", "sp_attack", "sp_defense", "speed" })
                    {
                        p.Effects.Add(new Effect { Kind = MoveEffectKind.ChangeStatStage, Target = EffectTarget.Self, Stat = st, Stages = 1, Chance = 10, Shared = !first });
                        first = false;
                    }
                }),
            P("Curación", "heal_pulse", "Pulso Cura", "psychic", MoveCategory.Status, 0, null, 10, "Cura el 50% de los PS máximos del RIVAL (útil en dobles o con reglas propias).")
                .With(p => p.Effects.Add(new Effect { Kind = MoveEffectKind.Heal, Target = EffectTarget.Opponent, Amount = 50 })),
            P("Curación", "refresh", "Alivio", "normal", MoveCategory.Status, 0, null, 20, "Se cura su estado alterado principal.")
                .With(p => p.Effects.Add(new Effect { Kind = MoveEffectKind.CureStatus, Target = EffectTarget.Self })),

            // ---------------- Trampas de campo y cambios forzados ----------------
            P("Trampas", "spikes", "Púas", "ground", MoveCategory.Status, 0, null, 20, "Pone púas en el lado rival (hasta 3 capas): quien entre pierde 1/8, 1/6 o 1/4 de sus PS. No afecta a Volador.")
                .With(p => p.Effects.Add(new Effect { Kind = MoveEffectKind.SetHazard, Hazard = "spikes" })),
            P("Trampas", "stealth_rock", "Trampa Rocas", "rock", MoveCategory.Status, 0, null, 20, "Rocas flotantes en el lado rival: quien entre pierde 1/8 × la eficacia de Roca (¡×4 a un Charizard!).")
                .With(p => p.Effects.Add(new Effect { Kind = MoveEffectKind.SetHazard, Hazard = "stealth_rock" })),
            P("Trampas", "toxic_spikes", "Púas Tóxicas", "poison", MoveCategory.Status, 0, null, 20, "1 capa: envenena a quien entre; 2 capas: veneno grave. Un tipo Veneno las retira al entrar.")
                .With(p => p.Effects.Add(new Effect { Kind = MoveEffectKind.SetHazard, Hazard = "toxic_spikes" })),
            P("Trampas", "sticky_web", "Red Viscosa", "bug", MoveCategory.Status, 0, null, 20, "Quien entre en el lado rival pierde 1 etapa de Velocidad.")
                .With(p => p.Effects.Add(new Effect { Kind = MoveEffectKind.SetHazard, Hazard = "sticky_web" })),
            P("Trampas", "rapid_spin", "Giro Rápido", "normal", MoveCategory.Physical, 50, 100, 40, "Golpea, quita TODAS las trampas de su propio lado y sube 1 etapa su Velocidad.")
                .With(p =>
                {
                    p.Contact = true;
                    p.Effects.Add(new Effect { Kind = MoveEffectKind.ClearHazards, Target = EffectTarget.Self });
                    p.Effects.Add(new Effect { Kind = MoveEffectKind.ChangeStatStage, Target = EffectTarget.Self, Stat = "speed", Stages = 1 });
                }),
            P("Trampas", "defog", "Despejar", "flying", MoveCategory.Status, 0, null, 15, "Baja 1 etapa la evasión del rival y quita las trampas de AMBOS lados.")
                .With(p =>
                {
                    p.Effects.Add(new Effect { Kind = MoveEffectKind.ChangeStatStage, Stat = "evasion", Stages = -1 });
                    p.Effects.Add(new Effect { Kind = MoveEffectKind.ClearHazards, Target = EffectTarget.Opponent });
                    p.Effects.Add(new Effect { Kind = MoveEffectKind.ClearHazards, Target = EffectTarget.Self });
                }),
            P("Cambio forzado", "roar", "Rugido", "normal", MoveCategory.Status, 0, null, 20, "Prioridad −6. Echa al rival: sale otro de su equipo al azar. Contra un salvaje, termina el combate.")
                .With(p => { p.Priority = -6; p.Tags = new[] { "sonido" }; p.Effects.Add(new Effect { Kind = MoveEffectKind.ForceSwitch }); }),
            P("Cambio forzado", "whirlwind", "Remolino", "normal", MoveCategory.Status, 0, null, 20, "Prioridad −6. Se lleva al rival: sale otro de su equipo al azar. Contra un salvaje, termina el combate.")
                .With(p => { p.Priority = -6; p.Effects.Add(new Effect { Kind = MoveEffectKind.ForceSwitch }); }),
            P("Cambio forzado", "dragon_tail", "Cola Dragón", "dragon", MoveCategory.Physical, 60, 90, 10, "Prioridad −6. Golpea y obliga al rival a cambiarse (si sigue en pie).")
                .With(p => { p.Priority = -6; p.Contact = true; p.Effects.Add(new Effect { Kind = MoveEffectKind.ForceSwitch }); }),

            // ---------------- Efectos de lado ----------------
            P("Efectos de lado", "reflect", "Reflejo", "psychic", MoveCategory.Status, 0, null, 20, "5 turnos: el daño FÍSICO que recibe tu equipo se reduce a la mitad (los críticos lo atraviesan).")
                .With(p => p.Effects.Add(new Effect { Kind = MoveEffectKind.SetSideCondition, Target = EffectTarget.Self, Side = "reflect" })),
            P("Efectos de lado", "light_screen", "Pantalla de Luz", "psychic", MoveCategory.Status, 0, null, 30, "5 turnos: el daño ESPECIAL que recibe tu equipo se reduce a la mitad.")
                .With(p => p.Effects.Add(new Effect { Kind = MoveEffectKind.SetSideCondition, Target = EffectTarget.Self, Side = "light_screen" })),
            P("Efectos de lado", "mist", "Neblina", "ice", MoveCategory.Status, 0, null, 30, "5 turnos: el rival no puede bajar las estadísticas de tu equipo.")
                .With(p => p.Effects.Add(new Effect { Kind = MoveEffectKind.SetSideCondition, Target = EffectTarget.Self, Side = "mist" })),
            P("Efectos de lado", "safeguard", "Velo Sagrado", "normal", MoveCategory.Status, 0, null, 25, "5 turnos: el rival no puede poner estados a tu equipo.")
                .With(p => p.Effects.Add(new Effect { Kind = MoveEffectKind.SetSideCondition, Target = EffectTarget.Self, Side = "safeguard" })),
            P("Efectos de lado", "tailwind", "Viento Afín", "flying", MoveCategory.Status, 0, null, 15, "4 turnos: la Velocidad de tu equipo se duplica.")
                .With(p => p.Effects.Add(new Effect { Kind = MoveEffectKind.SetSideCondition, Target = EffectTarget.Self, Side = "tailwind" })),

            // ---------------- Sustituto, Niebla, Foco Energía, Anulación, Otra Vez ----------------
            P("Apoyo", "substitute", "Sustituto", "normal", MoveCategory.Status, 0, null, 10, "Paga 1/4 de sus PS y crea un muñeco que recibe los golpes y bloquea estados y bajadas del rival.")
                .With(p => p.Effects.Add(new Effect { Kind = MoveEffectKind.Substitute, Target = EffectTarget.Self, Amount = 25 })),
            P("Apoyo", "haze", "Niebla", "ice", MoveCategory.Status, 0, null, 30, "Devuelve a 0 las etapas de TODOS los que están en el campo.")
                .With(p =>
                {
                    p.Effects.Add(new Effect { Kind = MoveEffectKind.ResetStages, Target = EffectTarget.Self });
                    p.Effects.Add(new Effect { Kind = MoveEffectKind.ResetStages, Target = EffectTarget.Opponent });
                }),
            P("Apoyo", "focus_energy", "Foco Energía", "normal", MoveCategory.Status, 0, null, 30, "Se concentra: +2 al índice de crítico mientras siga en el campo.")
                .With(p => p.Effects.Add(new Effect { Kind = MoveEffectKind.CritBoost, Target = EffectTarget.Self, Stages = 2 })),
            P("Apoyo", "disable", "Anulación", "normal", MoveCategory.Status, 0, 100, 20, "Anula el último movimiento que usó el rival durante 4 turnos.")
                .With(p => p.Effects.Add(new Effect { Kind = MoveEffectKind.DisableMove, Turns = 4 })),
            P("Apoyo", "encore", "Otra Vez", "normal", MoveCategory.Status, 0, 100, 5, "Obliga al rival a repetir su último movimiento durante 3 turnos.")
                .With(p => p.Effects.Add(new Effect { Kind = MoveEffectKind.Encore, Turns = 3 })),

            // ---------------- Devolver daño ----------------
            P("Devolver daño", "counter", "Contraataque", "fighting", MoveCategory.Physical, 0, 100, 20, "Prioridad −5. Devuelve el DOBLE del daño físico recibido este turno. Falla si no recibió.")
                .With(p => { p.Priority = -5; p.Contact = true; p.Fixed = FixedDamageKind.ReturnPhysical; p.Tags = new[] { NoMetronome }; }),
            P("Devolver daño", "mirror_coat", "Manto Espejo", "psychic", MoveCategory.Special, 0, 100, 20, "Prioridad −5. Devuelve el DOBLE del daño especial recibido este turno.")
                .With(p => { p.Priority = -5; p.Fixed = FixedDamageKind.ReturnSpecial; p.Tags = new[] { NoMetronome }; }),
            P("Devolver daño", "bide", "Venganza", "normal", MoveCategory.Physical, 0, null, 10, "Prioridad +1. Aguanta 2 turnos sin poder elegir y devuelve el DOBLE de todo el daño recibido.")
                .With(p => { p.Priority = 1; p.Contact = true; p.Fixed = FixedDamageKind.Bide; }),

            // ---------------- Encadenar turnos ----------------
            P("Encadenar", "thrash", "Saña", "normal", MoveCategory.Physical, 120, 100, 10, "Ataca 2-3 turnos seguidos sin poder elegir y después queda confuso.")
                .With(p => { p.Contact = true; p.Effects.Add(new Effect { Kind = MoveEffectKind.Rampage, Target = EffectTarget.Self, Turns = 3, Status = "confusion" }); }),
            P("Encadenar", "petal_dance", "Danza Pétalo", "grass", MoveCategory.Special, 120, 100, 10, "Ataca 2-3 turnos seguidos sin poder elegir y después queda confuso.")
                .With(p => { p.Contact = true; p.Effects.Add(new Effect { Kind = MoveEffectKind.Rampage, Target = EffectTarget.Self, Turns = 3, Status = "confusion" }); }),
            P("Encadenar", "outrage", "Enfado", "dragon", MoveCategory.Physical, 120, 100, 10, "Ataca 2-3 turnos seguidos sin poder elegir y después queda confuso.")
                .With(p => { p.Contact = true; p.Effects.Add(new Effect { Kind = MoveEffectKind.Rampage, Target = EffectTarget.Self, Turns = 3, Status = "confusion" }); }),
            P("Encadenar", "rage", "Furia", "normal", MoveCategory.Physical, 20, 100, 20, "Mientras lo siga usando, cada golpe que reciba le sube 1 etapa el Ataque.")
                .With(p => { p.Contact = true; p.Effects.Add(new Effect { Kind = MoveEffectKind.Rage, Target = EffectTarget.Self, Stat = "attack", Stages = 1 }); }),

            // ---------------- Llamar y copiar ----------------
            P("Llamar y copiar", "metronome", "Metrónomo", "normal", MoveCategory.Status, 0, null, 10, "Usa un movimiento AL AZAR de todos los del juego (menos los marcados «no_metronomo»).")
                .With(p => { p.Tags = new[] { NoMetronome }; p.Effects.Add(new Effect { Kind = MoveEffectKind.CallRandomMove, Target = EffectTarget.Self }); }),
            P("Llamar y copiar", "mirror_move", "Espejo", "flying", MoveCategory.Status, 0, null, 20, "Usa el último movimiento que usó el rival.")
                .With(p => { p.Tags = new[] { NoMetronome }; p.Effects.Add(new Effect { Kind = MoveEffectKind.CallLastMove }); }),
            P("Llamar y copiar", "mimic", "Mimético", "normal", MoveCategory.Status, 0, null, 10, "Copia el último movimiento del rival en este hueco (5 PP) hasta que se retire.")
                .With(p => { p.Tags = new[] { NoMetronome }; p.Effects.Add(new Effect { Kind = MoveEffectKind.CopyLastMove }); }),
            P("Llamar y copiar", "transform", "Transformación", "normal", MoveCategory.Status, 0, null, 10, "Se convierte en el rival: tipos, estadísticas (no los PS), etapas y movimientos (5 PP cada uno).")
                .With(p => { p.Tags = new[] { NoMetronome }; p.Effects.Add(new Effect { Kind = MoveEffectKind.Transform }); }),
            P("Llamar y copiar", "conversion", "Conversión", "normal", MoveCategory.Status, 0, null, 30, "Cambia su tipo al de su primer movimiento que no tenga ya.")
                .With(p => p.Effects.Add(new Effect { Kind = MoveEffectKind.ChangeType, Target = EffectTarget.Self })),

            // ---------------- Cambiarse uno mismo ----------------
            P("Cambiarse", "u_turn", "Ida y Vuelta", "bug", MoveCategory.Physical, 70, 100, 20, "Golpea y se retira: eliges quién entra (el turno sigue con el nuevo).")
                .With(p => { p.Contact = true; p.Effects.Add(new Effect { Kind = MoveEffectKind.SwitchSelf, Target = EffectTarget.Self }); }),
            P("Cambiarse", "volt_switch", "Voltiocambio", "electric", MoveCategory.Special, 70, 100, 20, "Golpea y se retira: eliges quién entra.")
                .With(p => p.Effects.Add(new Effect { Kind = MoveEffectKind.SwitchSelf, Target = EffectTarget.Self })),
            P("Cambiarse", "baton_pass", "Relevo", "normal", MoveCategory.Status, 0, null, 40, "Se retira y PASA sus etapas y su sustituto al que entra.")
                .With(p => p.Effects.Add(new Effect { Kind = MoveEffectKind.SwitchSelf, Target = EffectTarget.Self, Stages = 1 })),
            P("Cambiarse", "teleport", "Teletransporte", "psychic", MoveCategory.Status, 0, null, 20, "Prioridad −6. Escapa de un combate salvaje; contra un entrenador, se retira y entra otro.")
                .With(p => { p.Priority = -6; p.Effects.Add(new Effect { Kind = MoveEffectKind.Teleport, Target = EffectTarget.Self }); }),

            P("Especial", "struggle", "Forcejeo", "typeless", MoveCategory.Physical, 50, null, 1,
                "Último recurso cuando no quedan PP: sin tipo (nunca eficaz ni poco eficaz), y el usuario pierde 1/4 de sus PS máximos.")
                .With(p => { p.Contact = true; p.Effects.Add(new Effect { Kind = MoveEffectKind.RecoilMaxHp, Target = EffectTarget.Self, Amount = 25 }); }),
        };

        /// <summary>Aplica una plantilla sobre una ficha (no toca su id). Devuelve los tipos que falten.</summary>
        public static List<string> Fill(SerializedObject so, Preset p)
        {
            var missing = new List<string>();
            var type = FindOrCreateType(p.Type, missing);

            so.FindProperty("displayName").stringValue = p.Name;
            so.FindProperty("type").objectReferenceValue = type;
            so.FindProperty("category").enumValueIndex = (int)p.Category;
            so.FindProperty("power").intValue = p.Power;
            so.FindProperty("neverMisses").boolValue = !p.Accuracy.HasValue;
            so.FindProperty("accuracy").floatValue = p.Accuracy ?? 100f;
            so.FindProperty("maxPp").intValue = p.Pp;
            so.FindProperty("priority").intValue = p.Priority;
            so.FindProperty("minHits").intValue = p.MinHits;
            so.FindProperty("maxHits").intValue = p.MaxHits;
            so.FindProperty("critStage").intValue = p.Crit;
            so.FindProperty("twoTurn").enumValueIndex = (int)p.TwoTurn;
            so.FindProperty("makesContact").boolValue = p.Contact;
            so.FindProperty("fixedDamage").intValue = (int)p.Fixed;
            so.FindProperty("fixedDamageAmount").intValue = p.FixedAmount;
            so.FindProperty("respectsTypeImmunity").boolValue = p.RespectsImmunity;
            ConditionText.WriteModifiers(so.FindProperty("powerModifiers"), p.PowerMods);
            so.FindProperty("powerFormula").stringValue = p.Formula ?? "";
            so.FindProperty("attackStat").stringValue = p.AtkStat ?? "";
            so.FindProperty("defenseStat").stringValue = p.DefStat ?? "";
            so.FindProperty("attackStatFromTarget").boolValue = p.FromTarget;
            var tags = so.FindProperty("tags");
            tags.arraySize = p.Tags.Length;
            for (int i = 0; i < p.Tags.Length; i++) tags.GetArrayElementAtIndex(i).stringValue = p.Tags[i];
            // Movimientos "de lado" (poner trampas) o sobre uno mismo: no apuntan al rival (no fallan, no los para Protección).
            so.FindProperty("target").enumValueIndex = (int)(p.Effects.Count > 0 && p.Category == MoveCategory.Status
                && p.Effects.TrueForAll(e => e.Target == EffectTarget.Self || e.Kind == MoveEffectKind.SetHazard) ? MoveTarget.Self : MoveTarget.SingleEnemy);

            var arr = so.FindProperty("secondaryEffects");
            arr.arraySize = p.Effects.Count;
            for (int i = 0; i < p.Effects.Count; i++)
            {
                var e = p.Effects[i];
                var el = arr.GetArrayElementAtIndex(i);
                el.FindPropertyRelative("chancePercent").floatValue = e.Chance;
                el.FindPropertyRelative("kind").enumValueIndex = (int)e.Kind;
                el.FindPropertyRelative("target").enumValueIndex = (int)e.Target;
                el.FindPropertyRelative("statusId").stringValue = e.Status ?? "";
                el.FindPropertyRelative("amountPercent").floatValue = e.Amount;
                el.FindPropertyRelative("statStatId").stringValue = e.Stat ?? "";
                el.FindPropertyRelative("statStages").intValue = e.Stages;
                el.FindPropertyRelative("weatherId").stringValue = e.Weather ?? "";
                el.FindPropertyRelative("weatherTurns").intValue = 0;
                el.FindPropertyRelative("hazardId").stringValue = e.Hazard ?? "";
                el.FindPropertyRelative("turns").intValue = e.Turns;
                el.FindPropertyRelative("sideConditionId").stringValue = e.Side ?? "";
                el.FindPropertyRelative("typeId").stringValue = e.TypeId ?? "";
                el.FindPropertyRelative("sharesPreviousRoll").boolValue = e.Shared;
                ConditionText.WriteAll(el.FindPropertyRelative("conditions"), e.Conditions);
            }
            return missing;
        }

        /// <summary>Crea las plantillas que falten. Devuelve cuántas creó y los tipos que faltaban.</summary>
        public static int CreateAll(out List<string> missingTypes)
        {
            int created = 0;
            var missing = new List<string>();
            foreach (var p in All)
            {
                var preset = p;
                if (ContentAssets.CreateIfMissing<MoveData>(ContentFolders.Moves, p.Id, p.Name,
                        so => missing.AddRange(Fill(so, preset))))
                    created++;
            }
            AssetDatabase.SaveAssets();
            missingTypes = new List<string>(new HashSet<string>(missing));
            return created;
        }

        // El tipo "typeless" (Sin tipo) del Forcejeo se crea solo si hace falta: no está en la tabla de
        // tipos, así que siempre es neutro, y ninguna especie lo tiene, así que nunca da STAB.
        private static ElementTypeData FindOrCreateType(string id, List<string> missing)
        {
            var t = ContentAssets.FindById<ElementTypeData>(id);
            if (t != null) return t;
            if (id == "typeless")
                return ContentAssets.Create<ElementTypeData>(ContentFolders.Types, "typeless", "Sin tipo",
                    so => so.FindProperty("color").colorValue = new UnityEngine.Color(0.55f, 0.6f, 0.6f));
            missing.Add(id);
            return null;
        }
    }
}
