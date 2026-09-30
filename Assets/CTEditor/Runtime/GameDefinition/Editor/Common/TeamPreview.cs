using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CTEditor.GameDefinition.Domain.Trainers;
using CTEditor.GameDefinition.Infrastructure.Catalog;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// Vista previa de un EQUIPO (entrenador o equipo prearmado): una tarjeta por miembro con sus tipos
    /// en color, nivel, estadísticas base y los movimientos con los que saldrá (los elegidos o, si no
    /// eligió ninguno, los que aprende a su nivel). Así el autor ve de un vistazo lo que va a pasar.
    /// Además, utilidades para rellenar equipos desde plantillas por id de especie.
    /// </summary>
    public static class TeamPreview
    {
        public static void Draw(TeamMemberData[] team, Color accent, MovesetStyle style = MovesetStyle.Classic, AiProfile profile = null)
        {
            var members = (team ?? new TeamMemberData[0]).Where(m => m != null && m.species != null).ToList();
            if (members.Count == 0)
            {
                EditorTheme.Tip("El equipo está vacío: añade miembros en «Equipo» (especie y nivel bastan).", EditorTheme.Warn, "⚠");
                return;
            }
            foreach (var m in members)
            {
                var s = m.species;
                string title = $"{(string.IsNullOrWhiteSpace(m.nickname) ? ContentAssets.Label(s) : $"{m.nickname} ({s.DisplayName})")}  ·  Nv. {m.level}";
                EditorTheme.BeginCard(accent, title);
                var pills = new List<(string, Color)>();
                if (s.Types != null) foreach (var t in s.Types) if (t != null) pills.Add((t.DisplayName, t.Color));
                if (!string.IsNullOrWhiteSpace(m.heldItem)) pills.Add(("🎒 " + m.heldItem, EditorTheme.Items));
                if (m.fixedIvs >= 0) pills.Add(($"IV {m.fixedIvs}", EditorTheme.Tools));
                if (m.nature != null) pills.Add((m.nature.DisplayName, EditorTheme.Natures));
                EditorTheme.Pills(pills.ToArray());
                var st = EstimatedStats(s, m.level, m.fixedIvs >= 0 ? m.fixedIvs : 15);
                EditorTheme.Paragraph($"≈ PS {st[0]} · Atq {st[1]} · Def {st[2]} · At.Esp {st[3]} · Def.Esp {st[4]} · Vel {st[5]}" +
                                      (m.fixedIvs >= 0 ? "" : "  (IV medio)"));
                if (!string.IsNullOrWhiteSpace(s.AbilityId)) EditorTheme.Paragraph("Habilidad: " + s.AbilityId);
                EditorTheme.Paragraph("Movimientos: " + MovesText(m, style, profile));
                EditorTheme.EndCard();
            }
            DrawAnalysis(members, accent);
        }

        /// <summary>
        /// Estadísticas aproximadas a su nivel (fórmula clásica, sin EVs ni naturaleza): PS, Atq, Def,
        /// At.Esp, Def.Esp y Vel. Sirven para ver de un vistazo si el equipo está equilibrado.
        /// </summary>
        public static int[] EstimatedStats(SpeciesData s, int level, int iv)
        {
            int L = System.Math.Max(1, level);
            int Other(int b) => (2 * b + iv) * L / 100 + 5;
            return new[] { (2 * s.Hp + iv) * L / 100 + L + 10, Other(s.Attack), Other(s.Defense), Other(s.SpAttack), Other(s.SpDefense), Other(s.Speed) };
        }

        /// <summary>
        /// ANÁLISIS DEL EQUIPO: niveles, tipos y a qué tipos es débil la mayoría (según la tabla de tipos).
        /// Ayuda a diseñar rivales justos (o retos a propósito).
        /// </summary>
        public static void DrawAnalysis(List<TeamMemberData> members, Color accent)
        {
            if (members.Count == 0) return;
            EditorTheme.Section("Análisis del equipo", accent);
            int min = members.Min(m => m.level), max = members.Max(m => m.level);
            double avg = members.Average(m => m.level);
            var types = members.SelectMany(m => m.species.Types ?? new ElementTypeData[0]).Where(t => t != null)
                .GroupBy(t => t).Select(g => (g.Key.DisplayName + (g.Count() > 1 ? $" ×{g.Count()}" : ""), g.Key.Color)).ToArray();
            EditorTheme.Tip($"{members.Count} monstruo(s) · nivel {min}{(max != min ? $"–{max}" : "")} (media {avg:0.#})", accent, "📊");
            if (types.Length > 0) EditorTheme.Pills(types);

            var chart = ContentAssets.LoadAll<TypeChartData>().FirstOrDefault();
            var allTypes = ContentAssets.LoadAll<ElementTypeData>();
            if (chart == null || allTypes.Count == 0) { EditorTheme.Tip("Crea la tabla de tipos para ver sus debilidades.", EditorTheme.Tools, "ℹ"); return; }

            float Mult(ElementTypeData atk, SpeciesData def)
            {
                float m = 1f;
                foreach (var d in def.Types ?? new ElementTypeData[0])
                {
                    if (d == null) continue;
                    foreach (var e in chart.Matchups ?? new TypeChartData.MatchupEntry[0])
                        if (e.attacking == atk && e.defending == d) m *= e.multiplier;
                }
                return m;
            }

            var weak = new List<(string, Color)>();
            var immune = new List<string>();
            foreach (var atk in allTypes)
            {
                int hits = members.Count(m => Mult(atk, m.species) >= 2f);
                int resist = members.Count(m => Mult(atk, m.species) < 1f);
                if (hits * 2 >= members.Count && hits > resist) weak.Add(($"{atk.DisplayName} ({hits}/{members.Count})", atk.Color));
                if (members.All(m => Mult(atk, m.species) == 0f)) immune.Add(atk.DisplayName);
            }
            if (weak.Count > 0)
            {
                EditorTheme.Tip("Punto débil: la mitad o más del equipo recibe daño doble de estos tipos.", EditorTheme.Warn, "⚠");
                EditorTheme.Pills(weak.ToArray());
            }
            else EditorTheme.Tip("Sin un punto débil claro: ningún tipo pega fuerte a la mitad del equipo.", EditorTheme.Ok, "✔");
            if (immune.Count > 0) EditorTheme.Tip("Todo el equipo es inmune a: " + string.Join(", ", immune), EditorTheme.Ok, "🛡");
        }

        // El texto del plan se guarda un momento: calcularlo en cada repintado (y en cada tecla) ralentiza el editor.
        private static readonly Dictionary<(SpeciesData, int, MovesetStyle, int), (string text, int version, double time)> PlanCache
            = new Dictionary<(SpeciesData, int, MovesetStyle, int), (string, int, double)>();

        /// <summary>Los movimientos con los que saldrá: los elegidos o los últimos que aprende a su nivel.</summary>
        public static string MovesText(TeamMemberData m, MovesetStyle style = MovesetStyle.Classic, AiProfile profile = null)
        {
            var chosen = (m.moves ?? new MoveData[0]).Where(x => x != null).Select(x => x.DisplayName).ToList();
            if (chosen.Count > 0) return string.Join(", ", chosen);
            if (m.species == null) return "⚠ sin especie";
            if (style == MovesetStyle.Balanced || style == MovesetStyle.Strong || style == MovesetStyle.Competitive)
            {
                var key = (m.species, m.level, style, profile?.Level ?? 0);
                double now = UnityEditor.EditorApplication.timeSinceStartup;
                if (PlanCache.TryGetValue(key, out var c) && c.version == ContentAssets.Version && now - c.time < 3) return c.text;
                string text = PlannedText(m, style, profile);
                if (PlanCache.Count > 200) PlanCache.Clear();
                PlanCache[key] = (text, ContentAssets.Version, now);
                if (text != null) return text;
            }
            var auto = (m.species.Learnset ?? new SpeciesData.LearnableMoveEntry[0])
                .Where(l => l.move != null && l.level <= m.level).OrderBy(l => l.level)
                .Select(l => l.move.DisplayName).Distinct().ToList();
            if (auto.Count == 0) return "⚠ ninguno (no aprende nada hasta este nivel)";
            return string.Join(", ", auto.Skip(System.Math.Max(0, auto.Count - 4))) + "  (automáticos por nivel)";
        }

        // Lo que elegirá el planificador en el juego (el mismo código). null = no se pudo (se enseña el clásico).
        private static string PlannedText(TeamMemberData m, MovesetStyle style, AiProfile profile)
        {
            var learn = new List<(CTEditor.GameDefinition.Domain.Moves.Move, int)>();
            foreach (var l in m.species.Learnset ?? new SpeciesData.LearnableMoveEntry[0])
            {
                if (l.move == null || l.level > m.level) continue;
                try { learn.Add((CTEditor.GameDefinition.Infrastructure.Acl.MoveMapper.ToDomain(l.move), l.level)); }
                catch (System.Exception ex) when (ex is System.ArgumentException || ex is System.InvalidOperationException) { /* movimiento a medio hacer: el validador ya avisa; se ignora aquí */ }
            }
            var types = (m.species.Types ?? new ElementTypeData[0]).Where(t => t != null && !string.IsNullOrEmpty(t.Id))
                .Select(t => new CTEditor.SharedKernel.ValueObjects.Id<CTEditor.GameDefinition.Domain.Types.ElementType>(t.Id)).ToList();
            // Lo que su nivel de IA le deja usar: MT (≥ Aficionado), tutor (≥ Veterano), huevo (≥ Élite).
            var extra = new List<CTEditor.GameDefinition.Domain.Moves.Move>();
            void Add(MoveData[] arr, bool on)
            {
                if (!on || arr == null) return;
                foreach (var md in arr)
                {
                    if (md == null) continue;
                    try { extra.Add(CTEditor.GameDefinition.Infrastructure.Acl.MoveMapper.ToDomain(md)); }
                    catch (System.Exception ex) when (ex is System.ArgumentException || ex is System.InvalidOperationException) { }
                }
            }
            if (profile != null)
            {
                Add(m.species.MachineMoves, profile.UseMachineMoves);
                Add(m.species.TutorMoves, profile.UseTutorMoves);
                Add(m.species.EggMoves, profile.UseEggMoves);
            }
            var options = profile == null ? null : new PlanOptions { ExtraMoves = extra, Synergies = profile.Synergies };
            var plan = MovesetPlanner.Plan(learn, types, m.species.Attack, m.species.SpAttack, m.level, style, 4, options);
            if (plan == null || plan.Count == 0) return null;
            var all = learn.Select(x => x.Item1).Concat(extra).ToList();
            return string.Join(", ", plan.Select(id => { var mv = all.First(x => x.Id.Equals(id)); return mv.DisplayName + (learn.Any(x => x.Item1.Id.Equals(id)) ? "" : "*"); })) +
                   (style == MovesetStyle.Competitive ? "  (automáticos: de competición" : style == MovesetStyle.Strong ? "  (automáticos: fuertes" : "  (automáticos: equilibrados") +
                   (extra.Count > 0 ? "; * = de MT/tutor/huevo)" : ")");
        }

        // ---------------- Rellenar desde plantillas ----------------

        /// <summary>La primera especie que exista de la lista (las plantillas prueban nombres clásicos).</summary>
        public static SpeciesData FirstExisting(params string[] ids)
        {
            foreach (var id in ids)
            {
                var s = ContentAssets.FindById<SpeciesData>(id);
                if (s != null) return s;
            }
            return null;
        }

        /// <summary>
        /// Escribe un equipo en una lista serializada (team / members) desde (especie, nivel). Las especies
        /// que no existan se saltan; si no existe NINGUNA, usa las primeras especies del proyecto (así la
        /// plantilla siempre deja algo jugable).
        /// </summary>
        public static int FillTeam(SerializedProperty list, params (string species, int level)[] members)
        {
            var found = new List<(SpeciesData, int)>();
            foreach (var (id, lvl) in members)
            {
                var s = ContentAssets.FindById<SpeciesData>(id);
                if (s != null) found.Add((s, lvl));
            }
            if (found.Count == 0)
            {
                var any = ContentAssets.LoadAll<SpeciesData>().Take(members.Length).ToList();
                for (int i = 0; i < any.Count; i++) found.Add((any[i], members[i].level));
            }

            list.arraySize = found.Count;
            for (int i = 0; i < found.Count; i++)
            {
                var el = list.GetArrayElementAtIndex(i);
                el.FindPropertyRelative("species").objectReferenceValue = found[i].Item1;
                el.FindPropertyRelative("speciesId").stringValue = found[i].Item1.Id;
                el.FindPropertyRelative("level").intValue = found[i].Item2;
                el.FindPropertyRelative("moves").arraySize = 0;
                el.FindPropertyRelative("moveIds").arraySize = 0;
                el.FindPropertyRelative("heldItem").stringValue = "";
                el.FindPropertyRelative("nature").objectReferenceValue = null;
                el.FindPropertyRelative("natureId").stringValue = "";
                el.FindPropertyRelative("fixedIvs").intValue = -1;
                el.FindPropertyRelative("nickname").stringValue = "";
            }
            return found.Count;
        }

        /// <summary>Pone un objeto equipado al miembro 'index' (si existe ese miembro).</summary>
        public static void SetHeld(SerializedProperty list, int index, string itemId)
        {
            if (index < list.arraySize) list.GetArrayElementAtIndex(index).FindPropertyRelative("heldItem").stringValue = itemId;
        }
    }
}
