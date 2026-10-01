using System;
using System.Collections.Generic;
using System.Linq;
using CTEditor.GameDefinition.Domain.Moves;

namespace CTEditor.GameDefinition.Text
{
    /// <summary>What a kind of move effect needs, so the editor shows exactly those fields.</summary>
    [Flags]
    public enum MoveEffectField
    {
        None = 0,
        Status = 1,          // a status id (InflictStatus, CureStatus «vacío = el principal», Rampage «after»)
        Amount = 2,          // a % (drain, recoil, heal, substitute...)
        Stat = 4,            // a stat id
        Stages = 8,          // stages (±)
        Weather = 16,        // a weather id
        WeatherTurns = 32,   // turns of the weather (0 = the weather's own)
        Hazard = 64,         // a hazard id
        Side = 128,          // a side condition id
        Turns = 256,         // turns
        Type = 512,          // a type id
        Ability = 1024,      // Text = an ability id
        Move = 2048,         // Text = a move id
        Target = 4096,       // the user or the foe can be chosen
        StatPair = 8192,     // Text = two stats «attack,defense»
    }

    /// <summary>The catalogue of move effects for people: label, group (for the «add» menu) and fields.</summary>
    public static partial class MoveEffectText
    {
        public sealed class KindInfo
        {
            public MoveEffectKind Kind;
            public string Label, Group;
            public MoveEffectField Fields;
            public EffectTarget DefaultTarget;
        }

        private static KindInfo K(MoveEffectKind k, string label, string group, MoveEffectField f, EffectTarget target = EffectTarget.Opponent) =>
            new KindInfo { Kind = k, Label = label, Group = group, Fields = f, DefaultTarget = target };

