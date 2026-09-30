using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CTEditor.GameDefinition.Domain.Trainers;
using CTEditor.GameDefinition.Editor.Csv;
using CTEditor.GameDefinition.Infrastructure.Catalog;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// Editor de ENTRENADORES (menú CTEditor → Personajes → Entrenadores): el equipo de cada rival, cómo piensa, cuánto
    /// paga al perder y qué dice. Trae entrenadores clásicos de ejemplo (un Joven, un Cazabichos, dos
    /// Líderes de gimnasio y el Rival) que usan las especies de la 1ª generación si las tienes.
    /// </summary>
    public sealed class TrainerEditorWindow : ContentEditorWindow<TrainerData>
    {
        [MenuItem(EditorMenus.Characters + "Entrenadores", false, EditorMenus.CharactersOrder + 1)]
        public static void Open() => OpenWindow<TrainerEditorWindow>("Entrenadores");

        protected override string Category => ContentFolders.Trainers;
        protected override string Noun => "entrenador";
        protected override string Title => "Entrenadores";
        protected override string Intro =>
            "Los rivales del jugador: su equipo (con la misma receta que el del jugador), cómo combaten, el dinero que pagan al perder y sus frases.";

        public override string[] GuideSteps => new[]
        {
            "Pulsa «Crear los entrenadores de ejemplo» o crea uno nuevo escribiendo su id.",
            "En «Equipo» añade miembros: especie y nivel bastan (sin movimientos elegidos, usa los que aprende a su nivel).",
            "Elige su NIVEL DE IA (1 Novato … 5 Campeón, 6 Maestro, 7 Injusto) y, si quieres, su mochila (si no, usa la de su nivel). Los niveles se ajustan en «Niveles de IA».",
            "¿Prisa? «🎲 Preparar un reto» genera un entrenador con equipo coherente para el nivel que digas.",
            "Pon su dinero base: el premio es ese valor × el nivel de su último monstruo. Abajo verás su equipo con estadísticas y sus puntos débiles.",
            "Pruébalo en la escena de pruebas: CTEditor → Pruebas → Crear escena de pruebas de combate.",
        };

        protected override Color? RowMark(TrainerData d) => AiLevelEditorWindow.LevelColor(d.EffectiveAiLevel);
        protected override string RowTooltip(TrainerData d)
            => $"{d.TrainerClass} {d.DisplayName} · IA {AiLevelEditorWindow.LevelLabel(d.EffectiveAiLevel)} · Nv. máx. {MaxLevel(d)} · " +
               $"{(d.Team ?? new TeamMemberData[0]).Count(m => m != null && m.species != null)} monstruo(s)";

        private static int MaxLevel(TrainerData d) => (d.Team ?? new TeamMemberData[0]).Where(m => m != null && m.species != null).Select(m => m.level).DefaultIfEmpty(0).Max();

        /// <summary>La ficha de IA que usa un entrenador: su IA personalizada (si existe) o la de su nivel.</summary>
        public static AiLevelData AiDataFor(TrainerData d)
        {
            var all = ContentAssets.LoadAll<AiLevelData>();
            var custom = string.IsNullOrWhiteSpace(d.AiProfileId) ? null : all.FirstOrDefault(a => a.Id == d.AiProfileId.Trim());
            return custom ?? all.FirstOrDefault(a => !a.Custom && a.Level == d.EffectiveAiLevel);
        }

        /// <summary>La IA de un entrenador tal como la verá el juego (personalizada, la de su nivel, o la clásica).</summary>
        public static AiProfile ProfileFor(TrainerData d)
        {
            var data = AiDataFor(d);
            if (data != null)
                try { return CTEditor.GameDefinition.Infrastructure.Acl.AiLevelMapper.ToDomain(data); } catch (System.Exception) { }
            return AiProfile.Classic(d.EffectiveAiLevel);
        }

        /// <summary>El nivel de IA tal como lo verá el juego (la ficha del autor, o el clásico).</summary>
        public static AiProfile ProfileFor(int level)
        {
            var data = ContentAssets.LoadAll<AiLevelData>().FirstOrDefault(a => !a.Custom && a.Level == level);
            if (data != null)
                try { return CTEditor.GameDefinition.Infrastructure.Acl.AiLevelMapper.ToDomain(data); } catch (System.Exception) { }
            return AiProfile.Classic(level);
        }

        // ---------------- Filtros y orden ----------------

        private int _filterAi;                  // 0 = todos, 1-7
        private int _filterLevelFrom, _filterLevelTo;
        private int _filterClass;               // 0 = todas
        private bool _showFilters;

        protected override string[] SortOptions => new[] { "Nombre", "Nivel de IA (dificultad)", "Nivel del equipo", "Clase", "Premio" };

        protected override int CompareBy(int sortIndex, TrainerData a, TrainerData b)
        {
            switch (sortIndex)
            {
                case 1: return a.EffectiveAiLevel != b.EffectiveAiLevel ? a.EffectiveAiLevel.CompareTo(b.EffectiveAiLevel) : MaxLevel(a).CompareTo(MaxLevel(b));
                case 2: return MaxLevel(a).CompareTo(MaxLevel(b));
                case 3: return string.Compare(a.TrainerClass, b.TrainerClass, System.StringComparison.OrdinalIgnoreCase);
                case 4: return (a.BaseMoney * MaxLevel(a)).CompareTo(b.BaseMoney * MaxLevel(b));
                default: return CompareItems(a, b);
            }
        }

        protected override void DrawFilters()
        {
            int active = (_filterAi > 0 ? 1 : 0) + (_filterLevelFrom > 0 || _filterLevelTo > 0 ? 1 : 0) + (_filterClass > 0 ? 1 : 0);
            _showFilters = EditorGUILayout.Foldout(_showFilters, active > 0 ? $"Filtros ({active} activos)" : "Filtros", true);
            if (!_showFilters) return;
            EditorGUI.BeginChangeCheck();
            _filterAi = EditorGUILayout.Popup("Nivel de IA", _filterAi, new[] { "Todos" }.Concat(Enumerable.Range(1, AiProfile.MaxLevel).Select(AiLevelEditorWindow.LevelLabel)).ToArray());
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Nivel equipo", GUILayout.Width(80));
            _filterLevelFrom = EditorGUILayout.IntField(_filterLevelFrom, GUILayout.Width(40));
            GUILayout.Label("a", GUILayout.Width(12));
            _filterLevelTo = EditorGUILayout.IntField(_filterLevelTo, GUILayout.Width(40));
            EditorGUILayout.EndHorizontal();
            var classes = Classes();
            _filterClass = EditorGUILayout.Popup("Clase", Mathf.Clamp(_filterClass, 0, classes.Count), new[] { "Todas" }.Concat(classes).ToArray());
            if (GUILayout.Button("Quitar filtros", EditorStyles.miniButton)) { _filterAi = _filterLevelFrom = _filterLevelTo = _filterClass = 0; }
            if (EditorGUI.EndChangeCheck()) FiltersChanged();
        }

        private static List<string> Classes()
            => ContentAssets.LoadAll<TrainerData>().Select(t => t.TrainerClass ?? "").Where(c => c.Length > 0).Distinct().OrderBy(c => c).ToList();

        protected override bool PassesFilter(TrainerData d)
        {
            if (_filterAi > 0 && d.EffectiveAiLevel != _filterAi) return false;
            int lvl = MaxLevel(d);
            if (_filterLevelFrom > 0 && lvl < _filterLevelFrom) return false;
            if (_filterLevelTo > 0 && lvl > _filterLevelTo) return false;
            if (_filterClass > 0)
            {
                var classes = Classes();
                if (_filterClass - 1 < classes.Count && d.TrainerClass != classes[_filterClass - 1]) return false;
            }
            return true;
        }

        // ---------------- Niveles de IA (textos y colores para toda la interfaz) ----------------

        public static string AiName(TrainerAi ai) => ai == TrainerAi.Expert ? "Experto" : ai == TrainerAi.Smart ? "Listo" : "Novato";
        public static string AiIcon(TrainerAi ai) => ai == TrainerAi.Expert ? "🧠" : ai == TrainerAi.Smart ? "🎯" : "🎲";
        public static Color AiColor(TrainerAi ai) => ai == TrainerAi.Expert ? EditorTheme.Bad : ai == TrainerAi.Smart ? EditorTheme.Warn : EditorTheme.Ok;
        public static string AiDescription(TrainerAi ai)
        {
            switch (ai)
            {
                case TrainerAi.Expert:
                    return "Calcula el daño REAL de cada golpe (estadísticas, etapas, tipos, habilidades), remata cuando puede, mejora sus estadísticas " +
                           "o pone estados con cabeza, no malgasta curaciones si lo van a debilitar igual y CAMBIA de monstruo si pierde claramente el duelo.";
                case TrainerAi.Smart:
                    return "Usa el golpe que más daño hace (potencia × eficacia × mismo tipo), evita lo que no afecta y usa sus objetos a tiempo.";
                default:
                    return "Elige movimientos al azar y a veces se le olvida usar sus objetos. Ideal para los primeros combates.";
            }
        }

        // (id, clase, nombre, IA, dinero base, frases, equipo, objetos equipados, mochila)
        public static readonly (string id, string cls, string name, TrainerAi ai, int money, string intro, string defeat, string win, (string, int)[] team, string[] held, (string, int)[] bag)[] Library =
        {
            ("joven_joaquin", "Joven", "Joaquín", TrainerAi.Random, 16,
                "¡Eh, tú! ¡Mis monstruos son los más rápidos!", "¡Jo! Ha sido mala suerte...", "¡Te lo dije! ¡Son los mejores!",
                new[] { ("rattata", 4), ("pidgey", 5) }, new string[0], new (string, int)[0]),
            ("cazabichos_rick", "Cazabichos", "Rick", TrainerAi.Random, 12,
                "¡Mira lo que he atrapado en el bosque!", "¡Mis bichos...!", "¡Los bichos mandan!",
                new[] { ("caterpie", 6), ("weedle", 6), ("metapod", 7) }, new string[0], new[] { ("potion", 1) }),
            ("lider_brock", "Líder de gimnasio", "Brock", TrainerAi.Smart, 100,
                "¡Mi voluntad es firme como una roca! ¡Demuéstrame lo que vales!", "Te he subestimado. Toma la Medalla Roca.", "Tus monstruos necesitan más entrenamiento.",
                new[] { ("geodude", 12), ("onix", 14) }, new string[0], new[] { ("super_potion", 1) }),
            ("lider_misty", "Líder de gimnasio", "Misty", TrainerAi.Smart, 100,
                "¡Mi política es un ataque total con monstruos de agua!", "Vaya, eres fuerte. Toma la Medalla Cascada.", "¿Lo ves? ¡El agua lo arrasa todo!",
                new[] { ("staryu", 18), ("starmie", 21) }, new[] { "", "sitrus_berry" }, new[] { ("super_potion", 1), ("x_defense", 1) }),
            ("rival_azul", "Rival", "Azul", TrainerAi.Expert, 35,
                "¡Llegas tarde! Ya te estaba esperando. ¡Veamos cuánto has mejorado!", "¡¿Qué?! ¡No puede ser!", "¡Ja! ¡Como siempre, soy el mejor!",
                new[] { ("pidgeotto", 17), ("abra", 16), ("rattata", 15), ("squirtle", 18) }, new[] { "", "", "", "oran_berry" },
                new[] { ("potion", 2), ("full_heal", 1) }),
        };

        /// <summary>
        /// Entrenadores de ejemplo: los 7 SUGERIDOS del pack (uno por nivel de IA, con su equipo del pack). Si no hay pack,
        /// los ejemplos de siempre. Público: lo usa el Centro de Contenido. Devuelve cuántos creó o reparó.
        /// </summary>
        public static int CreateClassicSet()
        {
            var templates = PackTools.Available ? PackTools.TrainerTemplates() : null;
            if (templates != null && templates.Count > 0)
            {
                var suggested = TrainerTemplatesWindow.Suggested(templates.Select(t => (t.id, t.label, t.level)));
                int before = ContentAssets.LoadAll<TrainerData>().Count;
                PackTools.ImportTrainers(suggested, ImportMode.CreateOnly);
                return Mathf.Max(0, ContentAssets.LoadAll<TrainerData>().Count - before);
            }
            return CreateLibrarySet();
        }

        /// <summary>Los ejemplos escritos en el código (sin pack).</summary>
        public static int CreateLibrarySet()
        {
            int created = 0;
            foreach (var t in Library)
            {
                string id = t.id;
                if (ContentAssets.CreateOrRepair<TrainerData>(ContentFolders.Trainers, t.id, t.name, so => Fill(so, id))) created++;
            }
            AssetDatabase.SaveAssets();
            return created;
        }

        /// <summary>Rellena un entrenador con una plantilla (no toca el id).</summary>
        public static void Fill(SerializedObject so, string presetId)
        {
            foreach (var t in Library)
            {
                if (t.id != presetId) continue;
                so.FindProperty("displayName").stringValue = t.name;
                so.FindProperty("trainerClass").stringValue = t.cls;
                so.FindProperty("ai").enumValueIndex = (int)t.ai;
                so.FindProperty("useItems").boolValue = true;
                so.FindProperty("healBelowPercent").intValue = 25;
                so.FindProperty("canSwitch").boolValue = true;
                var bag = so.FindProperty("items");
                var existing = t.bag.Where(b => ContentAssets.FindById<ItemData>(b.Item1) != null).ToList();
                bag.arraySize = existing.Count;
                for (int i = 0; i < existing.Count; i++)
                {
                    bag.GetArrayElementAtIndex(i).FindPropertyRelative("itemId").stringValue = existing[i].Item1;
                    bag.GetArrayElementAtIndex(i).FindPropertyRelative("quantity").intValue = existing[i].Item2;
                }
                so.FindProperty("baseMoney").intValue = t.money;
                so.FindProperty("introLine").stringValue = t.intro;
                so.FindProperty("defeatLine").stringValue = t.defeat;
                so.FindProperty("victoryLine").stringValue = t.win;
                var team = so.FindProperty("team");
                TeamPreview.FillTeam(team, t.team);
                for (int i = 0; i < t.held.Length; i++)
                    if (!string.IsNullOrEmpty(t.held[i]) && ContentAssets.FindById<ItemData>(t.held[i]) != null)
                        TeamPreview.SetHeld(team, i, t.held[i]);
            }
        }

        protected override void OnCreated(SerializedObject so)
        {
            so.FindProperty("trainerClass").stringValue = "Entrenador";
            so.FindProperty("introLine").stringValue = "¡Te reto a un combate!";
            so.FindProperty("defeatLine").stringValue = "¡Me has ganado!";
        }

        protected override IReadOnlyList<(string id, string name, string group)> Templates
            => Library.Select(p => (p.id, p.name, p.cls)).ToList();

        protected override void ApplyTemplate(SerializedObject so, string id)
        {
            Fill(so, id);
            var t = Templates.FirstOrDefault(x => x.id == id); if (t.id != null) so.FindProperty("displayName").stringValue = t.name;
        }

        protected override void DrawBulkPresets()
        {
            if (GUILayout.Button(new GUIContent("📋 Plantillas del pack… (todos, algunos o los 7 sugeridos, con vista previa)",
                    "Todos los entrenadores del pack elegido como plantillas: los ves antes de añadirlos.")))
                TrainerTemplatesWindow.Open();
            // Los entrenadores del pack clásico (líderes, Alto Mando, rivales, Team Rocket y de ruta), sin borrar nada.
            PackTools.DrawPackMenu("entrenadores", PackTools.ImportMissingTrainers, () => { PackTools.UpdateAllTrainers(); });
            DrawChallengeMaker();
        }

        // ---------------- 🎲 Preparar un reto ----------------

        private bool _showChallenge;
        private int _chAi = 2, _chSize = 3, _chMin = 10, _chMax = 14, _chType, _chZone;
        private bool _chLegend;
        private string _chName = "";

        private void DrawChallengeMaker()
        {
            EditorGUILayout.Space();
            _showChallenge = EditorGUILayout.Foldout(_showChallenge, "🎲 Preparar un reto", true);
            if (!_showChallenge) return;
            EditorTheme.Paragraph("Genera un entrenador con un equipo coherente: etapa según el nivel (Ivysaur a nivel 20), sin repetir especie y " +
                                  "con los movimientos de su nivel de IA. Luego lo ajustas como quieras.");
            _chAi = EditorGUILayout.Popup("Nivel de IA", _chAi - 1, Enumerable.Range(1, AiProfile.MaxLevel).Select(AiLevelEditorWindow.LevelLabel).ToArray()) + 1;
            _chSize = EditorGUILayout.IntSlider("Monstruos", _chSize, 1, 6);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Niveles", GUILayout.Width(60));
            _chMin = Mathf.Clamp(EditorGUILayout.IntField(_chMin, GUILayout.Width(40)), 1, 100);
            GUILayout.Label("a", GUILayout.Width(12));
            _chMax = Mathf.Clamp(EditorGUILayout.IntField(_chMax, GUILayout.Width(40)), 1, 100);
            EditorGUILayout.EndHorizontal();
            var types = ContentAssets.LoadAll<ElementTypeData>();
            _chType = EditorGUILayout.Popup("Tipo (temático)", Mathf.Clamp(_chType, 0, types.Count), new[] { "Cualquiera" }.Concat(types.Select(t => t.DisplayName)).ToArray());
            var zones = ContentAssets.LoadAll<EncounterZoneData>();
            _chZone = EditorGUILayout.Popup("Especies de", Mathf.Clamp(_chZone, 0, zones.Count), new[] { "Todas" }.Concat(zones.Select(z => "Zona: " + z.DisplayName)).ToArray());
            _chLegend = EditorGUILayout.Toggle("Permitir legendarios", _chLegend);
            _chName = EditorGUILayout.TextField("Nombre (opcional)", _chName);
            if (GUILayout.Button("Crear el entrenador")) MakeChallenge(types, zones);
        }

        private void MakeChallenge(List<ElementTypeData> types, List<EncounterZoneData> zones)
        {
            var species = new List<CTEditor.GameDefinition.Domain.Species.Species>();
            foreach (var sd in ContentAssets.LoadAll<SpeciesData>())
                try { species.Add(CTEditor.GameDefinition.Infrastructure.Acl.SpeciesMapper.ToDomain(sd)); } catch (System.Exception) { }
            if (species.Count == 0) { EditorUtility.DisplayDialog("Preparar un reto", "No hay especies: importa el pack o crea algunas antes.", "Vale"); return; }

            var req = new ChallengeRequest
            {
                AiLevel = _chAi, TeamSize = _chSize, MinLevel = Mathf.Min(_chMin, _chMax), MaxLevel = Mathf.Max(_chMin, _chMax),
                AllowLegendary = _chLegend, Name = _chName,
                Type = _chType > 0 ? new CTEditor.SharedKernel.ValueObjects.Id<CTEditor.GameDefinition.Domain.Types.ElementType>(types[_chType - 1].Id)
                                   : (CTEditor.SharedKernel.ValueObjects.Id<CTEditor.GameDefinition.Domain.Types.ElementType>?)null,
            };
            if (_chZone > 0 && zones[_chZone - 1].Entries != null)
                req.Pool = zones[_chZone - 1].Entries.Where(e => e != null && e.species != null)
                    .Select(e => new CTEditor.SharedKernel.ValueObjects.Id<CTEditor.GameDefinition.Domain.Species.Species>(e.species.Id)).ToList();

            int n = 1;
            while (ContentAssets.FindById<TrainerData>($"reto_{n}") != null) n++;
            req.Id = $"reto_{n}";
            if (string.IsNullOrWhiteSpace(req.Name)) req.Name = $"Reto {n}";
            var def = ChallengeBuilder.Build(species, req, new EditorRng());

            var asset = ContentAssets.Create<TrainerData>(ContentFolders.Trainers, def.Id, def.DisplayName, so =>
            {
                so.FindProperty("trainerClass").stringValue = def.TrainerClass;
                so.FindProperty("aiLevel").intValue = req.AiLevel;
                so.FindProperty("ai").enumValueIndex = (int)def.Ai;
                so.FindProperty("baseMoney").intValue = def.BaseMoney;
                so.FindProperty("introLine").stringValue = def.IntroLine;
                so.FindProperty("defeatLine").stringValue = def.DefeatLine;
                so.FindProperty("victoryLine").stringValue = def.VictoryLine;
                TeamPreview.FillTeam(so.FindProperty("team"), def.Team.Select(t => (t.Species.Value, t.Level)).ToArray());
            });
            AssetDatabase.SaveAssets();
            Refresh();
            Select(asset);
            ShowNotification(new GUIContent($"🎲 {def.FullName}: {def.Team.Count} monstruo(s)"));
        }

        // Azar del editor (para generar retos).
        private sealed class EditorRng : CTEditor.SharedKernel.Abstractions.IRng
        {
            private readonly System.Random _r = new System.Random();
            public int Next(int minInclusive, int maxExclusive) => _r.Next(minInclusive, maxExclusive);
            public float NextFloat() => (float)_r.NextDouble();
        }

        protected override void DrawPresets(TrainerData d)
        {
            // Plantillas = los entrenadores del pack (o de tu Excel): copia equipo, IA, mochila y frases; el id se mantiene.
            if (GUILayout.Button(new GUIContent("📋 Rellenar desde una plantilla del pack…", "Copia en este entrenador el equipo, la IA, la mochila y " +
                    "las frases de un entrenador del pack. El id se mantiene."), EditorStyles.miniButton))
            {
                var menu = new GenericMenu();
                string target = d.Id;
                foreach (var t in PackTools.TrainerTemplates().OrderBy(x => x.level).ThenBy(x => x.label))
                {
                    var tpl = t;
                    menu.AddItem(new GUIContent($"Nivel {(tpl.level > 0 ? AiLevelEditorWindow.LevelLabel(tpl.level) : "sin nivel")}/{tpl.label} ({tpl.id})"), false, () =>
                    {
                        if (!EditorUtility.DisplayDialog("Rellenar desde plantilla", $"¿Sustituir los datos de '{target}' por los de '{tpl.label}'?", "Rellenar", "Cancelar")) return;
                        string report = PackTools.FillTrainerFromTemplate(tpl.id, target);
                        EditorUtility.DisplayDialog("Rellenar desde plantilla", report ?? "No se encontró la plantilla.", "Vale");
                        Refresh();
                    });
                }
                if (menu.GetItemCount() == 0) menu.AddDisabledItem(new GUIContent("La fuente no tiene entrenadores.csv"));
                menu.ShowAsContext();
            }

            // Nivel de IA en un clic (lo más cambiado): 1 Novato … 7 Injusto (en dos filas).
            EditorGUILayout.LabelField("Nivel de IA", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            for (int lvl = 1; lvl <= AiProfile.MaxLevel; lvl++)
            {
                if (lvl == 5) { EditorGUILayout.EndHorizontal(); EditorGUILayout.BeginHorizontal(); }
                var old = GUI.backgroundColor;
                if (d.EffectiveAiLevel == lvl) GUI.backgroundColor = AiLevelEditorWindow.LevelColor(lvl);
                int l = lvl;
                if (GUILayout.Button(new GUIContent(AiLevelEditorWindow.LevelLabel(lvl), ProfileFor(lvl).Description), EditorStyles.miniButton))
                    EditSelected(so =>
                    {
                        so.FindProperty("aiLevel").intValue = l;
                        so.FindProperty("ai").enumValueIndex = (int)AiProfile.Classic(l).LegacyAi;
                        so.FindProperty("aiProfileId").stringValue = "";   // elegir un nivel quita la IA personalizada
                    });
                GUI.backgroundColor = old;
            }
            EditorGUILayout.EndHorizontal();
            DrawAiLinks(d);

            // Lo que el juego le pondrá a cada miembro, visible y editable ANTES de darle a Play.
            EditorGUILayout.LabelField("Equipo según su IA", EditorStyles.boldLabel);
            var sugProfile = ProfileFor(d);
            var sugStyle = d.MovesetStyle != MovesetStyle.ByAi ? d.MovesetStyle : sugProfile.Moveset;
            TeamPreview.DrawSuggestButtons("team", d.Team, sugProfile, sugStyle, "su IA", EditSelected);
            DrawBagEditor(d);
        }

        // ---------------- IA: abrirla y personalizarla ----------------

        private void DrawAiLinks(TrainerData d)
        {
            var data = AiDataFor(d);
            bool custom = data != null && data.Custom;
            if (custom) EditorTheme.Tip($"Usa su IA PERSONALIZADA «{data.DisplayName}» ({data.Id}). Solo la usa este entrenador (y los que la elijan).", AiLevelEditorWindow.LevelColor(data.Level), "✨");
            else if (!string.IsNullOrWhiteSpace(d.AiProfileId))
                EditorGUILayout.HelpBox($"Su IA personalizada '{d.AiProfileId}' no existe: usa la de su nivel.", MessageType.Warning);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent(custom ? "🧠 Abrir su IA personalizada" : "🧠 Abrir su nivel de IA",
                    custom ? "Abre el editor de Niveles de IA en la IA de este entrenador."
                           : "Abre el editor de Niveles de IA en su nivel. OJO: lo que cambies ahí vale para TODOS los entrenadores de ese nivel."), EditorStyles.miniButton))
            {
                if (data == null) { AiLevelEditorWindow.CreateClassicSet(); data = AiDataFor(d); }
                AiLevelEditorWindow.OpenAt(data);
            }
            if (!custom && GUILayout.Button(new GUIContent("✨ Personalizar la IA de este entrenador",
                    "Copia su nivel en una IA propia (ia_" + d.Id + ") que solo usa él: así lo ajustas sin cambiar a los demás."), EditorStyles.miniButton))
            {
                var copy = AiLevelEditorWindow.CreateCustomFor(d.Id, d.DisplayName, d.EffectiveAiLevel);
                EditSelected(so => so.FindProperty("aiProfileId").stringValue = copy.Id);
                AiLevelEditorWindow.OpenAt(copy);
            }
            if (custom && GUILayout.Button(new GUIContent("↩ Volver a la IA de su nivel", "Deja de usar la IA personalizada (la ficha no se borra)."), EditorStyles.miniButton))
                EditSelected(so => so.FindProperty("aiProfileId").stringValue = "");
            EditorGUILayout.EndHorizontal();
        }

        // ---------------- Mochila ----------------

        private void DrawBagEditor(TrainerData d)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Mochila (objetos que usa en combate)", EditorStyles.boldLabel);
            var items = ContentAssets.LoadAll<ItemData>().Where(i => i != null && !string.IsNullOrEmpty(i.Id)).ToList();
            var names = items.Select(i => i.DisplayName + "  (" + i.Id + ")").ToArray();
            var bag = d.Items ?? new BagEntryData[0];
            if (bag.Length == 0)
            {
                var prof = ProfileFor(d.EffectiveAiLevel);
                EditorTheme.Paragraph(prof.DefaultBag.Count == 0 ? "Vacía: no usará objetos."
                    : "Vacía: usará la de su nivel de IA (" + string.Join(", ", prof.DefaultBag.Select(b => b.itemId + " ×" + b.quantity)) + ").");
            }
            for (int i = 0; i < bag.Length; i++)
            {
                var e = bag[i];
                if (e == null) continue;
                EditorGUILayout.BeginHorizontal();
                int idx = items.FindIndex(it => it.Id == e.itemId);
                int ni = EditorGUILayout.Popup(idx, names);
                int nq = EditorGUILayout.IntField(e.quantity, GUILayout.Width(40));
                int row = i;
                bool remove = GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22));
                EditorGUILayout.EndHorizontal();
                if (ni != idx && ni >= 0) EditSelected(so => so.FindProperty("items").GetArrayElementAtIndex(row).FindPropertyRelative("itemId").stringValue = items[ni].Id);
                if (nq != e.quantity) EditSelected(so => so.FindProperty("items").GetArrayElementAtIndex(row).FindPropertyRelative("quantity").intValue = Mathf.Max(1, nq));
                if (remove) { EditSelected(so => so.FindProperty("items").DeleteArrayElementAtIndex(row)); GUIUtility.ExitGUI(); }
            }
            EditorGUILayout.BeginHorizontal();
            if (items.Count > 0 && GUILayout.Button("+ Añadir objeto", EditorStyles.miniButton))
                EditSelected(so =>
                {
                    var arr = so.FindProperty("items");
                    arr.arraySize++;
                    var el = arr.GetArrayElementAtIndex(arr.arraySize - 1);
                    var potion = items.FirstOrDefault(it => it.Id == "hyper_potion") ?? items[0];
                    el.FindPropertyRelative("itemId").stringValue = potion.Id;
                    el.FindPropertyRelative("quantity").intValue = 1;
                });
            if (bag.Length > 0 && GUILayout.Button("Vaciar (usar la de su nivel)", EditorStyles.miniButton))
                EditSelected(so => so.FindProperty("items").arraySize = 0);
            EditorGUILayout.EndHorizontal();
        }

        protected override void DrawPreview(TrainerData d)
        {
            EditorTheme.Section("Así se verá en combate", Accent);
            var members = (d.Team ?? new TeamMemberData[0]).Where(m => m != null && m.species != null).ToList();
            string full = string.IsNullOrWhiteSpace(d.TrainerClass) ? d.DisplayName : $"{d.TrainerClass} {d.DisplayName}";
            int prize = members.Count > 0 ? d.BaseMoney * members[members.Count - 1].level : 0;
            EditorTheme.Chip($"{full}  ·  premio {prize} ₽", Accent);
            if (!string.IsNullOrWhiteSpace(d.IntroLine)) EditorTheme.Tip($"«{d.IntroLine}»", Accent, "💬");

            // --- Cómo piensa ---
            int level = d.EffectiveAiLevel;
            var profile = ProfileFor(d);
            var lc = AiLevelEditorWindow.LevelColor(level);
            EditorTheme.Section(profile.IsCustom ? $"Cómo piensa: IA personalizada «{profile.DisplayName}» (base nivel {level})"
                                                 : $"Cómo piensa: nivel {AiLevelEditorWindow.LevelLabel(level)}", lc);
            EditorTheme.Tip(profile.Description, lc, "🧠");
            var bag = (d.Items ?? new BagEntryData[0]).Where(b => b != null && !string.IsNullOrWhiteSpace(b.itemId)).ToList();
            if (!d.UseItems)
                EditorTheme.Tip("No usa objetos (interruptor «Usa objetos» apagado).", EditorTheme.Tools, "🚫");
            else
            {
                var shown = bag.Count > 0 ? bag.Select(b => (b.itemId, b.quantity)).ToList() : profile.DefaultBag.ToList();
                if (shown.Count == 0) EditorTheme.Tip("Usa objetos, pero ni él ni su nivel llevan ninguno.", EditorTheme.Warn, "🎒");
                else
                {
                    var pills = shown.Select(b =>
                    {
                        var item = ContentAssets.FindById<ItemData>(b.Item1);
                        return ((item != null ? item.DisplayName : "⚠ " + b.Item1) + $" ×{b.Item2}", item != null ? EditorTheme.Items : EditorTheme.Bad);
                    }).ToArray();
                    EditorTheme.Pills(pills);
                    string heal = profile.Heal == HealStyle.Smart
                        ? "Se cura solo si le sirve (si el rival lo tumba igual o le quita más de lo que cura, ataca)."
                        : profile.Heal == HealStyle.Simple ? $"Se cura en cuanto baja del {profile.HealBelowPercent} % de PS." : "Nunca se cura.";
                    EditorTheme.Tip((bag.Count == 0 ? "(Mochila de su nivel.) " : "") + heal +
                                    (profile.ItemUsePercent < 100 ? $" Se acuerda de sus objetos un {profile.ItemUsePercent} % de las veces." : ""), EditorTheme.Items, "🎒");
                }
            }
            if (profile.CanSwitch)
                EditorTheme.Tip(d.CanSwitch ? "Cambia de monstruo si pierde claramente el duelo (como mucho cada 3 turnos)."
                                            : "No cambia de monstruo (interruptor «Puede cambiar» apagado).", EditorTheme.Tools, "🔄");

            // --- Su equipo ---
            EditorTheme.Section("Su equipo", Accent);
            var style = d.MovesetStyle != MovesetStyle.ByAi ? d.MovesetStyle : profile.Moveset;
            EditorTheme.Tip(style == MovesetStyle.Classic ? "Los miembros sin movimientos escritos usan los 4 últimos que aprenden (clásico, a veces flojo)."
                          : style == MovesetStyle.Strong ? "Los miembros sin movimientos escritos eligen sus ataques MÁS FUERTES con cobertura (difícil)."
                          : style == MovesetStyle.Competitive ? "Sets DE COMPETICIÓN: STAB + cobertura, prioridad, pivotes y sinergias (muy difícil)."
                          : "Los miembros sin movimientos escritos eligen su mejor ataque, cobertura y un apoyo (equilibrado).", EditorTheme.Tools, "🎯");
            var srcs = new List<string>();
            if (profile.UseMachineMoves) srcs.Add("MT");
            if (profile.UseTutorMoves) srcs.Add("tutor");
            if (profile.UseEggMoves) srcs.Add("huevo");
            if (style != MovesetStyle.Classic && (srcs.Count > 0 || profile.Synergies))
                EditorTheme.Tip("Por su nivel también usa movimientos de " + (srcs.Count > 0 ? string.Join(", ", srcs) : "nivel") +
                                (profile.Synergies ? " y busca sinergias (Hipnosis + Comesueños, Danza Lluvia + Agua...)." : "."), EditorTheme.Tools, "✨");
            TeamPreview.Draw(d.Team, Accent, style, profile);
            if (!string.IsNullOrWhiteSpace(d.DefeatLine)) EditorTheme.Tip($"Al perder: «{d.DefeatLine}»", EditorTheme.Ok, "🏳");
            if (!string.IsNullOrWhiteSpace(d.VictoryLine)) EditorTheme.Tip($"Si gana: «{d.VictoryLine}»", EditorTheme.Bad, "🏆");
            EditorTheme.Tip("Reglas: no se puede huir ni capturar sus monstruos (cámbialo en Reglas del juego). Cuando uno cae, saca el siguiente en el orden de la lista.", EditorTheme.Tools);
        }
    }
}
