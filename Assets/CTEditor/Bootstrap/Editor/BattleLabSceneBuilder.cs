using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using CTEditor.GameDefinition.Editor;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.Bootstrap.Editor
{
    /// <summary>
    /// Crea la ESCENA DE PRUEBAS DE COMBATE ya montada y cableada, sin tocar nada a mano:
    ///
    ///   • "Partida del jugador" (PartyHolder) con un equipo prearmado (o un inicial si no hay equipos).
    ///   • Laboratorio: combate salvaje por zona, contra entrenadores, Centro, orden del equipo y PC,
    ///     y la MOCHILA Y EL EQUIPO fuera del combate (usar objetos, piedras, equipar, resumen).
    ///   • Pantalla de combate completa: datos de ambos monstruos, barras de PS, caja de mensajes,
    ///     menú Luchar / Mochila / Equipo / Capturar / Huir, movimientos, equipo, mochila y Sí/No.
    ///
    /// Se guarda en Assets/GameContent/Scenes/PruebaCombate.unity. Pulsa Play y a jugar.
    /// </summary>
    public static class BattleLabSceneBuilder
    {
        /// <summary>Se apunta al catálogo de editores (Centro de Contenido → Pruebas) al cargar el editor.</summary>
        [InitializeOnLoadMethod]
        private static void RegisterInCatalog()
            => EditorCatalog.Register(new EditorEntry
            {
                Category = EditorCategory.Testing, Order = 10, Title = "Escena de pruebas de combate", Icon = "🎮",
                Description = "Genera una escena lista para jugar: tu equipo, combates salvajes y contra entrenadores, capturas, mochila, Centro y PC.",
                Color = EditorTheme.Ok, Open = Build, Keywords = "jugar laboratorio escena",
            });

        public const string ScenePath = "Assets/GameContent/Scenes/PruebaCombate.unity";

        // Paleta de la interfaz.
        private static readonly Color Bg = new Color(0.10f, 0.13f, 0.17f);
        private static readonly Color Panel = new Color(0.16f, 0.20f, 0.27f, 0.97f);
        private static readonly Color InfoBox = new Color(0.93f, 0.95f, 0.90f);
        private static readonly Color Ink = new Color(0.12f, 0.12f, 0.14f);
        private static readonly Color MessageBg = new Color(0.14f, 0.24f, 0.40f);
        private static readonly Color Red = new Color(0.86f, 0.33f, 0.30f);
        private static readonly Color Yellow = new Color(0.95f, 0.76f, 0.26f);
        private static readonly Color Green = new Color(0.36f, 0.70f, 0.40f);
        private static readonly Color Blue = new Color(0.35f, 0.55f, 0.90f);
        private static readonly Color Grey = new Color(0.50f, 0.52f, 0.56f);
        private static readonly Color Purple = new Color(0.60f, 0.45f, 0.85f);

        [MenuItem(CTEditor.GameDefinition.Editor.EditorMenus.BattleScenePath, false, CTEditor.GameDefinition.Editor.EditorMenus.TestingOrder + 10)]
        public static void Build()
        {
            // 1) Contenido mínimo para poder jugar.
            if (ContentAssets.LoadAll<SpeciesData>().Count == 0)
            {
                EditorUtility.DisplayDialog("Faltan especies",
                    "Para probar combates necesitas especies y movimientos.\n\nAbre CTEditor → Centro de Contenido y pulsa «Importar la 1ª generación» (o crea las tuyas).", "Vale");
                return;
            }
            if (TMP_Settings.defaultFontAsset == null)
            {
                EditorUtility.DisplayDialog("Falta TextMeshPro",
                    "Antes hay que importar los recursos básicos de TextMeshPro (la fuente de los textos).\n\n" +
                    "Se abrirá la ventana: pulsa «Import TMP Essentials» y luego vuelve a usar este menú.", "Vale");
                EditorApplication.ExecuteMenuItem("Window/TextMeshPro/Import TMP Essential Resources");
                return;
            }
            if (ContentAssets.LoadAll<TrainerData>().Count == 0 || ContentAssets.LoadAll<TeamPresetData>().Count == 0 ||
                ContentAssets.LoadAll<EncounterZoneData>().Count == 0)
            {
                if (EditorUtility.DisplayDialog("Ejemplos de combate",
                        "No tienes entrenadores, equipos prearmados o zonas salvajes. ¿Creo los de ejemplo? (No toca nada tuyo.)", "Sí, créalos", "No"))
                {
                    TrainerEditorWindow.CreateClassicSet();
                    TeamPresetEditorWindow.CreateClassicSet();
                    EncounterZoneEditorWindow.CreateClassicSet();
                    AssetDatabase.SaveAssets();
                }
            }
            if (File.Exists(ScenePath) && !EditorUtility.DisplayDialog("Escena de pruebas",
                    "Ya existe la escena de pruebas. ¿Quieres volver a crearla? (Se reemplaza.)", "Reemplazar", "Cancelar"))
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            // 2) Escena vacía con cámara, sistema de eventos y lienzo.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Bg;
            CreateEventSystem();

            var canvasGo = new GameObject("Interfaz", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            var root = (RectTransform)canvasGo.transform;

            // 3) Partida del jugador.
            var partyGo = new GameObject("Partida del jugador");
            var party = partyGo.AddComponent<PartyHolder>();
            ConfigureParty(party);

            // 4) Pantalla de combate y laboratorio.
            var battleRoot = Stretch(Box(root, "Combate", Bg));
            var screen = BuildBattleUi(battleRoot);
            var labRoot = Stretch(Box(root, "Laboratorio", Bg));
            var lab = BuildLab(labRoot, screen, battleRoot.gameObject);

            // 5) Guardar.
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            Selection.activeGameObject = lab.gameObject;
            EditorUtility.DisplayDialog("Escena de pruebas lista",
                "Creada en " + ScenePath + ".\n\nPulsa ▶ Play: verás el Laboratorio. Desde ahí lanzas combates salvajes (para capturar) y " +
                "contra entrenadores, curas en el Centro y ordenas tu equipo. Con la mochila usas objetos, piedras evolutivas y objetos equipados, " +
                "y «Resumen» enseña las estadísticas, IV/EV y PP del elegido. Tu equipo se va actualizando con cada combate.\n\n" +
                "TECLADO: flechas para moverte, Z/Espacio/Intro para elegir, X para volver y Esc para el MENÚ DE PAUSA (equipo, mochila, " +
                "resumen, opciones). Las teclas y los menús se cambian en CTEditor → Interfaz.\n\n" +
                "Para cambiar el equipo inicial, selecciona «Partida del jugador» en la escena.", "¡A jugar!");
        }

        // ---------------- Partida ----------------

        private static void ConfigureParty(PartyHolder party)
        {
            var so = new SerializedObject(party);
            var preset = ContentAssets.FindById<TeamPresetData>("equipo_prueba_50") ?? ContentAssets.LoadAll<TeamPresetData>().FirstOrDefault();
            if (preset != null)
            {
                so.FindProperty("startMode").enumValueIndex = (int)PartyHolder.StartMode.Preset;
                so.FindProperty("teamPreset").objectReferenceValue = preset;
            }
            else
            {
                so.FindProperty("startMode").enumValueIndex = (int)PartyHolder.StartMode.Starter;
                so.FindProperty("starterSpecies").objectReferenceValue =
                    ContentAssets.FindById<SpeciesData>("charmander") ?? ContentAssets.LoadAll<SpeciesData>().First();
            }
            var starter = ContentAssets.FindById<SpeciesData>("charmander") ?? ContentAssets.LoadAll<SpeciesData>().First();
            if (so.FindProperty("starterSpecies").objectReferenceValue == null) so.FindProperty("starterSpecies").objectReferenceValue = starter;
            so.FindProperty("persistAcrossScenes").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------------- Pantalla de combate ----------------

        private static BattleScreen BuildBattleUi(RectTransform root)
        {
            var screen = root.gameObject.AddComponent<BattleScreen>();

            // Escenario: dos plataformas.
            Place(Box(root, "Plataforma rival", new Color(0.24f, 0.36f, 0.28f)), 760, 190, 400, 36);
            Place(Box(root, "Plataforma jugador", new Color(0.24f, 0.36f, 0.28f)), 120, 430, 400, 36);

            // Datos del rival (arriba a la izquierda) y del jugador (abajo a la derecha).
            var enemy = InfoBoxUi(root, "Datos del rival", 40, 30);
            var player = InfoBoxUi(root, "Datos del jugador", 800, 340);

            // Caja de mensajes.
            var msgBox = Place(Box(root, "Caja de mensajes", MessageBg), 0, 500, 1280, 220);
            var message = Text(msgBox, "Mensaje", "", 28, TextAlignmentOptions.TopLeft, Color.white);
            Place(message.rectTransform, 30, 22, 720, 180);
            var advance = ButtonUi(msgBox, "Avanzar (clic)", "", new Color(0, 0, 0, 0));
            Place((RectTransform)advance.transform, 0, 0, 770, 220);

            // Menú de acción.
            var action = Place(Box(root, "Menú de acción", new Color(0, 0, 0, 0)), 780, 510, 480, 200);
            var fight = PlaceButton(action, "⚔ Luchar", Red, 0, 0, 230, 60);
            var bag = PlaceButton(action, "🎒 Mochila", Yellow, 240, 0, 230, 60);
            var team = PlaceButton(action, "👥 Equipo", Green, 0, 70, 230, 60);
            var ball = PlaceButton(action, "⚪ Capturar", Blue, 240, 70, 230, 60);
            var run = PlaceButton(action, "🏃 Huir", Grey, 0, 140, 470, 50);

            // Movimientos (2×2).
            var movePanel = Place(Box(root, "Movimientos", new Color(0, 0, 0, 0)), 780, 510, 480, 170);
            var moves = new Button[4];
            for (int i = 0; i < 4; i++) moves[i] = PlaceButton(movePanel, "Movimiento", Purple, (i % 2) * 240, (i / 2) * 85, 230, 78);

            // Equipo (6).
            var switchPanel = Place(Box(root, "Equipo", Panel), 300, 30, 680, 460);
            var switches = new Button[6];
            for (int i = 0; i < 6; i++) switches[i] = PlaceButton(switchPanel, "Miembro", Green, 20, 20 + i * 70, 640, 62, 20);

            // Mochila (8, en dos columnas).
            var bagPanel = Place(Box(root, "Mochila", Panel), 300, 30, 680, 460);
            var bagButtons = new Button[8];
            for (int i = 0; i < 8; i++) bagButtons[i] = PlaceButton(bagPanel, "Objeto", Yellow, 20 + (i % 2) * 330, 20 + (i / 2) * 105, 310, 95, 20);

            // Sí / No.
            var confirm = Place(Box(root, "Sí o No", Panel), 1000, 380, 250, 110);
            var yes = PlaceButton(confirm, "Sí", Green, 10, 10, 110, 90);
            var no = PlaceButton(confirm, "No", Red, 130, 10, 110, 90);

            // Atrás (común a los menús).
            var back = ButtonUi(root, "Atrás", "Atrás", Grey);
            Place((RectTransform)back.transform, 1100, 452, 160, 42);

            // Cablear todo en el componente.
            var so = new SerializedObject(screen);
            so.FindProperty("startOnPlay").boolValue = false;          // lo lanza el Laboratorio
            Set(so, "playerNameText", player.name); Set(so, "playerLevelText", player.level);
            Set(so, "playerHpBar", player.bar); Set(so, "playerHpText", player.hp);
            Set(so, "enemyNameText", enemy.name); Set(so, "enemyLevelText", enemy.level);
            Set(so, "enemyHpBar", enemy.bar); Set(so, "enemyHpText", enemy.hp);
            Set(so, "messageText", message); Set(so, "advanceButton", advance);
            Set(so, "actionPanel", action.gameObject); Set(so, "movePanel", movePanel.gameObject);
            Set(so, "switchPanel", switchPanel.gameObject); Set(so, "bagPanel", bagPanel.gameObject);
            Set(so, "confirmPanel", confirm.gameObject);
            Set(so, "fightButton", fight); Set(so, "bagButton", bag); Set(so, "switchButton", team);
            Set(so, "catchButton", ball); Set(so, "fleeButton", run); Set(so, "backButton", back);
            Set(so, "yesButton", yes); Set(so, "noButton", no);
            SetArray(so, "moveButtons", moves); SetArray(so, "switchButtons", switches); SetArray(so, "bagButtons", bagButtons);
            so.ApplyModifiedPropertiesWithoutUndo();

            foreach (var p in new[] { action, movePanel, switchPanel, bagPanel, confirm }) p.gameObject.SetActive(false);
            back.gameObject.SetActive(false);
            return screen;
        }

        private struct InfoUi { public TMP_Text name, level, hp; public Slider bar; }

        private static InfoUi InfoBoxUi(RectTransform root, string title, float x, float y)
        {
            var box = Place(Box(root, title, InfoBox), x, y, 440, 110);
            var ui = new InfoUi
            {
                name = Text(box, "Nombre", "—", 26, TextAlignmentOptions.MidlineLeft, Ink, bold: true),
                level = Text(box, "Nivel", "Nv.", 22, TextAlignmentOptions.MidlineRight, Ink),
                hp = Text(box, "PS", "0/0", 20, TextAlignmentOptions.MidlineRight, Ink),
            };
            Place(ui.name.rectTransform, 16, 10, 320, 34);
            Place(ui.level.rectTransform, 330, 10, 94, 34);
            var psLabel = Text(box, "Etiqueta PS", "PS", 18, TextAlignmentOptions.MidlineLeft, Ink, bold: true);
            Place(psLabel.rectTransform, 16, 58, 40, 26);
            ui.bar = SliderUi(box);
            Place((RectTransform)ui.bar.transform, 56, 64, 260, 16);
            Place(ui.hp.rectTransform, 320, 56, 104, 30);
            return ui;
        }

        // ---------------- Laboratorio ----------------

        private static BattleLab BuildLab(RectTransform root, BattleScreen screen, GameObject battleRoot)
        {
            var lab = root.gameObject.AddComponent<BattleLab>();
            var title = Text(root, "Título", "🧪 Laboratorio de combate", 36, TextAlignmentOptions.MidlineLeft, Color.white, bold: true);
            Place(title.rectTransform, 40, 16, 1200, 50);
            var sub = Text(root, "Subtítulo", "Prueba tu juego con la partida REAL. Teclado: flechas + Z (elegir) · X (atrás) · Esc (menú de pausa). También con ratón o mando.",
                17, TextAlignmentOptions.MidlineLeft, new Color(0.75f, 0.8f, 0.9f));
            Place(sub.rectTransform, 40, 66, 1200, 30);

            var infoBox = Place(Box(root, "Información", Panel), 30, 105, 780, 190);
            var info = Text(infoBox, "Texto", "", 19, TextAlignmentOptions.TopLeft, Color.white);
            Place(info.rectTransform, 16, 12, 750, 168);
            var teamBox = Place(Box(root, "Tu equipo", Panel), 30, 305, 780, 400);
            var team = Text(teamBox, "Texto", "", 18, TextAlignmentOptions.TopLeft, Color.white);
            Place(team.rectTransform, 16, 12, 750, 378);
            // El resumen es largo: el texto se encoge solo para que quepa.
            team.enableAutoSizing = true;
            team.fontSizeMin = 11;
            team.fontSizeMax = 18;

            // Pregunta "¿qué movimiento olvida?" (solo aparece cuando hace falta).
            var learnPanel = Place(Box(teamBox, "Aprender movimiento", new Color(0.30f, 0.22f, 0.42f)), 10, 330, 760, 62);
            var forget = new Button[4];
            for (int i = 0; i < 4; i++) forget[i] = PlaceButton(learnPanel, "Olvidar", Purple, 8 + i * 150, 8, 142, 46, 15);
            var skipLearn = PlaceButton(learnPanel, "No aprenderlo", Grey, 608, 8, 144, 46, 15);
            learnPanel.gameObject.SetActive(false);

            float x = 830, w = 420;
            var trainerBtn = PlaceButton(root, "⚔ Combate contra entrenador", Red, x, 105, w, 58, 22);
            var prevT = PlaceButton(root, "◀ Entrenador", Grey, x, 168, 205, 38, 18);
            var nextT = PlaceButton(root, "Entrenador ▶", Grey, x + 215, 168, 205, 38, 18);
            var wildBtn = PlaceButton(root, "🌿 Combate salvaje", Green, x, 218, w, 58, 22);
            var prevZ = PlaceButton(root, "◀ Zona", Grey, x, 281, 205, 38, 18);
            var nextZ = PlaceButton(root, "Zona ▶", Grey, x + 215, 281, 205, 38, 18);
            var heal = PlaceButton(root, "🏥 Centro (curar a todo el equipo)", Blue, x, 332, w, 50, 20);

            var leadLabel = Text(root, "Elegir miembro", "Elige un miembro del equipo (◆):", 17, TextAlignmentOptions.MidlineLeft, Color.white);
            Place(leadLabel.rectTransform, x, 388, w, 26);
            var leads = new Button[6];
            for (int i = 0; i < 6; i++) leads[i] = PlaceButton(root, (i + 1).ToString(), Purple, x + i * 71, 414, 64, 40, 20);
            var makeLead = PlaceButton(root, "▶ Poner primero", Purple, x, 460, 136, 42, 15);
            var deposit = PlaceButton(root, "Dejar en el PC", Yellow, x + 142, 460, 136, 42, 15);
            var withdraw = PlaceButton(root, "Sacar del PC", Yellow, x + 284, 460, 136, 42, 15);

            // Mochila y equipo (fuera del combate): ◀ objeto ▶ y qué hacer con él.
            var bagLabel = Text(root, "Mochila", "🎒 Mochila (sobre el miembro elegido):", 17, TextAlignmentOptions.MidlineLeft, Color.white);
            Place(bagLabel.rectTransform, x, 508, w, 24);
            var prevItem = PlaceButton(root, "◀", Grey, x, 534, 48, 42, 20);
            var itemBox = Place(Box(root, "Objeto elegido", InfoBox), x + 54, 534, 312, 42);
            var itemText = Text(itemBox, "Texto", "", 17, TextAlignmentOptions.Center, Ink);
            Stretch(itemText.rectTransform);
            itemText.margin = new Vector4(6, 2, 6, 2);
            itemText.enableAutoSizing = true;
            itemText.fontSizeMin = 11;
            itemText.fontSizeMax = 17;
            var nextItem = PlaceButton(root, "▶", Grey, x + 372, 534, 48, 42, 20);
            var useItem = PlaceButton(root, "Usar", Yellow, x, 582, 136, 42, 17);
            var giveItem = PlaceButton(root, "Dar para llevar", Yellow, x + 142, 582, 136, 42, 15);
            var takeItem = PlaceButton(root, "Quitar objeto", Yellow, x + 284, 582, 136, 42, 15);
            var summary = PlaceButton(root, "📋 Resumen", Blue, x, 638, 205, 44, 17);
            var reset = PlaceButton(root, "↺ Reiniciar la partida", Red, x + 215, 638, 205, 44, 16);

            var so = new SerializedObject(lab);
            SetArray(so, "trainers", ContentAssets.LoadAll<TrainerData>().OrderBy(t => t.BaseMoney * LastLevel(t)).ToArray());
            SetArray(so, "zones", ContentAssets.LoadAll<EncounterZoneData>().ToArray());
            Set(so, "battleScreen", screen); Set(so, "labPanel", root.gameObject); Set(so, "battleRoot", battleRoot);
            Set(so, "infoText", info); Set(so, "teamText", team);
            Set(so, "wildButton", wildBtn); Set(so, "trainerButton", trainerBtn);
            Set(so, "prevTrainerButton", prevT); Set(so, "nextTrainerButton", nextT);
            Set(so, "prevZoneButton", prevZ); Set(so, "nextZoneButton", nextZ);
            Set(so, "healButton", heal); Set(so, "resetButton", reset);
            Set(so, "depositButton", deposit); Set(so, "withdrawButton", withdraw);
            SetArray(so, "leadButtons", leads);
            Set(so, "makeLeadButton", makeLead);
            Set(so, "itemText", itemText);
            Set(so, "prevItemButton", prevItem); Set(so, "nextItemButton", nextItem);
            Set(so, "useItemButton", useItem); Set(so, "giveItemButton", giveItem); Set(so, "takeItemButton", takeItem);
            Set(so, "summaryButton", summary);
            Set(so, "learnPanel", learnPanel.gameObject);
            SetArray(so, "forgetButtons", forget);
            Set(so, "skipLearnButton", skipLearn);
            so.ApplyModifiedPropertiesWithoutUndo();
            return lab;
        }

        private static int LastLevel(TrainerData t)
        {
            var team = (t.Team ?? new TeamMemberData[0]).Where(m => m != null && m.species != null).ToList();
            return team.Count == 0 ? 0 : team[team.Count - 1].level;
        }

        // ---------------- Piezas de interfaz ----------------

        private static RectTransform Box(RectTransform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = color.a > 0.01f;
            return (RectTransform)go.transform;
        }

        private static RectTransform Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        // Posición en píxeles de referencia (1280×720) desde la esquina superior izquierda del padre.
        private static RectTransform Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        private static TMP_Text Text(RectTransform parent, string name, string text, float size, TextAlignmentOptions align, Color color, bool bold = false)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = TextSafety.Clean(text, t.font);   // sin emojis ni símbolos que la fuente no tenga
            t.fontSize = size;
            t.alignment = align;
            t.color = color;
            t.richText = true;
            t.raycastTarget = false;
            if (bold) t.fontStyle = FontStyles.Bold;
            return t;
        }

        private static Button ButtonUi(RectTransform parent, string name, string label, Color color, float fontSize = 24)
        {
            var rt = Box(parent, name, color);
            var img = rt.GetComponent<Image>();
            img.raycastTarget = true;
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var t = Text(rt, "Texto", label, fontSize, TextAlignmentOptions.Center, color.a < 0.01f ? Color.white : Ink, bold: true);
            Stretch(t.rectTransform);
            t.margin = new Vector4(8, 4, 8, 4);
            return b;
        }

        private static Button PlaceButton(RectTransform parent, string label, Color color, float x, float y, float w, float h, float fontSize = 24)
        {
            var b = ButtonUi(parent, label, label, color, fontSize);
            Place((RectTransform)b.transform, x, y, w, h);
            return b;
        }

        private static Slider SliderUi(RectTransform parent)
        {
            var bg = Box(parent, "Barra de PS", new Color(0.25f, 0.25f, 0.25f));
            var area = new GameObject("Relleno", typeof(RectTransform));
            area.transform.SetParent(bg, false);
            Stretch((RectTransform)area.transform);
            var fill = Box((RectTransform)area.transform, "Barra", new Color(0.30f, 0.80f, 0.40f));
            Stretch(fill);
            var slider = bg.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = null;
            slider.transition = Selectable.Transition.None;
            slider.interactable = false;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 1f;
            return slider;
        }

        private static void CreateEventSystem()
        {
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            // Unity 6 suele usar el Input System nuevo: si está, su módulo; si no, el clásico.
            var inputSystemModule = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (inputSystemModule != null) go.AddComponent(inputSystemModule);
            else go.AddComponent<StandaloneInputModule>();
        }

        private static void Set(SerializedObject so, string field, UnityEngine.Object value)
        {
            var p = so.FindProperty(field);
            if (p != null) p.objectReferenceValue = value;
            else Debug.LogWarning($"[CTEditor] Campo '{field}' no encontrado al montar la escena.");
        }

        private static void SetArray<T>(SerializedObject so, string field, T[] values) where T : UnityEngine.Object
        {
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogWarning($"[CTEditor] Lista '{field}' no encontrada al montar la escena."); return; }
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        private static void AddToBuildSettings(string path)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(s => s.path == path)) return;
            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
