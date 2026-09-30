using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CTEditor.GameDefinition.Infrastructure.Catalog;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// Editor de EQUIPOS PREARMADOS (menú CTEditor → Personajes → Equipos prearmados): equipos listos para empezar
    /// una partida o probar combates (con su dinero y su mochila). La escena de pruebas deja elegir uno,
    /// o empezar desde cero con un solo monstruo inicial.
    /// </summary>
    public sealed class TeamPresetEditorWindow : ContentEditorWindow<TeamPresetData>
    {
        [MenuItem(EditorMenus.Characters + "Equipos prearmados", false, EditorMenus.CharactersOrder + 3)]
        public static void Open() => OpenWindow<TeamPresetEditorWindow>("Equipos prearmados");

        protected override string Category => ContentFolders.Teams;
        protected override string Noun => "equipo";
        protected override string Title => "Equipos prearmados";
        protected override string Intro =>
            "Equipos listos para empezar a jugar o para probar combates: miembros, dinero y mochila. Se arman con la misma receta que los entrenadores.";

        public override string[] GuideSteps => new[]
        {
            "Pulsa «Crear los equipos de ejemplo» (un inicial, uno de ruta y uno de nivel 50 para probarlo todo).",
            "En «Equipo» elige especie y nivel de cada miembro; los movimientos son opcionales.",
            "En «Mochila» pon los objetos con los que empieza (pociones, bolas...).",
            "En la escena de pruebas, elige este equipo en «Partida del jugador».",
        };

        protected override string RowTooltip(TeamPresetData d)
            => string.IsNullOrWhiteSpace(d.Description) ? ContentAssets.Label(d) : $"{ContentAssets.Label(d)} — {d.Description}";

        public static readonly (string id, string name, string desc, int money, (string, int)[] team, (string, int)[] items)[] Library =
        {
            ("inicial_fuego", "Inicial de fuego (desde el principio)", "Un solo monstruo de nivel 5, como al empezar la aventura.", -1,
                new[] { ("charmander", 5) },
                new[] { ("potion", 5), ("poke_ball", 10) }),
            ("equipo_ruta", "Equipo de ruta (Nv. 12-15)", "Tres monstruos a mitad de la primera ruta: para probar entrenadores novatos y capturas.", 3000,
                new[] { ("bulbasaur", 14), ("pidgeotto", 15), ("pikachu", 12) },
                new[] { ("potion", 10), ("super_potion", 3), ("antidote", 3), ("paralyze_heal", 3), ("poke_ball", 15), ("great_ball", 5) }),
            ("equipo_prueba_50", "Equipo de prueba (Nv. 50)", "Seis monstruos a nivel 50 con IVs perfectos y una mochila completa: para probarlo todo.", 50000,
                new[] { ("venusaur", 50), ("charizard", 50), ("blastoise", 50), ("pikachu", 50), ("snorlax", 50), ("gengar", 50) },
                new[] { ("hyper_potion", 10), ("full_restore", 5), ("revive", 5), ("full_heal", 5), ("ether", 5),
                        ("poke_ball", 20), ("great_ball", 10), ("ultra_ball", 10), ("master_ball", 3), ("x_attack", 3) }),
        };

        public static int CreateClassicSet()
        {
            int created = 0;
            foreach (var t in Library)
            {
                string id = t.id;
                if (ContentAssets.CreateIfMissing<TeamPresetData>(ContentFolders.Teams, t.id, t.name, so => Fill(so, id))) created++;
            }
            AssetDatabase.SaveAssets();
            return created;
        }

        public static void Fill(SerializedObject so, string presetId)
        {
            foreach (var t in Library)
            {
                if (t.id != presetId) continue;
                so.FindProperty("displayName").stringValue = t.name;
                so.FindProperty("description").stringValue = t.desc;
                so.FindProperty("money").intValue = t.money;
                var team = so.FindProperty("members");
                TeamPreview.FillTeam(team, t.team);
                if (t.id == "equipo_prueba_50")
                    for (int i = 0; i < team.arraySize; i++) team.GetArrayElementAtIndex(i).FindPropertyRelative("fixedIvs").intValue = 31;

                // Mochila: solo los objetos que existan.
                var items = t.items.Where(x => ContentAssets.FindById<ItemData>(x.Item1) != null).ToList();
                var bag = so.FindProperty("items");
                bag.arraySize = items.Count;
                for (int i = 0; i < items.Count; i++)
                {
                    bag.GetArrayElementAtIndex(i).FindPropertyRelative("itemId").stringValue = items[i].Item1;
                    bag.GetArrayElementAtIndex(i).FindPropertyRelative("quantity").intValue = items[i].Item2;
                }
            }
        }

        protected override IReadOnlyList<(string id, string name, string group)> Templates
            => Library.Select(p => (p.id, p.name, "Equipos de ejemplo")).ToList();

        protected override void ApplyTemplate(SerializedObject so, string id)
        {
            Fill(so, id);
            var t = Templates.FirstOrDefault(x => x.id == id); if (t.id != null) so.FindProperty("displayName").stringValue = t.name;
        }

        protected override void DrawBulkPresets()
        {
            if (GUILayout.Button($"Crear los {Library.Length} equipos de ejemplo")) FinishBulk(CreateClassicSet(), "equipos");
        }

        protected override void DrawPresets(TeamPresetData d)
        {
            EditorGUILayout.LabelField("Plantillas (rellenan todo; el id se mantiene)", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            foreach (var t in Library)
            {
                string id = t.id;
                if (GUILayout.Button(new GUIContent(t.name, t.desc), EditorStyles.miniButton)) EditSelected(so => Fill(so, id));
            }
            EditorGUILayout.EndHorizontal();
        }

        protected override void DrawPreview(TeamPresetData d)
        {
            EditorTheme.Section("Así empezará la partida", Accent);
            if (!string.IsNullOrWhiteSpace(d.Description)) EditorTheme.Paragraph(d.Description, small: false);
            TeamPreview.Draw(d.Members, Accent);
            var bag = (d.Items ?? new BagEntryData[0]).Where(i => i != null && !string.IsNullOrWhiteSpace(i.itemId)).ToList();
            EditorTheme.Tip((d.Money >= 0 ? $"Dinero: {d.Money} ₽" : "Dinero: el de las reglas del juego") + "   ·   Mochila: " +
                (bag.Count == 0 ? "vacía" : string.Join(", ", bag.Select(i => $"{NameOfItem(i.itemId)} ×{i.quantity}"))), EditorTheme.Items, "🎒");
        }

        private static string NameOfItem(string id)
        {
            var it = ContentAssets.FindById<ItemData>(id);
            return it != null ? it.DisplayName : $"⚠ {id}";
        }
    }
}
