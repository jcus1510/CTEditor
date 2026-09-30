using System.Collections.Generic;
using System.Linq;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Formulas;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// Validación de los Lotes A y B: fórmulas de potencia, condiciones (que lo que nombran exista),
    /// climas, estados volátiles clásicos y la tabla de críticos. Reutiliza las comprobaciones en vivo
    /// del editor de movimientos, así el validador y el editor dicen exactamente lo mismo.
    /// </summary>
    public static partial class ContentValidator
    {
        private static void ValidateAdvanced(List<MoveData> moves, List<StatusConditionData> statuses,
            List<RulesetData> rulesets, List<ValidationIssue> issues)
        {
            foreach (var move in moves)
                foreach (var (error, text) in MoveEditorWindow.Check(move))
                    issues.Add(error ? Error($"'{Name(move)}': {text}", move) : Warning($"'{Name(move)}': {text}", move));

            foreach (var st in statuses)
                if (ClassicStatusPresets.IsClassicVolatile(st.Id) && !st.IsVolatile)
                    issues.Add(Warning($"El estado '{Name(st)}' es volátil en los juegos (se suma al principal) pero aquí está como PRINCIPAL. " +
                                       "Abre CTEditor → Combate → Estados y pulsa su plantilla clásica para actualizarlo.", st));

            var weathers = ContentAssets.LoadAll<WeatherData>();
            var seen = new HashSet<string>();
            foreach (var w in weathers)
            {
                if (string.IsNullOrWhiteSpace(w.Id)) { issues.Add(Error("Hay un clima sin id.", w)); continue; }
                if (!seen.Add(w.Id)) issues.Add(Error($"El id de clima '{w.Id}' está repetido.", w));
                if (w.TypePowerMultipliers != null && w.TypePowerMultipliers.Any(m => m != null && m.type == null))
                    issues.Add(Warning($"El clima '{w.Id}' tiene un multiplicador sin tipo (se ignora).", w));
            }

            // Trampas: ids únicos y que existan las que usan los movimientos.
            var hazardIds = new HashSet<string>();
            foreach (var h in ContentAssets.LoadAll<HazardData>())
            {
                if (string.IsNullOrWhiteSpace(h.Id)) { issues.Add(Error("Hay una trampa de campo sin id.", h)); continue; }
                if (!hazardIds.Add(h.Id)) issues.Add(Error($"El id de trampa '{h.Id}' está repetido.", h));
                if ((h.DamagePercentByLayer == null || h.DamagePercentByLayer.Length == 0) && (h.StatusByLayer == null || h.StatusByLayer.Length == 0)
                    && string.IsNullOrWhiteSpace(h.StatId))
                    issues.Add(Warning($"La trampa '{h.Id}' no hace nada (sin daño, estado ni estadística).", h));
            }
            foreach (var move in moves)
                foreach (var e in move.SecondaryEffects ?? new MoveData.MoveEffectData[0])
                    if (e != null && e.kind == Domain.Moves.MoveEffectKind.SetHazard && !hazardIds.Contains((e.hazardId ?? "").Trim()))
                        issues.Add(Error($"'{Name(move)}' pone la trampa '{e.hazardId}', que no existe (créala en Trampas de campo).", move));

            // Efectos de lado: ids únicos, que hagan algo y que existan los que usan los movimientos.
            var sideIds = new HashSet<string>();
            foreach (var sc in ContentAssets.LoadAll<SideConditionData>())
            {
                if (string.IsNullOrWhiteSpace(sc.Id)) { issues.Add(Error("Hay un efecto de lado sin id.", sc)); continue; }
                if (!sideIds.Add(sc.Id)) issues.Add(Error($"El id de efecto de lado '{sc.Id}' está repetido.", sc));
                if (sc.PhysicalDamageMultiplier == 1f && sc.SpecialDamageMultiplier == 1f && !sc.BlocksStatDrops && !sc.BlocksStatus && sc.SpeedMultiplier == 1f)
                    issues.Add(Warning($"El efecto de lado '{sc.Id}' no hace nada.", sc));
            }
            var typeIdsAll = new HashSet<string>(ContentAssets.LoadAll<ElementTypeData>().Where(t => !string.IsNullOrEmpty(t.Id)).Select(t => t.Id));
            var statusIdsAll = new HashSet<string>(ContentAssets.LoadAll<StatusConditionData>().Where(t => !string.IsNullOrEmpty(t.Id)).Select(t => t.Id));
            foreach (var move in moves)
                foreach (var e in move.SecondaryEffects ?? new MoveData.MoveEffectData[0])
                {
                    if (e == null) continue;
                    if (e.kind == Domain.Moves.MoveEffectKind.SetSideCondition && !sideIds.Contains((e.sideConditionId ?? "").Trim()))
                        issues.Add(Error($"'{Name(move)}' pone el efecto de lado '{e.sideConditionId}', que no existe (créalo en Efectos de lado).", move));
                    if (e.kind == Domain.Moves.MoveEffectKind.ChangeType && !string.IsNullOrWhiteSpace(e.typeId) && !typeIdsAll.Contains(e.typeId.Trim()))
                        issues.Add(Error($"'{Name(move)}' cambia al tipo '{e.typeId}', que no existe.", move));
                    if (e.kind == Domain.Moves.MoveEffectKind.Rampage && !string.IsNullOrWhiteSpace(e.statusId) && !statusIdsAll.Contains(e.statusId.Trim()))
                        issues.Add(Warning($"'{Name(move)}' deja el estado '{e.statusId}' al acabar, pero ese estado no existe.", move));
                }

            // Objetos: ids únicos + las comprobaciones del editor de objetos.
            var items = ContentAssets.LoadAll<ItemData>();
            var itemIds = new HashSet<string>();
            foreach (var it in items)
            {
                if (string.IsNullOrWhiteSpace(it.Id)) { issues.Add(Error("Hay un objeto sin id.", it)); continue; }
                if (!itemIds.Add(it.Id)) issues.Add(Error($"El id de objeto '{it.Id}' está repetido.", it));
                foreach (var w in ItemEditorWindow.Check(it)) issues.Add(Warning($"Objeto '{it.Id}': {w}", it));
            }

            // Lote E: grupos huevo (máximo 2, como en los juegos) y habilidades 2.ª / oculta que existan.
            var abilityIds = new HashSet<string>(ContentAssets.LoadAll<AbilityData>().Select(a => a.Id));
            foreach (var sp in ContentAssets.LoadAll<SpeciesData>())
            {
                var groups = (sp.EggGroups ?? new EggGroupData[0]).Where(g => g != null).ToList();
                if (groups.Count > 2) issues.Add(Warning($"La especie '{sp.Id}' tiene {groups.Count} grupos huevo: en los juegos son como mucho 2.", sp));
                if (groups.Select(g => g.Id).Distinct().Count() < groups.Count) issues.Add(Warning($"La especie '{sp.Id}' repite un grupo huevo.", sp));
                foreach (var (label, id) in new[] { ("segunda habilidad", sp.SecondAbilityId), ("habilidad oculta", sp.HiddenAbilityId) })
                    if (!string.IsNullOrWhiteSpace(id) && !abilityIds.Contains(id))
                        issues.Add(Warning($"La especie '{sp.Id}' tiene como {label} '{id}', que no existe (créala en Habilidades o importa el pack).", sp));
            }

            // Evoluciones con objeto: el objeto debe existir.
            foreach (var sp in ContentAssets.LoadAll<SpeciesData>())
            {
                if (sp.Evolutions == null) continue;
                foreach (var e in sp.Evolutions)
                {
                    if (e.target == null) continue;
                    bool needsItem = e.method == CTEditor.GameDefinition.Domain.Species.EvolutionMethod.Item;
                    if (needsItem && string.IsNullOrWhiteSpace(e.itemId))
                        issues.Add(Error($"La especie '{sp.Id}' evoluciona a '{e.target.Id}' con un objeto, pero no dice cuál.", sp));
                    else if (!string.IsNullOrWhiteSpace(e.itemId) && !itemIds.Contains(e.itemId))
                        issues.Add(Warning($"La especie '{sp.Id}' evoluciona con el objeto '{e.itemId}', que no existe (créalo en Objetos).", sp));
                    if (e.method == CTEditor.GameDefinition.Domain.Species.EvolutionMethod.Level && e.requiredLevel < 1)
                        issues.Add(Warning($"La especie '{sp.Id}' evoluciona por nivel a '{e.target.Id}' con nivel {e.requiredLevel} (debe ser 1 o más).", sp));
                    // Condiciones extra: completas y con sentido.
                    var conds = (e.conditions ?? new SpeciesData.EvolutionConditionData[0]).Where(c => c != null).ToList();
                    foreach (var c in conds)
                    {
                        var problem = EvolutionText.Problem(c);
                        if (problem != null) issues.Add(Error($"La especie '{sp.Id}' → '{e.target.Id}': condición «{Etiquetas.Enum(c.check.ToString())}»: {problem}.", sp));
                    }
                    if (e.method == CTEditor.GameDefinition.Domain.Species.EvolutionMethod.LevelUp && conds.Count == 0)
                        issues.Add(Warning($"La especie '{sp.Id}' evoluciona a '{e.target.Id}' «al subir de nivel» sin condiciones: lo hará al subir cualquier nivel.", sp));
                    if (conds.Count(c => c.check == CTEditor.GameDefinition.Domain.Species.EvolutionConditionKind.TimeOfDay && !c.negate) > 1)
                        issues.Add(Warning($"La especie '{sp.Id}' → '{e.target.Id}' pide dos momentos del día a la vez: revisa que puedan cumplirse juntos.", sp));
                }
            }

            foreach (var rs in rulesets)
            {
                var t = rs.CritDenominators;
                if (t != null && t.Any(n => n < 0))
                    issues.Add(Error($"Las reglas '{Name(rs)}' tienen un número negativo en la tabla de críticos (usa 0 = nunca).", rs));
            }
        }
    }
}