        public static readonly KindInfo[] Kinds =
        {
            K(MoveEffectKind.InflictStatus, "Poner un estado", "Estados", MoveEffectField.Status | MoveEffectField.Target),
            K(MoveEffectKind.CureStatus, "Curar un estado", "Estados", MoveEffectField.Status | MoveEffectField.Target, EffectTarget.Self),
            K(MoveEffectKind.TeamCureStatus, "Curar el estado de todo el equipo", "Estados", MoveEffectField.None, EffectTarget.Self),
            K(MoveEffectKind.TransferStatus, "Pasarle su estado al rival", "Estados", MoveEffectField.None),
            K(MoveEffectKind.Flinch, "Hacer retroceder", "Estados", MoveEffectField.None),
            K(MoveEffectKind.ChangeStatStage, "Subir o bajar una estadística", "Estadísticas", MoveEffectField.Stat | MoveEffectField.Stages | MoveEffectField.Target),
            K(MoveEffectKind.RaiseRandomStat, "Subir una estadística al azar", "Estadísticas", MoveEffectField.Stages, EffectTarget.Self),
            K(MoveEffectKind.ResetStages, "Devolver las etapas a 0", "Estadísticas", MoveEffectField.Target),
            K(MoveEffectKind.CritBoost, "Subir el índice de crítico", "Estadísticas", MoveEffectField.Stages, EffectTarget.Self),
            K(MoveEffectKind.SwapOwnStats, "Intercambiar dos estadísticas propias", "Estadísticas", MoveEffectField.StatPair, EffectTarget.Self),
            K(MoveEffectKind.SwapStages, "Intercambiar etapas con el rival", "Estadísticas", MoveEffectField.None),
            K(MoveEffectKind.CopyStages, "Copiar las etapas del rival", "Estadísticas", MoveEffectField.None),
            K(MoveEffectKind.InvertStages, "Invertir las etapas del rival", "Estadísticas", MoveEffectField.None),
            K(MoveEffectKind.Drain, "Recuperar parte del daño causado", "Daño y curación", MoveEffectField.Amount, EffectTarget.Self),
            K(MoveEffectKind.Recoil, "Retroceso (parte del daño causado)", "Daño y curación", MoveEffectField.Amount, EffectTarget.Self),
            K(MoveEffectKind.RecoilMaxHp, "Perder un % de sus PS máximos", "Daño y curación", MoveEffectField.Amount, EffectTarget.Self),
            K(MoveEffectKind.Heal, "Curar un % de los PS máximos", "Daño y curación", MoveEffectField.Amount | MoveEffectField.Target, EffectTarget.Self),
            K(MoveEffectKind.DelayedHeal, "Curar dentro de unos turnos (Deseo)", "Daño y curación", MoveEffectField.Turns | MoveEffectField.Amount, EffectTarget.Self),
            K(MoveEffectKind.DelayedDamage, "Golpear dentro de unos turnos (Premonición)", "Daño y curación", MoveEffectField.Turns),
            K(MoveEffectKind.PainSplit, "Repartir los PS (Divide Dolor)", "Daño y curación", MoveEffectField.None),
            K(MoveEffectKind.SelfFaint, "Debilitarse", "Daño y curación", MoveEffectField.None, EffectTarget.Self),
            K(MoveEffectKind.HealNextSwitchIn, "Curar del todo al que entre (Deseo Cura)", "Daño y curación", MoveEffectField.None, EffectTarget.Self),
            K(MoveEffectKind.Substitute, "Crear un sustituto", "Daño y curación", MoveEffectField.Amount, EffectTarget.Self),
            K(MoveEffectKind.Stockpile, "Hacer reserva", "Daño y curación", MoveEffectField.None, EffectTarget.Self),
            K(MoveEffectKind.UseStockpile, "Gastar las reservas", "Daño y curación", MoveEffectField.None, EffectTarget.Self),
            K(MoveEffectKind.SetWeather, "Cambiar el clima", "Clima y campo", MoveEffectField.Weather | MoveEffectField.WeatherTurns, EffectTarget.Self),
            K(MoveEffectKind.SetHazard, "Poner una trampa", "Clima y campo", MoveEffectField.Hazard),
            K(MoveEffectKind.ClearHazards, "Quitar trampas", "Clima y campo", MoveEffectField.Hazard | MoveEffectField.Target, EffectTarget.Self),
            K(MoveEffectKind.SetSideCondition, "Poner un efecto de lado (Reflejo…)", "Clima y campo", MoveEffectField.Side | MoveEffectField.Target, EffectTarget.Self),
            K(MoveEffectKind.ClearSideConditions, "Quitar efectos de lado del rival", "Clima y campo", MoveEffectField.None),
            K(MoveEffectKind.ForceSwitch, "Obligar al rival a cambiarse", "Cambios", MoveEffectField.None),
            K(MoveEffectKind.SwitchSelf, "Retirarse tras el golpe (Ida y Vuelta / Relevo)", "Cambios", MoveEffectField.None, EffectTarget.Self),
            K(MoveEffectKind.Teleport, "Escapar (Teletransporte)", "Cambios", MoveEffectField.None, EffectTarget.Self),
            K(MoveEffectKind.DisableMove, "Anular su último movimiento", "Control", MoveEffectField.Turns),
            K(MoveEffectKind.Encore, "Obligarle a repetir (Otra Vez)", "Control", MoveEffectField.Turns),
            K(MoveEffectKind.Rampage, "Desenfreno (varios turnos seguidos)", "Control", MoveEffectField.Turns | MoveEffectField.Status, EffectTarget.Self),
            K(MoveEffectKind.Rage, "Furia (sube al recibir golpes)", "Control", MoveEffectField.Stat | MoveEffectField.Stages, EffectTarget.Self),
            K(MoveEffectKind.Transform, "Transformarse en el rival", "Tipos y forma", MoveEffectField.None),
            K(MoveEffectKind.ChangeType, "Cambiar de tipo", "Tipos y forma", MoveEffectField.Type | MoveEffectField.Target, EffectTarget.Self),
            K(MoveEffectKind.AddType, "Añadir un tipo al rival", "Tipos y forma", MoveEffectField.Type),
            K(MoveEffectKind.CopyTypes, "Copiar los tipos del rival", "Tipos y forma", MoveEffectField.None),
            K(MoveEffectKind.RemoveItem, "Quitarle el objeto", "Objetos y habilidades", MoveEffectField.None),
            K(MoveEffectKind.StealItem, "Robarle el objeto", "Objetos y habilidades", MoveEffectField.None),
            K(MoveEffectKind.SwapItems, "Intercambiar objetos", "Objetos y habilidades", MoveEffectField.None),
            K(MoveEffectKind.GiveItem, "Darle su objeto", "Objetos y habilidades", MoveEffectField.None),
            K(MoveEffectKind.ConsumeTargetBerry, "Comerse su baya", "Objetos y habilidades", MoveEffectField.None),
            K(MoveEffectKind.RestoreItem, "Recuperar el objeto gastado", "Objetos y habilidades", MoveEffectField.None, EffectTarget.Self),
            K(MoveEffectKind.CopyAbility, "Copiar su habilidad", "Objetos y habilidades", MoveEffectField.None),
            K(MoveEffectKind.SwapAbility, "Intercambiar habilidades", "Objetos y habilidades", MoveEffectField.None),
            K(MoveEffectKind.SetAbility, "Cambiarle la habilidad", "Objetos y habilidades", MoveEffectField.Ability),
            K(MoveEffectKind.GiveAbility, "Darle su habilidad", "Objetos y habilidades", MoveEffectField.None),
            K(MoveEffectKind.CallRandomMove, "Usar uno al azar (Metrónomo)", "Usar otros movimientos", MoveEffectField.None, EffectTarget.Self),
            K(MoveEffectKind.CallLastMove, "Usar el último del rival (Espejo)", "Usar otros movimientos", MoveEffectField.None),
            K(MoveEffectKind.CopyLastMove, "Copiar el último del rival (Mimético)", "Usar otros movimientos", MoveEffectField.None),
            K(MoveEffectKind.CallMove, "Usar un movimiento concreto", "Usar otros movimientos", MoveEffectField.Move),
            K(MoveEffectKind.CallTargetMove, "Usar antes el del rival (Yo Primero)", "Usar otros movimientos", MoveEffectField.None),
            K(MoveEffectKind.CallTeamMove, "Usar uno de un compañero (Ayuda)", "Usar otros movimientos", MoveEffectField.None, EffectTarget.Self),
            K(MoveEffectKind.CallOwnMove, "Usar otro de los suyos (Sonámbulo)", "Usar otros movimientos", MoveEffectField.None, EffectTarget.Self),
        };

