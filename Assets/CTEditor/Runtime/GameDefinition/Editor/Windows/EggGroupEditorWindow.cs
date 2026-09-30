using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CTEditor.GameDefinition.Infrastructure.Catalog;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// Editor de GRUPOS HUEVO (menú CTEditor → Criaturas → Grupos huevo): los 15 clásicos, su color y la
    /// lista de especies que pertenecen a cada uno (con botón para abrir cada especie). Se asignan en la
    /// ficha de la especie con un desplegable. Base para la crianza futura.
    /// </summary>
    public sealed class EggGroupEditorWindow : ContentEditorWindow<EggGroupData>
    {
        [MenuItem(EditorMenus.Creatures + "Grupos huevo", false, EditorMenus.CreaturesOrder + 6)]
        public static void Open() => OpenWindow<EggGroupEditorWindow>("Grupos huevo");

        /// <summary>Abre el editor con un grupo ya seleccionado.</summary>
        public static void OpenFor(EggGroupData group) => OpenWindow<EggGroupEditorWindow>("Grupos huevo").FocusOn(group);

        protected override string Category => ContentFolders.EggGroups;
        protected override string Noun => "grupo huevo";
        protected override string Title => "Grupos huevo";
        protected override string Intro =>
            "Dos especies pueden criar juntas si comparten un grupo huevo. Cada especie tiene hasta 2 (se eligen en su ficha). " +
            "«Desconocido» = no puede criar. Aquí ves qué especies hay en cada grupo.";

        public override string[] GuideSteps => new[]
        {
            "Pulsa «Crear los 15 grupos clásicos» (o importa el pack: las especies ya traen los suyos).",
            "En la ficha de cada especie, elige sus grupos con los desplegables de «Habilidades y crianza».",
            "Aquí ves las especies de cada grupo; pulsa una para abrirla.",
        };

        protected override Color? RowMark(EggGroupData d) => d.Color;

        // (id, nombre, color)
        public static readonly (string id, string name, string hex, bool noBreed)[] Library =
        {
            ("monster", "Monstruo", "97724C", false), ("water1", "Agua 1", "6BD1F9", false), ("bug", "Bicho", "AAC22A", false),
            ("flying", "Volador", "90AFF1", false), ("ground", "Campo", "E5BA65", false), ("fairy", "Hada", "FF9EB9", false),
            ("plant", "Planta", "82D25A", false), ("humanshape", "Humanoide", "47B7AE", false), ("water3", "Agua 3", "2271B4", false),
            ("mineral", "Mineral", "979067", false), ("indeterminate", "Amorfo", "9F82CC", false), ("water2", "Agua 2", "4B94ED", false),
            ("ditto", "Ditto", "B6AAD5", false), ("dragon", "Dragón", "5E57BF", false), ("no_eggs", "Desconocido", "A3A3A3", true),
        };

        public static int CreateClassicSet()
        {
            int created = 0;
            foreach (var g in Library)
            {
                var gg = g;
                if (ContentAssets.CreateIfMissing<EggGroupData>(ContentFolders.EggGroups, g.id, g.name, so => Fill(so, gg))) created++;
            }
            AssetDatabase.SaveAssets();
            return created;
        }

        private static void Fill(SerializedObject so, (string id, string name, string hex, bool noBreed) g)
        {
            so.FindProperty("displayName").stringValue = g.name;
            so.FindProperty("color").colorValue = TypeChartTools.FromHex(g.hex);
            so.FindProperty("cannotBreed").boolValue = g.noBreed;
            so.FindProperty("description").stringValue = g.noBreed
                ? "No puede criar (legendarios, bebés y especies especiales)."
                : $"Las especies del grupo {g.name} pueden criar entre sí.";
        }

        protected override IReadOnlyList<(string id, string name, string group)> Templates
            => Library.Select(g => (g.id, g.name, "Grupos clásicos")).ToList();

        protected override void ApplyTemplate(SerializedObject so, string id)
        {
            foreach (var g in Library) if (g.id == id) Fill(so, g);
        }

        protected override void DrawBulkPresets()
        {
            if (GUILayout.Button("Crear los 15 grupos clásicos")) FinishBulk(CreateClassicSet(), "grupos huevo");
        }

        private Vector2 _membersScroll;

        protected override void DrawPreview(EggGroupData d)
        {
            var members = ContentAssets.LoadAll<SpeciesData>().Where(s => s.EggGroups != null && s.EggGroups.Contains(d))
                .OrderBy(s => s.DexNumber > 0 ? s.DexNumber : int.MaxValue).ToList();
            EditorTheme.Section($"{d.DisplayName}: {members.Count} especie(s)", d.Color);
            if (d.CannotBreed) EditorTheme.Tip("Las especies de este grupo no pueden criar.", EditorTheme.Warn, "⚠");
            if (members.Count == 0)
            {
                EditorTheme.Paragraph("Ninguna especie tiene este grupo. Asígnalo en la ficha de la especie (desplegable «Grupos huevo»).");
                return;
            }
            _membersScroll = EditorGUILayout.BeginScrollView(_membersScroll, GUILayout.MaxHeight(420));
            const int perRow = 3;
            for (int i = 0; i < members.Count; i += perRow)
            {
                EditorGUILayout.BeginHorizontal();
                for (int j = i; j < Mathf.Min(i + perRow, members.Count); j++)
                {
                    var s = members[j];
                    var other = (s.EggGroups ?? new EggGroupData[0]).Where(g => g != null && g != d).Select(g => g.DisplayName).ToList();
                    string label = (s.DexNumber > 0 ? $"#{s.DexNumber:000} " : "") + s.DisplayName + (other.Count > 0 ? $"  (+{other[0]})" : "");
                    if (GUILayout.Button(new GUIContent(label, "Abrir su ficha"), EditorStyles.miniButton, GUILayout.Width(190)))
                        SpeciesEditorWindow.OpenAndSelect(s);
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
