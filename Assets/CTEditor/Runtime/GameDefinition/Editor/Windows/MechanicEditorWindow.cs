using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CTEditor.GameDefinition.Domain.Rules.Mechanics;
using CTEditor.GameDefinition.Infrastructure.Catalog;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// Editor de MECÁNICAS ESPECIALES (menú CTEditor → Combate → Mecánicas especiales). Cada ficha configura una
    /// mecánica que el motor sabe ejecutar (hoy: Megaevolución). Puedes tener varias del mismo tipo y activar en las
    /// REGLAS las que quieras, incluso varias a la vez. Una ficha que no está activa en las reglas no hace nada.
    /// </summary>
    public sealed class MechanicEditorWindow : ContentEditorWindow<MechanicData>
    {
        [MenuItem(EditorMenus.Battle + "Mecánicas especiales", false, EditorMenus.BattleOrder + 9)]
        public static void Open() => OpenWindow<MechanicEditorWindow>("Mecánicas");

        /// <summary>Abre el editor con una mecánica ya seleccionada.</summary>
        public static void OpenAt(MechanicData data) => OpenWindow<MechanicEditorWindow>("Mecánicas").FocusOn(data);

        protected override string Category => ContentFolders.Mechanics;
        protected override string Noun => "mecánica";
        protected override string Title => "Mecánicas especiales";
        protected override string Intro =>
            "Mecánicas que cambian el combate (Megaevolución...). Aquí decides CÓMO funcionan; en las Reglas del juego " +
            "eliges CUÁLES están activas (puedes combinar varias).";

        public override string[] GuideSteps => new[]
        {
            "Pulsa «Crear la Megaevolución y los movimientos Z oficiales» (o crea una ficha nueva y elige su tipo).",
            "Ajusta cuántas megas hay por combate, qué objeto clave necesita el jugador y si vuelve a su forma al retirarse.",
            "En las Reglas del juego, añádela a «Mecánicas especiales activas». Sin eso no hace nada.",
            "Las megapiedras son objetos; cada entrenador decide en su ficha si puede megaevolucionar.",
        };

        // (id, nombre, tipo, máximo por combate, objeto clave, vuelve al retirarse, descripción)
        private static readonly (string id, string name, MechanicKind kind, int max, string key, bool revert, string desc)[] Library =
        {
            ("mega_evolution", "Megaevolución", MechanicKind.MegaEvolution, 1, "mega_ring", false,
                "La oficial (6.ª-7.ª gen.): una por combate y lado, el jugador necesita la Megapulsera y se queda megaevolucionado hasta el final."),
            ("mega_evolution_free", "Megaevolución sin límite", MechanicKind.MegaEvolution, 0, "", false,
                "Variante: todas las que quieras por combate y sin objeto clave."),
            ("z_moves", "Movimientos Z", MechanicKind.ZMove, 1, "z_ring", false,
                "Los oficiales (7.ª gen.): con su cristal Z, un movimiento se convierte en movimiento Z una vez por combate; el jugador necesita la Pulsera Z."),
            ("z_moves_free", "Movimientos Z sin límite", MechanicKind.ZMove, 0, "", false,
                "Variante: todos los que quieras por combate y sin objeto clave."),
        };

        /// <summary>Crea la Megaevolución y los movimientos Z oficiales si faltan (activarlos lo deciden las Reglas). Público:
        /// lo usan el Centro de Contenido y el asistente de generación.</summary>
        public static int CreateClassicSet()
        {
            int n = 0;
            foreach (var g in Library.Where(x => x.id == "mega_evolution" || x.id == "z_moves"))
            {
                var t = g;
                if (ContentAssets.CreateIfMissing<MechanicData>(ContentFolders.Mechanics, t.id, t.name, so => Fill(so, t))) n++;
            }
            AssetDatabase.SaveAssets();
            return n;
        }

        private static void Fill(SerializedObject so, (string id, string name, MechanicKind kind, int max, string key, bool revert, string desc) g)
        {
            so.FindProperty("displayName").stringValue = g.name;
            so.FindProperty("mechanicKind").enumValueIndex = (int)g.kind;
            so.FindProperty("description").stringValue = g.desc;
            if (g.kind == MechanicKind.MegaEvolution)
            {
                so.FindProperty("megaMaxPerBattle").intValue = g.max;
                so.FindProperty("megaRequiredKeyItem").stringValue = g.key;
                so.FindProperty("megaRevertOnSwitch").boolValue = g.revert;
            }
            else
            {
                so.FindProperty("zMaxPerBattle").intValue = g.max;
                so.FindProperty("zRequiredKeyItem").stringValue = g.key;
                so.FindProperty("zProtectDamagePercent").floatValue = 25f;
                so.FindProperty("zPowerTable").stringValue = ZMoveSettings.OfficialTable;
            }
        }

        protected override IReadOnlyList<(string id, string name, string group)> Templates
            => Library.Select(g => (g.id, g.name, g.kind == MechanicKind.ZMove ? "Movimientos Z" : "Megaevolución")).ToList();

        protected override void ApplyTemplate(SerializedObject so, string id)
        {
            foreach (var g in Library) if (g.id == id) Fill(so, g);
        }

        protected override void DrawBulkPresets()
        {
            if (GUILayout.Button("Crear la Megaevolución y los movimientos Z oficiales")) FinishBulk(CreateClassicSet(), "mecánicas");
        }

        /// <summary>¿Está esta mecánica activa en las reglas del juego (la primera ficha de reglas)?</summary>
        public static bool IsActive(MechanicData d)
        {
            var rules = ContentAssets.LoadAll<RulesetData>();
            return rules.Count > 0 && d != null && rules[0].MechanicIds != null
                && rules[0].MechanicIds.Any(id => string.Equals(id, d.Id, System.StringComparison.OrdinalIgnoreCase));
        }

        protected override Color? RowMark(MechanicData d) => IsActive(d) ? EditorTheme.Ok : (Color?)null;

        protected override string RowTooltip(MechanicData d) => IsActive(d) ? "Activa en las reglas del juego" : "No está activa en las reglas";

        protected override void DrawPreview(MechanicData d)
        {
            EditorTheme.Section("Qué hace", Accent);
            if (!string.IsNullOrWhiteSpace(d.Description)) EditorTheme.Paragraph(d.Description);
            switch (d.Kind)
            {
                case MechanicKind.MegaEvolution:
                    string key = string.IsNullOrWhiteSpace(d.MegaRequiredKeyItem) ? null : d.MegaRequiredKeyItem;
                    var keyItem = key == null ? null : ContentAssets.LoadAll<ItemData>().FirstOrDefault(i => i.Id == key);
                    EditorGUILayout.HelpBox(
                        "• Un monstruo que lleva SU megapiedra puede megaevolucionar en combate (cambia de forma: tipos, estadísticas y habilidad).\n" +
                        (d.MegaMaxPerBattle == 0 ? "• Sin límite: cada lado puede megaevolucionar a todos los que quiera.\n"
                                                 : $"• Cada lado puede megaevolucionar {d.MegaMaxPerBattle} vez/veces por combate.\n") +
                        (key == null ? "• El jugador no necesita ningún objeto clave.\n"
                                     : $"• El jugador necesita «{(keyItem != null ? keyItem.DisplayName : key)}» en la mochila.\n") +
                        "• Los rivales megaevolucionan si su ficha lo permite («Puede megaevolucionar»).\n" +
                        (d.MegaRevertOnSwitch ? "• Al retirarse vuelve a su forma normal." : "• Se queda megaevolucionado hasta el final del combate (oficial)."),
                        MessageType.Info);
                    if (key != null && keyItem == null)
                        EditorGUILayout.HelpBox($"El objeto clave '{key}' no existe todavía: créalo en el editor de objetos o deja el campo vacío.", MessageType.Warning);
                    break;
                case MechanicKind.ZMove:
                {
                    string zkey = string.IsNullOrWhiteSpace(d.ZRequiredKeyItem) ? null : d.ZRequiredKeyItem;
                    var zItem = zkey == null ? null : ContentAssets.FindById<ItemData>(zkey);
                    var settings = new ZMoveSettings(d.ZMaxPerBattle, d.ZRequiredKeyItem, d.ZProtectDamagePercent, d.ZPowerTable);
                    EditorGUILayout.HelpBox(
                        "• Un monstruo que lleva un CRISTAL Z (objeto con el efecto «Cristal Z») convierte los movimientos que su cristal indica " +
                        "(por tipo, un movimiento concreto o una especie) en su movimiento Z.\n" +
                        (d.ZMaxPerBattle == 0 ? "• Sin límite de movimientos Z por combate.\n" : $"• Cada lado puede usar {d.ZMaxPerBattle} movimiento(s) Z por combate.\n") +
                        (zkey == null ? "• El jugador no necesita ningún objeto clave.\n"
                                      : $"• El jugador necesita «{(zItem != null ? zItem.DisplayName : zkey)}» en la mochila.\n") +
                        $"• Potencia Z: 40 → {settings.PowerFor(40)}, 80 → {settings.PowerFor(80)}, 120 → {settings.PowerFor(120)} (los exclusivos usan la suya).\n" +
                        $"• Nunca fallan y atraviesan Protección con el {d.ZProtectDamagePercent:0.#} % del daño.\n" +
                        "• Los de ESTADO hacen además su «efecto Z» (columna efecto_z del movimiento).\n" +
                        "• Los rivales los usan si su ficha lo permite («Puede usar movimientos Z»).", MessageType.Info);
                    if (zkey != null && zItem == null)
                        EditorGUILayout.HelpBox($"El objeto clave '{zkey}' no existe todavía: créalo en el editor de objetos o deja el campo vacío.", MessageType.Warning);
                    break;
                }
            }

            EditorTheme.Section("En tus reglas", Accent);
            var rules = ContentAssets.LoadAll<RulesetData>();
            if (rules.Count == 0)
            {
                EditorGUILayout.HelpBox("Todavía no tienes reglas del juego. Créalas en el editor de reglas.", MessageType.Warning);
                return;
            }
            bool active = IsActive(d);
            EditorGUILayout.HelpBox(active ? "✔ Activa: el juego la usa." : "No está activa: el juego no la usa hasta que la añadas a las reglas.",
                active ? MessageType.Info : MessageType.None);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(active ? "Desactivar en las reglas" : "✔ Activar en las reglas")) SetActive(rules[0], d, !active);
            if (GUILayout.Button("📜 Abrir las reglas")) RulesetEditorWindow.Open();
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>Añade o quita una mecánica de la lista de activas de unas reglas (y las guarda).</summary>
        public static void SetActive(RulesetData rules, MechanicData d, bool active)
        {
            if (rules == null || d == null || string.IsNullOrWhiteSpace(d.Id)) return;
            var so = new SerializedObject(rules);
            SetActive(so, d.Id, active);
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(rules);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Lo mismo sobre un SerializedObject de reglas que ya se está editando (no aplica ni guarda).</summary>
        public static void SetActive(SerializedObject rules, string mechanicId, bool active)
        {
            var arr = rules.FindProperty("mechanicIds");
            var ids = new List<string>();
            for (int i = 0; i < arr.arraySize; i++) ids.Add(arr.GetArrayElementAtIndex(i).stringValue);
            ids.RemoveAll(x => string.Equals(x, mechanicId, System.StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(x));
            if (active) ids.Add(mechanicId);
            arr.arraySize = ids.Count;
            for (int i = 0; i < ids.Count; i++) arr.GetArrayElementAtIndex(i).stringValue = ids[i];
        }
    }
}
