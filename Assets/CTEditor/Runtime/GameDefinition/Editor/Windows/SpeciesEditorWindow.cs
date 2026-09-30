using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Growth;
using CTEditor.GameDefinition.Infrastructure.Acl;
using CTEditor.GameDefinition.Infrastructure.Catalog;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;
using CTEditor.Party.Domain;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// Editor de ESPECIES (menú CTEditor → Criaturas → Especies). Además del inspector (con desplegables de curva y
    /// habilidad), incluye:
    ///   - stats base con barras y su total (BST),
    ///   - una CALCULADORA de stats reales: nivel, IVs, EVs y naturaleza → valor final, junto al
    ///     mínimo y máximo posibles, usando la MISMA fórmula que el juego (ClassicStatGrowthFormula),
    ///   - el gráfico de su curva de XP,
    ///   - el learnset ordenado y asistentes para tareas repetitivas.
    /// </summary>
    public sealed class SpeciesEditorWindow : ContentEditorWindow<SpeciesData>
    {
        [MenuItem(EditorMenus.Creatures + "Especies", false, EditorMenus.CreaturesOrder + 1)]
        public static void Open() => OpenWindow<SpeciesEditorWindow>("Especies");

        /// <summary>Abre el editor con una especie ya seleccionada.</summary>
        public static void OpenAndSelect(SpeciesData species) => OpenWindow<SpeciesEditorWindow>("Especies").FocusOn(species);

        protected override string Category => ContentFolders.Species;
        protected override string Noun => "especie";
        protected override string Intro =>
            "Las especies de tu juego. Su curva de XP y su habilidad se eligen con desplegables; " +
            "abajo tienes una calculadora para ver sus estadísticas reales a cualquier nivel.";

        protected override string Title => "Especies";
        // En la lista: una marca con el color de su primer tipo.
        protected override Color? RowMark(SpeciesData d)
            => d.Types != null && d.Types.Length > 0 && d.Types[0] != null ? d.Types[0].Color : (Color?)null;

        public override string[] GuideSteps => new[]
        {
            "Crea una especie escribiendo su id, o importa la 1ª generación desde el Centro de Contenido.",
            "Rellena tipos y estadísticas base; la calculadora te enseña cómo quedan a cada nivel.",
            "Añade los movimientos que aprende (nivel + movimiento) y sus evoluciones (por nivel, objeto, amistad o intercambio).",
            "Elige su habilidad, su curva de experiencia y cuánta experiencia y EV da al derrotarla.",
            "Pulsa «Ver su árbol de familia» para revisar evoluciones, formas de combate y variantes.",
        };

        private static readonly ClassicStatGrowthFormula Formula = new ClassicStatGrowthFormula();

        // Estado de la calculadora (propio de la ventana, no se guarda en la ficha).
        private int _level = 50;
        private int _iv = 31;
        private int _natureIndex; // 0 = neutra
        private bool _showEvs;
        private readonly Dictionary<string, int> _evs = new Dictionary<string, int>();

        protected override void DrawBulkPresets()
        {
            EditorGUILayout.HelpBox("Consejo: crea antes tipos, curvas y habilidades (CTEditor → Centro de Contenido) para poder elegirlos aquí.", MessageType.None);
            // ¿Borraste una especie o quieres volver a los datos del pack? Sin borrar nada:
            PackTools.DrawPackMenu("especies", PackTools.ImportMissingSpecies, PackTools.UpdateAllSpecies);
            PackTools.DrawRepairWarning(AfterPackChange);
        }

        // Tras tocar datos desde el pack: recargar la lista y volver a validar.
        private void AfterPackChange() { Refresh(); Revalidate(); Repaint(); }

        // ---------------- Asistentes ----------------

        protected override void DrawPresets(SpeciesData d)
        {
            EditorGUILayout.LabelField("Asistentes", EditorStyles.boldLabel);
            int b = ButtonRow("Ordenar learnset por nivel", "Sugerir EV (1 en su estadística más alta)", "Ver su árbol de familia");
            if (b == 0) SortLearnset();
            if (b == 1) SuggestEvYield(d);
            if (b == 2) EvolutionChainWindow.OpenFor(d);
            PackTools.DrawRestoreButton(PackTools.SpeciesFile, d.Id, AfterPackChange);
            EditorGUILayout.Space();
        }

        private void SortLearnset()
        {
            EditSelected(so =>
            {
                var arr = so.FindProperty("learnset");
                var entries = new List<(UnityEngine.Object move, int level)>();
                for (int i = 0; i < arr.arraySize; i++)
                {
                    var e = arr.GetArrayElementAtIndex(i);
                    entries.Add((e.FindPropertyRelative("move").objectReferenceValue, e.FindPropertyRelative("level").intValue));
                }
                entries.Sort((x, y) => x.level.CompareTo(y.level));
                for (int i = 0; i < entries.Count; i++)
                {
                    var e = arr.GetArrayElementAtIndex(i);
                    e.FindPropertyRelative("move").objectReferenceValue = entries[i].move;
                    e.FindPropertyRelative("level").intValue = entries[i].level;
                }
            });
        }

        // Heurística sencilla (no oficial): 1 EV en la stat base más alta. Punto de partida editable.
        private void SuggestEvYield(SpeciesData d)
        {
            string best = null; int bestValue = -1;
            foreach (var (id, value) in BaseStats(d))
                if (value > bestValue) { best = id; bestValue = value; }
            if (best == null) return;
            EditSelected(so =>
            {
                var arr = so.FindProperty("evYield");
                arr.arraySize = 1;
                var e = arr.GetArrayElementAtIndex(0);
                e.FindPropertyRelative("statId").stringValue = best;
                e.FindPropertyRelative("amount").intValue = 1;
            });
        }

        // ---------------- Vista previa ----------------

        // La ficha de Pokédex, como en los juegos.
        private void DrawDex(SpeciesData d)
        {
            EditorTheme.Section("Pokédex", Accent);
            var types = (d.Types ?? new ElementTypeData[0]).Where(t => t != null).ToArray();
            EditorTheme.BeginCard(types.Length > 0 ? types[0].Color : Accent);
            string num = d.DexNumber > 0 ? "Nº " + d.DexNumber.ToString("000") : "Nº ---";
            EditorGUILayout.LabelField($"{num}   {d.DisplayName}", EditorStyles.boldLabel);
            if (!string.IsNullOrWhiteSpace(d.Category)) EditorTheme.Paragraph($"Pokémon {d.Category}", false);
            if (types.Length > 0) EditorTheme.Pills(types.Select(t => (t.DisplayName, t.Color)).ToArray());
            EditorTheme.Paragraph($"Altura: {(d.HeightM > 0 ? d.HeightM.ToString("0.0#") + " m" : "—")}   ·   Peso: {(d.WeightKg > 0 ? d.WeightKg.ToString("0.0#") + " kg" : "—")}" +
                                  (string.IsNullOrWhiteSpace(d.DexColor) ? "" : $"   ·   Color: {d.DexColor}") +
                                  $"   ·   {(d.FemalePercent < 0 ? "Sin género" : $"♀ {d.FemalePercent:0.#}% · ♂ {100 - d.FemalePercent:0.#}%")}" +
                                  (d.Legendary ? "   ·   ★ Legendario" : ""), false);
            if (!string.IsNullOrWhiteSpace(d.DexDescription)) EditorTheme.Paragraph("«" + d.DexDescription + "»", false);
            else EditorTheme.Paragraph("Sin descripción (rellénala en «Pokédex», más abajo en los datos, o impórtala del pack).");
            EditorTheme.EndCard();
        }

        // Por número de Pokédex (las que no tienen número, al final y por nombre).
        protected override int CompareItems(SpeciesData a, SpeciesData b)
        {
            int na = a.DexNumber > 0 ? a.DexNumber : int.MaxValue, nb = b.DexNumber > 0 ? b.DexNumber : int.MaxValue;
            return na != nb ? na.CompareTo(nb) : string.Compare(a.DisplayName, b.DisplayName, System.StringComparison.OrdinalIgnoreCase);
        }

        // ---------------- Filtros y orden de la lista ----------------

        private int _filterType;            // 0 = todos
        private int _filterEgg;             // 0 = todos
        private int _filterLegend;          // 0 todos, 1 solo legendarios, 2 sin legendarios
        private int _dexFrom, _dexTo;       // 0 = sin límite
        private bool _showFilters;

        protected override string[] SortOptions => new[] { "Nº Pokédex", "Nombre", "Peso", "Altura", "Total de estadísticas", "Tipo principal" };

        protected override int CompareBy(int sortIndex, SpeciesData a, SpeciesData b)
        {
            switch (sortIndex)
            {
                case 1: return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
                case 2: return a.WeightKg.CompareTo(b.WeightKg);
                case 3: return a.HeightM.CompareTo(b.HeightM);
                case 4: return Bst(a).CompareTo(Bst(b));
                case 5: return string.Compare(FirstType(a), FirstType(b), StringComparison.OrdinalIgnoreCase);
                default: return CompareItems(a, b);
            }
        }

        private static int Bst(SpeciesData d) => d.Hp + d.Attack + d.Defense + d.SpAttack + d.SpDefense + d.Speed;
        private static string FirstType(SpeciesData d) => d.Types != null && d.Types.Length > 0 && d.Types[0] != null ? d.Types[0].DisplayName : "";

        protected override void DrawFilters()
        {
            var types = ContentAssets.LoadAll<ElementTypeData>();
            var eggs = ContentAssets.LoadAll<EggGroupData>();
            _showFilters = EditorGUILayout.Foldout(_showFilters, ActiveFilters() > 0 ? $"Filtros ({ActiveFilters()} activos)" : "Filtros", true);
            if (!_showFilters) return;
            EditorGUI.BeginChangeCheck();
            _filterType = EditorGUILayout.Popup("Tipo", Mathf.Clamp(_filterType, 0, types.Count), new[] { "Todos" }.Concat(types.Select(t => t.DisplayName)).ToArray());
            if (eggs.Count > 0)
                _filterEgg = EditorGUILayout.Popup("Grupo huevo", Mathf.Clamp(_filterEgg, 0, eggs.Count), new[] { "Todos" }.Concat(eggs.Select(g => g.DisplayName)).ToArray());
            _filterLegend = EditorGUILayout.Popup("Legendarios", _filterLegend, new[] { "Todos", "Solo legendarios", "Sin legendarios" });
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Nº Pokédex", GUILayout.Width(80));
            _dexFrom = EditorGUILayout.IntField(_dexFrom, GUILayout.Width(50));
            GUILayout.Label("a", GUILayout.Width(12));
            _dexTo = EditorGUILayout.IntField(_dexTo, GUILayout.Width(50));
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("Quitar filtros", EditorStyles.miniButton)) { _filterType = _filterEgg = _filterLegend = _dexFrom = _dexTo = 0; }
            if (EditorGUI.EndChangeCheck()) FiltersChanged();
        }

        private int ActiveFilters() => (_filterType > 0 ? 1 : 0) + (_filterEgg > 0 ? 1 : 0) + (_filterLegend > 0 ? 1 : 0) + (_dexFrom > 0 || _dexTo > 0 ? 1 : 0);

        protected override bool PassesFilter(SpeciesData d)
        {
            if (_filterType > 0)
            {
                var types = ContentAssets.LoadAll<ElementTypeData>();
                var t = _filterType - 1 < types.Count ? types[_filterType - 1] : null;
                if (t != null && (d.Types == null || !d.Types.Contains(t))) return false;
            }
            if (_filterEgg > 0)
            {
                var eggs = ContentAssets.LoadAll<EggGroupData>();
                var g = _filterEgg - 1 < eggs.Count ? eggs[_filterEgg - 1] : null;
                if (g != null && (d.EggGroups == null || !d.EggGroups.Contains(g))) return false;
            }
            if (_filterLegend == 1 && !d.Legendary) return false;
            if (_filterLegend == 2 && d.Legendary) return false;
            if (_dexFrom > 0 && d.DexNumber < _dexFrom) return false;
            if (_dexTo > 0 && d.DexNumber > _dexTo) return false;
            return true;
        }

        // ---------------- Habilidades, otros movimientos y crianza ----------------

        private void DrawAbilitiesAndBreeding(SpeciesData d)
        {
            EditorTheme.Section("Habilidades y crianza", Accent);
            string Ab(string id)
            {
                if (string.IsNullOrWhiteSpace(id)) return "—";
                var a = ContentAssets.FindById<AbilityData>(id);
                return a != null ? a.DisplayName : id + " (no existe)";
            }
            EditorTheme.Paragraph($"Habilidad: {Ab(d.AbilityId)}   ·   2.ª: {Ab(d.SecondAbilityId)}   ·   Oculta: {Ab(d.HiddenAbilityId)}", false);
            EditorTheme.Paragraph("Cada individuo sale con la 1.ª o la 2.ª al azar; la oculta solo si tú la eliges (se cambian más abajo, en los datos).");

            // Grupos huevo con desplegables (hasta 2).
            var eggs = ContentAssets.LoadAll<EggGroupData>();
            if (eggs.Count == 0)
            {
                EditorGUILayout.HelpBox("Aún no hay grupos huevo. Créalos en CTEditor → Criaturas → Grupos huevo (hay un set clásico).", MessageType.None);
            }
            else
            {
                var names = new[] { "—" }.Concat(eggs.Select(g => g.DisplayName)).ToArray();
                var current = (d.EggGroups ?? new EggGroupData[0]).Where(g => g != null).ToList();
                EditorGUILayout.BeginHorizontal();
                int a = current.Count > 0 ? eggs.IndexOf(current[0]) + 1 : 0;
                int b = current.Count > 1 ? eggs.IndexOf(current[1]) + 1 : 0;
                int na = EditorGUILayout.Popup("Grupos huevo", a, names);
                int nb = EditorGUILayout.Popup(b, names, GUILayout.Width(140));
                EditorGUILayout.EndHorizontal();
                if (na != a || nb != b)
                {
                    var chosen = new[] { na, nb }.Where(i => i > 0).Distinct().Select(i => eggs[i - 1]).ToList();
                    EditSelected(so =>
                    {
                        var arr = so.FindProperty("eggGroups");
                        arr.arraySize = chosen.Count;
                        for (int i = 0; i < chosen.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = chosen[i];
                    });
                }
                if (current.Count > 0 && GUILayout.Button("Ver las especies de sus grupos huevo", EditorStyles.miniButton))
                    EggGroupEditorWindow.OpenFor(current[0]);
            }

            int mt = (d.MachineMoves ?? new MoveData[0]).Count(m => m != null);
            int tu = (d.TutorMoves ?? new MoveData[0]).Count(m => m != null);
            int eg = (d.EggMoves ?? new MoveData[0]).Count(m => m != null);
            EditorTheme.Paragraph($"Otros movimientos: {mt} por MT · {tu} por tutor · {eg} huevo. " +
                                  "Los entrenadores Aficionado usan los de MT, los Veteranos también tutor y los de Élite y Campeón los huevo.", false);
        }

        private int _formTemplate;

        // FORMAS DE COMBATE (cambian en mitad del combate) y VARIANTES (especies enlazadas con «es forma de»).
        private void DrawForms(SpeciesData d)
        {
            EditorTheme.Section("Formas y variantes", Accent);
            if (!string.IsNullOrWhiteSpace(d.FormOf))
            {
                var baseSpecies = ContentAssets.FindById<SpeciesData>(d.FormOf);
                EditorGUILayout.BeginHorizontal();
                EditorTheme.Paragraph($"Es una VARIANTE de {(baseSpecies != null ? baseSpecies.DisplayName : d.FormOf + " (no existe)")}. " +
                    (string.IsNullOrWhiteSpace(d.VariantItem)
                        ? "Cambia a ella con un personaje del mapa."
                        : $"Cambia a ella (y vuelve) usando {ItemLabel(d.VariantItem)} fuera del combate."), false);
                if (baseSpecies != null && GUILayout.Button("Abrir la base", EditorStyles.miniButton, GUILayout.Width(90))) OpenAndSelect(baseSpecies);
                EditorGUILayout.EndHorizontal();
            }

            var variants = FormEditing.VariantsOf(d);
            if (variants.Count > 0)
            {
                EditorGUILayout.LabelField("Variantes", EditorStyles.miniBoldLabel);
                EditorGUILayout.BeginHorizontal();
                foreach (var v in variants.Take(6))
                    if (GUILayout.Button(new GUIContent(v.DisplayName, string.IsNullOrWhiteSpace(v.VariantItem) ? "Con un personaje" : "Con " + ItemLabel(v.VariantItem)),
                            EditorStyles.miniButton)) OpenAndSelect(v);
                if (variants.Count > 6) GUILayout.Label($"+{variants.Count - 6}", EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
            }

            var forms = (d.Forms ?? new SpeciesData.FormEntry[0]).Where(f => f != null).ToList();
            if (forms.Count == 0)
                EditorTheme.Paragraph("Sin formas de combate. Una forma de combate cambia tipos, estadísticas o habilidad EN MITAD del combate " +
                                      "(Modo Daruma, Aegislash, megas) y al acabar vuelve a la normal.");
            foreach (var f in forms)
            {
                EditorGUILayout.BeginHorizontal();
                string types = string.Join("/", new[] { f.type1, f.type2 }.Where(t => !string.IsNullOrWhiteSpace(t)));
                string stats = f.attack + f.defense + f.spAttack + f.spDefense + f.speed > 0
                    ? $"{Or(f.attack, d.Attack)}/{Or(f.defense, d.Defense)}/{Or(f.spAttack, d.SpAttack)}/{Or(f.spDefense, d.SpDefense)}/{Or(f.speed, d.Speed)}"
                    : "mismas estadísticas";
                EditorTheme.Paragraph($"⚔ {FormEditing.FormName(d, f.id)}  ·  {(types.Length > 0 ? types : "mismos tipos")}  ·  {stats}" +
                                      (string.IsNullOrWhiteSpace(f.ability) ? "" : $"  ·  {f.ability}") + (f.revertsOnSwitch ? "  ·  vuelve al retirarse" : ""), false);
                if (GUILayout.Button(new GUIContent("✕", "Quitar esta forma y sus cambios"), EditorStyles.miniButton, GUILayout.Width(22))
                    && EditorUtility.DisplayDialog("Quitar forma", $"¿Quitar la forma «{FormEditing.FormName(d, f.id)}» y sus cambios de forma?", "Quitar", "Cancelar"))
                {
                    FormEditing.RemoveForm(d, f.id);
                    Revalidate();
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();
            }
            foreach (var c in (d.FormChanges ?? new SpeciesData.FormChangeEntry[0]).Where(c => c != null))
                EditorTheme.Paragraph("   ↪ " + FormEditing.Describe(d, c));
            foreach (var p in FormEditing.Problems(d)) EditorGUILayout.HelpBox(p, MessageType.Warning);

            EditorGUILayout.BeginHorizontal();
            var names = new[] { "Sin cambios (los pongo yo)" }.Concat(FormEditing.Templates.Select(t => t.name)).ToArray();
            _formTemplate = EditorGUILayout.Popup(_formTemplate, names);
            if (GUILayout.Button("+ Forma de combate", GUILayout.Width(140)))
            {
                FormEditing.AddForm(d, _formTemplate - 1);
                Revalidate();
            }
            if (GUILayout.Button(new GUIContent("+ Variante", "Crea otra especie (copia de esta) enlazada con «es forma de»: Rotom Lavado, Deoxys Ataque, formas regionales..."),
                    GUILayout.Width(90)))
            {
                var v = FormEditing.CreateVariant(d);
                if (v != null) { Refresh(); Select(v); GUIUtility.ExitGUI(); }
            }
            if (GUILayout.Button("🌳 Árbol de familia", GUILayout.Width(130))) EvolutionChainWindow.OpenFor(d);
            EditorGUILayout.EndHorizontal();
            EditorTheme.Paragraph("Rellena los detalles (tipos, estadísticas, objeto o movimiento que la provoca) más abajo, en «Formas y variantes» de los datos. " +
                                  "Estadística 0 o tipo vacío = igual que la especie; los PS no cambian en combate.");
        }

        private static int Or(int v, int fallback) => v > 0 ? v : fallback;

        private static string ItemLabel(string id)
        {
            var it = string.IsNullOrWhiteSpace(id) ? null : ContentAssets.FindById<ItemData>(id);
            return it != null ? it.DisplayName : id;
        }

        protected override void DrawPreview(SpeciesData d)
        {
            DrawDex(d);
            DrawAbilitiesAndBreeding(d);
            DrawForms(d);
            var stats = BaseStats(d);
            DrawBaseStats(stats);
            DrawCalculator(d, stats);
            DrawCurve(d);
            DrawLearnset(d);
        }

        private static void DrawBaseStats(List<(string id, int value)> stats)
        {
            EditorGUILayout.LabelField("Estadísticas base", EditorStyles.boldLabel);
            int total = 0;
            foreach (var (id, value) in stats)
            {
                total += value;
                Rect r = EditorGUILayout.GetControlRect();
                EditorGUI.ProgressBar(r, Mathf.Clamp01(value / 255f), $"{StatLabels.NameOf(id)}: {value}");
            }
            EditorGUILayout.LabelField($"Total (BST): {total}", EditorStyles.miniLabel);
        }

        private void DrawCalculator(SpeciesData d, List<(string id, int value)> stats)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Calculadora de estadísticas (con la fórmula del juego)", EditorStyles.boldLabel);

            var rules = ContentAssets.LoadAll<RulesetData>();
            int cap = rules.Count > 0 ? rules[0].LevelCap : 100;
            int maxIv = rules.Count > 0 ? rules[0].MaxIv : 31;
            int maxEv = rules.Count > 0 ? rules[0].MaxEvPerStat : 252;

            _level = EditorGUILayout.IntSlider("Nivel", Mathf.Clamp(_level, 1, cap), 1, cap);
            _iv = maxIv > 0 ? EditorGUILayout.IntSlider("IV (todas las estadísticas)", Mathf.Clamp(_iv, 0, maxIv), 0, maxIv) : 0;

            // Naturalezas disponibles (del proyecto) + neutra.
            var natureAssets = ContentAssets.LoadAll<NatureData>();
            var natureLabels = new List<string> { "Neutra" };
            foreach (var n in natureAssets) natureLabels.Add(ContentAssets.Label(n));
            _natureIndex = Mathf.Clamp(EditorGUILayout.Popup("Naturaleza", _natureIndex, natureLabels.ToArray()), 0, natureLabels.Count - 1);
            Nature nature = null;
            if (_natureIndex > 0)
            {
                try { nature = NatureMapper.ToDomain(natureAssets[_natureIndex - 1]); } catch (Exception) { nature = null; }
            }

            if (maxEv > 0)
            {
                _showEvs = EditorGUILayout.Foldout(_showEvs, "EV por estadística", true);
                if (_showEvs)
                {
                    int evTotal = 0;
                    foreach (var (id, _) in stats)
                    {
                        _evs.TryGetValue(id, out var ev);
                        ev = EditorGUILayout.IntSlider("   " + StatLabels.NameOf(id), Mathf.Clamp(ev, 0, maxEv), 0, maxEv);
                        _evs[id] = ev;
                        evTotal += ev;
                    }
                    int maxTotal = rules.Count > 0 ? rules[0].MaxEvTotal : 510;
                    if (evTotal > maxTotal)
                        EditorGUILayout.HelpBox($"Total de EVs {evTotal} > tope {maxTotal}: en el juego no se podría llegar a esto.", MessageType.Warning);
                }
            }

            // Tabla: mínimo posible, tu configuración y máximo posible a este nivel.
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Estadística", EditorStyles.miniLabel, GUILayout.Width(90));
            EditorGUILayout.LabelField("Mínimo", EditorStyles.miniLabel, GUILayout.Width(70));
            EditorGUILayout.LabelField("Tu configuración", EditorStyles.miniLabel, GUILayout.Width(110));
            EditorGUILayout.LabelField("Máximo", EditorStyles.miniLabel, GUILayout.Width(70));
            EditorGUILayout.EndHorizontal();

            foreach (var (id, value) in stats)
            {
                var stat = new StatId(id);
                int pct = nature != null ? nature.PercentFor(stat) : 100;
                _evs.TryGetValue(id, out var ev);
                int min = Formula.Compute(stat, value, _level, 0, 0, stat == StatId.Hp ? 100 : 90);
                int mine = Formula.Compute(stat, value, _level, _iv, ev, pct);
                int max = Formula.Compute(stat, value, _level, maxIv, maxEv, stat == StatId.Hp ? 100 : 110);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(StatLabels.NameOf(id), GUILayout.Width(90));
                EditorGUILayout.LabelField(min.ToString(), GUILayout.Width(70));
                EditorGUILayout.LabelField(mine.ToString() + (pct > 100 ? " ▲" : pct < 100 ? " ▼" : ""), EditorStyles.boldLabel, GUILayout.Width(110));
                EditorGUILayout.LabelField(max.ToString(), GUILayout.Width(70));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.LabelField("Mínimo = IV 0, sin EVs, naturaleza desfavorable · Máximo = IV y EVs al tope, naturaleza favorable.", EditorStyles.miniLabel);
        }

        private static void DrawCurve(SpeciesData d)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Curva de experiencia", EditorStyles.boldLabel);

            GrowthCurve curve = null;
            string source;
            var data = ContentAssets.FindById<GrowthCurveData>(d.GrowthCurveId);
            if (data != null)
            {
                try { curve = GrowthCurveMapper.ToDomain(data); } catch (Exception) { curve = null; }
                source = ContentAssets.Label(data);
            }
            else
            {
                source = string.IsNullOrEmpty(d.GrowthCurveId) ? "ninguna elegida → Media (por defecto)" : $"'{d.GrowthCurveId}' no existe → Media (por defecto)";
            }
            curve = curve ?? GrowthCurvePresets.MediumFast(new Id<GrowthCurve>("default"), "Media", 100);

            EditorGUILayout.LabelField("Usa: " + source, EditorStyles.miniLabel);
            var c = curve;
            LineChart.Draw(140f, new List<ChartSeries>
            {
                new ChartSeries("XP total", EditorGUIUtility.isProSkin ? Color.white : Color.black, 2.5f, x => c.XpToReachLevel(x))
            }, 1, c.MaxLevel, "Nivel", LineChart.Compact);
            if (GUILayout.Button("Abrir editor de curvas")) GrowthCurveEditorWindow.Open();
        }

        private static void DrawLearnset(SpeciesData d)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Movimientos que aprende (por nivel)", EditorStyles.boldLabel);
            if (d.Learnset == null || d.Learnset.Length == 0)
            {
                EditorGUILayout.LabelField("Todavía no aprende ningún movimiento.", EditorStyles.miniLabel);
                return;
            }
            var list = new List<SpeciesData.LearnableMoveEntry>(d.Learnset);
            list.Sort((a, b) => a.level.CompareTo(b.level));
            var parts = new List<string>();
            foreach (var e in list)
                parts.Add($"Nv.{e.level} {(e.move != null ? e.move.DisplayName : "(vacío)")}");
            EditorGUILayout.LabelField(string.Join(" · ", parts), EditorStyles.wordWrappedLabel);
        }

        // Stats base leídas de la ficha: las 6 clásicas + las inventadas.
        private static List<(string id, int value)> BaseStats(SpeciesData d)
        {
            var list = new List<(string, int)>
            {
                ("hp", d.Hp), ("attack", d.Attack), ("defense", d.Defense),
                ("sp_attack", d.SpAttack), ("sp_defense", d.SpDefense), ("speed", d.Speed),
            };
            if (d.CustomStats != null)
                foreach (var cs in d.CustomStats)
                    if (!string.IsNullOrWhiteSpace(cs.statId)) list.Add((cs.statId.Trim(), cs.value));
            return list;
        }
    }
}
