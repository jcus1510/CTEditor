using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Conditions;
using CTEditor.GameDefinition.Domain.Formulas;
using CTEditor.GameDefinition.Infrastructure.Catalog;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;
using CTEditor.GameDefinition.Text;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// Editor de MOVIMIENTOS (menú CTEditor → Combate → Movimientos), sobre la ventana base común: buscador, crear
    /// por id en GameContent, duplicar, borrar, "¿quién usa esto?", renombrar y validación.
    ///
    /// Además: plantillas por MECÁNICA (ClassicMovePresets), un resumen en palabras de lo que hace el
    /// movimiento, y acceso directo a la calculadora de daño para probarlo.
    /// </summary>
    public sealed class MoveEditorWindow : ContentEditorWindow<MoveData>
    {
        [MenuItem(EditorMenus.Battle + "Movimientos", false, EditorMenus.BattleOrder + 1)]
        public static void Open() => OpenWindow<MoveEditorWindow>("Movimientos");

        protected override string Category => ContentFolders.Moves;
        protected override string Noun => "movimiento";
        protected override string Intro =>
            "Los ataques y técnicas. Usa una plantilla por mecánica como punto de partida y ajústala, o combina los campos para inventar la tuya.";

        protected override string Title => "Movimientos";
        // En la lista: una marca con el color de su tipo.
        protected override Color? RowMark(MoveData d) => d.Type != null ? d.Type.Color : (Color?)null;
        protected override string RowTooltip(MoveData d)
            => $"{ContentAssets.Label(d)} · {(d.Type != null ? d.Type.DisplayName : "sin tipo")} · {Etiquetas.Enum(d.Category.ToString())}";

        public override string[] GuideSteps => new[]
        {
            "Si empiezas de cero, pulsa «Crear los movimientos-plantilla»: hay uno por cada mecánica.",
            "Elige un movimiento de la lista y ajusta tipo, categoría, potencia, precisión y PP.",
            "En «Efectos» pulsa «+ Añadir» y elige «Qué hace» (infligir estado, drenar, subir estadísticas…) y su probabilidad.",
            "¿Solo debe pasar a veces? Añade «Condiciones» al efecto (ej.: si el rival tiene menos del 50% de vida).",
            "Para potencia variable usa «Modificadores de potencia» (×2 si…) o una «Fórmula de potencia».",
            "Lee el resumen en palabras de abajo y pruébalo con «Probar en la calculadora de daño».",
        };

        private int _preset;

        protected override void OnCreated(SerializedObject so)
        {
            // Un movimiento nuevo arranca con valores razonables (no con 0 PP ni 0 de potencia).
            so.FindProperty("power").intValue = 40;
            so.FindProperty("maxPp").intValue = 20;
            so.FindProperty("accuracy").floatValue = 100f;
        }

        protected override IReadOnlyList<(string id, string name, string group)> Templates
            => ClassicMovePresets.All.Select(p => (p.Id, p.Name, p.Group)).ToList();

        protected override void ApplyTemplate(SerializedObject so, string id)
        {
            var p = ClassicMovePresets.All.FirstOrDefault(x => x.Id == id);
            if (p != null) ClassicMovePresets.Fill(so, p);
        }

        protected override void DrawBulkPresets()
        {
            if (GUILayout.Button($"Crear los {ClassicMovePresets.All.Length} movimientos-plantilla"))
            {
                int n = ClassicMovePresets.CreateAll(out var missing);
                FinishBulk(n, "movimientos");
                if (missing.Count > 0)
                    EditorUtility.DisplayDialog("Faltan tipos", "Estos movimientos usan tipos que no existen: " + string.Join(", ", missing) +
                        ".\nCréalos (CTEditor → Combate → Tipos → 'Crear los 18 tipos clásicos') y vuelve a aplicar sus plantillas.", "Vale");
            }
            // Los 165 movimientos del pack: importar los que falten o actualizarlos sin borrar.
            PackTools.DrawPackMenu("movimientos", PackTools.ImportMissingMoves, PackTools.UpdateAllMoves);
            PackTools.DrawRepairWarning(AfterPackChange);
        }

        private void AfterPackChange() { Refresh(); Revalidate(); Repaint(); }

        protected override void DrawPresets(MoveData d)
        {
            EditorGUILayout.LabelField("Plantilla por mecánica (reemplaza los datos; el id se mantiene)", EditorStyles.boldLabel);
            var labels = new string[ClassicMovePresets.All.Length];
            for (int i = 0; i < labels.Length; i++) labels[i] = $"{ClassicMovePresets.All[i].Group}/{ClassicMovePresets.All[i].Name}";

            EditorGUILayout.BeginHorizontal();
            _preset = EditorGUILayout.Popup(_preset, labels);
            if (GUILayout.Button("Aplicar", GUILayout.Width(70)))
            {
                var p = ClassicMovePresets.All[_preset];
                List<string> missing = null;
                EditSelected(so => missing = ClassicMovePresets.Fill(so, p));
                if (missing != null && missing.Count > 0)
                    ShowNotification(new GUIContent("Falta el tipo: " + string.Join(", ", missing)));
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(ClassicMovePresets.All[_preset].Summary, EditorStyles.wordWrappedLabel);
            PackTools.DrawRestoreButton(PackTools.MovesFile, d.Id, AfterPackChange);
            EditorGUILayout.Space();
        }

        protected override void DrawPreview(MoveData d)
        {
            // Indicador de categoría con color (naranja físico, azul especial, gris estado).
            var catColor = d.Category == MoveCategory.Physical ? new Color(0.95f, 0.55f, 0.3f)
                         : d.Category == MoveCategory.Special ? new Color(0.35f, 0.6f, 0.95f) : new Color(0.6f, 0.6f, 0.6f);
            var chip = EditorGUILayout.GetControlRect(GUILayout.Height(20));
            EditorGUI.DrawRect(chip, new Color(catColor.r, catColor.g, catColor.b, 0.35f));
            EditorGUI.LabelField(new Rect(chip.x + 6, chip.y + 1, chip.width - 6, chip.height),
                $"{CategoryName(d.Category).ToUpperInvariant()} · {(d.Type != null ? d.Type.DisplayName : "SIN TIPO")}" +
                $" · {(IsAdvanced(d) ? "con reglas avanzadas" : "sencillo")}", EditorStyles.boldLabel);

            EditorGUILayout.LabelField("Qué hace (en palabras)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(string.Join("\n", Describe(d)), MessageType.Info);

            // Problemas y avisos en vivo (rojo = no funcionará; amarillo = revisa).
            foreach (var (error, text) in Check(d))
                EditorGUILayout.HelpBox(text, error ? MessageType.Error : MessageType.Warning);

            if (GUILayout.Button("Probar en la calculadora de daño")) DamageCalculatorWindow.OpenWithMove(d);
        }

        private static string CategoryName(MoveCategory c)
            => c == MoveCategory.Physical ? "físico" : c == MoveCategory.Special ? "especial" : "de estado";

        private static bool IsAdvanced(MoveData d)
            => (d.PowerModifiers != null && d.PowerModifiers.Length > 0) || !string.IsNullOrWhiteSpace(d.PowerFormula)
               || !string.IsNullOrWhiteSpace(d.AttackStat) || !string.IsNullOrWhiteSpace(d.DefenseStat) || d.AttackStatFromTarget
               || (d.SecondaryEffects != null && d.SecondaryEffects.Any(e => e != null && ((e.conditions?.Length ?? 0) > 0 || e.sharesPreviousRoll)));

        /// <summary>El movimiento en frases (para que el autor compruebe que hace lo que quiere).</summary>
        internal static List<string> Describe(MoveData d)
        {
            var l = new List<string>();
            string type = d.Type != null ? d.Type.DisplayName : "SIN TIPO ASIGNADO";
            string power = !string.IsNullOrWhiteSpace(d.PowerFormula) ? $"potencia según la fórmula «{d.PowerFormula}»"
                         : d.FixedDamage != FixedDamageKind.None ? SpecialDamageText(d)
                         : d.Power > 0 ? $"potencia {d.Power}" : "sin daño directo";
            l.Add($"• Movimiento {CategoryName(d.Category)} de tipo {type}: {power}.");
            l.Add($"• Precisión: {(d.NeverMisses ? "nunca falla" : d.Accuracy.ToString("0") + "%")}  ·  PP: {d.MaxPp}" +
                  (d.Priority != 0 ? $"  ·  Prioridad {d.Priority:+#;-#}" : ""));

            // Stats del daño (solo si se cambiaron: lo normal no hace falta decirlo).
            if (d.Category != MoveCategory.Status && (d.AttackStatFromTarget || !string.IsNullOrWhiteSpace(d.AttackStat) || !string.IsNullOrWhiteSpace(d.DefenseStat)))
            {
                string atk = string.IsNullOrWhiteSpace(d.AttackStat) ? (d.Category == MoveCategory.Physical ? "Ataque" : "Atq. Esp.") : StatLabels.NameOf(d.AttackStat);
                string def = string.IsNullOrWhiteSpace(d.DefenseStat) ? (d.Category == MoveCategory.Physical ? "Defensa" : "Def. Esp.") : StatLabels.NameOf(d.DefenseStat);
                l.Add($"• Daño: {atk} {(d.AttackStatFromTarget ? "DEL RIVAL" : "propio")} contra {def} del rival.");
            }
            if (d.PowerModifiers != null)
                foreach (var m in d.PowerModifiers.Where(m => m != null))
                    l.Add($"• Potencia ×{m.multiplier:0.##} {ConditionText.Describe((m.conditions ?? new ConditionData[0]).Select(ConditionTextUnity.FromData))}.");

            if (d.MaxHits > 1) l.Add(d.MinHits == d.MaxHits ? $"• Golpea {d.MinHits} veces." : $"• Golpea de {d.MinHits} a {d.MaxHits} veces.");
            if (d.CritStage > 0) l.Add($"• Crítico: etapa {d.CritStage} (más probable; la tabla está en las Reglas).");
            if (d.TwoTurn == TwoTurnKind.Charge) l.Add("• Carga un turno y golpea al siguiente.");
            if (d.TwoTurn == TwoTurnKind.Recharge) l.Add("• Tras usarlo, el siguiente turno debe recargar.");
            if (d.MakesContact) l.Add("• Hace contacto (activa Electricidad Estática, Cuerpo Llama...).");
            if (d.RespectsTypeImmunity) l.Add("• No afecta a quien sea inmune por tipo.");
            if (d.Tags != null && d.Tags.Length > 0) l.Add($"• Etiquetas: {string.Join(", ", d.Tags)}.");
            if (d.Requirements.Length > 0)
                l.Add($"• Solo funciona {ConditionText.Describe(d.Requirements.Where(c => c != null).Select(ConditionTextUnity.FromData))}; si no, «¡Pero falló!».");

            if (d.SecondaryEffects != null)
                foreach (var e in d.SecondaryEffects)
                    if (e != null) l.Add("• " + DescribeEffect(e));
            return l;
        }

        private static string SpecialDamageText(MoveData d)
        {
            switch (d.FixedDamage)
            {
                case FixedDamageKind.Fixed: return $"quita siempre {d.FixedDamageAmount} PS";
                case FixedDamageKind.UserLevel: return "quita tantos PS como el nivel del usuario";
                case FixedDamageKind.HalfTargetHp: return "quita la mitad de los PS actuales del rival";
                case FixedDamageKind.OneHitKo: return "KO directo (falla si el rival tiene más nivel)";
                case FixedDamageKind.ReturnPhysical: return $"devuelve el {(d.FixedDamageAmount > 0 ? d.FixedDamageAmount : 200)}% del daño FÍSICO recibido este turno (falla si no recibió)";
                case FixedDamageKind.ReturnSpecial: return $"devuelve el {(d.FixedDamageAmount > 0 ? d.FixedDamageAmount : 200)}% del daño ESPECIAL recibido este turno (falla si no recibió)";
                case FixedDamageKind.Bide: return $"aguanta 2 turnos y devuelve el {(d.FixedDamageAmount > 0 ? d.FixedDamageAmount : 200)}% de todo el daño recibido";
                default: return "";
            }
        }

        /// <summary>Un efecto en una frase: probabilidad, qué hace, a quién y cuándo.</summary>
        internal static string DescribeEffect(MoveData.MoveEffectData e)
        {
            string who = e.target == EffectTarget.Self ? "al usuario" : "al rival";
            string chance = e.sharesPreviousRoll ? "(con el mismo dado que el anterior) "
                          : e.chancePercent >= 100f ? "" : $"{e.chancePercent:0.#}% de ";
            string what;
            switch (e.kind)
            {
                case MoveEffectKind.InflictStatus: what = $"infligir '{e.statusId}' {who}"; break;
                case MoveEffectKind.Drain: what = $"recuperar el {e.amountPercent:0}% del daño causado"; break;
                case MoveEffectKind.Recoil: what = $"recibir el {e.amountPercent:0}% del daño causado"; break;
                case MoveEffectKind.RecoilMaxHp: what = $"perder el {e.amountPercent:0}% de sus PS máximos"; break;
                case MoveEffectKind.HealSelf: what = $"curarse el {e.amountPercent:0}% de sus PS máximos"; break;
                case MoveEffectKind.Heal: what = $"curar {who} el {e.amountPercent:0}% de sus PS máximos"; break;
                case MoveEffectKind.CureStatus: what = $"quitar {(string.IsNullOrWhiteSpace(e.statusId) ? "el estado principal" : $"'{e.statusId}'")} {who}"; break;
                case MoveEffectKind.SetWeather: what = $"cambiar el clima a '{e.weatherId}'" + (e.weatherTurns > 0 ? $" ({e.weatherTurns} turnos)" : ""); break;
                case MoveEffectKind.Flinch: what = "hacer retroceder al rival (pierde el turno si aún no actuó)"; break;
                case MoveEffectKind.SetHazard: what = $"poner una capa de '{e.hazardId}' en el lado {(e.target == EffectTarget.Self ? "propio" : "del rival")}"; break;
                case MoveEffectKind.ClearHazards:
                    what = $"quitar {(string.IsNullOrWhiteSpace(e.hazardId) ? "todas las trampas" : $"'{e.hazardId}'")} del lado {(e.target == EffectTarget.Self ? "propio" : "del rival")}"; break;
                case MoveEffectKind.ForceSwitch: what = $"obligar {who} a cambiarse por otro al azar (en combates salvajes, termina el combate)"; break;
                case MoveEffectKind.ChangeStatStage:
                    what = $"{(e.statStages > 0 ? "subir" : "bajar")} {System.Math.Abs(e.statStages)} etapa(s) de {StatLabels.NameOf(e.statStatId)} {who}"; break;
                case MoveEffectKind.SetSideCondition:
                    what = $"poner '{e.sideConditionId}' en el lado {(e.target == EffectTarget.Self ? "propio" : "del rival")}"; break;
                case MoveEffectKind.ResetStages: what = $"devolver a 0 todas las etapas {who}"; break;
                case MoveEffectKind.CritBoost: what = $"subir {(e.statStages > 0 ? e.statStages : 2)} el índice de crítico {who} (mientras siga en el campo)"; break;
                case MoveEffectKind.Substitute: what = $"crear un sustituto pagando el {(e.amountPercent > 0 ? e.amountPercent : 25):0}% de sus PS máximos"; break;
                case MoveEffectKind.DisableMove: what = $"anular el último movimiento {who} durante {(e.turns > 0 ? e.turns : 4)} turnos"; break;
                case MoveEffectKind.Encore: what = $"obligar {who} a repetir su último movimiento durante {(e.turns > 0 ? e.turns : 3)} turnos"; break;
                case MoveEffectKind.Rampage:
                    what = $"seguir usándolo 2-{(e.turns > 0 ? e.turns : 3)} turnos sin poder elegir" + (string.IsNullOrWhiteSpace(e.statusId) ? "" : $" y después quedar '{e.statusId}'"); break;
                case MoveEffectKind.Rage: what = $"mientras lo siga usando, cada golpe recibido sube {(e.statStages != 0 ? e.statStages : 1)} etapa(s) de {StatLabels.NameOf(string.IsNullOrWhiteSpace(e.statStatId) ? "attack" : e.statStatId)}"; break;
                case MoveEffectKind.CallRandomMove: what = "usar un movimiento AL AZAR de todos los del juego (menos los marcados «no_metronomo»)"; break;
                case MoveEffectKind.CallLastMove: what = "usar el último movimiento que usó el rival"; break;
                case MoveEffectKind.CopyLastMove: what = "copiar el último movimiento del rival en este hueco (5 PP) hasta que se retire"; break;
                case MoveEffectKind.Transform: what = "transformarse en el rival: tipos, estadísticas (no PS), etapas y movimientos con 5 PP"; break;
                case MoveEffectKind.ChangeType: what = string.IsNullOrWhiteSpace(e.typeId) ? "cambiar su tipo al de su primer movimiento que no tenga ya" : $"cambiar su tipo a '{e.typeId}'"; break;
                case MoveEffectKind.SwitchSelf:
                    what = e.statStages > 0 ? "retirarse y pasar sus etapas y su sustituto al que entre (Relevo)" : "retirarse tras el golpe; entra otro de su equipo (Ida y Vuelta)"; break;
                case MoveEffectKind.Teleport: what = "escapar de un combate salvaje; contra un entrenador, retirarse como Ida y Vuelta"; break;
                default: what = e.kind.ToString(); break;
            }
            var conds = (e.conditions ?? new ConditionData[0]).Select(ConditionTextUnity.FromData).ToList();
            return $"{chance}{what}" + (conds.Count > 0 ? $" — {ConditionText.Describe(conds)}" : "") + ".";
        }

        /// <summary>Comprobaciones en vivo: (true = error, false = aviso).</summary>
        internal static List<(bool error, string text)> Check(MoveData d)
        {
            var l = new List<(bool, string)>();
            if (!string.IsNullOrWhiteSpace(d.PowerFormula) &&
                !MathExpression.TryParse(d.PowerFormula, Move.PowerFormulaVariables, out _, out var err))
                l.Add((true, $"La fórmula de potencia tiene un error: {err}\nVariables: {string.Join(", ", Move.PowerFormulaVariables)}."));
            if (d.Category == MoveCategory.Status && (!string.IsNullOrWhiteSpace(d.PowerFormula) || (d.PowerModifiers?.Length ?? 0) > 0))
                l.Add((false, "Es de categoría Estado: la potencia (fórmula o modificadores) no se usa."));
            if (d.PowerModifiers != null)
                foreach (var m in d.PowerModifiers.Where(m => m != null && m.multiplier == 0f))
                    l.Add((false, "Un modificador de potencia vale ×0: anula el daño cuando se cumple."));
            if (d.SecondaryEffects != null)
                for (int i = 0; i < d.SecondaryEffects.Length; i++)
                {
                    var e = d.SecondaryEffects[i];
                    if (e == null) continue;
                    if (i == 0 && e.sharesPreviousRoll) l.Add((false, "El primer efecto no puede compartir dado (no hay uno anterior): tirará el suyo."));
                    if (e.kind == MoveEffectKind.SetWeather && string.IsNullOrWhiteSpace(e.weatherId))
                        l.Add((true, "Un efecto 'Cambiar clima' no tiene clima elegido."));
                    if (e.kind == MoveEffectKind.SetWeather && !string.IsNullOrWhiteSpace(e.weatherId) && ContentAssets.FindById<WeatherData>(e.weatherId) == null)
                        l.Add((false, $"El clima '{e.weatherId}' no existe (créalo en CTEditor → Combate → Climas): el efecto funcionará pero sin cambiar tipos ni dañar."));
                    foreach (var c in e.conditions ?? new ConditionData[0]) if (c != null) CheckCondition(c, l);
                }
            if (d.PowerModifiers != null)
                foreach (var m in d.PowerModifiers) foreach (var c in m?.conditions ?? new ConditionData[0]) if (c != null) CheckCondition(c, l);
            return l;
        }

        private static void CheckCondition(ConditionData c, List<(bool, string)> l)
        {
            if (!Condition.UsesText(c.kind) || c.kind == ConditionKind.MoveHasTag || c.kind == ConditionKind.MoveCategory) return;
            if (string.IsNullOrWhiteSpace(c.text)) { l.Add((true, $"Una condición '{ConditionTextUnity.Describe(c)}' no tiene valor elegido.")); return; }
            bool exists = c.kind == ConditionKind.HasStatus ? ContentAssets.FindById<StatusConditionData>(c.text) != null
                        : c.kind == ConditionKind.Weather ? ContentAssets.FindById<WeatherData>(c.text) != null
                        : c.kind == ConditionKind.StatStage ? true
                        : ContentAssets.FindById<ElementTypeData>(c.text) != null;
            if (!exists) l.Add((false, $"La condición «{ConditionTextUnity.Describe(c)}» usa '{c.text}', que no existe."));
        }
    }
}
