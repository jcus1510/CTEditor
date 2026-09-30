using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Infrastructure.Catalog;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// Editor de EFECTOS DE LADO (menú CTEditor → Combate → Efectos de lado): Reflejo, Pantalla de Luz,
    /// Neblina, Velo Sagrado y Viento Afín como plantillas, y la libertad de inventar los tuyos (menos daño
    /// físico o especial, sin bajadas, sin estados, más velocidad) durante unos turnos.
    /// </summary>
    public sealed class SideConditionEditorWindow : ContentEditorWindow<SideConditionData>
    {
        [MenuItem(EditorMenus.Battle + "Efectos de lado", false, EditorMenus.BattleOrder + 7)]
        public static void Open() => OpenWindow<SideConditionEditorWindow>("Efectos de lado");

        protected override string Category => ContentFolders.SideConditions;
        protected override string Noun => "efecto de lado";
        protected override string Title => "Efectos de lado";
        protected override string Intro =>
            "Protegen o ayudan a TODO un bando durante unos turnos, esté quien esté en el campo: menos daño, sin bajadas de estadísticas, sin estados o más velocidad.";

        public override string[] GuideSteps => new[]
        {
            "Pulsa «Crear los efectos clásicos» (Reflejo, Pantalla de Luz, Neblina, Viento Afín, Espacio Raro, Gravedad, Zona Extraña, los tres Campos...).",
            "Cada efecto dura unos turnos. Elige qué hace: multiplicar el daño físico o especial recibido, impedir bajadas o estados, o multiplicar la velocidad.",
            "Un movimiento lo pone con el efecto «Poner un efecto de lado» (objetivo: uno mismo = tu lado). Los golpes críticos ignoran las reducciones de daño.",
        };

        protected override Color? RowMark(SideConditionData d) => d.Color;

        // (id, nombre, turnos, ×físico, ×especial, sin bajadas, sin estados, ×velocidad, color)
        public static readonly (string id, string name, int turns, float phys, float spec, bool noDrops, bool noStatus, float speed, Color color)[] Library =
        {
            ("reflect", "Reflejo", 5, 0.5f, 1f, false, false, 1f, new Color(0.95f, 0.80f, 0.35f)),
            ("light_screen", "Pantalla de Luz", 5, 1f, 0.5f, false, false, 1f, new Color(0.95f, 0.55f, 0.85f)),
            ("aurora_veil", "Velo Aurora", 5, 0.5f, 0.5f, false, false, 1f, new Color(0.60f, 0.90f, 0.95f)),
            ("mist", "Neblina", 5, 1f, 1f, true, false, 1f, new Color(0.75f, 0.85f, 0.90f)),
            ("safeguard", "Velo Sagrado", 5, 1f, 1f, false, true, 1f, new Color(0.70f, 0.60f, 0.95f)),
            ("tailwind", "Viento Afín", 4, 1f, 1f, false, false, 2f, new Color(0.55f, 0.85f, 0.70f)),
            // 4.ª generación (efectos de campo: el movimiento los pone en los DOS lados)
            ("trick_room", "Espacio Raro", 5, 1f, 1f, false, false, 1f, new Color(0.85f, 0.45f, 0.85f)),
            ("gravity", "Gravedad", 5, 1f, 1f, false, false, 1f, new Color(0.55f, 0.45f, 0.80f)),
            ("mud_sport", "Chapoteo Lodo", 5, 1f, 1f, false, false, 1f, new Color(0.75f, 0.60f, 0.35f)),
            ("water_sport", "Hidrochorro", 5, 1f, 1f, false, false, 1f, new Color(0.40f, 0.65f, 0.95f)),
            ("lucky_chant", "Conjuro", 5, 1f, 1f, false, false, 1f, new Color(0.95f, 0.90f, 0.50f)),
            // 5.ª y 6.ª generación: zonas y CAMPOS (solo puede haber un campo a la vez: grupo «campo»)
            ("wonder_room", "Zona Extraña", 5, 1f, 1f, false, false, 1f, new Color(0.80f, 0.55f, 0.95f)),
            ("magic_room", "Zona Mágica", 5, 1f, 1f, false, false, 1f, new Color(0.95f, 0.55f, 0.75f)),
            ("grassy_terrain", "Campo de Hierba", 5, 1f, 1f, false, false, 1f, new Color(0.45f, 0.80f, 0.40f)),
            ("misty_terrain", "Campo de Niebla", 5, 1f, 1f, false, false, 1f, new Color(0.95f, 0.70f, 0.90f)),
            ("electric_terrain", "Campo Eléctrico", 5, 1f, 1f, false, false, 1f, new Color(0.98f, 0.85f, 0.25f)),
            // 7.ª generación
            ("psychic_terrain", "Campo Psíquico", 5, 1f, 1f, false, false, 1f, new Color(0.90f, 0.45f, 0.75f)),
        };

        public static int CreateClassicSet()
        {
            int created = 0;
            foreach (var c in Library)
            {
                string id = c.id;
                if (ContentAssets.CreateIfMissing<SideConditionData>(ContentFolders.SideConditions, c.id, c.name, so => Fill(so, id))) created++;
            }
            AssetDatabase.SaveAssets();
            return created;
        }

        public static void Fill(SerializedObject so, string presetId)
        {
            foreach (var c in Library)
            {
                if (c.id != presetId) continue;
                so.FindProperty("displayName").stringValue = c.name;
                so.FindProperty("color").colorValue = c.color;
                so.FindProperty("turns").intValue = c.turns;
                so.FindProperty("physicalDamageMultiplier").floatValue = c.phys;
                so.FindProperty("specialDamageMultiplier").floatValue = c.spec;
                so.FindProperty("blocksStatDrops").boolValue = c.noDrops;
                so.FindProperty("blocksStatus").boolValue = c.noStatus;
                so.FindProperty("speedMultiplier").floatValue = c.speed;
                // Los de la 4.ª generación.
                void B(string f, bool v) { var p = so.FindProperty(f); if (p != null) p.boolValue = v; }
                void F(string f, float v) { var p = so.FindProperty(f); if (p != null) p.floatValue = v; }
                B("reversesTurnOrder", c.id == "trick_room");
                F("accuracyMultiplier", c.id == "gravity" ? 1.67f : 1f);
                B("groundsTargets", c.id == "gravity");
                B("blocksCrits", c.id == "lucky_chant");
                // 5.ª y 6.ª generación.
                B("swapsDefenses", c.id == "wonder_room");
                B("suppressesItems", c.id == "magic_room");
                B("blocksPriorityOnGrounded", c.id == "psychic_terrain");
                bool terrain = c.id.EndsWith("_terrain");
                var gp = so.FindProperty("group"); if (gp != null) gp.stringValue = terrain ? "campo" : "";
                F("endOfTurnHealPercent", c.id == "grassy_terrain" ? 6.25f : 0f);
                var block = so.FindProperty("groundedStatusBlock");
                if (block != null)
                {
                    string[] ids = c.id == "misty_terrain" ? new[] { "*" } : c.id == "electric_terrain" ? new[] { "sleep" } : new string[0];
                    block.arraySize = ids.Length;
                    for (int i = 0; i < ids.Length; i++) block.GetArrayElementAtIndex(i).stringValue = ids[i];
                }
                var power = so.FindProperty("typePowerMultipliers");
                if (power != null)
                {
                    // Campo Eléctrico: Eléctrico ×1,5; de Hierba: Planta ×1,5; de Niebla: Dragón ×0,5 (a quien pisa el suelo).
                    string pt = c.id == "electric_terrain" ? "electric" : c.id == "grassy_terrain" ? "grass" : c.id == "misty_terrain" ? "dragon"
                        : c.id == "psychic_terrain" ? "psychic" : null;
                    var pty = pt != null ? ContentAssets.FindById<ElementTypeData>(pt) : null;
                    power.arraySize = pty != null ? 1 : 0;
                    if (pty != null)
                    {
                        power.GetArrayElementAtIndex(0).FindPropertyRelative("type").objectReferenceValue = pty;
                        power.GetArrayElementAtIndex(0).FindPropertyRelative("multiplier").floatValue = c.id == "misty_terrain" ? 0.5f : 1.5f;
                    }
                }
                var types = so.FindProperty("typeDamageMultipliers");
                if (types != null)
                {
                    string t = c.id == "mud_sport" ? "electric" : c.id == "water_sport" ? "fire" : null;
                    var ty = t != null ? ContentAssets.FindById<ElementTypeData>(t) : null;
                    types.arraySize = ty != null ? 1 : 0;
                    if (ty != null)
                    {
                        types.GetArrayElementAtIndex(0).FindPropertyRelative("type").objectReferenceValue = ty;
                        types.GetArrayElementAtIndex(0).FindPropertyRelative("multiplier").floatValue = 0.33f;
                    }
                }
            }
        }

        protected override IReadOnlyList<(string id, string name, string group)> Templates
            => Library.Select(p => (p.id, p.name, "Efectos clásicos")).ToList();

        protected override void ApplyTemplate(SerializedObject so, string id)
        {
            Fill(so, id);
            var t = Templates.FirstOrDefault(x => x.id == id); if (t.id != null) so.FindProperty("displayName").stringValue = t.name;
        }

        protected override void DrawBulkPresets()
        {
            if (GUILayout.Button($"Crear los {Library.Length} efectos clásicos")) FinishBulk(CreateClassicSet(), "efectos de lado");
        }

        protected override void DrawPresets(SideConditionData d)
        {
            EditorGUILayout.LabelField("Plantillas clásicas (rellenan los campos; el id se mantiene)", EditorStyles.boldLabel);
            // En filas de 6 botones (así caben aunque la ventana sea estrecha).
            for (int row = 0; row < Library.Length; row += 6)
            {
                EditorGUILayout.BeginHorizontal();
                for (int i = row; i < System.Math.Min(row + 6, Library.Length); i++)
                {
                    string id = Library[i].id;
                    if (GUILayout.Button(Library[i].name, EditorStyles.miniButton)) EditSelected(so => Fill(so, id));
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        protected override void DrawPreview(SideConditionData d)
        {
            EditorTheme.Section("Qué hace en su lado", Accent);
            foreach (var line in Describe(d)) EditorTheme.Tip(line, d.Color, "▸");
            var users = ContentAssets.LoadAll<MoveData>()
                .Where(m => m.SecondaryEffects != null && m.SecondaryEffects.Any(e => e != null && e.kind == MoveEffectKind.SetSideCondition && e.sideConditionId == d.Id))
                .Select(ContentAssets.Label).ToList();
            EditorTheme.Tip(users.Count == 0 ? "Ningún movimiento lo usa todavía: añade a uno el efecto «Poner un efecto de lado» con este efecto."
                                             : "Lo usan: " + string.Join(", ", users), users.Count == 0 ? EditorTheme.Warn : EditorTheme.Ok);
        }

        /// <summary>El efecto explicado en frases.</summary>
        public static List<string> Describe(SideConditionData d)
        {
            var l = new List<string> { $"Dura {d.Turns} turno{(d.Turns == 1 ? "" : "s")} (contando el turno en que se pone)." };
            if (d.PhysicalDamageMultiplier != 1f) l.Add($"El daño FÍSICO que recibe este lado se multiplica por {d.PhysicalDamageMultiplier:0.##} (los críticos lo ignoran).");
            if (d.SpecialDamageMultiplier != 1f) l.Add($"El daño ESPECIAL que recibe este lado se multiplica por {d.SpecialDamageMultiplier:0.##} (los críticos lo ignoran).");
            if (d.BlocksStatDrops) l.Add("El rival no puede BAJAR las estadísticas de este lado.");
            if (d.BlocksStatus) l.Add("El rival no puede poner ESTADOS a este lado.");
            if (d.SpeedMultiplier != 1f) l.Add($"La Velocidad de este lado se multiplica por {d.SpeedMultiplier:0.##}.");
            if (d.ReversesTurnOrder) l.Add("Los más LENTOS actúan primero (si el efecto está en cualquiera de los dos lados).");
            if (d.AccuracyMultiplier != 1f) l.Add($"La precisión de lo que se lanza contra este lado se multiplica por {d.AccuracyMultiplier:0.##}.");
            if (d.GroundsTargets) l.Add("Este lado queda en el suelo: le afectan los movimientos de Tierra aunque vuele o levite.");
            if (d.BlocksCrits) l.Add("Los golpes contra este lado no pueden ser críticos.");
            if (d.TypeDamageMultipliers != null)
                foreach (var e in d.TypeDamageMultipliers)
                    if (e != null && e.type != null) l.Add($"El daño de tipo {e.type.DisplayName} que recibe este lado se multiplica por {e.multiplier:0.##}.");
            if (d.SwapsDefenses) l.Add("Defensa y Def. Esp. se INTERCAMBIAN para calcular el daño (en todo el campo).");
            if (d.SuppressesItems) l.Add("Ningún objeto equipado funciona (en todo el campo).");
            if (!string.IsNullOrWhiteSpace(d.Group)) l.Add($"Grupo «{d.Group}»: al ponerse, quita los demás efectos de ese grupo (solo un campo a la vez).");
            if (d.EndOfTurnHealPercent > 0) l.Add($"Quien pisa el suelo recupera el {d.EndOfTurnHealPercent:0.##}% de sus PS al final del turno.");
            if (d.BlocksPriorityOnGrounded) l.Add("Los movimientos con prioridad fallan contra quien pisa el suelo.");
            if (d.GroundedStatusBlock != null && d.GroundedStatusBlock.Length > 0)
                l.Add(System.Array.IndexOf(d.GroundedStatusBlock, "*") >= 0 ? "Nadie que pise el suelo puede sufrir estados principales ni confusión."
                    : $"Nadie que pise el suelo puede sufrir: {string.Join(", ", d.GroundedStatusBlock)}.");
            if (d.TypePowerMultipliers != null)
                foreach (var e in d.TypePowerMultipliers)
                    if (e != null && e.type != null) l.Add($"Los movimientos de tipo {e.type.DisplayName} de quien pisa el suelo ×{e.multiplier:0.##}.");
            if (l.Count == 1) l.Add("⚠ Ahora mismo no hace nada: elige al menos un efecto.");
            return l;
        }
    }
}
