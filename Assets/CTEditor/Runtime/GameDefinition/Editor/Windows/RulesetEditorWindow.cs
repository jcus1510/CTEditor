using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CTEditor.GameDefinition.Domain.Rules;
using CTEditor.GameDefinition.Infrastructure.Catalog;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// Editor de REGLAS DEL JUEGO (menú CTEditor → Combate → Reglas del juego). Las "perillas" globales: tamaño
    /// del equipo, movimientos por monstruo, nivel máximo, fórmula de daño y la genética (IV/EV).
    /// Trae presets reconocibles y explica en palabras lo que implica cada ajuste.
    /// </summary>
    public sealed class RulesetEditorWindow : ContentEditorWindow<RulesetData>
    {
        [MenuItem(EditorMenus.Battle + "Reglas del juego", false, EditorMenus.BattleOrder + 8)]
        public static void Open() => OpenWindow<RulesetEditorWindow>("Reglas");

        protected override string Category => ContentFolders.Rulesets;
        protected override string Noun => "regla";
        protected override string Intro =>
            "Las reglas globales de tu juego. El juego usa UNA sola: la primera de la carpeta Rulesets " +
            "(si tienes varias, deja solo la que quieras usar).";

        protected override string Title => "Reglas del juego";

        public override string[] GuideSteps => new[]
        {
            "Pulsa «Crear reglas clásicas». El juego usa UNA sola ficha de reglas.",
            "Elige una plantilla (clásico, sin genética, reto corto…) y ajusta equipo, nivel máximo, IV/EV y PP.",
            "En «Golpes críticos» elige la tabla por generación o escribe la tuya.",
            "Si inventaste estadísticas, puedes decidir con cuáles se calcula el daño físico y el especial.",
            "En «Aventura» decides huir, capturar, dinero, derrota y Repartir Experiencia (plantillas: clásica, fácil o reto).",
            "En «Reglas de generación» pon las de una generación de golpe (1.ª: sin habilidades ni objetos...) y retoca lo que quieras.",
            "En «Mecánicas especiales» activa las que quieras (Megaevolución...). Se pueden combinar.",
        };

        // (nombre, equipo, movimientos, nivel máx, IV máx, EV por stat, EV total)
        private static readonly (string name, int party, int moves, int cap, int iv, int evStat, int evTotal)[] Presets =
        {
            ("Clásico (Gen VI+)",            6, 4, 100, 31, 252, 510),
            ("Gen III–V (EV 255 por estadística)",  6, 4, 100, 31, 255, 510),
            ("Sin genética (sin IV ni EV)",  6, 4, 100,  0,   0,   0),
            ("Reto corto (equipo 3, Nv.50)", 3, 4,  50, 31, 252, 510),
        };

        /// <summary>Crea las reglas clásicas si no hay ninguna. Público: lo usa el Centro de Contenido.</summary>
        public static int CreateClassicSet()
        {
            if (ContentAssets.LoadAll<RulesetData>().Count > 0) return 0;
            ContentAssets.Create<RulesetData>(ContentFolders.Rulesets, "classic", "Clásico", so => Apply(so, 0));
            return 1;
        }

        protected override void DrawBulkPresets()
        {
            if (GUILayout.Button("Crear reglas clásicas")) FinishBulk(CreateClassicSet(), "reglas");
        }

        protected override void DrawPresets(RulesetData d)
        {
            EditorGUILayout.LabelField("Plantillas", EditorStyles.boldLabel);
            int a = ButtonRow(Presets[0].name, Presets[1].name);
            int b = ButtonRow(Presets[2].name, Presets[3].name);
            int pick = a >= 0 ? a : (b >= 0 ? b + 2 : -1);
            if (pick >= 0) EditSelected(so => Apply(so, pick));

            // Tabla de críticos: plantillas por generación (solo cambian los críticos).
            EditorGUILayout.LabelField("Golpes críticos", EditorStyles.boldLabel);
            int c = ButtonRow(CritPresets[0].name, CritPresets[1].name, CritPresets[2].name, CritPresets[3].name);
            if (c >= 0) EditSelected(so => ApplyCrit(so, c));

            // Reglas de generación: todas las perillas de una generación de golpe (luego se retocan una a una).
            EditorGUILayout.LabelField("Reglas de generación", EditorStyles.boldLabel);
            int g1 = ButtonRow("1.ª gen.", "2.ª gen.", "3.ª gen.", "4.ª gen.", "5.ª gen.");
            int g2 = ButtonRow("6.ª gen.", "7.ª gen.", "8.ª gen.", "9.ª gen.", "Moderno (todo)");
            int gen = g1 >= 0 ? g1 + 1 : g2 >= 0 ? (g2 == 4 ? 0 : g2 + 6) : -1;
            if (gen >= 0) EditSelected(so => ApplyGeneration(so, GenerationRules.ForGeneration(gen)));

            DrawMechanics(d);

            // Aventura: huir, capturar, dinero, derrota, experiencia.
            EditorGUILayout.LabelField("Aventura (huir, capturar, dinero, derrota)", EditorStyles.boldLabel);
            int adv = ButtonRow(AdventurePresets[0].name, AdventurePresets[1].name, AdventurePresets[2].name);
            if (adv >= 0) EditSelected(so => ApplyAdventure(so, adv));
            EditorGUILayout.Space();
        }

        /// <summary>Pone todas las reglas de generación de golpe (plantilla por generación).</summary>
        public static void ApplyGeneration(SerializedObject so, GenerationRules g)
        {
            so.FindProperty("generation").intValue = g.Generation;
            so.FindProperty("categoryByType").boolValue = g.CategoryByType;
            so.FindProperty("singleSpecialStat").boolValue = g.SingleSpecialStat;
            so.FindProperty("abilitiesEnabled").boolValue = g.Abilities;
            so.FindProperty("heldItemsEnabled").boolValue = g.HeldItems;
            so.FindProperty("naturesEnabled").boolValue = g.Natures;
            so.FindProperty("gendersEnabled").boolValue = g.Genders;
            var arr = so.FindProperty("specialTypes");
            var types = GenerationRules.ClassicSpecialTypes;
            arr.arraySize = types.Count;
            for (int i = 0; i < types.Count; i++) arr.GetArrayElementAtIndex(i).stringValue = types[i];
            // Críticos fieles a la generación (1.ª gen. usaba la velocidad; se aproxima con la de 2.ª-5.ª).
            if (g.Generation >= 1 && g.Generation <= 5) ApplyCrit(so, 2);
            else if (g.Generation == 6) ApplyCrit(so, 1);
            else if (g.Generation >= 7) ApplyCrit(so, 0);
        }

        // Lista de fichas de mecánica con su casilla «activa en estas reglas».
        private void DrawMechanics(RulesetData d)
        {
            EditorGUILayout.LabelField("Mecánicas especiales", EditorStyles.boldLabel);
            var all = ContentAssets.LoadAll<MechanicData>();
            var active = new HashSet<string>((d.MechanicIds ?? new string[0]).Where(x => !string.IsNullOrWhiteSpace(x)),
                System.StringComparer.OrdinalIgnoreCase);
            if (all.Count == 0)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("No hay fichas de mecánica.", EditorStyles.miniLabel);
                if (GUILayout.Button("💎 Crear la Megaevolución oficial", GUILayout.Width(220))) MechanicEditorWindow.CreateClassicSet();
                EditorGUILayout.EndHorizontal();
            }
            foreach (var m in all)
            {
                EditorGUILayout.BeginHorizontal();
                bool on = active.Contains(m.Id ?? "");
                bool now = EditorGUILayout.ToggleLeft($"{m.DisplayName}  ({Etiquetas.Enum(m.Kind.ToString())})", on);
                if (now != on && !string.IsNullOrWhiteSpace(m.Id)) { var id = m.Id; EditSelected(so => MechanicEditorWindow.SetActive(so, id, now)); }
                if (GUILayout.Button("Abrir", EditorStyles.miniButton, GUILayout.Width(50))) MechanicEditorWindow.OpenAt(m);
                EditorGUILayout.EndHorizontal();
            }
            foreach (var id in active)
                if (!all.Any(m => string.Equals(m.Id, id, System.StringComparison.OrdinalIgnoreCase)))
                    EditorGUILayout.HelpBox($"La mecánica '{id}' está activa pero no existe ninguna ficha con ese id: se ignora.", MessageType.Warning);
        }

        // (nombre, huir siempre, huir de entrenador, capturar de entrenador, captura ×, al PC, repartir exp,
        //  % repartir, dinero inicial, % dinero perdido, curar al perder)
        private static readonly (string name, bool fleeAlways, bool fleeTrainer, bool catchTrainer, float catchX, bool box,
            bool expShare, float sharePct, int money, float lostPct, bool heal)[] AdventurePresets =
        {
            ("Clásica", false, false, false, 1f, true, false, 50f, 3000, 50f, true),
            ("Fácil (huir siempre, captura ×2, Repartir Exp.)", true, false, false, 2f, true, true, 50f, 5000, 25f, true),
            ("Reto (captura ×0,5, pierdes todo el dinero)", false, false, false, 0.5f, false, false, 50f, 1000, 100f, true),
        };

        private static void ApplyAdventure(SerializedObject so, int preset)
        {
            var p = AdventurePresets[preset];
            so.FindProperty("fleeAlwaysWorks").boolValue = p.fleeAlways;
            so.FindProperty("canFleeTrainerBattles").boolValue = p.fleeTrainer;
            so.FindProperty("canCatchTrainerMonsters").boolValue = p.catchTrainer;
            so.FindProperty("catchRateMultiplier").floatValue = p.catchX;
            so.FindProperty("sendToBoxWhenFull").boolValue = p.box;
            so.FindProperty("expShareAll").boolValue = p.expShare;
            so.FindProperty("expShareOthersPercent").floatValue = p.sharePct;
            so.FindProperty("learnMovesOnLevelUp").boolValue = true;
            so.FindProperty("evolveAfterBattle").boolValue = true;
            so.FindProperty("friendshipPerLevelUp").intValue = 3;
            so.FindProperty("friendshipLostOnFaint").intValue = 1;
            so.FindProperty("startingMoney").intValue = p.money;
            so.FindProperty("moneyLostOnBlackoutPercent").floatValue = p.lostPct;
            so.FindProperty("healOnBlackout").boolValue = p.heal;
        }

        // (nombre, tabla "1 entre N" por etapa, multiplicador)
        private static readonly (string name, int[] table, float mult)[] CritPresets =
        {
            ("Moderno (7ª+)", new[] { 24, 8, 2, 1 }, 1.5f),
            ("6ª gen.", new[] { 16, 8, 2, 1 }, 1.5f),
            ("2ª-5ª gen.", new[] { 16, 8, 4, 3, 2 }, 2f),
            ("Sin críticos", new[] { 0 }, 1.5f),
        };

        private static void ApplyCrit(SerializedObject so, int preset)
        {
            var (_, table, mult) = CritPresets[preset];
            var arr = so.FindProperty("critDenominators");
            arr.arraySize = table.Length;
            for (int i = 0; i < table.Length; i++) arr.GetArrayElementAtIndex(i).intValue = table[i];
            so.FindProperty("critMultiplier").floatValue = mult;
        }

        private static void Apply(SerializedObject so, int preset)
        {
            var p = Presets[preset];
            so.FindProperty("maxPartySize").intValue = p.party;
            so.FindProperty("maxMovesPerMonster").intValue = p.moves;
            so.FindProperty("levelCap").intValue = p.cap;
            so.FindProperty("damageFormulaId").stringValue = "classic";
            so.FindProperty("maxIv").intValue = p.iv;
            so.FindProperty("maxEvPerStat").intValue = p.evStat;
            so.FindProperty("maxEvTotal").intValue = p.evTotal;
            so.FindProperty("usePp").boolValue = true;
            so.FindProperty("struggleMoveId").stringValue = "struggle";
            ApplyCrit(so, 0);
            ApplyAdventure(so, 0);
            so.FindProperty("physicalAttackStat").stringValue = "attack";
            so.FindProperty("physicalDefenseStat").stringValue = "defense";
            so.FindProperty("specialAttackStat").stringValue = "sp_attack";
            so.FindProperty("specialDefenseStat").stringValue = "sp_defense";
        }

        private static string CritText(RulesetData d)
        {
            var t = d.CritDenominators;
            if (t == null || t.Length == 0) return "tabla moderna.";
            var parts = new List<string>();
            for (int i = 0; i < t.Length; i++)
                parts.Add($"etapa {i}{(i == t.Length - 1 && i > 0 ? "+" : "")}: " +
                          (t[i] <= 0 ? "nunca" : t[i] == 1 ? "siempre" : $"1 de {t[i]} ({100f / t[i]:0.#}%)"));
            return string.Join(" · ", parts) + ".";
        }

        protected override void DrawPreview(RulesetData d)
        {
            EditorGUILayout.LabelField("Qué significa", EditorStyles.boldLabel);

            string genetics =
                d.MaxIv <= 0 && d.MaxEvPerStat <= 0 ? "Sin genética: dos monstruos de la misma especie y nivel tendrán EXACTAMENTE las mismas estadísticas." :
                d.MaxIv <= 0 ? "Sin IVs: todos nacen iguales, pero el entrenamiento (EVs) los diferencia." :
                d.MaxEvPerStat <= 0 ? "Sin EVs: los individuos difieren al nacer (IVs), pero entrenar no mejora sus estadísticas." :
                $"Cada individuo nace con IVs de 0 a {d.MaxIv} y puede entrenar hasta {d.MaxEvPerStat} EVs por estadística ({d.MaxEvTotal} en total).";

            int maxEvBonus = d.MaxEvPerStat / 4; // en la fórmula clásica, cada 4 EVs = +1 punto al Nv.100
            EditorGUILayout.HelpBox(
                $"• Equipo de hasta {d.MaxPartySize} monstruos, con {d.MaxMovesPerMonster} movimientos cada uno.\n" +
                $"• Nivel máximo: {d.LevelCap}.\n" +
                $"• {genetics}\n" +
                (d.MaxEvPerStat > 0 ? $"• Al Nv.100, el entrenamiento máximo suma hasta +{maxEvBonus} puntos a una estadística.\n" : "") +
                (d.MaxIv > 0 ? $"• Al Nv.100, la genética perfecta suma hasta +{d.MaxIv} puntos a una estadística.\n" : "") +
                (d.UsePp ? $"• Los movimientos gastan PP; sin PP se usa '{(string.IsNullOrEmpty(d.StruggleMoveId) ? "(nada: se pierde el turno)" : d.StruggleMoveId)}'.\n"
                         : "• Los movimientos NO gastan PP (usos ilimitados).\n") +
                $"• Fórmula de daño: '{d.DamageFormulaId}'.\n" +
                $"• Críticos: {CritText(d)} Un crítico multiplica el daño ×{d.CritMultiplier:0.##}.\n" +
                $"• Físico: {StatLabels.NameOf(d.PhysicalAttackStat)} contra {StatLabels.NameOf(d.PhysicalDefenseStat)}. " +
                $"Especial: {StatLabels.NameOf(d.SpecialAttackStat)} contra {StatLabels.NameOf(d.SpecialDefenseStat)}.",
                MessageType.Info);
            EditorTheme.Section("Aventura", Accent);
            EditorGUILayout.HelpBox(
                (d.FleeAlwaysWorks ? "• Huir de un salvaje funciona SIEMPRE.\n"
                                   : "• Huir de un salvaje: si eres igual o más rápido escapas; si no, depende de la velocidad y cada intento facilita el siguiente.\n") +
                (d.CanFleeTrainerBattles ? "• Se puede huir de los entrenadores.\n" : "• No se puede huir de los entrenadores.\n") +
                (d.CanCatchTrainerMonsters ? "• ¡Se pueden capturar los monstruos de los entrenadores!\n" : "• Los monstruos de los entrenadores no se pueden capturar.\n") +
                $"• Captura: fórmula clásica con sacudidas{(System.Math.Abs(d.CatchRateMultiplier - 1f) > 0.001f ? $", ×{d.CatchRateMultiplier:0.##} más fácil" : "")}. " +
                (d.SendToBoxWhenFull ? "Con el equipo lleno, el capturado va al PC.\n" : "Con el equipo lleno, el capturado se libera.\n") +
                (d.ExpShareAll ? $"• Repartir Experiencia: los que no lucharon ganan el {d.ExpShareOthersPercent:0}% de la experiencia.\n"
                               : "• La experiencia se reparte solo entre los que lucharon.\n") +
                $"• Al subir de nivel: {(d.LearnMovesOnLevelUp ? "aprende movimientos" : "NO aprende movimientos")}, +{d.FriendshipPerLevelUp} de amistad" +
                $"{(d.EvolveAfterBattle ? "; al terminar el combate puede evolucionar (el jugador puede cancelarlo)" : "")}.\n" +
                $"• Empiezas con {d.StartingMoney} ₽. Al perder un combate pierdes el {d.MoneyLostOnBlackoutPercent:0}% del dinero" +
                (d.HealOnBlackout ? " y vuelves curado al Centro." : "."),
                MessageType.Info);

            EditorTheme.Section("Generación y mecánicas", Accent);
            var mechNames = ContentAssets.LoadAll<MechanicData>()
                .Where(m => (d.MechanicIds ?? new string[0]).Any(id => string.Equals(id, m.Id, System.StringComparison.OrdinalIgnoreCase)))
                .Select(m => m.DisplayName).ToList();
            EditorGUILayout.HelpBox(
                (d.Generation > 0 ? $"• Reglas de referencia: {d.Generation}.ª generación.\n" : "• Reglas de generación personalizadas.\n") +
                (d.CategoryByType ? $"• Físico o Especial lo decide el TIPO (especiales: {string.Join(", ", d.SpecialTypes ?? new string[0])}).\n"
                                  : "• Físico o Especial lo decide cada movimiento.\n") +
                (d.SingleSpecialStat ? "• Especial único: lo que sube o baja el Ataque Especial también mueve la Defensa Especial.\n" : "") +
                $"• Habilidades: {(d.Abilities ? "sí" : "NO")} · Objetos equipados: {(d.HeldItems ? "sí" : "NO")} · " +
                $"Naturalezas: {(d.Natures ? "sí" : "NO")} · Géneros: {(d.Genders ? "sí" : "NO")}.\n" +
                (mechNames.Count > 0 ? $"• Mecánicas especiales activas: {string.Join(", ", mechNames)}." : "• Sin mecánicas especiales."),
                MessageType.Info);

            if (d.CritDenominators == null || d.CritDenominators.Length == 0)
                EditorGUILayout.HelpBox("La tabla de críticos está vacía: se usará la moderna (24, 8, 2, 1).", MessageType.Warning);

            if (d.MaxEvTotal > 0 && d.MaxEvTotal < d.MaxEvPerStat)
                EditorGUILayout.HelpBox("El tope TOTAL de EVs es menor que el tope por estadística: ninguna estadística podrá llegar a su máximo.", MessageType.Warning);

            var all = ContentAssets.LoadAll<RulesetData>();
            if (all.Count > 1)
                EditorGUILayout.HelpBox($"Tienes {all.Count} reglas en el proyecto. El juego usa solo la primera que encuentre en la carpeta Rulesets; deja una sola para evitar sorpresas.", MessageType.Warning);
        }
    }
}
