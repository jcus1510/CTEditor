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
    /// Editor de NIVELES DE IA (menú CTEditor → Personajes → Niveles de IA). Los 5 niveles clásicos
    /// (Novato, Aficionado, Veterano, Élite, Campeón) listos para usar o ajustar. Cada entrenador elige su
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
            "Cómo piensa cada nivel de entrenador, del 1 (Novato) al 5 (Campeón). Cada entrenador elige su nivel y así " +
            "puedes repartir la dificultad por el mapa sin configurar la IA uno a uno.";

        public override string[] GuideSteps => new[]
        {
            "Pulsa «Crear los 5 niveles clásicos» (si no, el juego usa esos mismos valores por defecto).",
            "Ajusta cada nivel: cuánto se equivoca, si se cura con cabeza, si cambia de monstruo y a qué movimientos llega (MT, tutor, huevo).",
            "En cada entrenador elige su nivel (1-5). Puedes darle su propia mochila o dejar la del nivel.",
        };

        protected override Color? RowMark(AiLevelData d) => LevelColor(d.Level);

        protected override int CompareItems(AiLevelData a, AiLevelData b) => a.Level.CompareTo(b.Level);

        /// <summary>Color de cada nivel (verde fácil → rojo difícil). Lo usan también los entrenadores.</summary>
        public static Color LevelColor(int level)
        {
            switch (Mathf.Clamp(level, 1, 5))
            {
                case 1: return new Color(0.45f, 0.8f, 0.45f);
                case 2: return new Color(0.55f, 0.75f, 0.95f);
                case 3: return new Color(0.95f, 0.8f, 0.35f);
                case 4: return new Color(0.95f, 0.55f, 0.3f);
                default: return new Color(0.85f, 0.3f, 0.35f);
            }
        }

        public static string LevelLabel(int level) => $"{level} · {AiProfile.ClassicName(level)}";

        // ---------------- Set clásico ----------------

        public static int CreateClassicSet()
        {
            int created = 0;
            for (int lvl = 1; lvl <= 5; lvl++)
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
            var bag = so.FindProperty("defaultBag");
            bag.arraySize = p.DefaultBag.Count;
            for (int i = 0; i < p.DefaultBag.Count; i++)
            {
                bag.GetArrayElementAtIndex(i).FindPropertyRelative("itemId").stringValue = p.DefaultBag[i].itemId;
                bag.GetArrayElementAtIndex(i).FindPropertyRelative("quantity").intValue = p.DefaultBag[i].quantity;
            }
        }

        protected override IReadOnlyList<(string id, string name, string group)> Templates
            => Enumerable.Range(1, 5).Select(l => ("nivel_" + l, AiProfile.ClassicName(l), "Niveles clásicos")).ToList();

        protected override void ApplyTemplate(SerializedObject so, string id)
        {
            if (int.TryParse(id.Replace("nivel_", ""), out int l)) Fill(so, l);
        }

        protected override void OnCreated(SerializedObject so) => Fill(so, 2);

        protected override void DrawBulkPresets()
        {
            if (GUILayout.Button("Crear los 5 niveles clásicos")) FinishBulk(CreateClassicSet(), "niveles de IA");
        }

        // ---------------- Vista previa ----------------

        protected override void DrawPreview(AiLevelData d)
        {
            var c = LevelColor(d.Level);
            EditorTheme.Section($"Nivel {d.Level}: {d.DisplayName}", c);
            EditorTheme.BeginCard(c);
            if (!string.IsNullOrWhiteSpace(d.Description)) EditorTheme.Paragraph(d.Description, false);
            string brain = d.Brain == MoveBrain.Random ? "movimientos al azar"
                : d.Brain == MoveBrain.Aggressive ? "el movimiento más eficaz según los tipos"
                : "cálculo de daño real: remata, pone estados y mejoras con cabeza";
            EditorTheme.Paragraph($"• Elige: {brain}" + (d.MistakePercent > 0 ? $" (se equivoca un {d.MistakePercent} % de las veces)." : ", sin fallos."), false);
            string heal = d.Heal == HealStyle.Never ? "nunca se cura"
                : d.Heal == HealStyle.Simple ? $"se cura en cuanto baja del {d.HealBelowPercent} %"
                : $"se cura (desde el {d.HealBelowPercent} %) solo si le sirve: si el rival lo tumba igual o le quita más de lo que cura, ataca";
            EditorTheme.Paragraph($"• Objetos: se acuerda un {d.ItemUsePercent} % de las veces; {heal}.", false);
            EditorTheme.Paragraph("• " + (d.CanSwitch ? "Cambia de monstruo si pierde claramente el duelo." : "Nunca cambia de monstruo."), false);
            var sources = new List<string> { "por nivel" };
            if (d.UseMachineMoves) sources.Add("MT");
            if (d.UseTutorMoves) sources.Add("tutor");
            if (d.UseEggMoves) sources.Add("huevo");
            EditorTheme.Paragraph($"• Movimientos automáticos: {Etiquetas.Enum(d.Moveset.ToString())} ({string.Join(", ", sources)})" +
                                  (d.Synergies ? ", buscando sinergias." : "."), false);
            if (d.AutoHeldItems) EditorTheme.Paragraph("• Pone objetos equipados a los que no llevan (Restos, bayas, objetos de tipo).", false);
            EditorTheme.EndCard();

            // Cuántos entrenadores usan este nivel.
            var trainers = ContentAssets.LoadAll<TrainerData>().Where(t => t.EffectiveAiLevel == d.Level).ToList();
            EditorTheme.Paragraph(trainers.Count == 0 ? "Ningún entrenador usa este nivel todavía."
                : $"Lo usan {trainers.Count} entrenador(es): " + string.Join(", ", trainers.Take(12).Select(t => t.DisplayName)) + (trainers.Count > 12 ? "…" : ""));
        }
    }
}
