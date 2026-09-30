using System.Collections.Generic;
using System.Linq;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;
using CTEditor.GameDefinition.Infrastructure.Acl;
using CTEditor.GameDefinition.Domain.Formulas;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// Reglas de validación del contenido de progresión y de los editores nuevos: curvas, naturalezas,
    /// habilidades, referencias de las especies y reglas del juego.
    ///
    /// 'partial' permite repartir una misma clase en varios archivos: el compilador los junta. Así
    /// el validador crece por temas sin convertirse en un archivo enorme.
    /// </summary>
    public static partial class ContentValidator
    {
        private static void ValidateProgression(List<SpeciesData> species, List<StatusConditionData> statuses,
            HashSet<string> statusIds, List<RulesetData> rulesets, List<ValidationIssue> issues)
        {
            var curves = LoadAll<GrowthCurveData>();
            var natures = LoadAll<NatureData>();
            var abilities = LoadAll<AbilityData>();

            var curveIds = CollectIds(curves, c => c.Id, "curva", issues);
            CollectIds(natures, n => n.Id, "naturaleza", issues);
            var abilityIds = CollectIds(abilities, a => a.Id, "habilidad", issues);

            // Stats conocidas: las 6 clásicas + cualquier stat inventada que use alguna especie.
            var knownStats = new HashSet<string>(StatLabels.ClassicIds);
            foreach (var sp in species)
                if (sp.CustomStats != null)
                    foreach (var cs in sp.CustomStats)
                        if (!string.IsNullOrWhiteSpace(cs.statId)) knownStats.Add(cs.statId.Trim());

            ValidateCurves(curves, issues);
            ValidateNatures(natures, knownStats, issues);
            ValidateAbilities(abilities, statusIds, knownStats, issues);
            ValidateSpeciesReferences(species, curveIds, abilityIds, knownStats, issues);
            ValidateRulesetCount(rulesets, issues);
        }

        private static void ValidateCurves(List<GrowthCurveData> curves, List<ValidationIssue> issues)
        {
            foreach (var c in curves)
            {
                string name = string.IsNullOrWhiteSpace(c.Id) ? c.name : c.Id;

                if (c.Formula == GrowthCurveFormula.Expression)
                {
                    ValidateFormulaCurve(c, name, issues);
                    continue;
                }
                if (c.Formula != GrowthCurveFormula.Custom) continue;

                var t = c.CustomTable;
                if (t == null || t.Length < 3)
                {
                    issues.Add(Error($"La curva '{name}' es «Tabla propia» pero su tabla está vacía (se usará la Media). Usa 'Convertir en tabla personalizada' en el editor de curvas.", c));
                    continue;
                }
                if (t.Length <= c.MaxLevel)
                    issues.Add(Warning($"La tabla de la curva '{name}' llega al nivel {t.Length - 1}, pero el nivel máximo es {c.MaxLevel}: con esta curva no se podrá pasar del nivel {t.Length - 1}.", c));
                for (int level = 3; level < t.Length; level++)
                    if (t[level] < t[level - 1])
                    {
                        issues.Add(Warning($"En la curva '{name}', el nivel {level} necesita menos XP ({t[level]:N0}) que el nivel {level - 1} ({t[level - 1]:N0}).", c));
                        break;
                    }
            }
        }

        private static void ValidateFormulaCurve(GrowthCurveData c, string name, List<ValidationIssue> issues)
        {
            var segs = c.FormulaSegments;
            if (segs == null || segs.Length == 0)
            {
                issues.Add(Error($"La curva '{name}' usa fórmulas pero no tiene ningún tramo (se usará la Media).", c));
                return;
            }
            bool anyValid = false;
            for (int i = 0; i < segs.Length; i++)
            {
                if (segs[i] == null) continue;
                if (MathExpression.TryParse(segs[i].expression, GrowthCurveMapper.CurveVariables, out _, out var error))
                    anyValid = true;
                else
                    issues.Add(Error($"La curva '{name}', tramo {i + 1} ('{segs[i].expression}'): {error}. Ese tramo se ignora.", c));
                if (i > 0 && segs[i - 1] != null && segs[i].fromLevel <= segs[i - 1].fromLevel)
                    issues.Add(Warning($"En la curva '{name}', el tramo {i + 1} empieza en el nivel {segs[i].fromLevel}, que no es mayor que el anterior: ordénalos de menor a mayor.", c));
            }
            if (!anyValid) return;

            // La XP total no debería BAJAR al subir de nivel.
            try
            {
                var curve = GrowthCurveMapper.ToDomain(c);
                for (int level = 3; level <= curve.MaxLevel; level++)
                    if (curve.XpToReachLevel(level) < curve.XpToReachLevel(level - 1))
                    {
                        issues.Add(Warning($"En la curva '{name}', el nivel {level} necesita menos XP que el {level - 1}: revisa la fórmula de ese tramo.", c));
                        break;
                    }
            }
            catch (System.Exception) { /* sin id u otro error ya reportado */ }
        }

        private static void ValidateNatures(List<NatureData> natures, HashSet<string> knownStats, List<ValidationIssue> issues)
        {
            foreach (var n in natures)
            {
                string name = string.IsNullOrWhiteSpace(n.Id) ? n.name : n.Id;
                CheckStat(n.BoostedStatId, $"La naturaleza '{name}' sube", knownStats, n, issues);
                CheckStat(n.HinderedStatId, $"La naturaleza '{name}' baja", knownStats, n, issues);
                if (n.BoostPercent >= 100 && !string.IsNullOrEmpty(n.HinderedStatId))
                    issues.Add(Warning($"La naturaleza '{name}' tiene {n.BoostPercent}%: la estadística perjudicada quedará en 0.", n));
            }
        }

        private static void ValidateAbilities(List<AbilityData> abilities, HashSet<string> statusIds, HashSet<string> knownStats, List<ValidationIssue> issues)
        {
            // The effect blocks are checked like an item's: ids that do not exist, pieces the engine does not run yet.
            foreach (var a in abilities)
            {
                string name = string.IsNullOrWhiteSpace(a.Id) ? a.name : a.Id;
                List<Domain.Effects.EffectBlock> blocks;
                try { blocks = ItemEffectsEditing.Blocks(a); }
                catch (System.Exception e) { issues.Add(Error($"La habilidad '{name}' tiene un efecto que no se puede leer: {e.Message}", a)); continue; }
                foreach (var w in EffectChecks.Warnings(blocks, true))
                    issues.Add(w.Contains("no existe") ? Error($"La habilidad '{name}': {w}", a) : Warning($"La habilidad '{name}': {w}", a));
            }
        }

        private static void ValidateSpeciesReferences(List<SpeciesData> species, HashSet<string> curveIds,
            HashSet<string> abilityIds, HashSet<string> knownStats, List<ValidationIssue> issues)
        {
            foreach (var sp in species)
            {
                string name = string.IsNullOrWhiteSpace(sp.Id) ? sp.name : sp.Id;
                if (!string.IsNullOrWhiteSpace(sp.GrowthCurveId) && !curveIds.Contains(sp.GrowthCurveId))
                    issues.Add(Error($"La especie '{name}' usa la curva '{sp.GrowthCurveId}', que no existe (se usará la Media).", sp));
                if (!string.IsNullOrWhiteSpace(sp.AbilityId) && !abilityIds.Contains(sp.AbilityId))
                    issues.Add(Error($"La especie '{name}' tiene la habilidad '{sp.AbilityId}', que no existe.", sp));
                if (sp.EvYield != null)
                    foreach (var ev in sp.EvYield)
                        CheckStat(ev.statId, $"La especie '{name}' otorga EVs de", knownStats, sp, issues);
            }
        }

        private static void ValidateRulesetCount(List<RulesetData> rulesets, List<ValidationIssue> issues)
        {
            if (rulesets.Count > 1)
                foreach (var r in rulesets)
                    issues.Add(Warning($"Hay {rulesets.Count} reglas en el proyecto; el juego usa solo la primera de la carpeta Rulesets. Deja una sola.", r));
            var moveIds = new HashSet<string>();
            foreach (var m in LoadAll<MoveData>()) if (!string.IsNullOrWhiteSpace(m.Id)) moveIds.Add(m.Id);
            foreach (var r in rulesets)
                if (r.UsePp && !string.IsNullOrWhiteSpace(r.StruggleMoveId) && !moveIds.Contains(r.StruggleMoveId))
                    issues.Add(Warning($"Las reglas '{r.name}' usan '{r.StruggleMoveId}' como Forcejeo, pero ese movimiento no existe: sin PP se perderá el turno. Créalo desde Movimientos → plantilla 'Forcejeo'.", r));
            foreach (var r in rulesets)
                if (r.MaxEvTotal > 0 && r.MaxEvTotal < r.MaxEvPerStat)
                    issues.Add(Warning($"En las reglas '{r.name}', el tope TOTAL de EVs ({r.MaxEvTotal}) es menor que el tope por estadística ({r.MaxEvPerStat}).", r));
        }

        // Una stat escrita debe ser clásica o una inventada que exista en alguna especie.
        private static void CheckStat(string statId, string prefix, HashSet<string> knownStats, UnityEngine.Object ctx, List<ValidationIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(statId)) return;
            if (!knownStats.Contains(statId.Trim()))
                issues.Add(Warning($"{prefix} la estadística '{statId}', que no es clásica ni la tiene ninguna especie (¿error al escribirla?).", ctx));
        }
    }
}
