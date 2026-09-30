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
    /// Editor de NIVELES DE IA (menú CTEditor → Personajes → Niveles de IA). Los 7 niveles clásicos
    /// (Novato, Aficionado, Veterano, Élite, Campeón, Maestro, Injusto) listos para usar o ajustar, en 4 bloques:
    /// Conocimiento, Decisión, Gestión y Equipo. Cada entrenador elige su
    /// nivel; lo que cambies aquí vale para TODOS los entrenadores de ese nivel.
    /// </summary>
    public sealed class AiLevelEditorWindow : ContentEditorWindow<AiLevelData>
    {
        [MenuItem(EditorMenus.Characters + "Niveles de IA", false, EditorMenus.CharactersOrder + 2)]
        public static void Open() => OpenWindow<AiLevelEditorWindow>("Niveles de IA");

        protected override string Category => ContentFolders.AiLevels;
        protected override string Noun => "nivel de IA";
        protected override string Title => "Niveles de IA";
        protected override string Intro =>
            "Cómo piensa cada nivel de entrenador, del 1 (Novato) al 7 (Injusto), en 4 bloques: 🧠 qué sabe de ti, 🎯 cómo elige, " +
            "🛡️ objetos y cambios, y 🎒 su equipo. Cada entrenador elige su nivel y así repartes la dificultad por el mapa.";

        public override string[] GuideSteps => new[]
        {
            "Pulsa «Crear los 7 niveles clásicos» (si no, el juego usa esos mismos valores por defecto).",
            "Ajusta cada bloque: qué sabe de ti (nada, lo visto, memoria, todo), cómo elige (al azar … predictor), cómo se cura y cambia, y su equipo (MT, tutor, huevo, objetos, entrenamiento).",
            "En cada entrenador elige su nivel (1-7). Puedes darle su propia mochila o dejar la del nivel.",
        };

        protected override Color? RowMark(AiLevelData d) => LevelColor(d.Level);

        /// <summary>Abre el editor con esa ficha seleccionada (desde el editor de entrenadores).</summary>
        public static void OpenAt(AiLevelData data)
        {
            var w = OpenWindow<AiLevelEditorWindow>("Niveles de IA");
            if (data != null) w.FocusOn(data);
        }

        /// <summary>
        /// Crea (o devuelve, si ya existe) la IA PERSONALIZADA de un entrenador: una copia de su nivel con id
        /// ia_&lt;entrenador&gt;, marcada como personalizada para que no sustituya al nivel.
        /// </summary>
        public static AiLevelData CreateCustomFor(string trainerId, string trainerName, int level)
        {
            string id = "ia_" + trainerId;
            var existing = ContentAssets.FindById<AiLevelData>(id);
            if (existing != null) return existing;
            var src = ContentAssets.LoadAll<AiLevelData>().FirstOrDefault(a => !a.Custom && a.Level == level);
            AiLevelData copy;
            if (src != null)
            {
                string path = AssetDatabase.GenerateUniqueAssetPath($"{ContentFolders.PathOf(ContentFolders.AiLevels)}/{id}.asset");
                AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(src), path);
                ContentAssets.ClearCache();
                copy = AssetDatabase.LoadAssetAtPath<AiLevelData>(path);
            }
            else copy = ContentAssets.Create<AiLevelData>(ContentFolders.AiLevels, id, id, so => Fill(so, level));
            ContentAssets.Edit(copy, so =>
            {
                so.FindProperty("id").stringValue = id;
                so.FindProperty("displayName").stringValue = "IA de " + (string.IsNullOrWhiteSpace(trainerName) ? trainerId : trainerName);
                so.FindProperty("custom").boolValue = true;
                so.FindProperty("level").intValue = level;
            });
            AssetDatabase.SaveAssets();
            return copy;
        }

        protected override int CompareItems(AiLevelData a, AiLevelData b) => a.Level.CompareTo(b.Level);

        /// <summary>Color de cada nivel (verde fácil → rojo difícil). Lo usan también los entrenadores.</summary>
        public static Color LevelColor(int level)
        {
            switch (Mathf.Clamp(level, 1, AiProfile.MaxLevel))
            {
                case 1: return new Color(0.45f, 0.8f, 0.45f);
                case 2: return new Color(0.55f, 0.75f, 0.95f);
                case 3: return new Color(0.95f, 0.8f, 0.35f);
                case 4: return new Color(0.95f, 0.55f, 0.3f);
                case 5: return new Color(0.85f, 0.3f, 0.35f);
                case 6: return new Color(0.65f, 0.35f, 0.85f);
                default: return new Color(0.25f, 0.2f, 0.3f);
            }
        }

        public static string LevelLabel(int level) => $"{level} · {AiProfile.ClassicName(level)}";

        // ---------------- Set clásico ----------------

        public static int CreateClassicSet()
        {
            int created = 0;
            for (int lvl = 1; lvl <= AiProfile.MaxLevel; lvl++)
            {
                int l = lvl;
                var p = AiProfile.Classic(l);
                if (ContentAssets.CreateIfMissing<AiLevelData>(ContentFolders.AiLevels, "nivel_" + l, p.DisplayName, so => Fill(so, l))) created++;
            }
            AssetDatabase.SaveAssets();
            return created;
        }

        public static void Fill(SerializedObject so, int level)
        {
            var p = AiProfile.Classic(level);
            so.FindProperty("displayName").stringValue = p.DisplayName;
            so.FindProperty("level").intValue = p.Level;
            so.FindProperty("description").stringValue = p.Description;
            so.FindProperty("brain").enumValueIndex = (int)p.Brain;
            so.FindProperty("mistakePercent").intValue = p.MistakePercent;
            so.FindProperty("itemUsePercent").intValue = p.ItemUsePercent;
            so.FindProperty("heal").enumValueIndex = (int)p.Heal;
            so.FindProperty("healBelowPercent").intValue = p.HealBelowPercent;
            so.FindProperty("canSwitch").boolValue = p.CanSwitch;
            so.FindProperty("moveset").enumValueIndex = (int)p.Moveset;
            so.FindProperty("synergies").boolValue = p.Synergies;
            so.FindProperty("useMachineMoves").boolValue = p.UseMachineMoves;
            so.FindProperty("useTutorMoves").boolValue = p.UseTutorMoves;
            so.FindProperty("useEggMoves").boolValue = p.UseEggMoves;
            so.FindProperty("autoHeldItems").boolValue = p.AutoHeldItems;
            so.FindProperty("heldItems").enumValueIndex = (int)p.HeldItems;
            so.FindProperty("heldItemsMigrated").boolValue = true;
            so.FindProperty("knowledge").enumValueIndex = (int)p.Knowledge;
            so.FindProperty("competitiveTraining").boolValue = p.CompetitiveTraining;
            so.FindProperty("predictPercent").intValue = p.Brain == MoveBrain.Predictor ? p.PredictPercent : 60;
            so.FindProperty("megaTiming").enumValueIndex = (int)AiLevelData.MegaTimingChoice.ByLevel;
            var bag = so.FindProperty("defaultBag");
            bag.arraySize = p.DefaultBag.Count;
            for (int i = 0; i < p.DefaultBag.Count; i++)
            {
                bag.GetArrayElementAtIndex(i).FindPropertyRelative("itemId").stringValue = p.DefaultBag[i].itemId;
                bag.GetArrayElementAtIndex(i).FindPropertyRelative("quantity").intValue = p.DefaultBag[i].quantity;
            }
        }

        protected override IReadOnlyList<(string id, string name, string group)> Templates
            => Enumerable.Range(1, AiProfile.MaxLevel).Select(l => ("nivel_" + l, AiProfile.ClassicName(l), "Niveles clásicos")).ToList();

        protected override void ApplyTemplate(SerializedObject so, string id)
        {
            if (int.TryParse(id.Replace("nivel_", ""), out int l)) Fill(so, l);
        }

        protected override void OnCreated(SerializedObject so) => Fill(so, 2);

        protected override void DrawBulkPresets()
        {
            if (GUILayout.Button("Crear los 7 niveles clásicos")) FinishBulk(CreateClassicSet(), "niveles de IA");
        }

        // ---------------- Vista previa ----------------

        protected override void DrawPreview(AiLevelData d)
        {
            var c = LevelColor(d.Level);
            EditorTheme.Section(d.Custom ? $"IA personalizada: {d.DisplayName} (base nivel {d.Level})" : $"Nivel {d.Level}: {d.DisplayName}", c);
            EditorTheme.BeginCard(c);
            if (!string.IsNullOrWhiteSpace(d.Description)) EditorTheme.Paragraph(d.Description, false);
            string know = d.Knowledge == AiKnowledge.None ? "solo ve tu Pokémon y sus tipos (supone ataques de su tipo)"
                : d.Knowledge == AiKnowledge.Battle ? "recuerda lo que ve EN ESTE combate: tus movimientos y cuánto dañan"
                : d.Knowledge == AiKnowledge.Memory ? "te RECUERDA entre combates: tus movimientos y tus IVs/EVs estimados por el daño"
                : "lo sabe TODO desde el principio: tus movimientos, IVs, EVs y naturaleza (injusto)";
            EditorTheme.Paragraph($"🧠 Conocimiento: {know}.", false);
            string brain = d.Brain == MoveBrain.Random ? "movimientos al azar"
                : d.Brain == MoveBrain.Aggressive ? "el movimiento más eficaz según los tipos"
                : d.Brain == MoveBrain.Expert ? "cálculo de daño real: remata, pone estados y mejoras con cabeza"
                : $"predictor: calcula el daño real y un {d.PredictPercent} % de los turnos juega según lo que cree que harás " +
                  "(cambia al que resiste tu golpe, castiga tus cambios con el ataque que mejor le da a tu reserva)";
            EditorTheme.Paragraph($"🎯 Decisión: {brain}" + (d.MistakePercent > 0 ? $" (se equivoca un {d.MistakePercent} % de las veces)." : ", sin fallos."), false);
            string heal = d.Heal == HealStyle.Never ? "nunca se cura"
                : d.Heal == HealStyle.Simple ? $"se cura en cuanto baja del {d.HealBelowPercent} %"
                : $"se cura (desde el {d.HealBelowPercent} %) solo si le sirve: si el rival lo tumba igual o le quita más de lo que cura, ataca";
            EditorTheme.Paragraph($"🛡️ Gestión: se acuerda de sus objetos un {d.ItemUsePercent} % de las veces; {heal}. " +
                                  (d.CanSwitch ? "Cambia de monstruo si pierde el duelo." : "Nunca cambia de monstruo."), false);
            var sources = new List<string> { "por nivel" };
            if (d.UseMachineMoves) sources.Add("MT");
            if (d.UseTutorMoves) sources.Add("tutor");
            if (d.UseEggMoves) sources.Add("huevo");
            string held = d.HeldItems == HeldItemStyle.None ? "sin objetos equipados automáticos"
                : d.HeldItems == HeldItemStyle.Basic ? "objetos básicos (Restos, bayas, de tipo)" : "objetos DE COMPETICIÓN (Elección, Vidasfera, Banda Focus...)";
            EditorTheme.Paragraph($"🎒 Equipo: movimientos {Etiquetas.Enum(d.Moveset.ToString()).ToLowerInvariant()} ({string.Join(", ", sources)})" +
                                  (d.Synergies ? " con sinergias" : "") + $"; {held}" +
                                  (d.CompetitiveTraining ? "; entrenamiento de competición (IVs 31, 252 EVs, naturaleza)." : "."), false);
            EditorTheme.EndCard();

            // Cuántos entrenadores usan este nivel.
            var trainers = ContentAssets.LoadAll<TrainerData>().Where(t => TrainerEditorWindow.AiDataFor(t) == d).ToList();
            EditorTheme.Paragraph(trainers.Count == 0 ? (d.Custom ? "Ningún entrenador usa esta IA todavía (elígela en «IA personalizada» del entrenador)." : "Ningún entrenador usa este nivel todavía.")
                : $"Lo usan {trainers.Count} entrenador(es): " + string.Join(", ", trainers.Take(12).Select(t => t.DisplayName)) + (trainers.Count > 12 ? "…" : ""));
        }
    }
}
