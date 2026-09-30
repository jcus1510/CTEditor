using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CTEditor.GameDefinition.Infrastructure.Catalog;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// Editor de ESTADOS ALTERADOS (menú CTEditor → Combate → Estados), sobre la ventana base común. Aquí el autor
    /// define "Quemado", "Veneno", "Maldición"... con daño por turno, probabilidad de impedir la acción,
    /// duración, recuperación y modificadores de stats. Incluye los 8 clásicos como plantillas.
    /// </summary>
    public sealed class StatusEditorWindow : ContentEditorWindow<StatusConditionData>
    {
        [MenuItem(EditorMenus.Battle + "Estados", false, EditorMenus.BattleOrder + 4)]
        public static void Open() => OpenWindow<StatusEditorWindow>("Estados");

        protected override string Category => ContentFolders.Status;
        protected override string Noun => "estado";
        protected override string Intro =>
            "Los estados alterados (quemado, dormido...). Cada uno combina daño por turno, probabilidad de no poder moverse, duración y cambios de estadísticas.";

        protected override string Title => "Estados alterados";

        public override string[] GuideSteps => new[]
        {
            "Pulsa «Crear los estados clásicos» para tener quemado, parálisis, confusión, atrapado, protección…",
            "Decide si es PRINCIPAL (solo uno a la vez) o VOLÁTIL (se suma a otros) con «Es volátil».",
            "Combina: daño por turno, probabilidad de no actuar, duración, cambios de estadísticas, tipos inmunes…",
            "Mira el resumen de abajo para comprobar que hace lo que quieres.",
        };

        protected override IReadOnlyList<(string id, string name, string group)> Templates
            => ClassicStatusPresets.All.Select(p => (p.id, p.name, ClassicStatusPresets.IsClassicVolatile(p.id) ? "Volátiles" : "Principales")).ToList();

        protected override void ApplyTemplate(SerializedObject so, string id)
        {
            ClassicStatusPresets.Fill(so, id);
            var t = Templates.FirstOrDefault(x => x.id == id); if (t.id != null) so.FindProperty("displayName").stringValue = t.name;
        }

        protected override void DrawBulkPresets()
        {
            if (GUILayout.Button($"Crear los {ClassicStatusPresets.All.Length} estados clásicos"))
                FinishBulk(ClassicStatusPresets.CreateClassicSet(), "estados");
        }

        protected override void DrawPresets(StatusConditionData d)
        {
            EditorGUILayout.LabelField("Plantillas clásicas (rellenan los campos; el id se mantiene)", EditorStyles.boldLabel);
            var all = ClassicStatusPresets.All;
            for (int row = 0; row < all.Length; row += 4)
            {
                EditorGUILayout.BeginHorizontal();
                for (int i = row; i < row + 4 && i < all.Length; i++)
                    if (GUILayout.Button(all[i].name))
                    {
                        string kind = all[i].id;
                        EditSelected(so => ClassicStatusPresets.Fill(so, kind));
                    }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.Space();
        }

        protected override void DrawPreview(StatusConditionData d)
        {
            // Indicador: PRINCIPAL (morado, exclusivo) o VOLÁTIL (verde azulado, se suma).
            var chip = EditorGUILayout.GetControlRect(GUILayout.Height(20));
            EditorGUI.DrawRect(chip, d.IsVolatile ? new Color(0.2f, 0.7f, 0.65f, 0.35f) : new Color(0.6f, 0.35f, 0.8f, 0.35f));
            EditorGUI.LabelField(new Rect(chip.x + 6, chip.y + 1, chip.width - 6, chip.height),
                d.IsVolatile ? "VOLÁTIL · se suma al estado principal y a otros volátiles; se va al retirarse"
                             : "PRINCIPAL · solo uno a la vez (no se pisa con quemado, parálisis, sueño...)", EditorStyles.boldLabel);
            if (ClassicStatusPresets.IsClassicVolatile(d.Id) && !d.IsVolatile)
                EditorGUILayout.HelpBox($"En los juegos, '{d.Id}' es VOLÁTIL. Pulsa su plantilla clásica (arriba) para actualizarlo, o marca «Es volátil».", MessageType.Warning);

            var l = new List<string>();
            if (d.DurationMaxTurns > d.DurationTurns) l.Add($"• Duración al azar entre {d.DurationTurns} y {d.DurationMaxTurns} turnos.");
            if (d.PreventsSwitch) l.Add("• No puede cambiarse ni huir mientras dure.");
            if (d.BlocksIncomingMoves) l.Add("• Los movimientos del rival contra él fallan (protección).");
            if (d.SurvivesLethalHit) l.Add("• Aguanta con 1 PS un golpe que lo debilitaría.");
            if (d.ResidualHealsOpponent) l.Add("• Lo que pierde por turno lo recupera el rival (drenadoras).");
            if (d.HarderWhenRepeated) l.Add("• Si se aplica turnos seguidos, cada vez es más difícil (1/3, 1/9...).");
            if (d.ImmuneTypes != null)
            {
                var names = new List<string>();
                foreach (var t in d.ImmuneTypes) if (t != null) names.Add(t.DisplayName);
                if (names.Count > 0) l.Add($"• No afecta a los tipos: {string.Join(", ", names)}.");
            }
            if (d.ResidualDamagePercent > 0)
                l.Add(d.ResidualHeals
                    ? $"• Cura {d.ResidualDamagePercent}% de los PS máximos al final de cada turno."
                    : d.ProgressiveResidual
                        ? $"• Daño creciente cada turno: {d.ResidualDamagePercent}%, luego el doble, el triple..."
                        : $"• Pierde {d.ResidualDamagePercent}% de sus PS máximos al final de cada turno.");
            if (d.ActionPreventionChance > 0)
                l.Add(d.ActionPreventionChance >= 100 ? "• No puede actuar mientras dure." : $"• {d.ActionPreventionChance}% de no poder actuar cada turno.");
            if (d.SelfDamageOnPreventedPercent > 0)
                l.Add($"• Cuando no puede actuar, se hace {d.SelfDamageOnPreventedPercent}% de daño a sí mismo.");
            if (d.DurationTurns > 0 && d.DurationMaxTurns <= d.DurationTurns) l.Add($"• Dura como máximo {d.DurationTurns} turno(s).");
            if (d.RecoveryChancePerTurn > 0) l.Add($"• {d.RecoveryChancePerTurn}% de recuperarse cada turno.");
            if (!string.IsNullOrEmpty(d.TransformsToStatus)) l.Add($"• Al terminar, se convierte en '{d.TransformsToStatus}'.");
            if (d.ClearedOnSwitch) l.Add("• Se cura al retirarse del combate.");
            if (d.PassiveModifiers != null)
                foreach (var m in d.PassiveModifiers)
                    if (m != null && !string.IsNullOrEmpty(m.statId) && !Mathf.Approximately(m.multiplier, 1f))
                        l.Add($"• {StatLabels.NameOf(m.statId)} ×{m.multiplier} mientras dure.");

            EditorGUILayout.LabelField("Resumen", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(l.Count == 0 ? "Este estado todavía no hace nada (todos los campos en neutro)." : string.Join("\n", l), MessageType.Info);
        }
    }
}
