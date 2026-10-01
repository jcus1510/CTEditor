using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CTEditor.GameDefinition.Domain.Conditions;
using CTEditor.GameDefinition.Domain.Moves;

namespace CTEditor.GameDefinition.Text
{
    /// <summary>Un efecto de movimiento leído de una celda (independiente de Unity, para poder testearlo).</summary>
    public sealed class ParsedEffect
    {
        public MoveEffectKind Kind;
        public EffectTarget Target = EffectTarget.Opponent;
        public float Chance = 100;
        public string Status;
        public float Amount;
        public string Stat;
        public int Stages;
        public List<Condition> Conditions = new List<Condition>();
        public bool Shared;
        public string Weather;
        public int WeatherTurns;
        public string Hazard;
        public int Turns;
        public string Side;
        public string TypeId;
        public string Text;
    }


    /// <summary>
    /// The text of move effects in the sheets («estado:burn@10 | stat_propio:speed:+2 [si propio.vida<=50]») and a catalogue
    /// in Spanish (name, group and what each kind needs) for the block editor of the application.
    /// </summary>
    public static partial class MoveEffectText
    {
        public static List<ParsedEffect> Parse(string cell)
        {
            var list = new List<ParsedEffect>();
            foreach (var raw in (cell ?? "").Split('|').Select(x => x.Trim()).Where(x => x.Length > 0))
            {
                string item = raw.Trim();
                // Condiciones: "[si ...]" al final.
                var conditions = new List<Condition>();
                int br = item.IndexOf('[');
                if (br >= 0)
                {
                    int close = item.LastIndexOf(']');
                    if (close < br) throw new FormatException($"'{raw}': falta cerrar el corchete ].");
                    try { conditions = ConditionText.ParseAll(item.Substring(br + 1, close - br - 1)); }
                    catch (FormatException fe) { throw new FormatException(fe.Message); }
                    item = item.Substring(0, br).Trim();
                }
                // Dado compartido: "&" delante.
                bool shared = item.StartsWith("&");
                if (shared) item = item.Substring(1).Trim();
                float chance = 100;
                int at = item.LastIndexOf('@');
                if (at >= 0)
                {
                    if (!TextNumbers.TryNumber(item.Substring(at + 1), out chance) || chance < 0 || chance > 100)
                        throw new FormatException($"'{raw}': la probabilidad tras '@' debe ser un número de 0 a 100.");
                    item = item.Substring(0, at);
                }
                var p = item.Split(':').Select(s => s.Trim()).ToArray();
                var e = new ParsedEffect { Chance = chance, Conditions = conditions, Shared = shared };
                switch (p[0].ToLowerInvariant())
                {
                    case "estado": case "estado_propio":
                        if (p.Length != 2 || p[1].Length == 0) throw new FormatException($"'{raw}': usa estado:idDelEstado (ej. estado:burn@10).");
                        e.Kind = MoveEffectKind.InflictStatus; e.Status = p[1];
                        e.Target = p[0].EndsWith("propio") ? EffectTarget.Self : EffectTarget.Opponent;
                        break;
                    case "drenar": e.Kind = MoveEffectKind.Drain; e.Target = EffectTarget.Self; e.Amount = Amount(p, raw); break;
                    case "retroceso": e.Kind = MoveEffectKind.Recoil; e.Target = EffectTarget.Self; e.Amount = Amount(p, raw); break;
                    case "retroceso_ps": e.Kind = MoveEffectKind.RecoilMaxHp; e.Target = EffectTarget.Self; e.Amount = Amount(p, raw); break;
                    case "curar": e.Kind = MoveEffectKind.Heal; e.Target = EffectTarget.Self; e.Amount = Amount(p, raw); break;
                    case "curar_rival": e.Kind = MoveEffectKind.Heal; e.Target = EffectTarget.Opponent; e.Amount = Amount(p, raw); break;
                    case "curar_estado": case "curar_estado_rival":
                        e.Kind = MoveEffectKind.CureStatus;
                        e.Target = p[0].EndsWith("rival") ? EffectTarget.Opponent : EffectTarget.Self;
                        e.Status = p.Length > 1 ? p[1] : "";
                        break;
                    case "clima":
                        if (p.Length < 2 || p[1].Length == 0) throw new FormatException($"'{raw}': usa clima:idDelClima (ej. clima:rain o clima:rain:8).");
                        e.Kind = MoveEffectKind.SetWeather; e.Target = EffectTarget.Self; e.Weather = p[1];
                        if (p.Length > 2 && (!TextNumbers.TryInt(p[2], out e.WeatherTurns) || e.WeatherTurns < 0))
                            throw new FormatException($"'{raw}': los turnos del clima deben ser un número (ej. clima:rain:8).");
                        break;
                    case "amedrentar": e.Kind = MoveEffectKind.Flinch; break;
                    case "trampa":
                        if (p.Length != 2 || p[1].Length == 0) throw new FormatException($"'{raw}': usa trampa:idDeLaTrampa (ej. trampa:spikes).");
                        e.Kind = MoveEffectKind.SetHazard; e.Target = EffectTarget.Opponent; e.Hazard = p[1];
                        break;
                    case "quitar_trampas": case "quitar_trampas_rival":
                        e.Kind = MoveEffectKind.ClearHazards;
                        e.Target = p[0].EndsWith("rival") ? EffectTarget.Opponent : EffectTarget.Self;
                        e.Hazard = p.Length > 1 ? p[1] : "";
                        break;
                    case "forzar_cambio": e.Kind = MoveEffectKind.ForceSwitch; break;
                    // --- Cierre del combate ---
                    case "lado": case "lado_rival":
                        if (p.Length != 2 || p[1].Length == 0) throw new FormatException($"'{raw}': usa lado:idDelEfecto (ej. lado:reflect).");
                        e.Kind = MoveEffectKind.SetSideCondition; e.Side = p[1];
                        e.Target = p[0].EndsWith("rival") ? EffectTarget.Opponent : EffectTarget.Self;
                        break;
                    case "reiniciar_etapas": case "reiniciar_etapas_rival":
                        e.Kind = MoveEffectKind.ResetStages; e.Target = p[0].EndsWith("rival") ? EffectTarget.Opponent : EffectTarget.Self; break;
                    case "foco":
                        e.Kind = MoveEffectKind.CritBoost; e.Target = EffectTarget.Self; e.Stages = OptInt(p, 1, 2, raw); break;
                    case "sustituto":
                        e.Kind = MoveEffectKind.Substitute; e.Target = EffectTarget.Self; e.Amount = p.Length > 1 ? Amount(p, raw) : 25f; break;
                    case "anular": e.Kind = MoveEffectKind.DisableMove; e.Turns = OptInt(p, 1, 0, raw); break;
                    case "otra_vez": e.Kind = MoveEffectKind.Encore; e.Turns = OptInt(p, 1, 0, raw); break;
                    case "desenfreno":
                        e.Kind = MoveEffectKind.Rampage; e.Target = EffectTarget.Self; e.Turns = OptInt(p, 1, 0, raw);
                        e.Status = p.Length > 2 ? p[2] : "";
                        break;
                    case "furia":
                        e.Kind = MoveEffectKind.Rage; e.Target = EffectTarget.Self;
                        e.Stat = p.Length > 1 ? p[1] : "attack";
                        e.Stages = p.Length > 2 ? OptInt(new[] { p[0], p[2].Replace("+", "") }, 1, 1, raw) : 1;
                        break;
                    case "metronomo": e.Kind = MoveEffectKind.CallRandomMove; e.Target = EffectTarget.Self; break;
                    case "espejo": e.Kind = MoveEffectKind.CallLastMove; break;
                    case "mimetico": e.Kind = MoveEffectKind.CopyLastMove; break;
                    case "transformarse": e.Kind = MoveEffectKind.Transform; break;
                    case "cambiar_tipo": e.Kind = MoveEffectKind.ChangeType; e.Target = EffectTarget.Self; e.TypeId = p.Length > 1 ? p[1] : ""; break;
                    case "cambio_propio": e.Kind = MoveEffectKind.SwitchSelf; e.Target = EffectTarget.Self; break;
                    case "relevo": e.Kind = MoveEffectKind.SwitchSelf; e.Target = EffectTarget.Self; e.Stages = 1; break;
                    case "teletransporte": e.Kind = MoveEffectKind.Teleport; e.Target = EffectTarget.Self; break;
                    case "stat": case "stat_propio":
                        if (p.Length != 3 || p[1].Length == 0 || !TextNumbers.TryInt(p[2].Replace("+", ""), out int stages))
                            throw new FormatException($"'{raw}': usa stat:idDeStat:etapas (ej. stat:attack:-1 o stat_propio:speed:+2).");
                        e.Kind = MoveEffectKind.ChangeStatStage; e.Stat = p[1]; e.Stages = stages;
                        e.Target = p[0].EndsWith("propio") ? EffectTarget.Self : EffectTarget.Opponent;
                        break;
                    // --- 3.ª y 4.ª generación ---
                    case "quitar_lado": e.Kind = MoveEffectKind.ClearSideConditions; e.Text = p.Length > 1 ? string.Join("|", p.Skip(1)) : ""; break;
                    case "quitar_objeto": e.Kind = MoveEffectKind.RemoveItem; break;
                    case "robar_objeto": e.Kind = MoveEffectKind.StealItem; break;
                    case "cambiar_objetos": e.Kind = MoveEffectKind.SwapItems; break;
                    case "comer_baya": e.Kind = MoveEffectKind.ConsumeTargetBerry; break;
                    case "reciclar": e.Kind = MoveEffectKind.RestoreItem; e.Target = EffectTarget.Self; break;
                    case "copiar_habilidad": e.Kind = MoveEffectKind.CopyAbility; break;
                    case "cambiar_habilidades": e.Kind = MoveEffectKind.SwapAbility; break;
                    case "poner_habilidad":
                        if (p.Length != 2 || p[1].Length == 0) throw new FormatException($"'{raw}': usa poner_habilidad:idDeLaHabilidad (ej. poner_habilidad:insomnia).");
                        e.Kind = MoveEffectKind.SetAbility; e.Text = p[1]; break;
                    case "deseo": e.Kind = MoveEffectKind.DelayedHeal; e.Target = EffectTarget.Self; e.Turns = OptInt(p, 1, 1, raw); e.Amount = p.Length > 2 && TextNumbers.TryNumber(p[2], out var dh) ? dh : 50f; break;
                    case "premonicion": e.Kind = MoveEffectKind.DelayedDamage; e.Turns = OptInt(p, 1, 2, raw); break;
                    case "reserva": e.Kind = MoveEffectKind.Stockpile; e.Target = EffectTarget.Self; break;
                    case "usar_reserva": e.Kind = MoveEffectKind.UseStockpile; e.Target = EffectTarget.Self; e.Amount = p.Length > 1 && p[1].StartsWith("cur") ? 1f : 0f; break;
                    case "pasar_estado": e.Kind = MoveEffectKind.TransferStatus; break;
                    case "stat_al_azar": e.Kind = MoveEffectKind.RaiseRandomStat; e.Target = EffectTarget.Self; e.Stages = OptInt(p, 1, 2, raw); break;
                    case "cambiar_stats": e.Kind = MoveEffectKind.SwapOwnStats; e.Target = EffectTarget.Self; e.Text = p.Length > 1 ? p[1] : "attack,defense"; break;
                    case "intercambiar_etapas": e.Kind = MoveEffectKind.SwapStages; e.Text = p.Length > 1 ? p[1] : ""; break;
                    case "copiar_etapas": e.Kind = MoveEffectKind.CopyStages; break;
                    case "debilitarse": e.Kind = MoveEffectKind.SelfFaint; e.Target = EffectTarget.Self; break;
                    case "deseo_cura": e.Kind = MoveEffectKind.HealNextSwitchIn; e.Target = EffectTarget.Self; break;
                    case "dividir_dolor": e.Kind = MoveEffectKind.PainSplit; break;
                    case "ayuda": e.Kind = MoveEffectKind.CallTeamMove; e.Target = EffectTarget.Self; break;
                    case "usar":
                        if (p.Length != 2 || p[1].Length == 0) throw new FormatException($"'{raw}': usa usar:idDelMovimiento (ej. usar:tri_attack).");
                        e.Kind = MoveEffectKind.CallMove; e.Text = p[1]; break;
                    case "yo_primero": e.Kind = MoveEffectKind.CallTargetMove; break;
                    case "curar_equipo": e.Kind = MoveEffectKind.TeamCureStatus; e.Target = EffectTarget.Self; break;
                    case "sonambulo": e.Kind = MoveEffectKind.CallOwnMove; e.Target = EffectTarget.Self; break;
                    // --- 5.ª y 6.ª generación ---
                    case "dar_habilidad": e.Kind = MoveEffectKind.GiveAbility; break;
                    case "copiar_tipos": e.Kind = MoveEffectKind.CopyTypes; break;
                    case "dar_objeto": e.Kind = MoveEffectKind.GiveItem; break;
                    case "anadir_tipo": case "añadir_tipo":
                        if (p.Length != 2 || p[1].Length == 0) throw new FormatException($"'{raw}': usa añadir_tipo:idDelTipo (ej. añadir_tipo:ghost).");
                        e.Kind = MoveEffectKind.AddType; e.TypeId = p[1]; break;
                    case "invertir_etapas": e.Kind = MoveEffectKind.InvertStages; break;
                    case "cambiar_tipo_rival": e.Kind = MoveEffectKind.ChangeType; e.Target = EffectTarget.Opponent; e.TypeId = p.Length > 1 ? p[1] : ""; break;
                    default:
                        throw new FormatException($"'{raw}': efecto desconocido '{p[0]}'. Usa: estado, estado_propio, drenar, retroceso, retroceso_ps, curar, curar_rival, curar_estado, curar_estado_rival, clima, stat, stat_propio, amedrentar, trampa, quitar_trampas, quitar_trampas_rival, forzar_cambio, lado, lado_rival, reiniciar_etapas, reiniciar_etapas_rival, foco, sustituto, anular, otra_vez, desenfreno, furia, metronomo, espejo, mimetico, transformarse, cambiar_tipo, cambio_propio, relevo, teletransporte, quitar_lado, quitar_objeto, robar_objeto, cambiar_objetos, comer_baya, reciclar, copiar_habilidad, cambiar_habilidades, poner_habilidad, deseo, premonicion, reserva, usar_reserva, pasar_estado, stat_al_azar, cambiar_stats, intercambiar_etapas, copiar_etapas, debilitarse, deseo_cura, dividir_dolor, ayuda, usar, yo_primero, curar_equipo.");
                }
                list.Add(e);
            }
            return list;
        }

