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
                // Lo vacío también se enseña: qué pondrá el juego (según el nivel de IA) para que no haya sorpresas en Play.
                if (!string.IsNullOrWhiteSpace(m.heldItem)) pills.Add(("🎒 " + m.heldItem, EditorTheme.Items));
                else if (profile != null && profile.HeldItems != HeldItemStyle.None)
                    pills.Add(("🎒 automático (" + (profile.HeldItems == HeldItemStyle.Competitive ? "competición" : "básico") + ")", EditorTheme.Items));
                var evs = Spread(m.evs, out string evError);
                var ivs = Spread(m.ivs, out string ivError);
                if (m.fixedIvs >= 0) pills.Add(($"IV {m.fixedIvs}", EditorTheme.Tools));
                else if (profile != null && profile.CompetitiveTraining) pills.Add(("IV 31", EditorTheme.Tools));
                if (!ivs.IsEmpty) pills.Add(("IV " + ivs.Format(" · "), EditorTheme.Tools));
                if (!evs.IsEmpty) pills.Add(("EV " + evs.Format(" · "), EditorTheme.Tools));
                else if (profile != null && profile.CompetitiveTraining) pills.Add(("EVs de competición", EditorTheme.Tools));
                if (m.nature != null) pills.Add((m.nature.DisplayName, EditorTheme.Natures));
                else pills.Add((profile != null && profile.CompetitiveTraining ? "naturaleza automática (competición)" : "naturaleza al azar", EditorTheme.Natures));
                EditorTheme.Pills(pills.ToArray());
                var st = EstimatedStats(s, m.level, m.fixedIvs >= 0 ? m.fixedIvs : 15, ivs, evs);
                EditorTheme.Paragraph($"≈ PS {st[0]} · Atq {st[1]} · Def {st[2]} · At.Esp {st[3]} · Def.Esp {st[4]} · Vel {st[5]}" +
                                      (m.fixedIvs >= 0 || !ivs.IsEmpty ? "" : "  (IV medio)") + (evs.IsEmpty ? "" : "  (con sus EVs, sin naturaleza)"));
                string chosen = (m.abilityId ?? "").Trim();
                if (chosen.Length > 0)
                {
                    bool own = chosen == s.AbilityId || chosen == s.SecondAbilityId || chosen == s.HiddenAbilityId;
                    EditorTheme.Paragraph("Habilidad: " + AbilityName(chosen) + (chosen == s.HiddenAbilityId ? " (oculta)" : ""));
                    if (!own) EditorTheme.Tip($"{s.DisplayName} no tiene la habilidad «{AbilityName(chosen)}»: en el juego se quedará con la suya.", EditorTheme.Warn, "⚠");
                }
                else if (!string.IsNullOrWhiteSpace(s.AbilityId))
                    EditorTheme.Paragraph("Habilidad: " + AbilityName(s.AbilityId) +
                                          (string.IsNullOrWhiteSpace(s.SecondAbilityId) ? "" : " o " + AbilityName(s.SecondAbilityId) + " (al azar)"));
                if (evError != null) EditorTheme.Tip("EVs mal escritos: " + evError + " (ej. 252 Atq / 4 PS / 252 Vel)", EditorTheme.Warn, "⚠");
                if (ivError != null) EditorTheme.Tip("IVs mal escritos: " + ivError + " (ej. 0 Atq / 0 Vel)", EditorTheme.Warn, "⚠");
                EditorTheme.Paragraph("Movimientos: " + MovesText(m, style, profile));
                EditorTheme.EndCard();
            }
            DrawAnalysis(members, accent);
        }

        /// <summary>
        /// Estadísticas aproximadas a su nivel (fórmula clásica, sin EVs ni naturaleza): PS, Atq, Def,
        /// At.Esp, Def.Esp y Vel. Sirven para ver de un vistazo si el equipo está equilibrado.
        /// </summary>
        public static int[] EstimatedStats(SpeciesData s, int level, int iv,
            CTEditor.GameDefinition.Domain.Stats.StatSpread ivs = null, CTEditor.GameDefinition.Domain.Stats.StatSpread evs = null)
        {
            int L = System.Math.Max(1, level);
            int Iv(CTEditor.GameDefinition.Domain.Stats.StatId st) => ivs?.Of(st) ?? iv;
            int Ev(CTEditor.GameDefinition.Domain.Stats.StatId st) => (evs?.Of(st) ?? 0) / 4;
            int Other(int b, CTEditor.GameDefinition.Domain.Stats.StatId st) => (2 * b + Iv(st) + Ev(st)) * L / 100 + 5;
            var hp = CTEditor.GameDefinition.Domain.Stats.StatId.Hp;
            return new[] { (2 * s.Hp + Iv(hp) + Ev(hp)) * L / 100 + L + 10,
                Other(s.Attack, CTEditor.GameDefinition.Domain.Stats.StatId.Attack), Other(s.Defense, CTEditor.GameDefinition.Domain.Stats.StatId.Defense),
                Other(s.SpAttack, CTEditor.GameDefinition.Domain.Stats.StatId.SpAttack), Other(s.SpDefense, CTEditor.GameDefinition.Domain.Stats.StatId.SpDefense),
                Other(s.Speed, CTEditor.GameDefinition.Domain.Stats.StatId.Speed) };
        }

        /// <summary>Lee un reparto de EVs/IVs de la ficha; si está mal escrito, vacío y el motivo en 'error'.</summary>
        public static CTEditor.GameDefinition.Domain.Stats.StatSpread Spread(string text, out string error)
            => CTEditor.GameDefinition.Domain.Stats.StatSpread.TryParse(text, out var s, out error) ? s : CTEditor.GameDefinition.Domain.Stats.StatSpread.Empty;

        private static string AbilityName(string id)
        {
            var a = ContentAssets.FindById<AbilityData>(id);
            return a != null ? a.DisplayName : id;
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
        private static readonly Dictionary<(SpeciesData, int, MovesetStyle, int), (string text, int version, int stamp)> PlanCache
            = new Dictionary<(SpeciesData, int, MovesetStyle, int), (string, int, int)>();

        /// <summary>Los movimientos con los que saldrá: los elegidos o los últimos que aprende a su nivel.</summary>
        public static string MovesText(TeamMemberData m, MovesetStyle style = MovesetStyle.Classic, AiProfile profile = null)
        {
            var chosen = (m.moves ?? new MoveData[0]).Where(x => x != null).Select(x => x.DisplayName).ToList();
            if (chosen.Count > 0) return string.Join(", ", chosen);
            if (m.species == null) return "⚠ sin especie";
            if (style == MovesetStyle.Balanced || style == MovesetStyle.Strong || style == MovesetStyle.Competitive)
            {
                var key = (m.species, m.level, style, profile?.Level ?? 0);
                if (PlanCache.TryGetValue(key, out var c) && c.version == ContentAssets.Version && c.stamp == ContentAssets.EditStamp) return c.text;
                string text = PlannedText(m, style, profile);
                if (PlanCache.Count > 200) PlanCache.Clear();
                PlanCache[key] = (text, ContentAssets.Version, ContentAssets.EditStamp);
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

        // ---------------- ✨ Sugerir según la IA ----------------

        /// <summary>
        /// Escribe en el equipo lo que el juego le pondría a cada miembro según su nivel de IA (el MISMO código que en
        /// Play): movimientos, objeto y naturaleza. 'overwrite' = false solo rellena lo VACÍO; true lo sustituye todo.
        /// Lo que se deje vacío sigue siendo automático en el juego. Devuelve cuántos miembros cambió.
        /// </summary>
        public static int ApplySuggestions(UnityEditor.SerializedProperty list, TeamMemberData[] members, AiProfile profile,
            MovesetStyle style, bool overwrite)
        {
            if (list == null || members == null) return 0;
            var data = EditorGameData.Get();
            int changed = 0;
            for (int i = 0; i < members.Length && i < list.arraySize; i++)
            {
                var m = members[i];
                if (m == null || m.SpeciesKey.Length == 0) continue;
                CTEditor.GameDefinition.Domain.Trainers.TeamMemberSpec spec;
                try { spec = CTEditor.GameDefinition.Infrastructure.Acl.TrainerMapper.ToDomain(m); } catch (System.Exception) { continue; }
                var sug = spec == null ? null : CTEditor.Adventure.Domain.TeamBuilder.Suggest(spec, data, profile, style);
                if (sug == null) continue;
                var el = list.GetArrayElementAtIndex(i);
                bool did = false;

                if (overwrite || m.MoveKeys().Length == 0)
                {
                    var moves = el.FindPropertyRelative("moves");
                    var ids = el.FindPropertyRelative("moveIds");
                    var found = sug.Moves.Select(id => ContentAssets.FindById<MoveData>(id.Value)).Where(x => x != null).ToList();
                    moves.arraySize = found.Count;
                    ids.arraySize = found.Count;
                    for (int k = 0; k < found.Count; k++)
                    {
                        moves.GetArrayElementAtIndex(k).objectReferenceValue = found[k];
                        ids.GetArrayElementAtIndex(k).stringValue = found[k].Id;
                    }
                    did |= found.Count > 0;
                }
                if (overwrite || string.IsNullOrWhiteSpace(m.heldItem))
                {
                    var held = el.FindPropertyRelative("heldItem");
                    if (overwrite || sug.HeldItem.Length > 0) { did |= held.stringValue != sug.HeldItem; held.stringValue = sug.HeldItem; }
                }
                if (overwrite || m.NatureKey.Length == 0)
                {
                    var nat = sug.NatureId.Length > 0 ? ContentAssets.FindById<NatureData>(sug.NatureId) : null;
                    if (overwrite || nat != null)
                    {
                        el.FindPropertyRelative("nature").objectReferenceValue = nat;
                        el.FindPropertyRelative("natureId").stringValue = nat != null ? nat.Id : "";
                        did = true;
                    }
                }
                if ((overwrite || string.IsNullOrWhiteSpace(m.evs)) && (overwrite || !sug.Evs.IsEmpty))
                {
                    var evsProp = el.FindPropertyRelative("evs");
                    string text = sug.Evs.Format(" / ");
                    did |= evsProp.stringValue != text;
                    evsProp.stringValue = text;
                }
                // Con sets de competición también vienen habilidad e IVs.
                if (sug.AbilityId.Length > 0 && (overwrite || string.IsNullOrWhiteSpace(m.abilityId)))
                {
                    el.FindPropertyRelative("abilityId").stringValue = sug.AbilityId;
                    did = true;
                }
                if (!sug.Ivs.IsEmpty && (overwrite || string.IsNullOrWhiteSpace(m.ivs)))
                {
                    el.FindPropertyRelative("ivs").stringValue = sug.Ivs.Format(" / ");
                    did = true;
                }
                if (did) changed++;
            }
            return changed;
        }

        /// <summary>
        /// Botones «✨ Sugerir» para un equipo (entrenador o equipo prearmado). 'edit' aplica el cambio con deshacer
        /// (EditSelected de la ventana). 'what' = texto de a quién se refiere la IA («su nivel de IA», «IA Élite»...).
        /// </summary>
        public static void DrawSuggestButtons(string field, TeamMemberData[] members, AiProfile profile, MovesetStyle style, string what,
            System.Action<System.Action<UnityEditor.SerializedObject>> edit)
        {
            UnityEditor.EditorGUILayout.BeginHorizontal();
            bool fill = GUILayout.Button(new GUIContent("✨ Sugerir según " + what + " (rellena lo vacío)",
                "Pone en cada miembro los movimientos, objeto y naturaleza que el juego le daría en Play, para que los veas y " +
                "los ajustes. Lo que ya escribiste no se toca. Lo que dejes vacío sigue siendo automático."), UnityEditor.EditorStyles.miniButton);
            bool all = GUILayout.Button(new GUIContent("↻ Sugerir todo de nuevo", "Sustituye movimientos, objeto y naturaleza de TODOS los miembros por la sugerencia."),
                UnityEditor.EditorStyles.miniButton, GUILayout.Width(170));
            bool clear = GUILayout.Button(new GUIContent("∅ Dejar en automático", "Vacía movimientos, objeto, naturaleza y EVs: el juego los elegirá al combatir."),
                UnityEditor.EditorStyles.miniButton, GUILayout.Width(140));
            if (GUILayout.Button(new GUIContent("🏆 Set de Smogon…", "Elige para cada miembro uno de los sets de competición de su especie (filtra por formato) y guárdalo: moveset FIJO."),
                    UnityEditor.EditorStyles.miniButton, GUILayout.Width(130)))
                SetPickerWindow.Open(field, members, edit);
            UnityEditor.EditorGUILayout.EndHorizontal();
            if (fill || all)
            {
                int n = 0;
                edit(so => n = ApplySuggestions(so.FindProperty(field), members, profile, style, all));
                UnityEditor.EditorUtility.DisplayDialog("Sugerir según la IA", n == 0 ? "No había nada que rellenar." :
                    $"{n} miembro(s) actualizados con lo que el juego les daría. Revísalos y ajusta lo que quieras.", "Vale");
                GUIUtility.ExitGUI();
            }
            if (clear && UnityEditor.EditorUtility.DisplayDialog("Dejar en automático", "¿Vaciar movimientos, objeto, naturaleza y EVs de todo el equipo?", "Vaciar", "Cancelar"))
            {
                edit(so =>
                {
                    var list = so.FindProperty(field);
                    for (int i = 0; i < list.arraySize; i++)
                    {
                        var el = list.GetArrayElementAtIndex(i);
                        el.FindPropertyRelative("moves").arraySize = 0;
                        el.FindPropertyRelative("moveIds").arraySize = 0;
                        el.FindPropertyRelative("heldItem").stringValue = "";
                        el.FindPropertyRelative("nature").objectReferenceValue = null;
                        el.FindPropertyRelative("natureId").stringValue = "";
                        el.FindPropertyRelative("evs").stringValue = "";
                    }
                });
                GUIUtility.ExitGUI();
            }
        }

        /// <summary>Pone un objeto equipado al miembro 'index' (si existe ese miembro).</summary>
        public static void SetHeld(SerializedProperty list, int index, string itemId)
        {
            if (index < list.arraySize) list.GetArrayElementAtIndex(index).FindPropertyRelative("heldItem").stringValue = itemId;
        }
    }
}
