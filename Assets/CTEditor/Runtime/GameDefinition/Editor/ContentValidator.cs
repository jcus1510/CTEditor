using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using CTEditor.GameDefinition.Domain.Moves;
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
    public static partial class ContentValidator
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
            ValidateStatuses(statuses, statusIds, issues);
            ValidateSpecies(species, issues);
            ValidateRulesets(rulesets, issues);
            ValidateCharts(charts, issues);

            // Progresión y contenido nuevo (ContentValidator.Progression.cs).
            ValidateProgression(species, statuses, statusIds, rulesets, issues);
            ValidateAdvanced(moves, statuses, rulesets, issues);
            ValidateAdventure(rulesets, issues); // entrenadores, equipos prearmados y zonas (ContentValidator.Adventure.cs)
            ValidateInterface(issues); // menús y controles (ContentValidator.Interface.cs)
            ValidateDex(species, issues);
            ValidateTrashUsers(issues);

            return issues;
        }

        // --- Pokédex: números repetidos ---
        private static void ValidateDex(List<SpeciesData> species, List<ValidationIssue> issues)
        {
            foreach (var g in species.Where(s => s.DexNumber > 0).GroupBy(s => s.DexNumber).Where(g => g.Count() > 1))
                foreach (var s in g)
                    issues.Add(Warning($"El número de Pokédex {g.Key} lo tienen {string.Join(", ", g.Select(x => x.DisplayName))}.", s));
        }

        // --- Papelera: fichas vivas que todavía usan algo de la papelera ---
        private static void ValidateTrashUsers(List<ValidationIssue> issues)
        {
            foreach (var trashed in ContentTrash.Items())
                foreach (var r in ReferenceFinder.FindReferencesTo(trashed))
                    issues.Add(Warning($"'{ContentAssets.Label(r.Owner)}' usa '{ContentAssets.Label(trashed)}', que está en la PAPELERA ({r.Field}). " +
                                       "Recupérala (Herramientas → Papelera) o cambia la referencia.", r.Owner));
        }

        // --- Movimientos ---
        private static void ValidateMoves(List<MoveData> moves, HashSet<string> statusIds, List<ValidationIssue> issues)
        {
            foreach (var move in moves)
            {
                if (move.Type == null)
                    issues.Add(Error($"El movimiento '{Name(move)}' no tiene tipo asignado.", move));

                if (move.MaxHits < move.MinHits)
                    issues.Add(Warning($"El movimiento '{Name(move)}' tiene MaxHits ({move.MaxHits}) menor que MinHits ({move.MinHits}); se ajustará a MinHits.", move));

                // Daño especial (Lote 5): avisos de configuraciones que no hacen nada.
                if (move.FixedDamage != FixedDamageKind.None && move.Category == MoveCategory.Status)
                    issues.Add(Warning($"El movimiento '{Name(move)}' tiene daño especial pero es de categoría Estado: no hará daño (ponlo Físico o Especial).", move));
                if (move.FixedDamage == FixedDamageKind.Fixed && move.FixedDamageAmount <= 0)
                    issues.Add(Warning($"El movimiento '{Name(move)}' hace daño fijo de 0 PS.", move));
                if (move.FixedDamage == FixedDamageKind.None && move.Power == 0 && move.Category != MoveCategory.Status && string.IsNullOrWhiteSpace(move.PowerFormula))
                    issues.Add(Warning($"El movimiento '{Name(move)}' es de daño pero tiene potencia 0: no hará nada (sube la potencia o usa daño especial).", move));

                if (move.SecondaryEffects == null) continue;
                foreach (var effect in move.SecondaryEffects)
                {
                    if (effect == null) continue;

                    if (effect.kind == MoveEffectKind.InflictStatus)
                    {
                        if (string.IsNullOrWhiteSpace(effect.statusId))
                            issues.Add(Warning($"El movimiento '{Name(move)}' tiene un efecto de estado sin estado (se ignorará).", move));
                        else if (!statusIds.Contains(effect.statusId))
                            issues.Add(Error($"El movimiento '{Name(move)}' inflige el estado '{effect.statusId}', que no existe.", move));
                    }
                    else if (effect.kind == MoveEffectKind.ChangeStatStage)
                    {
                        if (string.IsNullOrWhiteSpace(effect.statStatId))
                            issues.Add(Warning($"El movimiento '{Name(move)}' tiene un efecto de etapa sin estadística (se ignorará).", move));
                        else if (effect.statStages == 0)
                            issues.Add(Warning($"El movimiento '{Name(move)}' cambia la estadística '{effect.statStatId}' en 0 etapas (sin efecto).", move));
                    }
                }
            }
        }

        // --- Estados ---
        private static void ValidateStatuses(List<StatusConditionData> statuses, HashSet<string> statusIds, List<ValidationIssue> issues)
        {
            foreach (var st in statuses)
            {
                if (!string.IsNullOrWhiteSpace(st.TransformsToStatus) && !statusIds.Contains(st.TransformsToStatus))
                    issues.Add(Error($"El estado '{Name(st)}' se transforma en '{st.TransformsToStatus}', que no existe.", st));
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
                            issues.Add(Error($"La especie '{Name(sp)}' tiene una entrada de learnset sin movimiento asignado. " +
                                "¿Borraste y volviste a crear un movimiento? Usa «🔧 Reparar especies» en el editor de Especies (o de Movimientos).", sp));
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
                            issues.Add(Warning($"La especie '{Name(sp)}' tiene una estadística inventada sin id.", sp));
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
                    issues.Add(Error($"Las reglas '{Name(rs)}' no tienen fórmula de daño.", rs));
                else if (!knownFormulas.Contains(rs.DamageFormulaId))
                    issues.Add(Warning($"Las reglas '{Name(rs)}' usan la fórmula '{rs.DamageFormulaId}', que no es una conocida ({string.Join(", ", knownFormulas)}).", rs));

                if (rs.MaxMovesPerMonster < 1)
                    issues.Add(Error($"Las reglas '{Name(rs)}' permiten menos de 1 movimiento por monstruo.", rs));

                // Mecánicas especiales activas: cada id debe tener su ficha; no se repiten dos del mismo tipo.
                var mechanics = LoadAll<MechanicData>();
                var kinds = new HashSet<CTEditor.GameDefinition.Domain.Rules.Mechanics.MechanicKind>();
                foreach (var id in rs.MechanicIds ?? new string[0])
                {
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    var m = mechanics.Find(x => string.Equals(x.Id, id, System.StringComparison.OrdinalIgnoreCase));
                    if (m == null) issues.Add(Warning($"Las reglas '{Name(rs)}' activan la mecánica '{id}', que no existe: se ignora.", rs));
                    else if (!kinds.Add(m.Kind))
                        issues.Add(Warning($"Las reglas '{Name(rs)}' activan dos mecánicas del mismo tipo ({Etiquetas.Enum(m.Kind.ToString())}): solo cuenta la primera.", rs));
                }
                if (rs.CategoryByType && (rs.SpecialTypes == null || rs.SpecialTypes.Length == 0))
                    issues.Add(Warning($"Las reglas '{Name(rs)}' deciden la categoría por tipo pero no tienen tipos especiales: todos los ataques serán físicos.", rs));
            }

            // Fichas de mecánica: el objeto clave debe existir.
            var items = new HashSet<string>();
            foreach (var it in LoadAll<ItemData>()) if (!string.IsNullOrWhiteSpace(it.Id)) items.Add(it.Id);
            foreach (var m in LoadAll<MechanicData>())
            {
                if (string.IsNullOrWhiteSpace(m.Id)) { issues.Add(Error($"Hay una mecánica sin id (asset '{m.name}').", m)); continue; }
                if (m.Kind == CTEditor.GameDefinition.Domain.Rules.Mechanics.MechanicKind.MegaEvolution
                    && !string.IsNullOrWhiteSpace(m.MegaRequiredKeyItem) && !items.Contains(m.MegaRequiredKeyItem))
                    issues.Add(Warning($"La mecánica '{m.Id}' pide el objeto clave '{m.MegaRequiredKeyItem}', que no existe: el jugador no podrá usarla.", m));
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
                if (ContentTrash.IsTrashedPath(path)) continue; // lo de la papelera no se valida (ni cuenta como duplicado)
                var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null) list.Add(asset);
            }
            return list;
        }

        private static string Name(MoveData m) => string.IsNullOrWhiteSpace(m.Id) ? m.name : m.Id;
        private static string Name(StatusConditionData s) => string.IsNullOrWhiteSpace(s.Id) ? s.name : s.Id;
        private static string Name(SpeciesData s) => string.IsNullOrWhiteSpace(s.Id) ? s.name : s.Id;
        private static string Name(RulesetData r) => r.name;
        private static string Name(TypeChartData c) => c.name;

        private static ValidationIssue Error(string msg, Object ctx) => new ValidationIssue(IssueSeverity.Error, msg, ctx);
        private static ValidationIssue Warning(string msg, Object ctx) => new ValidationIssue(IssueSeverity.Warning, msg, ctx);
    }
}
