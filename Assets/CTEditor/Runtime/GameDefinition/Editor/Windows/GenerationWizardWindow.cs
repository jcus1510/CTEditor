using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CTEditor.Adventure.Domain;
using CTEditor.GameDefinition.Editor.Csv;
using CTEditor.GameDefinition.Infrastructure.Catalog;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// CAMBIAR DE GENERACIÓN: pasa TODO el proyecto a otro pack (Gen1 … Gen6, o uno tuyo) en cuatro pasos:
    ///   1) Elegir el pack.
    ///   2) Lista de lo que se cambia (reglas, tipos, especies, movimientos...) y qué hacer con lo que el pack NO trae:
    ///      mandarlo a la PAPELERA en un grupo (se recupera de golpe), borrarlo para siempre o dejarlo.
    ///   3) Aplicar: copia de seguridad en Excel/copias, quitar lo que sobra, importar el pack, plantilla de reglas de la
    ///      generación (Megaevolución solo en 6.ª y 7.ª) y limpiar la tabla de tipos.
    ///   4) ADAPTAR A LAS REGLAS: los equipos escritos a mano (entrenadores, equipos prearmados, zonas) que ya no encajan.
    ///      En cada fila: «arreglar» o «dejar y avisar» (el juego nunca falla por ello: ignora lo que no existe).
    /// Tus entrenadores, equipos y zonas NUNCA se borran: solo se revisan.
    /// Menú: CTEditor → Herramientas → Cambiar de generación.
    /// </summary>
    public sealed class GenerationWizardWindow : EditorWindow
    {
        private enum Page { Pack, Checklist, Review, Done }
        private enum Leftover { Trash, Delete, Keep }

        /// <summary>Una categoría del pack: su ficha, sus hojas y los ids que nunca se quitan (los necesita el motor).</summary>
        private sealed class Category
        {
            public string Label, Help;
            public Type AssetType;
            public string[] Files;
            public Func<HashSet<string>> Protected = () => new HashSet<string>();
            public bool Selected = true;
            // Calculado al elegir el pack:
            public bool InPack;
            public int Leftovers;
        }

        /// <summary>Un conflicto con dónde está (para poder arreglarlo) y si el autor quiere arreglarlo.</summary>
        private sealed class ReviewRow
        {
            public RulesConflict Conflict;
            public ScriptableObject Asset;
            public string ArrayField;
            public bool Fix = true;
        }

        private Page _page;
        private List<string> _packs = new List<string>();
        private int _packIndex;
        private int _generation;               // 0 = no tocar las reglas
        private Leftover _leftover = Leftover.Trash;
        private bool _updateExisting = true;
        private bool _importTrainers = true;
        private bool _applyRules = true;
        private List<Category> _categories;
        private readonly List<ReviewRow> _review = new List<ReviewRow>();
        private readonly List<string> _report = new List<string>();
        private Vector2 _scroll;

        [MenuItem(EditorMenus.Tools + "Cambiar de generación", false, EditorMenus.ToolsOrder + 4)]
        public static void Open()
        {
            var w = GetWindow<GenerationWizardWindow>();
            w.titleContent = new GUIContent("🔁 Cambiar de generación");
            w.minSize = new Vector2(520, 460);
            w.Restart();
            w.Show();
        }

        private void OnEnable() { if (_categories == null) Restart(); }

        private void Restart()
        {
            _page = Page.Pack;
            _packs = ContentHubWindow.AvailablePacks();
            _packIndex = Mathf.Max(0, _packs.IndexOf(ContentHubWindow.PackFolder));
            _categories = BuildCategories();
            _review.Clear();
            _report.Clear();
            OnPackChanged();
        }

        private static List<Category> BuildCategories() => new List<Category>
        {
            new Category { Label = "Tipos y tabla de tipos", AssetType = typeof(ElementTypeData), Files = new[] { "tipos.csv", "tabla_tipos.csv" },
                Help = "Los tipos de la generación y quién es eficaz contra quién." },
            new Category { Label = "Especies (con formas y variantes)", AssetType = typeof(SpeciesData), Files = new[] { "especies.csv" },
                Help = "Estadísticas, tipos, movimientos que aprenden y evoluciones de esa generación." },
            new Category { Label = "Movimientos", AssetType = typeof(MoveData), Files = new[] { PackTools.MovesFile },
                Help = "Potencia, precisión, PP y efectos de esa generación.",
                Protected = () => new HashSet<string> { "struggle" } },
            new Category { Label = "Habilidades", AssetType = typeof(AbilityData), Files = new[] { PackTools.AbilitiesFile },
                Help = "Desde la 3.ª generación." },
            new Category { Label = "Objetos", AssetType = typeof(ItemData), Files = new[] { "objetos.csv" },
                Help = "Los objetos con efecto de las plantillas se quedan siempre (el resto sale del pack).",
                Protected = () => new HashSet<string>(ItemEditorWindow.Library.Select(p => p.Id)) },
            new Category { Label = "Naturalezas", AssetType = typeof(NatureData), Files = new[] { "naturalezas.csv" },
                Help = "Desde la 3.ª generación." },
            new Category { Label = "Grupos huevo", AssetType = typeof(EggGroupData), Files = new[] { "grupos_huevo.csv" },
                Help = "Desde la 2.ª generación." },
            new Category { Label = "Sets de competición (Smogon)", AssetType = typeof(CompetitiveSetData), Files = new[] { "sets.csv" },
                Help = "Los sets que usan las IAs expertas, sacados de esa generación." },
        };

        private string Pack => _packs.Count > 0 ? _packs[Mathf.Clamp(_packIndex, 0, _packs.Count - 1)] : null;

        /// <summary>«Gen4» → 4; otro nombre → 0 (el autor elige la plantilla de reglas).</summary>
        private static int GenerationOf(string folder)
        {
            var m = System.Text.RegularExpressions.Regex.Match(Path.GetFileName(folder ?? ""), @"^Gen(\d+)$");
            return m.Success ? int.Parse(m.Groups[1].Value) : 0;
        }

        private void OnPackChanged()
        {
            _generation = GenerationOf(Pack);
            if (Pack == null) return;
            var pack = new ContentHubWindow.PackContents(Pack);
            foreach (var c in _categories)
            {
                c.InPack = pack.Has(c.Files[0]);
                c.Leftovers = c.InPack ? Leftovers(c, pack).Count : 0;
            }
        }

        /// <summary>Las fichas de la categoría que el pack no trae (y no están protegidas).</summary>
        private static List<ScriptableObject> Leftovers(Category c, ContentHubWindow.PackContents pack)
        {
            var ids = pack.Ids(c.Files[0]);
            var keep = c.Protected();
            return ContentAssets.LoadAll(c.AssetType).Where(a => a is IContentAsset ca && !string.IsNullOrWhiteSpace(ca.Id)
                && !ids.Contains(ca.Id) && !keep.Contains(ca.Id)).ToList();
        }

        // ---------------- Dibujo ----------------

        private void OnGUI()
        {
            EditorGUILayout.Space(4);
            string[] steps = { "1. Pack", "2. Qué cambia", "3. Adaptar a las reglas", "Listo" };
            EditorGUILayout.LabelField(string.Join("   ›   ", steps.Select((s, i) => i == (int)_page ? "【" + s + "】" : s)), EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.Space(4);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            switch (_page)
            {
                case Page.Pack: DrawPack(); break;
                case Page.Checklist: DrawChecklist(); break;
                case Page.Review: DrawReview(); break;
                case Page.Done: DrawDone(); break;
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawPack()
        {
            EditorGUILayout.HelpBox("Pasa TODO el proyecto a otra generación: sus especies, movimientos, tipos, objetos... y sus reglas " +
                "(categoría por tipo, habilidades, naturalezas, Megaevolución...). Antes de tocar nada se guarda una copia de seguridad " +
                "en Excel/copias, y lo que quites va a la papelera en un grupo que se recupera de golpe.", MessageType.Info);
            if (_packs.Count == 0) { EditorGUILayout.HelpBox("No hay packs en " + ContentHubWindow.PacksRoot + ".", MessageType.Warning); return; }

            int picked = EditorGUILayout.Popup("Pack de destino", _packIndex, _packs.Select(p => $"{Path.GetFileName(p)} · {ContentHubWindow.NameOf(p)}").ToArray());
            if (picked != _packIndex) { _packIndex = picked; OnPackChanged(); }

            string informe = Path.Combine(Pack, "INFORME.txt");
            if (File.Exists(informe) && GUILayout.Button("📄 Leer el INFORME del pack (qué trae y qué se aproximó)"))
                EditorUtility.RevealInFinder(informe);

            EditorGUILayout.Space();
            if (GUILayout.Button("Siguiente ›", GUILayout.Height(28))) _page = Page.Checklist;
        }

        private void DrawChecklist()
        {
            EditorGUILayout.LabelField("Qué se cambia a " + ContentHubWindow.NameOf(Pack), EditorStyles.boldLabel);

            // Reglas
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                _applyRules = EditorGUILayout.ToggleLeft("Reglas de la generación", _applyRules, EditorStyles.boldLabel);
                if (_applyRules)
                {
                    _generation = EditorGUILayout.IntSlider(new GUIContent("Generación", "Qué plantilla de reglas se aplica (0 = moderna, sin tocar mecánicas)."), _generation, 0, 7);
                    var g = CTEditor.GameDefinition.Domain.Rules.GenerationRules.ForGeneration(_generation);
                    EditorGUILayout.LabelField(Describe(g), EditorStyles.wordWrappedMiniLabel);
                }
            }

            // Categorías
            foreach (var c in _categories)
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUI.DisabledScope(!c.InPack))
                        c.Selected = EditorGUILayout.ToggleLeft(c.Label, c.Selected && c.InPack, EditorStyles.boldLabel);
                    string state = !c.InPack
                        ? "El pack no trae esta hoja: lo que tengas se queda (las reglas de la generación lo apagan si no existía)."
                        : c.Leftovers > 0 ? $"{c.Help}\nTienes {c.Leftovers} que el pack no trae." : c.Help;
                    EditorGUILayout.LabelField(state, EditorStyles.wordWrappedMiniLabel);
                }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                _importTrainers = EditorGUILayout.ToggleLeft("Entrenadores del pack (crear y actualizar)", _importTrainers, EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Los TUYOS nunca se borran: en el paso 3 se revisa qué no encaja en sus equipos.", EditorStyles.wordWrappedMiniLabel);
            }

            _updateExisting = EditorGUILayout.ToggleLeft(new GUIContent("Poner los datos del pack también en lo que ya existe",
                "Si no, solo se crea lo que falta (lo tuyo con el mismo id no se toca)."), _updateExisting);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("‹ Atrás", GUILayout.Height(28), GUILayout.Width(90))) _page = Page.Pack;
                if (GUILayout.Button("Aplicar el cambio", GUILayout.Height(28))) Apply();
            }
        }

        private static string Describe(CTEditor.GameDefinition.Domain.Rules.GenerationRules g)
        {
            var parts = new List<string>
            {
                g.CategoryByType ? "físico/especial según el TIPO" : "físico/especial según el movimiento",
                g.SingleSpecialStat ? "un solo «Especial»" : "Atq. Esp. y Def. Esp.",
                g.Abilities ? "con habilidades" : "sin habilidades",
                g.HeldItems ? "con objetos equipados" : "sin objetos equipados",
                g.Natures ? "con naturalezas" : "sin naturalezas",
                g.Genders ? "con géneros" : "sin géneros",
            };
            if (g.Generation == 6 || g.Generation == 7) parts.Add("con Megaevolución");
            return (g.Generation == 0 ? "Moderna (no toca las mecánicas): " : "") + string.Join(" · ", parts) + ".";
        }

        // ---------------- Aplicar ----------------

        private void Apply()
        {
            int total = _categories.Where(c => c.Selected && c.InPack).Sum(c => c.Leftovers);
            if (total > 0)
            {
                int choice = EditorUtility.DisplayDialogComplex("Cambiar a " + ContentHubWindow.NameOf(Pack),
                    $"Tienes {total} fichas que {ContentHubWindow.NameOf(Pack)} no trae (especies, movimientos...).\n\n" +
                    "• Papelera: van a un GRUPO de la papelera y puedes recuperarlas todas de golpe.\n" +
                    "• Borrar para siempre: no se pueden recuperar (salvo desde la copia de Excel).\n" +
                    "• Dejarlas: siguen en el proyecto junto a las del pack.",
                    "Papelera", "Cancelar", "Borrar para siempre");
                if (choice == 1)
                {
                    if (!EditorUtility.DisplayDialog("Cambiar de generación", "¿Dejarlas en el proyecto (junto a las del pack)?", "Sí, dejarlas", "Cancelar")) return;
                    _leftover = Leftover.Keep;
                }
                else _leftover = choice == 0 ? Leftover.Trash : Leftover.Delete;
            }
            else if (!EditorUtility.DisplayDialog("Cambiar a " + ContentHubWindow.NameOf(Pack), "Se guardará antes una copia de seguridad en Excel/copias. ¿Seguimos?", "Sí", "Cancelar"))
                return;

            _report.Clear();
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
            string packName = Path.GetFileName(Pack);
            try
            {
                // 1) Copia de seguridad (todo el proyecto en Excel).
                EditorUtility.DisplayProgressBar("Cambiar de generación", "Copia de seguridad…", 0.05f);
                string backup = Path.Combine(CsvImporter.DefaultFolder, "copias", $"antes_de_{packName}_{stamp}");
                CsvImporter.ExportAll(backup);
                _report.Add("Copia de seguridad: " + backup);

                // 2) Lo que el pack no trae.
                var pack = new ContentHubWindow.PackContents(Pack);
                if (_leftover != Leftover.Keep)
                {
                    string group = $"Cambio a {packName} {DateTime.Now:yyyy-MM-dd HH.mm}";
                    int moved = 0;
                    foreach (var c in _categories.Where(c => c.Selected && c.InPack))
                    {
                        EditorUtility.DisplayProgressBar("Cambiar de generación", "Quitando " + c.Label.ToLowerInvariant() + "…", 0.2f);
                        foreach (var a in Leftovers(c, pack))
                        {
                            if (_leftover == Leftover.Trash) ContentTrash.MoveToTrash(a, group, false);
                            else AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(a));
                            moved++;
                        }
                    }
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                    ContentAssets.ClearCache();
                    if (moved > 0) _report.Add(_leftover == Leftover.Trash ? $"A la papelera (grupo «{group}»): {moved}" : $"Borradas para siempre: {moved}");
                }

                // 3) La base que el pack referencia (estados, climas, Forcejeo, objetos con efecto...) y el pack.
                EditorUtility.DisplayProgressBar("Cambiar de generación", "Preparando la base…", 0.35f);
                var baseReport = new List<string>();
                ContentHubWindow.CreateClassicBase(baseReport, pack);
                if (pack.Has(PackTools.AbilitiesFile))
                    AbilityEditorWindow.CreateClassicSet(out _, pack.Ids(PackTools.AbilitiesFile));

                var files = _categories.Where(c => c.Selected && c.InPack).SelectMany(c => c.Files).ToList();
                if (_importTrainers) files.Add("entrenadores.csv");
                var sources = new List<(CsvSchema, string)>();
                foreach (var f in files.Distinct())
                {
                    string path = Path.Combine(Pack, f);
                    if (!File.Exists(path)) continue;
                    var schema = CsvImporter.Detect(path);
                    if (schema != null) sources.Add((schema, path));
                }
                EditorUtility.DisplayProgressBar("Cambiar de generación", "Importando el pack…", 0.5f);
                var analyses = CsvImporter.AnalyzeFiles(sources, _updateExisting ? ImportMode.CreateAndUpdate : ImportMode.CreateOnly, out var ctx);
                foreach (var a in analyses.Where(a => a.LoadError != null)) _report.Add($"{Path.GetFileName(a.Path)}: {a.LoadError}");
                _report.Add(CsvImporter.Apply(analyses, ctx, null));
                ContentAssets.ClearCache();

                // 4) Tabla de tipos: fuera los cruces de tipos que ya no existen.
                int cleaned = CleanTypeChart();
                if (cleaned > 0) _report.Add($"Tabla de tipos: {cleaned} cruces de tipos que ya no existen, quitados");

                // 5) Reglas de la generación.
                if (_applyRules)
                {
                    if (ContentAssets.LoadAll<RulesetData>().Count == 0) RulesetEditorWindow.CreateClassicSet();
                    var rules = ContentAssets.LoadAll<RulesetData>().FirstOrDefault();
                    if (rules != null)
                    {
                        ContentAssets.Edit(rules, RulesetEditorWindow.GenerationPreset(_generation));
                        _report.Add($"Reglas «{rules.DisplayName}»: plantilla de " + (_generation == 0 ? "reglas modernas" : $"la {_generation}.ª generación"));
                    }
                }
                ContentHubWindow.PackFolder = Pack;
                AssetDatabase.SaveAssets();
                ContentAssets.ClearCache();
                EditorCatalog.ClearCounts();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _report.Add("ERROR: " + e.Message + " (detalle en la Consola). La copia de seguridad está en Excel/copias.");
            }
            finally { EditorUtility.ClearProgressBar(); }

            CollectConflicts();
            _page = _review.Count > 0 ? Page.Review : Page.Done;
        }

        /// <summary>Quita de la tabla los cruces cuyo tipo atacante o defensor ya no existe (o está en la papelera).</summary>
        private static int CleanTypeChart()
        {
            var alive = new HashSet<ElementTypeData>(ContentAssets.LoadAll<ElementTypeData>());
            int removed = 0;
            foreach (var chart in ContentAssets.LoadAll<TypeChartData>())
                ContentAssets.Edit(chart, so =>
                {
                    var arr = so.FindProperty("matchups");
                    for (int i = arr.arraySize - 1; i >= 0; i--)
                    {
                        var e = arr.GetArrayElementAtIndex(i);
                        var atk = e.FindPropertyRelative("attacking").objectReferenceValue as ElementTypeData;
                        var def = e.FindPropertyRelative("defending").objectReferenceValue as ElementTypeData;
                        if (atk != null && def != null && alive.Contains(atk) && alive.Contains(def)) continue;
                        arr.DeleteArrayElementAtIndex(i);
                        removed++;
                    }
                });
            return removed;
        }

        // ---------------- Adaptar a las reglas ----------------

        private void CollectConflicts()
        {
            _review.Clear();
            GameData data;
            try { data = EditorGameData.Get(); }
            catch (Exception e) { Debug.LogException(e); return; }

            void Members(ScriptableObject owner, string field, TeamMemberData[] team, string label)
            {
                if (team == null) return;
                for (int i = 0; i < team.Length; i++)
                {
                    var m = team[i];
                    if (m == null) continue;
                    var r = new MemberToReview
                    {
                        Owner = label, Index = i, SpeciesId = m.SpeciesKey, ItemId = m.heldItem ?? "", NatureId = m.NatureKey,
                        AbilityId = m.abilityId ?? "", Evs = m.evs ?? "", GenderFixed = m.gender != MemberGender.Random,
                    };
                    r.Moves.AddRange(m.MoveKeys());
                    foreach (var c in RulesReview.Review(r, data))
                        _review.Add(new ReviewRow { Conflict = c, Asset = owner, ArrayField = field });
                }
            }

            foreach (var t in ContentAssets.LoadAll<TrainerData>()) Members(t, "team", t.Team, "Entrenador «" + ContentAssets.Label(t) + "»");
            foreach (var p in ContentAssets.LoadAll<TeamPresetData>()) Members(p, "members", p.Members, "Equipo «" + ContentAssets.Label(p) + "»");
            foreach (var z in ContentAssets.LoadAll<EncounterZoneData>())
            {
                var entries = z.Entries;
                if (entries == null) continue;
                for (int i = 0; i < entries.Length; i++)
                {
                    if (entries[i] == null) continue;
                    var r = new MemberToReview { Owner = "Zona «" + ContentAssets.Label(z) + "»", Index = i, SpeciesId = entries[i].SpeciesKey };
                    foreach (var c in RulesReview.Review(r, data))
                        _review.Add(new ReviewRow { Conflict = c, Asset = z, ArrayField = "entries" });
                }
            }
        }

        private void DrawReview()
        {
            EditorGUILayout.LabelField("Adaptar a las reglas", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox($"{_review.Count} cosas de tus entrenadores, equipos y zonas ya no encajan con {ContentHubWindow.NameOf(Pack)}. " +
                "Marca «arreglar» (se aplica el arreglo que ves) o desmárcalo para dejarlo como está: el juego no falla (ignora lo que no existe), " +
                "pero el validador te lo seguirá avisando.", MessageType.Warning);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Arreglar todo", EditorStyles.miniButtonLeft)) _review.ForEach(r => r.Fix = true);
                if (GUILayout.Button("Dejar todo y avisar", EditorStyles.miniButtonRight)) _review.ForEach(r => r.Fix = false);
            }

            foreach (var owner in _review.GroupBy(r => r.Conflict.Owner))
            {
                EditorGUILayout.Space(2);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(owner.Key, EditorStyles.boldLabel);
                    if (GUILayout.Button("Abrir", EditorStyles.miniButton, GUILayout.Width(50)))
                    { Selection.activeObject = owner.First().Asset; EditorGUIUtility.PingObject(owner.First().Asset); }
                }
                foreach (var r in owner)
                    using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                    {
                        r.Fix = EditorGUILayout.ToggleLeft(r.Fix ? "Arreglar" : "Dejar y avisar", r.Fix, GUILayout.Width(110));
                        EditorGUILayout.LabelField(r.Conflict.Description + (r.Fix ? "\n→ " + r.Conflict.Fix : ""), EditorStyles.wordWrappedMiniLabel);
                    }
            }

            EditorGUILayout.Space();
            if (GUILayout.Button($"Aplicar ({_review.Count(r => r.Fix)} arreglos, {_review.Count(r => !r.Fix)} se dejan)", GUILayout.Height(28)))
            {
                int fixedCount = ApplyFixes(_review.Where(r => r.Fix).ToList());
                _report.Add($"Adaptar a las reglas: {fixedCount} arreglados, {_review.Count(r => !r.Fix)} se dejan y avisan");
                _page = Page.Done;
            }
        }

        /// <summary>Aplica los arreglos por ficha: primero los cambios de campos y luego se quitan miembros (del último al primero).</summary>
        private static int ApplyFixes(List<ReviewRow> rows)
        {
            int n = 0;
            foreach (var byAsset in rows.GroupBy(r => r.Asset))
            {
                var list = byAsset.ToList();
                ContentAssets.Edit(byAsset.Key, so =>
                {
                    foreach (var r in list.Where(r => r.Conflict.Kind != ConflictKind.MissingSpecies))
                    {
                        var arr = so.FindProperty(r.ArrayField);
                        if (arr == null || r.Conflict.Index >= arr.arraySize) continue;
                        FixMember(arr.GetArrayElementAtIndex(r.Conflict.Index), r.Conflict);
                        n++;
                    }
                    foreach (var r in list.Where(r => r.Conflict.Kind == ConflictKind.MissingSpecies).OrderByDescending(r => r.Conflict.Index))
                    {
                        var arr = so.FindProperty(r.ArrayField);
                        if (arr == null || r.Conflict.Index >= arr.arraySize) continue;
                        arr.DeleteArrayElementAtIndex(r.Conflict.Index);
                        n++;
                    }
                });
            }
            AssetDatabase.SaveAssets();
            ContentAssets.ClearCache();
            return n;
        }

        private static void FixMember(SerializedProperty m, RulesConflict c)
        {
            switch (c.Kind)
            {
                case ConflictKind.MissingMove:
                    var moves = m.FindPropertyRelative("moves");
                    var ids = m.FindPropertyRelative("moveIds");
                    int count = Math.Max(moves?.arraySize ?? 0, ids?.arraySize ?? 0);
                    for (int i = count - 1; i >= 0; i--)
                    {
                        var mv = moves != null && i < moves.arraySize ? moves.GetArrayElementAtIndex(i).objectReferenceValue as MoveData : null;
                        string key = mv != null && !string.IsNullOrWhiteSpace(mv.Id) ? mv.Id
                            : ids != null && i < ids.arraySize ? (ids.GetArrayElementAtIndex(i).stringValue ?? "").Trim() : "";
                        if (!string.Equals(key, c.Value, StringComparison.OrdinalIgnoreCase)) continue;
                        if (moves != null && i < moves.arraySize) RemoveAt(moves, i);
                        if (ids != null && i < ids.arraySize) ids.DeleteArrayElementAtIndex(i);
                    }
                    break;
                case ConflictKind.MissingItem:
                case ConflictKind.HeldItemsOff:
                case ConflictKind.MegaStoneWithoutMega:
                    m.FindPropertyRelative("heldItem").stringValue = "";
                    break;
                case ConflictKind.MissingNature:
                case ConflictKind.NaturesOff:
                    m.FindPropertyRelative("nature").objectReferenceValue = null;
                    m.FindPropertyRelative("natureId").stringValue = "";
                    break;
                case ConflictKind.GendersOff:
                    m.FindPropertyRelative("gender").intValue = (int)MemberGender.Random;
                    break;
                case ConflictKind.ForeignAbility:
                case ConflictKind.AbilitiesOff:
                    m.FindPropertyRelative("abilityId").stringValue = "";
                    break;
                case ConflictKind.EvsOff:
                    m.FindPropertyRelative("evs").stringValue = "";
                    break;
            }
        }

        /// <summary>Quita un elemento de un array de referencias (en versiones viejas de Unity el primer borrado solo lo vacía).</summary>
        private static void RemoveAt(SerializedProperty arr, int i)
        {
            int size = arr.arraySize;
            arr.GetArrayElementAtIndex(i).objectReferenceValue = null;
            arr.DeleteArrayElementAtIndex(i);
            if (arr.arraySize == size) arr.DeleteArrayElementAtIndex(i);
        }

        private void DrawDone()
        {
            EditorGUILayout.LabelField("Listo: el proyecto está en " + ContentHubWindow.NameOf(Pack), EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(string.Join("\n", _report.Where(s => !string.IsNullOrWhiteSpace(s))), MessageType.Info);
            EditorGUILayout.LabelField("¿Te arrepientes? En la Papelera, «↩ Recuperar todo el grupo» devuelve lo que se quitó; " +
                "la copia de Excel/copias se puede importar desde «Excel y compartir».", EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("🗑 Papelera")) TrashWindow.Open();
                if (GUILayout.Button("✅ Validar contenido")) ContentValidationWindow.Open();
                if (GUILayout.Button("Cerrar")) Close();
            }
        }
    }
}
