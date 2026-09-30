using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using CTEditor.GameDefinition.Domain.Species;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// FORMAS y VARIANTES en el editor: textos en español, plantillas de cambio de forma y las acciones de añadir o quitar
    /// (las usan la ficha de especie y el Árbol de familia). Los datos viven en la especie (forms, formChanges, formOf).
    /// </summary>
    public static class FormEditing
    {
        // ---------------- Textos ----------------

        /// <summary>Nombre de una forma de la especie ("" = «normal»).</summary>
        public static string FormName(SpeciesData s, string formId)
        {
            if (string.IsNullOrEmpty(formId)) return "normal";
            if (formId == FormChange.AnyForm) return "cualquier forma";
            var f = (s?.Forms ?? new SpeciesData.FormEntry[0]).FirstOrDefault(x => x != null && string.Equals(x.id, formId, StringComparison.OrdinalIgnoreCase));
            return f == null ? formId + " (no existe)" : (string.IsNullOrWhiteSpace(f.displayName) ? f.id : f.displayName);
        }

        private static string Name<T>(string id) where T : UnityEngine.ScriptableObject, IContentAsset
        {
            if (string.IsNullOrWhiteSpace(id)) return "¿?";
            var a = ContentAssets.FindById<T>(id);
            return a != null ? a.DisplayName : id;
        }

        /// <summary>«De normal a Modo Daruma: con menos del 50 % de PS (necesita Modo Daruma)».</summary>
        public static string Describe(SpeciesData s, SpeciesData.FormChangeEntry c)
        {
            if (c == null) return "";
            string when;
            switch (c.trigger)
            {
                case FormTrigger.HeldItem: when = $"al entrar llevando {Name<ItemData>(c.item)}"; break;
                case FormTrigger.UseMove: when = $"{(c.afterMove ? "después de usar" : "al usar")} {Name<MoveData>(c.move)}"; break;
                case FormTrigger.DamagingMove: when = $"{(c.afterMove ? "después de usar" : "al usar")} cualquier ataque"; break;
                case FormTrigger.HpBelow: when = $"al final del turno con menos del {c.hpPercent} % de PS"; break;
                case FormTrigger.HpAtLeast: when = $"al final del turno con el {c.hpPercent} % de PS o más"; break;
                case FormTrigger.Weather: when = string.IsNullOrWhiteSpace(c.weather) ? "cuando no hay clima" : $"con {Name<WeatherData>(c.weather)}"; break;
                case FormTrigger.MegaEvolution:
                    when = string.IsNullOrWhiteSpace(c.item) ? $"al megaevolucionar sabiendo {Name<MoveData>(c.move)}" : $"al megaevolucionar con {Name<ItemData>(c.item)}";
                    break;
                default: when = c.trigger.ToString(); break;
            }
            string ability = string.IsNullOrWhiteSpace(c.requiredAbility) ? "" : $" (necesita {Name<AbilityData>(c.requiredAbility)})";
            return $"De {FormName(s, c.from)} a {FormName(s, c.to)}: {when}{ability}.";
        }

        /// <summary>Problemas de las formas de una especie (formas sin id, cambios a formas que no existen...).</summary>
        public static List<string> Problems(SpeciesData s)
        {
            var list = new List<string>();
            if (s == null) return list;
            var forms = (s.Forms ?? new SpeciesData.FormEntry[0]).Where(f => f != null).ToList();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in forms)
            {
                if (string.IsNullOrWhiteSpace(f.id)) list.Add("Hay una forma sin id.");
                else if (!ids.Add(f.id)) list.Add($"La forma '{f.id}' está repetida.");
            }
            foreach (var c in (s.FormChanges ?? new SpeciesData.FormChangeEntry[0]).Where(c => c != null))
            {
                foreach (var fid in new[] { c.from, c.to })
                    if (!string.IsNullOrEmpty(fid) && fid != FormChange.AnyForm && !ids.Contains(fid))
                        list.Add($"Un cambio de forma usa '{fid}', que no es una forma de {s.DisplayName}.");
                if (string.Equals(c.from ?? "", c.to ?? "", StringComparison.OrdinalIgnoreCase))
                    list.Add($"Un cambio de forma va de «{FormName(s, c.from)}» a sí misma.");
                if (c.trigger == FormTrigger.HeldItem && string.IsNullOrWhiteSpace(c.item))
                    list.Add($"El cambio a «{FormName(s, c.to)}» necesita un objeto.");
                if (c.trigger == FormTrigger.MegaEvolution && string.IsNullOrWhiteSpace(c.item) && string.IsNullOrWhiteSpace(c.move))
                    list.Add($"La megaevolución a «{FormName(s, c.to)}» necesita su megapiedra (o un movimiento, como Rayquaza).");
                if (c.trigger == FormTrigger.UseMove && string.IsNullOrWhiteSpace(c.move))
                    list.Add($"El cambio a «{FormName(s, c.to)}» necesita un movimiento.");
            }
            if (!string.IsNullOrWhiteSpace(s.FormOf))
            {
                if (s.FormOf == s.Id) list.Add("Una especie no puede ser variante de sí misma.");
                else if (ContentAssets.FindById<SpeciesData>(s.FormOf) == null) list.Add($"Es variante de '{s.FormOf}', que no existe.");
            }
            return list;
        }

        // ---------------- Plantillas de cambio ----------------

        /// <summary>Plantillas: (nombre, reglas para la forma F). Los huecos (objeto, movimiento, clima) se rellenan luego.</summary>
        public static readonly (string name, Func<string, SpeciesData.FormChangeEntry[]> rules, bool reverts)[] Templates =
        {
            ("Por PS, ida y vuelta (como Modo Daruma)", f => new[]
            {
                new SpeciesData.FormChangeEntry { from = "", to = f, trigger = FormTrigger.HpBelow, hpPercent = 50 },
                new SpeciesData.FormChangeEntry { from = f, to = "", trigger = FormTrigger.HpAtLeast, hpPercent = 50 },
            }, true),
            ("Al atacar; vuelve con un movimiento (como Cambio Táctico)", f => new[]
            {
                new SpeciesData.FormChangeEntry { from = "", to = f, trigger = FormTrigger.DamagingMove },
                new SpeciesData.FormChangeEntry { from = f, to = "", trigger = FormTrigger.UseMove },
            }, true),
            ("Con un movimiento, ida y vuelta (como Meloetta)", f => new[]
            {
                new SpeciesData.FormChangeEntry { from = "", to = f, trigger = FormTrigger.UseMove, afterMove = true },
                new SpeciesData.FormChangeEntry { from = f, to = "", trigger = FormTrigger.UseMove, afterMove = true },
            }, false),
            ("Llevando un objeto (como Giratina Origen)", f => new[]
            {
                new SpeciesData.FormChangeEntry { from = "", to = f, trigger = FormTrigger.HeldItem },
            }, false),
            ("Con un clima; sin clima vuelve (como Castform)", f => new[]
            {
                new SpeciesData.FormChangeEntry { from = "*", to = f, trigger = FormTrigger.Weather },
                new SpeciesData.FormChangeEntry { from = f, to = "", trigger = FormTrigger.Weather, weather = "" },
            }, true),
            ("Megaevolución (con su megapiedra)", f => new[]
            {
                new SpeciesData.FormChangeEntry { from = "", to = f, trigger = FormTrigger.MegaEvolution },
            }, false),
        };

        // ---------------- Acciones ----------------

        /// <summary>Añade una forma de combate (copia los tipos de la especie) con las reglas de una plantilla (-1 = ninguna). Devuelve su id.</summary>
        public static string AddForm(SpeciesData s, int template = -1, string id = null, string name = null)
        {
            if (s == null) return null;
            var existing = new HashSet<string>((s.Forms ?? new SpeciesData.FormEntry[0]).Where(f => f != null).Select(f => f.id ?? ""), StringComparer.OrdinalIgnoreCase);
            string fid = string.IsNullOrWhiteSpace(id) ? "forma" : id.Trim();
            string baseId = fid;
            for (int n = 2; existing.Contains(fid); n++) fid = baseId + "_" + n;
            var types = (s.Types ?? new ElementTypeData[0]).Where(t => t != null).Select(t => t.Id).ToList();
            bool reverts = template >= 0 && template < Templates.Length && Templates[template].reverts;
            var rules = template >= 0 && template < Templates.Length ? Templates[template].rules(fid) : new SpeciesData.FormChangeEntry[0];
            ContentAssets.Edit(s, so =>
            {
                var arr = so.FindProperty("forms");
                arr.arraySize++;
                var el = arr.GetArrayElementAtIndex(arr.arraySize - 1);
                el.FindPropertyRelative("id").stringValue = fid;
                el.FindPropertyRelative("displayName").stringValue = string.IsNullOrWhiteSpace(name) ? $"{s.DisplayName} ({fid})" : name;
                el.FindPropertyRelative("type1").stringValue = types.Count > 0 ? types[0] : "";
                el.FindPropertyRelative("type2").stringValue = types.Count > 1 ? types[1] : "";
                foreach (var st in new[] { "attack", "defense", "spAttack", "spDefense", "speed" }) el.FindPropertyRelative(st).intValue = 0;
                el.FindPropertyRelative("ability").stringValue = "";
                el.FindPropertyRelative("revertsOnSwitch").boolValue = reverts;
                AppendRules(so, rules);
            });
            return fid;
        }

        /// <summary>Añade las reglas de una plantilla para una forma que ya existe.</summary>
        public static void AddRules(SpeciesData s, string formId, int template)
        {
            if (s == null || template < 0 || template >= Templates.Length) return;
            var rules = Templates[template].rules(formId);
            ContentAssets.Edit(s, so => AppendRules(so, rules));
        }

        private static void AppendRules(SerializedObject so, IEnumerable<SpeciesData.FormChangeEntry> rules)
        {
            var arr = so.FindProperty("formChanges");
            foreach (var c in rules)
            {
                arr.arraySize++;
                var el = arr.GetArrayElementAtIndex(arr.arraySize - 1);
                el.FindPropertyRelative("from").stringValue = c.from ?? "";
                el.FindPropertyRelative("to").stringValue = c.to ?? "";
                el.FindPropertyRelative("trigger").enumValueIndex = (int)c.trigger;
                el.FindPropertyRelative("item").stringValue = c.item ?? "";
                el.FindPropertyRelative("move").stringValue = c.move ?? "";
                el.FindPropertyRelative("hpPercent").intValue = c.hpPercent;
                el.FindPropertyRelative("weather").stringValue = c.weather ?? "";
                el.FindPropertyRelative("requiredAbility").stringValue = c.requiredAbility ?? "";
                el.FindPropertyRelative("afterMove").boolValue = c.afterMove;
            }
        }

        /// <summary>Quita una forma de combate y los cambios que llevan a ella o salen de ella.</summary>
        public static void RemoveForm(SpeciesData s, string formId)
        {
            if (s == null || string.IsNullOrEmpty(formId)) return;
            ContentAssets.Edit(s, so =>
            {
                var forms = so.FindProperty("forms");
                for (int i = forms.arraySize - 1; i >= 0; i--)
                    if (string.Equals(forms.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue, formId, StringComparison.OrdinalIgnoreCase))
                        forms.DeleteArrayElementAtIndex(i);
                var rules = so.FindProperty("formChanges");
                for (int i = rules.arraySize - 1; i >= 0; i--)
                {
                    var el = rules.GetArrayElementAtIndex(i);
                    if (string.Equals(el.FindPropertyRelative("from").stringValue, formId, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(el.FindPropertyRelative("to").stringValue, formId, StringComparison.OrdinalIgnoreCase))
                        rules.DeleteArrayElementAtIndex(i);
                }
            });
        }

        /// <summary>Las variantes de una especie (las que dicen «es forma de» ella).</summary>
        public static List<SpeciesData> VariantsOf(SpeciesData s, IEnumerable<SpeciesData> all = null)
            => s == null || string.IsNullOrEmpty(s.Id) ? new List<SpeciesData>()
                : (all ?? ContentAssets.LoadAll<SpeciesData>()).Where(x => x != null && x != s && string.Equals(x.FormOf, s.Id, StringComparison.OrdinalIgnoreCase)).ToList();

        /// <summary>
        /// Crea una VARIANTE: copia completa de la especie (datos, movimientos, Pokédex) con «es forma de» apuntando a ella.
        /// Luego se cambian sus tipos, estadísticas o habilidades. Devuelve la nueva especie.
        /// </summary>
        public static SpeciesData CreateVariant(SpeciesData s, string suffix = "variante")
        {
            if (s == null) return null;
            string src = AssetDatabase.GetAssetPath(s);
            string dst = AssetDatabase.GenerateUniqueAssetPath(src);
            if (!AssetDatabase.CopyAsset(src, dst)) return null;
            ContentAssets.ClearCache();
            var copy = AssetDatabase.LoadAssetAtPath<SpeciesData>(dst);
            string newId = s.Id + "_" + suffix;
            for (int n = 2; ContentAssets.FindById<SpeciesData>(newId) != null; n++) newId = s.Id + "_" + suffix + n;
            string baseId = s.Id, name = s.DisplayName + " (" + suffix + ")";
            ContentAssets.Edit(copy, so =>
            {
                ContentAssets.SetString(so, "id", newId);
                ContentAssets.SetString(so, "displayName", name);
                so.FindProperty("formOf").stringValue = baseId;
                so.FindProperty("variantItem").stringValue = "";
                so.FindProperty("forms").arraySize = 0;
                so.FindProperty("formChanges").arraySize = 0;
                so.FindProperty("evolutions").arraySize = 0;   // una variante no hereda las evoluciones de la base
            });
            AssetDatabase.SaveAssets();
            return copy;
        }
    }
}
