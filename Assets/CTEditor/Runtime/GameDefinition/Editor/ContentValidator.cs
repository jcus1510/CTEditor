using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    public enum IssueSeverity { Error, Warning }

    /// <summary>Un problema detectado en el contenido autorado. Context permite "hacer ping" al asset.</summary>
    public sealed class ValidationIssue
    {
        public IssueSeverity Severity { get; }
        public string Message { get; }
        public Object Context { get; }

        public ValidationIssue(IssueSeverity severity, string message, Object context)
        {
            Severity = severity;
            Message = message;
            Context = context;
        }
    }

    /// <summary>
    /// El VALIDADOR de contenido (L.8): la red de seguridad del autor. Recorre TODAS las fichas del
    /// proyecto y comprueba las invariantes de autoría: ids no vacíos ni duplicados, referencias que
    /// existen (un efecto que apunta a un estado real, un learnset a movimientos reales), etc.
    ///
    /// Es código de Editor (usa AssetDatabase). Trabaja sobre las fichas (ScriptableObjects), que es
    /// donde el autor comete los errores, ANTES de que el ACL las mapee al dominio. Devuelve una lista
    /// de problemas; la ventana los muestra. Esto es justo lo que evita que el autor descubra un fallo
    /// recién en pleno combate.
    /// </summary>
    public static class ContentValidator
    {
        /// <summary>Devuelve solo los problemas que afectan a un asset concreto (para la UI por-ficha).</summary>
        public static List<ValidationIssue> IssuesFor(Object asset)
        {
            var filtered = new List<ValidationIssue>();
            foreach (var issue in Validate())
                if (issue.Context == asset)
                    filtered.Add(issue);
            return filtered;
        }

        public static List<ValidationIssue> Validate()
        {
            var issues = new List<ValidationIssue>();

            var moves = LoadAll<MoveData>();
            var statuses = LoadAll<StatusConditionData>();
            var species = LoadAll<SpeciesData>();
            var types = LoadAll<ElementTypeData>();
            var rulesets = LoadAll<RulesetData>();
            var charts = LoadAll<TypeChartData>();

            // Conjuntos de ids conocidos, para validar referencias por texto.
            var statusIds = CollectIds(statuses, s => s.Id, "estado", issues);
            CollectIds(moves, m => m.Id, "movimiento", issues);
            CollectIds(species, s => s.Id, "especie", issues);
            CollectIds(types, t => t.Id, "tipo", issues);

            ValidateMoves(moves, statusIds, issues);
            ValidateSpecies(species, issues);
            ValidateRulesets(rulesets, issues);
            ValidateCharts(charts, issues);

            return issues;
        }

        // --- Movimientos ---
        private static void ValidateMoves(List<MoveData> moves, HashSet<string> statusIds, List<ValidationIssue> issues)
        {
            foreach (var move in moves)
            {
                if (move.Type == null)
                    issues.Add(Error($"El movimiento '{Name(move)}' no tiene tipo asignado.", move));

                if (move.SecondaryEffects == null) continue;
                foreach (var effect in move.SecondaryEffects)
                {
                    if (effect == null) continue;
                    if (string.IsNullOrWhiteSpace(effect.statusId))
                    {
                        issues.Add(Warning($"El movimiento '{Name(move)}' tiene un efecto secundario sin estado (se ignorará).", move));
                    }
                    else if (!statusIds.Contains(effect.statusId))
                    {
                        issues.Add(Error($"El movimiento '{Name(move)}' inflige el estado '{effect.statusId}', que no existe.", move));
                    }
                }
            }
        }

        // --- Especies ---
        private static void ValidateSpecies(List<SpeciesData> species, List<ValidationIssue> issues)
        {
            foreach (var sp in species)
            {
                if (sp.Types == null || sp.Types.Length == 0)
                    issues.Add(Warning($"La especie '{Name(sp)}' no tiene tipos asignados.", sp));

                if (sp.Learnset != null)
                {
                    foreach (var entry in sp.Learnset)
                    {
                        if (entry.move == null)
                            issues.Add(Error($"La especie '{Name(sp)}' tiene una entrada de learnset sin movimiento asignado.", sp));
                        else if (entry.level < 1)
                            issues.Add(Warning($"La especie '{Name(sp)}' aprende '{entry.move.Id}' a nivel {entry.level} (debería ser >= 1).", sp));
                    }
                }

                if (sp.Evolutions != null)
                {
                    foreach (var evo in sp.Evolutions)
                    {
                        if (evo.target == null)
                            issues.Add(Warning($"La especie '{Name(sp)}' tiene una evolución sin especie destino.", sp));
                    }
                }

                if (sp.CustomStats != null)
                {
                    foreach (var cs in sp.CustomStats)
                        if (string.IsNullOrWhiteSpace(cs.statId))
                            issues.Add(Warning($"La especie '{Name(sp)}' tiene un stat personalizado sin id.", sp));
                }
            }
        }

        // --- Rulesets ---
        private static void ValidateRulesets(List<RulesetData> rulesets, List<ValidationIssue> issues)
        {
            var knownFormulas = new HashSet<string> { "classic", "simple" };
            foreach (var rs in rulesets)
            {
                if (string.IsNullOrWhiteSpace(rs.DamageFormulaId))
                    issues.Add(Error($"El ruleset '{Name(rs)}' no tiene fórmula de daño.", rs));
                else if (!knownFormulas.Contains(rs.DamageFormulaId))
                    issues.Add(Warning($"El ruleset '{Name(rs)}' usa la fórmula '{rs.DamageFormulaId}', que no es una conocida ({string.Join(", ", knownFormulas)}).", rs));

                if (rs.MaxMovesPerMonster < 1)
                    issues.Add(Error($"El ruleset '{Name(rs)}' permite menos de 1 movimiento por monstruo.", rs));
            }
        }

        // --- Tabla de tipos ---
        private static void ValidateCharts(List<TypeChartData> charts, List<ValidationIssue> issues)
        {
            foreach (var chart in charts)
            {
                if (chart.Matchups == null) continue;
                var seenPairs = new HashSet<string>();
                foreach (var m in chart.Matchups)
                {
                    if (m.attacking == null || m.defending == null)
                    {
                        issues.Add(Error($"La tabla de tipos '{Name(chart)}' tiene una relación con un tipo sin asignar.", chart));
                        continue;
                    }
                    var key = m.attacking.Id + "->" + m.defending.Id;
                    if (!seenPairs.Add(key))
                        issues.Add(Warning($"La tabla de tipos '{Name(chart)}' repite la relación {key}.", chart));
                }
            }
        }

        // --- Helpers ---

        // Recolecta ids de una familia, reportando vacíos y duplicados. Devuelve el conjunto de ids válidos.
        private static HashSet<string> CollectIds<T>(List<T> assets, System.Func<T, string> getId, string family, List<ValidationIssue> issues)
            where T : Object
        {
            var ids = new HashSet<string>();
            foreach (var asset in assets)
            {
                var id = getId(asset);
                if (string.IsNullOrWhiteSpace(id))
                {
                    issues.Add(Error($"Hay un {family} sin id (asset '{asset.name}').", asset));
                    continue;
                }
                if (!ids.Add(id))
                    issues.Add(Error($"Id de {family} duplicado: '{id}'.", asset));
            }
            return ids;
        }

        private static List<T> LoadAll<T>() where T : Object
        {
            var list = new List<T>();
            foreach (var guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null) list.Add(asset);
            }
            return list;
        }

        private static string Name(MoveData m) => string.IsNullOrWhiteSpace(m.Id) ? m.name : m.Id;
        private static string Name(SpeciesData s) => string.IsNullOrWhiteSpace(s.Id) ? s.name : s.Id;
        private static string Name(RulesetData r) => r.name;
        private static string Name(TypeChartData c) => c.name;

        private static ValidationIssue Error(string msg, Object ctx) => new ValidationIssue(IssueSeverity.Error, msg, ctx);
        private static ValidationIssue Warning(string msg, Object ctx) => new ValidationIssue(IssueSeverity.Warning, msg, ctx);
    }
}