        public static KindInfo Info(MoveEffectKind k) => Kinds.FirstOrDefault(x => x.Kind == k) ?? K(k, k.ToString(), "Otros", MoveEffectField.None);

        public static IEnumerable<string> Groups => Kinds.Select(k => k.Group).Distinct();

        /// <summary>A new effect of that kind with sensible values.</summary>
        public static ParsedEffect Default(MoveEffectKind k)
        {
            var info = Info(k);
            var e = new ParsedEffect { Kind = k, Target = info.DefaultTarget, Chance = 100 };
            if ((info.Fields & MoveEffectField.Amount) != 0) e.Amount = k == MoveEffectKind.Substitute ? 25 : 50;
            if ((info.Fields & MoveEffectField.Stages) != 0) e.Stages = k == MoveEffectKind.CritBoost || k == MoveEffectKind.RaiseRandomStat ? 2 : 1;
            if ((info.Fields & MoveEffectField.Stat) != 0) e.Stat = "attack";
            if (k == MoveEffectKind.SwapOwnStats) e.Text = "attack,defense";
            return e;
        }

        /// <summary>The effect in a sentence: chance, what, on whom and when. 'name' turns ids into names.</summary>
        public static string Describe(ParsedEffect e, Func<string, string, string> name = null)
        {
            name ??= (kind, id) => id;
            string who = e.Target == EffectTarget.Self ? "al usuario" : "al rival";
            string chance = e.Shared ? "(con el mismo dado que el anterior) " : e.Chance >= 100f ? "" : $"{e.Chance:0.#} % de ";
            string side = e.Target == EffectTarget.Self ? "propio" : "del rival";
            string what = e.Kind switch
            {
                MoveEffectKind.InflictStatus => $"poner {name("estado", e.Status)} {who}",
                MoveEffectKind.Drain => $"recuperar el {e.Amount:0} % del daño causado",
                MoveEffectKind.Recoil => $"recibir el {e.Amount:0} % del daño causado",
                MoveEffectKind.RecoilMaxHp => $"perder el {e.Amount:0} % de sus PS máximos",
                MoveEffectKind.HealSelf => $"curarse el {e.Amount:0} % de sus PS máximos",
                MoveEffectKind.Heal => $"curar {who} el {e.Amount:0} % de sus PS máximos",
                MoveEffectKind.CureStatus => $"quitar {(string.IsNullOrWhiteSpace(e.Status) ? "el estado principal" : name("estado", e.Status))} {who}",
                MoveEffectKind.SetWeather => $"cambiar el clima a {name("clima", e.Weather)}" + (e.WeatherTurns > 0 ? $" ({e.WeatherTurns} turnos)" : ""),
                MoveEffectKind.Flinch => "hacer retroceder al rival (pierde el turno si aún no actuó)",
                MoveEffectKind.SetHazard => $"poner una capa de {name("trampa", e.Hazard)} en el lado del rival",
                MoveEffectKind.ClearHazards => $"quitar {(string.IsNullOrWhiteSpace(e.Hazard) ? "todas las trampas" : name("trampa", e.Hazard))} del lado {side}",
                MoveEffectKind.ForceSwitch => "obligar al rival a cambiarse (en un combate salvaje, lo termina)",
                MoveEffectKind.ChangeStatStage => $"{(e.Stages > 0 ? "subir" : "bajar")} {Math.Abs(e.Stages)} etapa(s) de {StatLabels.NameOf(e.Stat)} {who}",
                MoveEffectKind.SetSideCondition => $"poner {name("lado", e.Side)} en el lado {side}",
                MoveEffectKind.ResetStages => $"devolver a 0 las etapas {who}",
                MoveEffectKind.CritBoost => $"subir {(e.Stages > 0 ? e.Stages : 2)} el índice de crítico del usuario",
                MoveEffectKind.Substitute => $"crear un sustituto pagando el {(e.Amount > 0 ? e.Amount : 25):0} % de sus PS",
                MoveEffectKind.DisableMove => $"anular el último movimiento del rival {(e.Turns > 0 ? e.Turns : 4)} turnos",
                MoveEffectKind.Encore => $"obligar al rival a repetir su último movimiento {(e.Turns > 0 ? e.Turns : 3)} turnos",
                MoveEffectKind.Rampage => $"seguir usándolo 2-{(e.Turns > 0 ? e.Turns : 3)} turnos" + (string.IsNullOrWhiteSpace(e.Status) ? "" : $" y luego quedar {name("estado", e.Status)}"),
                MoveEffectKind.Rage => $"cada golpe recibido sube {(e.Stages != 0 ? e.Stages : 1)} etapa(s) de {StatLabels.NameOf(string.IsNullOrWhiteSpace(e.Stat) ? "attack" : e.Stat)}",
                MoveEffectKind.ChangeType => string.IsNullOrWhiteSpace(e.TypeId) ? $"cambiar el tipo {who} al de su primer movimiento" : $"cambiar el tipo {who} a {name("tipo", e.TypeId)}",
                MoveEffectKind.AddType => $"añadir el tipo {name("tipo", e.TypeId)} al rival",
                MoveEffectKind.SwitchSelf => e.Stages > 0 ? "retirarse y pasar sus etapas al que entre (Relevo)" : "retirarse tras el golpe (Ida y Vuelta)",
                MoveEffectKind.SetAbility => $"cambiar la habilidad del rival a {name("habilidad", e.Text)}",
                MoveEffectKind.CallMove => $"usar {name("movimiento", e.Text)}",
                MoveEffectKind.DelayedHeal => $"curar el {(e.Amount > 0 ? e.Amount : 50):0} % dentro de {(e.Turns > 0 ? e.Turns : 1)} turno(s)",
                MoveEffectKind.DelayedDamage => $"golpear dentro de {(e.Turns > 0 ? e.Turns : 2)} turnos",
                MoveEffectKind.RaiseRandomStat => $"subir {(e.Stages != 0 ? e.Stages : 2)} una estadística al azar",
                MoveEffectKind.SwapOwnStats => $"intercambiar {string.Join(" y ", (e.Text ?? "attack,defense").Split(',').Select(s => StatLabels.NameOf(s.Trim())))}",
                _ => char.ToLowerInvariant(Info(e.Kind).Label[0]) + Info(e.Kind).Label.Substring(1),
            };
            string conds = e.Conditions != null && e.Conditions.Count > 0 ? " — " + ConditionText.Describe(e.Conditions) : "";
            var s = chance + what + conds + ".";
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }
    }
}
