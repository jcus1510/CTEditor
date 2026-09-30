using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using CTEditor.GameDefinition.Domain.Conditions;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// Las CONDICIONES en texto, en dos formatos:
    ///
    ///   1) FRASE para el autor (lo que se ve en los editores):
    ///        "el rival tiene 50% de vida o menos", "hace el clima 'rain'", "el movimiento hace contacto".
    ///
    ///   2) CÓDIGO corto para Excel (y para escribir rápido):
    ///        rival.vida&lt;=50      propio.estado        rival.estado=poison     !propio.estado
    ///        rival.tipo=water     clima=rain           propio.amistad&gt;=200    propio.nivel&gt;=30
    ///        propio.dif_nivel&gt;0  rival.etapa:attack&gt;0 rival.ya_actuo         azar&lt;30
    ///        mov.tipo=fire        mov.categoria=fisico mov.potencia&lt;=60       mov.contacto
    ///        mov.etiqueta=puño
    ///      Varias condiciones se unen con &amp; (se tienen que cumplir TODAS).
    /// </summary>
    public static class ConditionText
    {
        // ---------------- Frase en español ----------------

        public static string Describe(Condition c)
        {
            string who = c.Subject == ConditionSubject.Self ? "propio" : "el rival";
            string whoCap = c.Subject == ConditionSubject.Self ? "Propio" : "El rival";
            string not = c.Negate ? "NO " : "";
            string body;
            switch (c.Kind)
            {
                case ConditionKind.HasAnyStatus: body = $"{whoCap} {not}tiene algún estado"; break;
                case ConditionKind.HasStatus: body = $"{whoCap} {not}tiene el estado '{c.Text}'"; break;
                case ConditionKind.HpPercent: body = $"{whoCap} {not}tiene {Cmp(c)} {Num(c.Number)}% de vida"; break;
                case ConditionKind.IsType: body = $"{whoCap} {not}es de tipo '{c.Text}'"; break;
                case ConditionKind.Weather: body = $"{(c.Negate ? "NO " : "")}hace el clima '{c.Text}'"; break;
                case ConditionKind.Friendship: body = $"La amistad de {who} {not}es {Cmp(c)} {Num(c.Number)}"; break;
                case ConditionKind.Level: body = $"El nivel de {who} {not}es {Cmp(c)} {Num(c.Number)}"; break;
                case ConditionKind.LevelDifference: body = $"El nivel de {who} menos el del otro {not}es {Cmp(c)} {Num(c.Number)}"; break;
                case ConditionKind.StatStage: body = $"La etapa de {StatLabels.NameOf(c.Text)} de {who} {not}es {Cmp(c)} {Num(c.Number)}"; break;
                case ConditionKind.AlreadyActed: body = $"{whoCap} {not}ya actuó este turno"; break;
                case ConditionKind.MoveType: body = $"El movimiento {not}es de tipo '{c.Text}'"; break;
                case ConditionKind.MoveCategory: body = $"El movimiento {not}es {CategoryName(c.Text)}"; break;
                case ConditionKind.MovePower: body = $"La potencia base del movimiento {not}es {Cmp(c)} {Num(c.Number)}"; break;
                case ConditionKind.MoveMakesContact: body = $"El movimiento {not}hace contacto"; break;
                case ConditionKind.MoveHasTag: body = $"El movimiento {not}tiene la etiqueta '{c.Text}'"; break;
                case ConditionKind.RandomChance: body = $"{(c.Negate ? "NO " : "")}sale un {Num(c.Number)}% de azar"; break;
                case ConditionKind.DamagedThisTurn: body = $"{whoCap} {not}recibió daño este turno"; break;
                case ConditionKind.TurnsOnField: body = $"{whoCap} {not}lleva {Cmp(c)} {Num(c.Number)} turnos en el campo"; break;
                case ConditionKind.TargetChoseAttack: body = $"{whoCap} {not}eligió un ataque este turno"; break;
                case ConditionKind.UsedAllOtherMoves: body = $"{whoCap} {not}ya usó todos sus otros movimientos"; break;
                case ConditionKind.StockpileCount: body = $"Las reservas de {who} {not}son {Cmp(c)} {Num(c.Number)}"; break;
                case ConditionKind.MoveEffectiveness: body = $"La eficacia del movimiento contra {who} {not}es {Cmp(c)} ×{Num(c.Number)}"; break;
                case ConditionKind.MoveHasSecondary: body = $"El movimiento {not}tiene efectos secundarios"; break;
                case ConditionKind.HasItem: body = $"{whoCap} {not}lleva objeto"; break;
                case ConditionKind.LostItem: body = $"{whoCap} {not}perdió su objeto"; break;
                case ConditionKind.WeightKg: body = $"El peso de {who} {not}es {Cmp(c)} {Num(c.Number)} kg"; break;
                case ConditionKind.SameGender: body = $"{(c.Negate ? "NO " : "")}son del mismo género"; break;
                case ConditionKind.OppositeGender: body = $"{(c.Negate ? "NO " : "")}son de género opuesto"; break;
                case ConditionKind.FieldCondition: body = $"{(c.Negate ? "NO " : "")}está activo el efecto de campo '{c.Text}'"; break;
                case ConditionKind.CanEvolve: body = $"{whoCap} {not}puede evolucionar"; break;
                case ConditionKind.MoveIs: body = $"el movimiento {not}es {c.Text}"; break;
                case ConditionKind.IsSpecies: body = $"{whoCap} {not}es un {c.Text}"; break;
                default: body = c.Kind.ToString(); break;
            }
            return body;
        }

        public static string Describe(IEnumerable<Condition> conditions)
        {
            var list = conditions?.ToList() ?? new List<Condition>();
            return list.Count == 0 ? "siempre" : "si " + string.Join(" y ", list.Select(c => LowerFirst(Describe(c))));
        }

        public static string Describe(ConditionData d) => Describe(FromData(d));

        private static string LowerFirst(string s) => string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);

        private static string Cmp(Condition c)
        {
            switch (c.Comparison)
            {
                case Comparison.Less: return "menos de";
                case Comparison.LessOrEqual: return "como mucho";
                case Comparison.Equal: return "exactamente";
                case Comparison.GreaterOrEqual: return "al menos";
                case Comparison.Greater: return "más de";
                default: return "distinto de";
            }
        }

        private static string Num(float v) => v.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',');

        public static string CategoryName(string text)
        {
            switch (NormalizeCategory(text))
            {
                case "Physical": return "físico";
                case "Special": return "especial";
                case "Status": return "de estado";
                default: return $"'{text}'";
            }
        }

        public static string NormalizeCategory(string text)
        {
            switch ((text ?? "").Trim().ToLowerInvariant())
            {
                case "fisico": case "físico": case "physical": return "Physical";
                case "especial": case "special": return "Special";
                case "estado": case "status": return "Status";
                default: return text ?? "";
            }
        }

        // ---------------- Datos <-> dominio ----------------

        public static Condition FromData(ConditionData d)
            => d == null ? new Condition(ConditionKind.HasAnyStatus) : new Condition(d.kind, d.subject, d.comparison, d.number, d.text, d.negate);

        /// <summary>Escribe una condición en un elemento de array serializado (ConditionData).</summary>
        public static void Write(SerializedProperty el, Condition c)
        {
            el.FindPropertyRelative("kind").intValue = (int)c.Kind;
            el.FindPropertyRelative("subject").intValue = (int)c.Subject;
            el.FindPropertyRelative("comparison").intValue = (int)c.Comparison;
            el.FindPropertyRelative("number").floatValue = c.Number;
            el.FindPropertyRelative("text").stringValue = c.Text ?? "";
            el.FindPropertyRelative("negate").boolValue = c.Negate;
        }

        public static void WriteAll(SerializedProperty array, IList<Condition> conditions)
        {
            array.arraySize = conditions?.Count ?? 0;
            for (int i = 0; i < array.arraySize; i++) Write(array.GetArrayElementAtIndex(i), conditions[i]);
        }

        /// <summary>Escribe una lista de modificadores de potencia (PowerModifierData[]).</summary>
        public static void WriteModifiers(SerializedProperty array, IList<(float multiplier, Condition[] conditions)> mods)
        {
            array.arraySize = mods?.Count ?? 0;
            for (int i = 0; i < array.arraySize; i++)
            {
                var el = array.GetArrayElementAtIndex(i);
                el.FindPropertyRelative("multiplier").floatValue = mods[i].multiplier;
                WriteAll(el.FindPropertyRelative("conditions"), mods[i].conditions);
            }
        }

        // ---------------- Código corto (Excel) ----------------

        private static readonly (string op, Comparison cmp)[] Ops =
        {
            ("<=", Comparison.LessOrEqual), (">=", Comparison.GreaterOrEqual), ("!=", Comparison.NotEqual),
            ("<", Comparison.Less), (">", Comparison.Greater), ("=", Comparison.Equal)
        };

        private static string OpText(Comparison c) => Ops.First(o => o.cmp == c).op;

        public static string Format(Condition c)
        {
            string not = c.Negate ? "!" : "";
            string s = c.Subject == ConditionSubject.Self ? "propio" : "rival";
            string n = c.Number.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',');
            switch (c.Kind)
            {
                case ConditionKind.HasAnyStatus: return $"{not}{s}.estado";
                case ConditionKind.HasStatus: return $"{not}{s}.estado={c.Text}";
                case ConditionKind.HpPercent: return $"{not}{s}.vida{OpText(c.Comparison)}{n}";
                case ConditionKind.IsType: return $"{not}{s}.tipo={c.Text}";
                case ConditionKind.Weather: return $"{not}clima={c.Text}";
                case ConditionKind.Friendship: return $"{not}{s}.amistad{OpText(c.Comparison)}{n}";
                case ConditionKind.Level: return $"{not}{s}.nivel{OpText(c.Comparison)}{n}";
                case ConditionKind.LevelDifference: return $"{not}{s}.dif_nivel{OpText(c.Comparison)}{n}";
                case ConditionKind.StatStage: return $"{not}{s}.etapa:{c.Text}{OpText(c.Comparison)}{n}";
                case ConditionKind.AlreadyActed: return $"{not}{s}.ya_actuo";
                case ConditionKind.MoveType: return $"{not}mov.tipo={c.Text}";
                case ConditionKind.MoveCategory: return $"{not}mov.categoria={CategoryName(c.Text).Replace("í", "i").Replace("de ", "")}";
                case ConditionKind.MovePower: return $"{not}mov.potencia{OpText(c.Comparison)}{n}";
                case ConditionKind.MoveMakesContact: return $"{not}mov.contacto";
                case ConditionKind.MoveHasTag: return $"{not}mov.etiqueta={c.Text}";
                case ConditionKind.RandomChance: return $"{not}azar<{n}";
                case ConditionKind.DamagedThisTurn: return $"{not}{s}.dañado";
                case ConditionKind.TurnsOnField: return $"{not}{s}.turnos_campo{OpText(c.Comparison)}{n}";
                case ConditionKind.TargetChoseAttack: return $"{not}{s}.va_a_atacar";
                case ConditionKind.UsedAllOtherMoves: return $"{not}{s}.uso_todos";
                case ConditionKind.StockpileCount: return $"{not}{s}.reserva{OpText(c.Comparison)}{n}";
                case ConditionKind.MoveEffectiveness: return $"{not}{s}.eficacia{OpText(c.Comparison)}{n}";
                case ConditionKind.MoveHasSecondary: return $"{not}mov.secundario";
                case ConditionKind.HasItem: return $"{not}{s}.objeto";
                case ConditionKind.LostItem: return $"{not}{s}.perdio_objeto";
                case ConditionKind.WeightKg: return $"{not}{s}.peso{OpText(c.Comparison)}{n}";
                case ConditionKind.SameGender: return $"{not}mismo_genero";
                case ConditionKind.OppositeGender: return $"{not}genero_opuesto";
                case ConditionKind.FieldCondition: return $"{not}campo={c.Text}";
                case ConditionKind.CanEvolve: return $"{not}{s}.puede_evolucionar";
                case ConditionKind.MoveIs: return $"{not}mov.id={c.Text}";
                case ConditionKind.IsSpecies: return $"{not}{s}.especie={c.Text}";
                default: return "";
            }
        }

        public static string FormatAll(IEnumerable<Condition> conditions) => string.Join(" & ", conditions.Select(Format));

        /// <summary>Lee varias condiciones unidas con &amp;. Lanza FormatException con un mensaje claro.</summary>
        public static List<Condition> ParseAll(string text)
        {
            var list = new List<Condition>();
            foreach (var part in (text ?? "").Split('&'))
                if (part.Trim().Length > 0) list.Add(Parse(part));
            return list;
        }

        public static Condition Parse(string text)
        {
            string t = (text ?? "").Trim();
            if (t.StartsWith("si ", StringComparison.OrdinalIgnoreCase)) t = t.Substring(3).Trim();
            bool negate = false;
            if (t.StartsWith("!")) { negate = true; t = t.Substring(1).Trim(); }
            else if (t.StartsWith("no ", StringComparison.OrdinalIgnoreCase)) { negate = true; t = t.Substring(3).Trim(); }

            // Separa "lado izquierdo" OP "valor".
            string left = t, value = null; Comparison cmp = Comparison.Equal; bool hasOp = false;
            foreach (var (op, c) in Ops)
            {
                int i = t.IndexOf(op, StringComparison.Ordinal);
                if (i > 0) { left = t.Substring(0, i).Trim(); value = t.Substring(i + op.Length).Trim(); cmp = c; hasOp = true; break; }
            }
            left = left.ToLowerInvariant();

            ConditionSubject subject = ConditionSubject.Self;
            string prop = left;
            if (left.StartsWith("propio.")) { subject = ConditionSubject.Self; prop = left.Substring(7); }
            else if (left.StartsWith("rival.")) { subject = ConditionSubject.Other; prop = left.Substring(6); }
            else if (left.StartsWith("mov.")) prop = "mov." + left.Substring(4);

            float Number()
            {
                if (value == null || !Csv.CsvTable.TryNumber(value, out float f))
                    throw new FormatException($"'{text}': falta un número tras la comparación (ej. rival.vida<50).");
                return f;
            }
            string Id()
            {
                if (string.IsNullOrWhiteSpace(value)) throw new FormatException($"'{text}': falta el valor tras '=' (ej. clima=rain).");
                return value;
            }

            if (prop.StartsWith("etapa:"))
                return new Condition(ConditionKind.StatStage, subject, cmp, Number(), prop.Substring(6), negate);

            switch (prop)
            {
                case "estado":
                    return hasOp ? new Condition(ConditionKind.HasStatus, subject, Comparison.Equal, 0, Id(), negate)
                                 : new Condition(ConditionKind.HasAnyStatus, subject, Comparison.Equal, 0, null, negate);
                case "vida": return new Condition(ConditionKind.HpPercent, subject, cmp, Number(), null, negate);
                case "tipo": return new Condition(ConditionKind.IsType, subject, Comparison.Equal, 0, Id(), negate);
                case "clima": return new Condition(ConditionKind.Weather, ConditionSubject.Self, Comparison.Equal, 0, Id(), negate);
                case "amistad": return new Condition(ConditionKind.Friendship, subject, cmp, Number(), null, negate);
                case "nivel": return new Condition(ConditionKind.Level, subject, cmp, Number(), null, negate);
                case "dif_nivel": return new Condition(ConditionKind.LevelDifference, subject, cmp, Number(), null, negate);
                case "ya_actuo": return new Condition(ConditionKind.AlreadyActed, subject, Comparison.Equal, 0, null, negate);
                case "azar": return new Condition(ConditionKind.RandomChance, ConditionSubject.Self, Comparison.Less, Number(), null, negate);
                case "mov.tipo": return new Condition(ConditionKind.MoveType, ConditionSubject.Self, Comparison.Equal, 0, Id(), negate);
                case "mov.categoria": return new Condition(ConditionKind.MoveCategory, ConditionSubject.Self, Comparison.Equal, 0, NormalizeCategory(Id()), negate);
                case "mov.potencia": return new Condition(ConditionKind.MovePower, ConditionSubject.Self, cmp, Number(), null, negate);
                case "mov.contacto": return new Condition(ConditionKind.MoveMakesContact, ConditionSubject.Self, Comparison.Equal, 0, null, negate);
                case "mov.etiqueta": return new Condition(ConditionKind.MoveHasTag, ConditionSubject.Self, Comparison.Equal, 0, Id(), negate);
                case "dañado": case "danado": return new Condition(ConditionKind.DamagedThisTurn, subject, Comparison.Equal, 0, null, negate);
                case "turnos_campo": return new Condition(ConditionKind.TurnsOnField, subject, cmp, Number(), null, negate);
                case "va_a_atacar": return new Condition(ConditionKind.TargetChoseAttack, subject, Comparison.Equal, 0, null, negate);
                case "uso_todos": return new Condition(ConditionKind.UsedAllOtherMoves, subject, Comparison.Equal, 0, null, negate);
                case "reserva": return new Condition(ConditionKind.StockpileCount, subject, cmp, Number(), null, negate);
                case "eficacia": return new Condition(ConditionKind.MoveEffectiveness, subject, cmp, Number(), null, negate);
                case "mov.secundario": return new Condition(ConditionKind.MoveHasSecondary, ConditionSubject.Self, Comparison.Equal, 0, null, negate);
                case "objeto": return new Condition(ConditionKind.HasItem, subject, Comparison.Equal, 0, null, negate);
                case "perdio_objeto": return new Condition(ConditionKind.LostItem, subject, Comparison.Equal, 0, null, negate);
                case "peso": return new Condition(ConditionKind.WeightKg, subject, cmp, Number(), null, negate);
                case "mismo_genero": return new Condition(ConditionKind.SameGender, ConditionSubject.Self, Comparison.Equal, 0, null, negate);
                case "genero_opuesto": return new Condition(ConditionKind.OppositeGender, ConditionSubject.Self, Comparison.Equal, 0, null, negate);
                case "campo": return new Condition(ConditionKind.FieldCondition, ConditionSubject.Self, Comparison.Equal, 0, Id(), negate);
                case "puede_evolucionar": return new Condition(ConditionKind.CanEvolve, subject, Comparison.Equal, 0, null, negate);
                case "mov.id": return new Condition(ConditionKind.MoveIs, ConditionSubject.Self, Comparison.Equal, 0, Id(), negate);
                case "especie": return new Condition(ConditionKind.IsSpecies, subject, Comparison.Equal, 0, Id(), negate);
            }
            throw new FormatException($"'{text}': condición desconocida. Usa, por ejemplo: rival.vida<50, propio.estado, rival.estado=poison, clima=rain, " +
                                      "propio.amistad>=200, propio.nivel>=30, rival.tipo=water, mov.contacto, mov.etiqueta=puño, azar<30, " +
                                      "propio.dañado, propio.turnos_campo<1, rival.va_a_atacar, propio.reserva>=1, rival.eficacia>1, mov.secundario, propio.objeto, rival.peso>=100, campo=grassy_terrain, propio.puede_evolucionar, mov.id=thunderbolt, propio.especie=pikachu.");
        }

        // ---------------- Modificadores de potencia en texto: "x2 [si propio.estado] | x1,5 [si clima=rain]" ----------------

        public static string FormatModifiers(IEnumerable<(float multiplier, IReadOnlyList<Condition> conditions)> mods)
            => string.Join(" | ", mods.Select(m =>
                "x" + m.multiplier.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',') +
                (m.conditions.Count > 0 ? " [si " + FormatAll(m.conditions) + "]" : "")));

        public static List<(float multiplier, Condition[] conditions)> ParseModifiers(string text)
        {
            var list = new List<(float, Condition[])>();
            foreach (var raw in (text ?? "").Split('|'))
            {
                string item = raw.Trim();
                if (item.Length == 0) continue;
                var conditions = new List<Condition>();
                int br = item.IndexOf('[');
                if (br >= 0)
                {
                    int close = item.LastIndexOf(']');
                    if (close < br) throw new FormatException($"'{raw}': falta cerrar el corchete ].");
                    conditions = ParseAll(item.Substring(br + 1, close - br - 1));
                    item = item.Substring(0, br).Trim();
                }
                if (item.StartsWith("x") || item.StartsWith("×") || item.StartsWith("X")) item = item.Substring(1);
                if (!Csv.CsvTable.TryNumber(item, out float mult) || mult < 0)
                    throw new FormatException($"'{raw}': usa x<número> [si condición], ej. x2 [si propio.estado].");
                list.Add((mult, conditions.ToArray()));
            }
            return list;
        }
    }
}
