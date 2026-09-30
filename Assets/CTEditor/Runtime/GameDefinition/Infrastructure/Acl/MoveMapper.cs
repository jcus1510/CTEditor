using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.GameDefinition.Domain.Status;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Infrastructure.Acl
{
    /// <summary>
    /// La ACL para movimientos: MoveData (Unity) -> Move (dominio puro). Muestra dos traducciones
    /// que se repetirán por todo el motor: resolver una referencia cruzada a id, y reconstruir un
    /// nullable a partir de campos amigables.
    /// </summary>
    public static class MoveMapper
    {
        public static Move ToDomain(MoveData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            // Un movimiento sin tipo no se puede mapear: el autor olvidó arrastrar la ficha del tipo.
            // Esto es un fallo "duro" del programador/datos; la validación amigable (L.8) lo cazaría
            // antes con un mensaje claro, pero aquí ponemos la última red.
            if (data.Type == null)
                throw new InvalidOperationException($"El movimiento '{data.Id}' no tiene tipo asignado.");

            // REFERENCIA CRUZADA -> ID: no metemos el ElementType entero, solo su id estable.
            // data.Type es la ficha referenciada; data.Type.Id es su id de texto; lo envolvemos en
            // el Id<ElementType> tipado del dominio.
            var typeId = new Id<ElementType>(data.Type.Id);

            // NULLABLE reconstruido: si "nunca falla", la precisión del dominio es null; si no, es el
            // Percentage con el número del Inspector. El '(Percentage?)' fuerza a que ambas ramas del
            // '?:' tengan el mismo tipo nullable (sin ese casteo el compilador se queja).
            Percentage? accuracy = data.NeverMisses
                ? (Percentage?)null
                : new Percentage(data.Accuracy);

            // EFECTOS SECUNDARIOS: cada sub-ficha -> un MoveEffect del dominio. Las entradas vacías
            // (sin estado) se ignoran.
            var effects = new List<MoveEffect>();
            if (data.SecondaryEffects != null)
            {
                foreach (var e in data.SecondaryEffects)
                {
                    if (e == null) continue;
                    var chance = new Percentage(e.chancePercent);
                    var conditions = ConditionMapper.ToDomain(e.conditions);   // Lote A: condiciones
                    bool shared = e.sharesPreviousRoll;                          // Lote A: dado compartido

                    switch (e.kind)
                    {
                        case MoveEffectKind.InflictStatus:
                            if (string.IsNullOrWhiteSpace(e.statusId)) continue; // efecto de estado vacío: se ignora
                            effects.Add(new MoveEffect(chance, MoveEffectKind.InflictStatus, e.target, status: new StatusId(e.statusId),
                                conditions: conditions, sharesPreviousRoll: shared));
                            break;

                        case MoveEffectKind.ChangeStatStage:
                            if (string.IsNullOrWhiteSpace(e.statStatId)) continue; // sin stat: se ignora
                            effects.Add(new MoveEffect(chance, MoveEffectKind.ChangeStatStage, e.target,
                                stat: new StatId(e.statStatId), stages: e.statStages, conditions: conditions, sharesPreviousRoll: shared));
                            break;

                        case MoveEffectKind.CureStatus:
                            // Sin id = cura el estado PRINCIPAL; con id = ese estado (principal o volátil).
                            effects.Add(new MoveEffect(chance, MoveEffectKind.CureStatus, e.target,
                                status: string.IsNullOrWhiteSpace(e.statusId) ? default : new StatusId(e.statusId.Trim()),
                                conditions: conditions, sharesPreviousRoll: shared));
                            break;

                        case MoveEffectKind.SetWeather:
                            if (string.IsNullOrWhiteSpace(e.weatherId)) continue; // sin clima: se ignora
                            effects.Add(new MoveEffect(chance, MoveEffectKind.SetWeather, e.target, conditions: conditions,
                                sharesPreviousRoll: shared, weatherId: e.weatherId.Trim(), weatherTurns: e.weatherTurns));
                            break;

                        case MoveEffectKind.SetHazard:
                            if (string.IsNullOrWhiteSpace(e.hazardId)) continue; // sin trampa: se ignora
                            effects.Add(new MoveEffect(chance, MoveEffectKind.SetHazard, e.target, conditions: conditions,
                                sharesPreviousRoll: shared, hazardId: e.hazardId.Trim()));
                            break;

                        case MoveEffectKind.ClearHazards: // sin id = todas
                            effects.Add(new MoveEffect(chance, MoveEffectKind.ClearHazards, e.target, conditions: conditions,
                                sharesPreviousRoll: shared, hazardId: (e.hazardId ?? "").Trim()));
                            break;

                        case MoveEffectKind.ForceSwitch:
                            effects.Add(new MoveEffect(chance, MoveEffectKind.ForceSwitch, e.target, conditions: conditions, sharesPreviousRoll: shared));
                            break;

                        case MoveEffectKind.SetSideCondition:
                            if (string.IsNullOrWhiteSpace(e.sideConditionId)) continue; // sin efecto de lado: se ignora
                            effects.Add(new MoveEffect(chance, MoveEffectKind.SetSideCondition, e.target, conditions: conditions,
                                sharesPreviousRoll: shared, sideConditionId: e.sideConditionId.Trim()));
                            break;

                        default:
                            // Drain / Recoil / HealSelf / Heal / Flinch usan el porcentaje; Foco Energía y Relevo las etapas;
                            // Anulación, Otra Vez y Saña los turnos (y Saña el estado final); Furia la stat y las etapas;
                            // Conversión el tipo. Se pasa todo y cada efecto usa lo suyo.
                            effects.Add(new MoveEffect(chance, e.kind, e.target,
                                status: string.IsNullOrWhiteSpace(e.statusId) ? default : new StatusId(e.statusId.Trim()),
                                amount: new Percentage(e.amountPercent),
                                stat: string.IsNullOrWhiteSpace(e.statStatId) ? default : new StatId(e.statStatId.Trim()),
                                stages: e.statStages, conditions: conditions, sharesPreviousRoll: shared,
                                turns: e.turns, typeId: (e.typeId ?? "").Trim(), text: (e.text ?? "").Trim(),
                                sideConditionId: (e.sideConditionId ?? "").Trim(), weatherId: (e.weatherId ?? "").Trim(), weatherTurns: e.weatherTurns));
                            break;
                    }
                }
            }

            return new Move(
                new Id<Move>(data.Id),
                data.DisplayName,
                typeId,
                data.Category,
                data.Power,
                accuracy,
                data.MaxPp,
                data.Priority,
                data.Target,
                effects,
                data.MinHits,
                data.MaxHits,
                data.CritStage,
                data.TwoTurn,
                data.MakesContact,
                data.FixedDamage,
                data.FixedDamageAmount,
                data.RespectsTypeImmunity,
                ConditionMapper.ToDomain(data.PowerModifiers),
                data.PowerFormula,
                string.IsNullOrWhiteSpace(data.AttackStat) ? (StatId?)null : new StatId(data.AttackStat.Trim()),
                string.IsNullOrWhiteSpace(data.DefenseStat) ? (StatId?)null : new StatId(data.DefenseStat.Trim()),
                data.AttackStatFromTarget,
                data.Tags,
                ConditionMapper.ToDomain(data.Requirements),
                WeatherTypes(data.TypeByWeather));
        }

        private static Dictionary<string, Id<ElementType>> WeatherTypes(MoveData.WeatherTypeEntry[] entries)
        {
            var map = new Dictionary<string, Id<ElementType>>();
            if (entries != null)
                foreach (var e in entries)
                    if (e != null && e.type != null && !string.IsNullOrWhiteSpace(e.weatherId)) map[e.weatherId.Trim()] = new Id<ElementType>(e.type.Id);
            return map;
        }
    }
}