        // Número opcional en la posición 'i' (si falta, 'fallback').
        private static int OptInt(string[] p, int i, int fallback, string raw)
        {
            if (p.Length <= i || p[i].Length == 0) return fallback;
            if (!TextNumbers.TryInt(p[i].Replace("+", ""), out int v)) throw new FormatException($"'{raw}': '{p[i]}' debería ser un número.");
            return v;
        }

        private static float Amount(string[] p, string raw)
        {
            if (p.Length != 2 || !TextNumbers.TryNumber(p[1], out float v))
                throw new FormatException($"'{raw}': falta el porcentaje (ej. {p[0]}:50).");
            return v;
        }

        public static string Format(IEnumerable<ParsedEffect> effects)
        {
            string C(float c) => c >= 100f ? "" : "@" + c.ToString("0.##", CultureInfo.InvariantCulture);
            string N(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
            return string.Join("|", effects.Select(e =>
            {
                string text = FormatOne(e);
                if (text.Length == 0) return text;
                if (e.Shared) text = "&" + text;
                if (e.Conditions != null && e.Conditions.Count > 0) text += " [si " + ConditionText.FormatAll(e.Conditions) + "]";
                return text;
            }).Where(x => !string.IsNullOrEmpty(x)));

            string FormatOne(ParsedEffect e)
            {
                bool self = e.Target == EffectTarget.Self;
                switch (e.Kind)
                {
                    case MoveEffectKind.InflictStatus: return $"{(self ? "estado_propio" : "estado")}:{e.Status}{C(e.Chance)}";
                    case MoveEffectKind.Drain: return $"drenar:{N(e.Amount)}{C(e.Chance)}";
                    case MoveEffectKind.Recoil: return $"retroceso:{N(e.Amount)}{C(e.Chance)}";
                    case MoveEffectKind.RecoilMaxHp: return $"retroceso_ps:{N(e.Amount)}{C(e.Chance)}";
                    case MoveEffectKind.HealSelf: return $"curar:{N(e.Amount)}{C(e.Chance)}";
                    case MoveEffectKind.Heal: return $"{(self ? "curar" : "curar_rival")}:{N(e.Amount)}{C(e.Chance)}";
                    case MoveEffectKind.CureStatus:
                        return $"{(self ? "curar_estado" : "curar_estado_rival")}{(string.IsNullOrEmpty(e.Status) ? "" : ":" + e.Status)}{C(e.Chance)}";
                    case MoveEffectKind.SetWeather: return $"clima:{e.Weather}{(e.WeatherTurns > 0 ? ":" + e.WeatherTurns : "")}{C(e.Chance)}";
                    case MoveEffectKind.Flinch: return $"amedrentar{C(e.Chance)}";
                    case MoveEffectKind.SetHazard: return $"trampa:{e.Hazard}{C(e.Chance)}";
                    case MoveEffectKind.ClearHazards: return $"{(self ? "quitar_trampas" : "quitar_trampas_rival")}{(string.IsNullOrEmpty(e.Hazard) ? "" : ":" + e.Hazard)}{C(e.Chance)}";
                    case MoveEffectKind.ForceSwitch: return $"forzar_cambio{C(e.Chance)}";
                    case MoveEffectKind.ChangeStatStage: return $"{(self ? "stat_propio" : "stat")}:{e.Stat}:{(e.Stages > 0 ? "+" : "")}{e.Stages}{C(e.Chance)}";
                    case MoveEffectKind.SetSideCondition: return $"{(self ? "lado" : "lado_rival")}:{e.Side}{C(e.Chance)}";
                    case MoveEffectKind.ResetStages: return $"{(self ? "reiniciar_etapas" : "reiniciar_etapas_rival")}{C(e.Chance)}";
                    case MoveEffectKind.CritBoost: return $"foco{(e.Stages > 0 && e.Stages != 2 ? ":" + e.Stages : "")}{C(e.Chance)}";
                    case MoveEffectKind.Substitute: return $"sustituto{(e.Amount > 0 && e.Amount != 25f ? ":" + N(e.Amount) : "")}{C(e.Chance)}";
                    case MoveEffectKind.DisableMove: return $"anular{(e.Turns > 0 ? ":" + e.Turns : "")}{C(e.Chance)}";
                    case MoveEffectKind.Encore: return $"otra_vez{(e.Turns > 0 ? ":" + e.Turns : "")}{C(e.Chance)}";
                    case MoveEffectKind.Rampage:
                        return $"desenfreno{(e.Turns > 0 || !string.IsNullOrEmpty(e.Status) ? ":" + e.Turns : "")}{(string.IsNullOrEmpty(e.Status) ? "" : ":" + e.Status)}{C(e.Chance)}";
                    case MoveEffectKind.Rage:
                        return $"furia{(string.IsNullOrEmpty(e.Stat) || e.Stat == "attack" && e.Stages == 1 ? "" : $":{e.Stat}:{(e.Stages > 0 ? "+" : "")}{e.Stages}")}{C(e.Chance)}";
                    case MoveEffectKind.CallRandomMove: return $"metronomo{C(e.Chance)}";
                    case MoveEffectKind.CallLastMove: return $"espejo{C(e.Chance)}";
                    case MoveEffectKind.CopyLastMove: return $"mimetico{C(e.Chance)}";
                    case MoveEffectKind.Transform: return $"transformarse{C(e.Chance)}";
                    case MoveEffectKind.ChangeType: return $"{(e.Target == EffectTarget.Opponent ? "cambiar_tipo_rival" : "cambiar_tipo")}{(string.IsNullOrEmpty(e.TypeId) ? "" : ":" + e.TypeId)}{C(e.Chance)}";
                    case MoveEffectKind.SwitchSelf: return $"{(e.Stages > 0 ? "relevo" : "cambio_propio")}{C(e.Chance)}";
                    case MoveEffectKind.Teleport: return $"teletransporte{C(e.Chance)}";
                    case MoveEffectKind.ClearSideConditions: return $"quitar_lado{(string.IsNullOrEmpty(e.Text) ? "" : ":" + e.Text.Replace("|", ":"))}{C(e.Chance)}";
                    case MoveEffectKind.RemoveItem: return $"quitar_objeto{C(e.Chance)}";
                    case MoveEffectKind.StealItem: return $"robar_objeto{C(e.Chance)}";
                    case MoveEffectKind.SwapItems: return $"cambiar_objetos{C(e.Chance)}";
                    case MoveEffectKind.ConsumeTargetBerry: return $"comer_baya{C(e.Chance)}";
                    case MoveEffectKind.RestoreItem: return $"reciclar{C(e.Chance)}";
                    case MoveEffectKind.CopyAbility: return $"copiar_habilidad{C(e.Chance)}";
                    case MoveEffectKind.SwapAbility: return $"cambiar_habilidades{C(e.Chance)}";
                    case MoveEffectKind.SetAbility: return $"poner_habilidad:{e.Text}{C(e.Chance)}";
                    case MoveEffectKind.DelayedHeal: return $"deseo:{(e.Turns > 0 ? e.Turns : 1)}:{N(e.Amount > 0 ? e.Amount : 50)}{C(e.Chance)}";
                    case MoveEffectKind.DelayedDamage: return $"premonicion:{(e.Turns > 0 ? e.Turns : 2)}{C(e.Chance)}";
                    case MoveEffectKind.Stockpile: return $"reserva{C(e.Chance)}";
                    case MoveEffectKind.UseStockpile: return $"usar_reserva{(e.Amount > 0 ? ":curar" : "")}{C(e.Chance)}";
                    case MoveEffectKind.TransferStatus: return $"pasar_estado{C(e.Chance)}";
                    case MoveEffectKind.RaiseRandomStat: return $"stat_al_azar:{(e.Stages != 0 ? e.Stages : 2)}{C(e.Chance)}";
                    case MoveEffectKind.SwapOwnStats: return $"cambiar_stats:{(string.IsNullOrEmpty(e.Text) ? "attack,defense" : e.Text)}{C(e.Chance)}";
                    case MoveEffectKind.SwapStages: return $"intercambiar_etapas{(string.IsNullOrEmpty(e.Text) ? "" : ":" + e.Text)}{C(e.Chance)}";
                    case MoveEffectKind.CopyStages: return $"copiar_etapas{C(e.Chance)}";
                    case MoveEffectKind.SelfFaint: return $"debilitarse{C(e.Chance)}";
                    case MoveEffectKind.HealNextSwitchIn: return $"deseo_cura{C(e.Chance)}";
                    case MoveEffectKind.PainSplit: return $"dividir_dolor{C(e.Chance)}";
                    case MoveEffectKind.CallTeamMove: return $"ayuda{C(e.Chance)}";
                    case MoveEffectKind.CallMove: return $"usar:{e.Text}{C(e.Chance)}";
                    case MoveEffectKind.CallTargetMove: return $"yo_primero{C(e.Chance)}";
                    case MoveEffectKind.TeamCureStatus: return $"curar_equipo{C(e.Chance)}";
                    case MoveEffectKind.CallOwnMove: return $"sonambulo{C(e.Chance)}";
                    case MoveEffectKind.GiveAbility: return $"dar_habilidad{C(e.Chance)}";
                    case MoveEffectKind.CopyTypes: return $"copiar_tipos{C(e.Chance)}";
                    case MoveEffectKind.GiveItem: return $"dar_objeto{C(e.Chance)}";
                    case MoveEffectKind.AddType: return $"añadir_tipo:{e.TypeId}{C(e.Chance)}";
                    case MoveEffectKind.InvertStages: return $"invertir_etapas{C(e.Chance)}";
                    default: return "";
                }
            }
        }

    }
}
