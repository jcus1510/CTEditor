using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using CTEditor.GameDefinition.Domain.Species;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;
using Cond = CTEditor.GameDefinition.Infrastructure.ScriptableObjects.SpeciesData.EvolutionConditionData;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// Las evoluciones EXPLICADAS en frases y las PLANTILLAS de condiciones (Espeon, Umbreon, Hitmonlee,
    /// Sylveon...). Lo usan el editor de cadenas evolutivas, el de especies y el validador, para que
    /// todos digan lo mismo.
    /// </summary>
    public static class EvolutionText
    {
        /// <summary>"al subir de nivel con amistad ≥ 220, de día".</summary>
        public static string Describe(SpeciesData.EvolutionEntry e)
        {
            string how;
            switch (e.method)
            {
                case EvolutionMethod.Item: how = $"al usar {ItemName(e.itemId)}"; break;
                case EvolutionMethod.Friendship: how = $"al subir de nivel con amistad ≥ {(e.minFriendship > 0 ? e.minFriendship : 220)}"; break;
                case EvolutionMethod.Trade: how = "al intercambiarlo" + (string.IsNullOrWhiteSpace(e.itemId) ? "" : $" llevando {ItemName(e.itemId)}"); break;
                case EvolutionMethod.LevelUp: how = "al subir de nivel"; break;
                default: how = $"al llegar al nivel {e.requiredLevel}"; break;
            }
            if (e.method != EvolutionMethod.Level && e.requiredLevel > 0) how += $" (desde el nivel {e.requiredLevel})";
            var conds = (e.conditions ?? new Cond[0]).Where(c => c != null).Select(Describe).ToList();
            return conds.Count == 0 ? how : how + ", " + string.Join(" y ", conds);
        }

        /// <summary>Texto corto para la flecha del árbol: "Nv 20 · Atq>Def".</summary>
        public static string Short(SpeciesData.EvolutionEntry e)
        {
            string how;
            switch (e.method)
            {
                case EvolutionMethod.Item: how = "🪨 " + ItemName(e.itemId); break;
                case EvolutionMethod.Friendship: how = "♥ " + (e.minFriendship > 0 ? e.minFriendship : 220); break;
                case EvolutionMethod.Trade: how = "⇄" + (string.IsNullOrWhiteSpace(e.itemId) ? "" : " " + ItemName(e.itemId)); break;
                case EvolutionMethod.LevelUp: how = "▲ nivel"; break;
                default: how = "Nv " + e.requiredLevel; break;
            }
            var conds = (e.conditions ?? new Cond[0]).Where(c => c != null).Select(ShortOf).ToList();
            return conds.Count == 0 ? how : how + " · " + string.Join(" · ", conds);
        }

        public static string Describe(Cond c)
        {
            string s;
            switch (c.check)
            {
                case EvolutionConditionKind.MinLevel: s = $"con nivel {c.value} o más"; break;
                case EvolutionConditionKind.MinFriendship: s = $"con amistad {c.value} o más"; break;
                case EvolutionConditionKind.HoldsItem: s = $"llevando {ItemName(c.itemId)}"; break;
                case EvolutionConditionKind.KnowsMove: s = $"sabiendo {(c.move != null ? c.move.DisplayName : "¿movimiento?")}"; break;
                case EvolutionConditionKind.KnowsMoveOfType: s = $"sabiendo un movimiento de tipo {(c.type != null ? c.type.DisplayName : "¿tipo?")}"; break;
                case EvolutionConditionKind.TimeOfDay: s = Etiquetas.Enum(c.time.ToString()).ToLowerInvariant(); break;
                case EvolutionConditionKind.AtLocation: s = $"en el lugar «{(string.IsNullOrWhiteSpace(c.text) ? "¿lugar?" : c.text)}»"; break;
                case EvolutionConditionKind.MapWeather: s = $"con clima {WeatherName(c.weatherId)} en el mapa"; break;
                case EvolutionConditionKind.StatRelation: s = "con " + Etiquetas.Enum(c.relation.ToString()).ToLowerInvariant(); break;
                case EvolutionConditionKind.PartyHasSpecies: s = $"con {(c.species != null ? c.species.DisplayName : "¿especie?")} en el equipo"; break;
                case EvolutionConditionKind.PartyHasType: s = $"con un tipo {(c.type != null ? c.type.DisplayName : "¿tipo?")} en el equipo"; break;
                case EvolutionConditionKind.Nature: s = $"con naturaleza {(c.nature != null ? c.nature.DisplayName : "¿naturaleza?")}"; break;
                case EvolutionConditionKind.Chance: s = $"solo el {c.value}% de los individuos (según su personalidad)"; break;
                case EvolutionConditionKind.GameFlag: s = $"con la marca «{(string.IsNullOrWhiteSpace(c.text) ? "¿marca?" : c.text)}» activa"; break;
                case EvolutionConditionKind.Gender: s = c.value == 2 ? "si es hembra" : "si es macho"; break;
                default: s = c.check.ToString(); break;
            }
            return c.negate ? "NO " + s : s;
        }

        private static string ShortOf(Cond c)
        {
            string s;
            switch (c.check)
            {
                case EvolutionConditionKind.MinLevel: s = "Nv≥" + c.value; break;
                case EvolutionConditionKind.MinFriendship: s = "♥≥" + c.value; break;
                case EvolutionConditionKind.HoldsItem: s = "🎒" + ItemName(c.itemId); break;
                case EvolutionConditionKind.KnowsMove: s = "sabe " + (c.move != null ? c.move.DisplayName : "?"); break;
                case EvolutionConditionKind.KnowsMoveOfType: s = "mov. " + (c.type != null ? c.type.DisplayName : "?"); break;
                case EvolutionConditionKind.TimeOfDay:
                    s = c.time == DayTime.Night ? "🌙" : c.time == DayTime.Day ? "☀" : c.time == DayTime.Morning ? "🌅" : "🌇"; break;
                case EvolutionConditionKind.AtLocation: s = "📍" + c.text; break;
                case EvolutionConditionKind.MapWeather: s = "☁" + WeatherName(c.weatherId); break;
                case EvolutionConditionKind.StatRelation:
                    s = c.relation == StatRelation.AttackHigher ? "Atq>Def" : c.relation == StatRelation.DefenseHigher ? "Atq<Def" : "Atq=Def"; break;
                case EvolutionConditionKind.PartyHasSpecies: s = "+" + (c.species != null ? c.species.DisplayName : "?"); break;
                case EvolutionConditionKind.PartyHasType: s = "+tipo " + (c.type != null ? c.type.DisplayName : "?"); break;
                case EvolutionConditionKind.Nature: s = c.nature != null ? c.nature.DisplayName : "?"; break;
                case EvolutionConditionKind.Chance: s = c.value + "%"; break;
                case EvolutionConditionKind.GameFlag: s = "🚩" + c.text; break;
                case EvolutionConditionKind.Gender: s = c.value == 2 ? "♀" : "♂"; break;
                default: s = "?"; break;
            }
            return c.negate ? "¬" + s : s;
        }

        /// <summary>Qué falta por rellenar en una condición (null = está bien).</summary>
        public static string Problem(Cond c)
        {
            switch (c.check)
            {
                case EvolutionConditionKind.HoldsItem:
                    if (string.IsNullOrWhiteSpace(c.itemId)) return "falta el objeto";
                    return ContentAssets.FindById<ItemData>(c.itemId) == null ? $"el objeto '{c.itemId}' no existe" : null;
                case EvolutionConditionKind.KnowsMove: return c.move == null ? "falta el movimiento" : null;
                case EvolutionConditionKind.KnowsMoveOfType: case EvolutionConditionKind.PartyHasType: return c.type == null ? "falta el tipo" : null;
                case EvolutionConditionKind.PartyHasSpecies: return c.species == null ? "falta la especie" : null;
                case EvolutionConditionKind.Nature: return c.nature == null ? "falta la naturaleza" : null;
                case EvolutionConditionKind.MapWeather: return string.IsNullOrWhiteSpace(c.weatherId) ? "falta el clima" : null;
                case EvolutionConditionKind.AtLocation: return string.IsNullOrWhiteSpace(c.text) ? "falta el lugar" : null;
                case EvolutionConditionKind.GameFlag: return string.IsNullOrWhiteSpace(c.text) ? "falta la marca" : null;
                case EvolutionConditionKind.Chance: return c.value < 1 || c.value > 99 ? "el porcentaje debe ir de 1 a 99" : null;
                case EvolutionConditionKind.MinFriendship: return c.value > 255 ? "la amistad llega como mucho a 255" : null;
                case EvolutionConditionKind.MinLevel: return c.value < 1 ? "el nivel mínimo debe ser 1 o más" : null;
                default: return null;
            }
        }

        private static string ItemName(string id)
        {
            var item = string.IsNullOrWhiteSpace(id) ? null : ContentAssets.FindById<ItemData>(id);
            return item != null ? item.DisplayName : (string.IsNullOrWhiteSpace(id) ? "¿objeto?" : id);
        }

        private static string WeatherName(string id)
        {
            var w = string.IsNullOrWhiteSpace(id) ? null : ContentAssets.FindById<WeatherData>(id);
            return w != null ? w.DisplayName : (string.IsNullOrWhiteSpace(id) ? "¿clima?" : id);
        }

        // ---------------- Plantillas ----------------

        /// <summary>Una plantilla: nombre, ejemplo clásico y cómo rellena la evolución.</summary>
        public sealed class Template
        {
            public string Name, Example;
            public EvolutionMethod Method;
            public int Level, Friendship;
            public string ItemId = "";
            public (EvolutionConditionKind kind, int value, string id, DayTime time, StatRelation rel)[] Conditions;
        }

        public static readonly Template[] Templates =
        {
            new Template { Name = "Amistad + de día", Example = "Eevee → Espeon", Method = EvolutionMethod.Friendship, Friendship = 220,
                Conditions = new[] { (EvolutionConditionKind.TimeOfDay, 0, "", DayTime.Day, StatRelation.AttackHigher) } },
            new Template { Name = "Amistad + de noche", Example = "Eevee → Umbreon", Method = EvolutionMethod.Friendship, Friendship = 220,
                Conditions = new[] { (EvolutionConditionKind.TimeOfDay, 0, "", DayTime.Night, StatRelation.AttackHigher) } },
            new Template { Name = "Nivel + Ataque > Defensa", Example = "Tyrogue → Hitmonlee", Method = EvolutionMethod.Level, Level = 20,
                Conditions = new[] { (EvolutionConditionKind.StatRelation, 0, "", DayTime.Day, StatRelation.AttackHigher) } },
            new Template { Name = "Nivel + Defensa > Ataque", Example = "Tyrogue → Hitmonchan", Method = EvolutionMethod.Level, Level = 20,
                Conditions = new[] { (EvolutionConditionKind.StatRelation, 0, "", DayTime.Day, StatRelation.DefenseHigher) } },
            new Template { Name = "Objeto equipado + de noche", Example = "Sneasel → Weavile (Garra Afilada)", Method = EvolutionMethod.LevelUp,
                Conditions = new[] { (EvolutionConditionKind.HoldsItem, 0, "razor_claw", DayTime.Day, StatRelation.AttackHigher),
                                     (EvolutionConditionKind.TimeOfDay, 0, "", DayTime.Night, StatRelation.AttackHigher) } },
            new Template { Name = "Saber un movimiento", Example = "Aipom → Ambipom (Doble Golpe)", Method = EvolutionMethod.LevelUp,
                Conditions = new[] { (EvolutionConditionKind.KnowsMove, 0, "double_hit", DayTime.Day, StatRelation.AttackHigher) } },
            new Template { Name = "Amistad + movimiento de un tipo", Example = "Eevee → Sylveon (Hada)", Method = EvolutionMethod.LevelUp,
                Conditions = new[] { (EvolutionConditionKind.MinFriendship, 160, "", DayTime.Day, StatRelation.AttackHigher),
                                     (EvolutionConditionKind.KnowsMoveOfType, 0, "fairy", DayTime.Day, StatRelation.AttackHigher) } },
            new Template { Name = "Nivel + especie en el equipo", Example = "Mantyke → Mantine (con Remoraid)", Method = EvolutionMethod.LevelUp,
                Conditions = new[] { (EvolutionConditionKind.PartyHasSpecies, 0, "remoraid", DayTime.Day, StatRelation.AttackHigher) } },
            new Template { Name = "Nivel + tipo en el equipo", Example = "Pancham → Pangoro (con Siniestro)", Method = EvolutionMethod.Level, Level = 32,
                Conditions = new[] { (EvolutionConditionKind.PartyHasType, 0, "dark", DayTime.Day, StatRelation.AttackHigher) } },
            new Template { Name = "Nivel + lluvia en el mapa", Example = "Sliggoo → Goodra", Method = EvolutionMethod.Level, Level = 50,
                Conditions = new[] { (EvolutionConditionKind.MapWeather, 0, "rain", DayTime.Day, StatRelation.AttackHigher) } },
            new Template { Name = "Nivel + 50 % de los individuos", Example = "Wurmple → Silcoon / Cascoon", Method = EvolutionMethod.Level, Level = 7,
                Conditions = new[] { (EvolutionConditionKind.Chance, 50, "", DayTime.Day, StatRelation.AttackHigher) } },
            new Template { Name = "Marca de la historia", Example = "Evolución especial tras un evento", Method = EvolutionMethod.LevelUp,
                Conditions = new[] { (EvolutionConditionKind.GameFlag, 0, "evento_especial", DayTime.Day, StatRelation.AttackHigher) } },
        };

        /// <summary>Aplica una plantilla a la evolución 'el' (mantiene la especie destino).</summary>
        public static void Apply(SerializedProperty el, Template t)
        {
            el.FindPropertyRelative("method").intValue = (int)t.Method;
            el.FindPropertyRelative("requiredLevel").intValue = t.Level;
            el.FindPropertyRelative("minFriendship").intValue = t.Friendship;
            el.FindPropertyRelative("itemId").stringValue = t.ItemId ?? "";
            var arr = el.FindPropertyRelative("conditions");
            arr.arraySize = t.Conditions.Length;
            for (int i = 0; i < t.Conditions.Length; i++)
            {
                var (kind, value, id, time, rel) = t.Conditions[i];
                var c = arr.GetArrayElementAtIndex(i);
                Reset(c);
                c.FindPropertyRelative("check").intValue = (int)kind;
                c.FindPropertyRelative("value").intValue = value;
                c.FindPropertyRelative("time").intValue = (int)time;
                c.FindPropertyRelative("relation").intValue = (int)rel;
                SetId(c, kind, id);
            }
        }

        /// <summary>Deja una condición en blanco.</summary>
        public static void Reset(SerializedProperty c)
        {
            c.FindPropertyRelative("value").intValue = 0;
            c.FindPropertyRelative("itemId").stringValue = "";
            c.FindPropertyRelative("move").objectReferenceValue = null;
            c.FindPropertyRelative("type").objectReferenceValue = null;
            c.FindPropertyRelative("species").objectReferenceValue = null;
            c.FindPropertyRelative("nature").objectReferenceValue = null;
            c.FindPropertyRelative("weatherId").stringValue = "";
            c.FindPropertyRelative("text").stringValue = "";
            c.FindPropertyRelative("negate").boolValue = false;
        }

        /// <summary>Escribe el id en el campo que toca según lo que compruebe (si existe ese contenido).</summary>
        public static void SetId(SerializedProperty c, EvolutionConditionKind kind, string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            switch (kind)
            {
                case EvolutionConditionKind.HoldsItem: c.FindPropertyRelative("itemId").stringValue = id; break;
                case EvolutionConditionKind.KnowsMove: c.FindPropertyRelative("move").objectReferenceValue = ContentAssets.FindById<MoveData>(id); break;
                case EvolutionConditionKind.KnowsMoveOfType:
                case EvolutionConditionKind.PartyHasType: c.FindPropertyRelative("type").objectReferenceValue = ContentAssets.FindById<ElementTypeData>(id); break;
                case EvolutionConditionKind.PartyHasSpecies: c.FindPropertyRelative("species").objectReferenceValue = ContentAssets.FindById<SpeciesData>(id); break;
                case EvolutionConditionKind.Nature: c.FindPropertyRelative("nature").objectReferenceValue = ContentAssets.FindById<NatureData>(id); break;
                case EvolutionConditionKind.MapWeather: c.FindPropertyRelative("weatherId").stringValue = id; break;
                case EvolutionConditionKind.AtLocation:
                case EvolutionConditionKind.GameFlag: c.FindPropertyRelative("text").stringValue = id; break;
            }
        }
    }
}
