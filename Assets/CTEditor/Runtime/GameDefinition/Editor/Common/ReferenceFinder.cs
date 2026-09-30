using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using CTEditor.GameDefinition.Domain.Conditions;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;
using Object = UnityEngine.Object;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>Una referencia encontrada: QUIÉN apunta a la ficha y DESDE qué campo.</summary>
    public sealed class ContentReference
    {
        public ScriptableObject Owner;
        /// <summary>Ruta del campo al estilo Unity (p. ej. "learnset.Array.data[2].move").</summary>
        public string PropertyPath;
        /// <summary>true = la referencia es un ID de texto (hay que actualizarla al renombrar).</summary>
        public bool ById;

        /// <summary>Descripción legible: "learnset[3].move".</summary>
        public string Field => Regex.Replace(PropertyPath, @"\.Array\.data\[(\d+)\]", m => $"[{int.Parse(m.Groups[1].Value) + 1}]");
    }

    /// <summary>
    /// "¿QUIÉN USA ESTO?": encuentra todas las fichas que apuntan a una ficha dada, y permite RENOMBRAR
    /// su id actualizando esas referencias.
    ///
    /// Es genérico: recorre por REFLEXIÓN los campos serializados de todas las fichas (también listas y
    /// estructuras anidadas). Reconoce dos clases de referencia:
    ///   - por OBJETO: un campo que apunta al asset (un learnset -> un MoveData). Unity las guarda por su
    ///     GUID interno, así que sobreviven a un renombrado sin hacer nada.
    ///   - por ID de texto: un campo string marcado con [ContentIdReference(typeof(X))] o [StatusIdReference].
    ///     Gracias a esos atributos sabemos A QUÉ TIPO apunta cada texto, y no confundimos el TIPO "poison"
    ///     con el ESTADO "poison" aunque compartan id.
    /// </summary>
    public static class ReferenceFinder
    {
        // Las familias de contenido donde puede haber referencias: TODAS las fichas (ScriptableObject) del
        // ensamblado de contenido. Se calculan una vez por reflexión, así un tipo nuevo (menús, mapas...)
        // entra solo sin tener que acordarse de añadirlo aquí.
        private static Type[] _contentTypes;
        private static Type[] ContentTypes => _contentTypes ?? (_contentTypes =
            typeof(IContentAsset).Assembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(ScriptableObject).IsAssignableFrom(t))
                .ToArray());

        /// <summary>Todas las referencias a 'target' desde cualquier ficha de contenido.</summary>
        public static List<ContentReference> FindReferencesTo(ScriptableObject target)
        {
            var found = new List<ContentReference>();
            if (target == null) return found;
            foreach (var (_, r) in FindReferencesToAny(new[] { target })) found.Add(r);
            return found;
        }

        /// <summary>
        /// Referencias a CUALQUIERA de 'targets' en UNA sola pasada por el proyecto (mucho más rápido que buscar una a una:
        /// la validación de la papelera lo usa con cientos de fichas a la vez). Devuelve (a quién apunta, la referencia).
        /// </summary>
        public static List<(ScriptableObject target, ContentReference reference)> FindReferencesToAny(ICollection<ScriptableObject> targets)
        {
            var found = new List<(ScriptableObject, ContentReference)>();
            if (targets == null || targets.Count == 0) return found;
            var byObject = new HashSet<Object>();
            var byId = new Dictionary<(Type, string), ScriptableObject>();
            foreach (var t in targets)
            {
                if (t == null) continue;
                byObject.Add(t);
                if (t is IContentAsset c && !string.IsNullOrEmpty(c.Id) && !byId.ContainsKey((t.GetType(), c.Id))) byId[(t.GetType(), c.Id)] = t;
            }
            var search = new Search { Objects = byObject, Ids = byId, Found = found };
            foreach (var type in ContentTypes)
            {
                if (!PlanOf(type).MayHaveRefs) continue;
                foreach (var owner in ContentAssets.LoadAll(type))
                {
                    if (byObject.Contains(owner)) continue;
                    search.Owner = owner;
                    Walk(owner, owner.GetType(), "", search, 0);
                }
            }
            return found;
        }

        /// <summary>Referencias a 'target' dentro de UNA ficha concreta (útil para tests y herramientas).</summary>
        public static List<ContentReference> FindIn(ScriptableObject owner, ScriptableObject target)
        {
            var found = new List<ContentReference>();
            if (owner == null || target == null || owner == target) return found;
            var list = new List<(ScriptableObject, ContentReference)>();
            var byId = new Dictionary<(Type, string), ScriptableObject>();
            if (target is IContentAsset c && !string.IsNullOrEmpty(c.Id)) byId[(target.GetType(), c.Id)] = target;
            var search = new Search { Objects = new HashSet<Object> { target }, Ids = byId, Found = list, Owner = owner };
            Walk(owner, owner.GetType(), "", search, 0);
            foreach (var (_, r) in list) found.Add(r);
            return found;
        }

        /// <summary>
        /// Cambia el id de 'target' y actualiza todas las referencias POR ID (las de objeto no lo
        /// necesitan). También renombra el archivo para que coincida. Devuelve cuántas actualizó.
        /// </summary>
        public static int RenameId(ScriptableObject target, string newId)
        {
            if (target == null || string.IsNullOrWhiteSpace(newId)) return 0;
            newId = newId.Trim();
            var refs = FindReferencesTo(target);

            int updated = 0;
            foreach (var r in refs)
            {
                if (!r.ById) continue;
                ContentAssets.Edit(r.Owner, so =>
                {
                    var p = so.FindProperty(r.PropertyPath);
                    if (p != null) { p.stringValue = newId; updated++; }
                });
            }

            ContentAssets.Edit(target, so => ContentAssets.SetString(so, "id", newId));
            string path = AssetDatabase.GetAssetPath(target);
            if (!string.IsNullOrEmpty(path)) AssetDatabase.RenameAsset(path, newId);
            AssetDatabase.SaveAssets();
            return updated;
        }

        // ---------------- Recorrido por reflexión (con un «plan» por tipo, calculado una sola vez) ----------------

        private sealed class Search
        {
            public HashSet<Object> Objects;
            public Dictionary<(Type, string), ScriptableObject> Ids;
            public List<(ScriptableObject, ContentReference)> Found;
            public ScriptableObject Owner;
        }

        private enum FieldKind { ObjectRef, IdString, Nested, Effect, Condition }

        private sealed class FieldPlan
        {
            public FieldInfo Field;
            public bool IsList;
            public Type ElementType;
            public FieldKind Kind;
            public Type IdTarget;        // IdString: a qué tipo de ficha apunta el texto
        }

        private sealed class TypePlan
        {
            public List<FieldPlan> Fields = new List<FieldPlan>();
            public bool MayHaveRefs;
        }

        private static readonly Dictionary<Type, TypePlan> Plans = new Dictionary<Type, TypePlan>();

        private static TypePlan PlanOf(Type type)
        {
            if (Plans.TryGetValue(type, out var plan)) return plan;
            plan = new TypePlan();
            Plans[type] = plan;   // antes de recorrer: tipos que se contienen a sí mismos no dan vueltas sin fin
            foreach (var field in SerializedFields(type))
            {
                Type ft = field.FieldType;
                bool isList = ft.IsArray || (ft.IsGenericType && ft.GetGenericTypeDefinition() == typeof(List<>));
                Type et = isList ? (ft.IsArray ? ft.GetElementType() : ft.GetGenericArguments()[0]) : ft;
                FieldPlan fp = null;
                if (typeof(Object).IsAssignableFrom(et)) fp = new FieldPlan { Kind = FieldKind.ObjectRef };
                // Effect blocks and conditions keep ids as plain text whose kind depends on the action / condition.
                else if (et == typeof(EffectBlockData)) fp = new FieldPlan { Kind = FieldKind.Effect };
                else if (et == typeof(ConditionData)) fp = new FieldPlan { Kind = FieldKind.Condition };
                else if (et == typeof(string))
                {
                    var target = IdTargetOf(field);
                    if (target != null) fp = new FieldPlan { Kind = FieldKind.IdString, IdTarget = target };
                }
                else if (!et.IsPrimitive && !et.IsEnum && et.IsDefined(typeof(SerializableAttribute), false) && PlanOf(et).MayHaveRefs)
                    fp = new FieldPlan { Kind = FieldKind.Nested };
                if (fp == null) continue;
                fp.Field = field; fp.IsList = isList; fp.ElementType = et;
                plan.Fields.Add(fp);
            }
            plan.MayHaveRefs = plan.Fields.Count > 0;
            return plan;
        }

        // ¿A qué tipo de ficha apunta este campo de texto? (null = no es una referencia)
        private static Type IdTargetOf(FieldInfo field)
        {
            var content = field.GetCustomAttribute<ContentIdReferenceAttribute>();
            if (content != null) return content.DataType;
            if (field.GetCustomAttribute<StatusIdReferenceAttribute>() != null) return typeof(StatusConditionData);
            return null;
        }

        private static void Walk(object obj, Type type, string path, Search s, int depth)
        {
            if (obj == null || depth > 6) return;
            foreach (var fp in PlanOf(type).Fields)
            {
                string fieldPath = path.Length == 0 ? fp.Field.Name : path + "." + fp.Field.Name;
                object value = fp.Field.GetValue(obj);
                if (fp.IsList)
                {
                    if (!(value is IList list)) continue;
                    for (int i = 0; i < list.Count; i++) Check(list[i], fp, $"{fieldPath}.Array.data[{i}]", s, depth);
                }
                else Check(value, fp, fieldPath, s, depth);
            }
        }

        private static void Check(object value, FieldPlan fp, string path, Search s, int depth)
        {
            if (value == null) return;
            switch (fp.Kind)
            {
                case FieldKind.ObjectRef:
                    if (value is Object o && s.Objects.Contains(o))
                        s.Found.Add(((ScriptableObject)o, new ContentReference { Owner = s.Owner, PropertyPath = path, ById = false }));
                    break;
                case FieldKind.IdString:
                    var id = (string)value;
                    if (id.Length > 0 && s.Ids.TryGetValue((fp.IdTarget, id), out var target))
                        s.Found.Add((target, new ContentReference { Owner = s.Owner, PropertyPath = path, ById = true }));
                    break;
                case FieldKind.Nested:
                    Walk(value, fp.ElementType, path, s, depth + 1);
                    break;
                case FieldKind.Effect:
                {
                    var b = (EffectBlockData)value;
                    var refType = RefTarget(EffectText.RefOf(b.action));
                    if (refType != null)
                        foreach (var rid in (b.reference ?? "").Split('|'))
                            FoundId(refType, rid.Trim(), path + ".reference", s);
                    var conds = b.conditions ?? new ConditionData[0];
                    for (int i = 0; i < conds.Length; i++) CheckCondition(conds[i], $"{path}.conditions.Array.data[{i}]", s);
                    break;
                }
                case FieldKind.Condition:
                    CheckCondition((ConditionData)value, path, s);
                    break;
            }
        }

        private static void FoundId(Type type, string id, string path, Search s)
        {
            if (id.Length > 0 && s.Ids.TryGetValue((type, id), out var target))
                s.Found.Add((target, new ContentReference { Owner = s.Owner, PropertyPath = path, ById = true }));
        }

        private static void CheckCondition(ConditionData c, string path, Search s)
        {
            if (c == null) return;
            Type type = null;
            switch (c.kind)
            {
                case ConditionKind.MoveType: case ConditionKind.IsType: type = typeof(ElementTypeData); break;
                case ConditionKind.Weather: type = typeof(WeatherData); break;
                case ConditionKind.HasStatus: type = typeof(StatusConditionData); break;
            }
            if (type != null) FoundId(type, (c.text ?? "").Trim(), path + ".text", s);
        }

        // What kind of asset the id of an effect block's «reference» names.
        private static Type RefTarget(EffectRefKind k)
        {
            switch (k)
            {
                case EffectRefKind.Type: case EffectRefKind.TypeList: return typeof(ElementTypeData);
                case EffectRefKind.Status: case EffectRefKind.StatusList: return typeof(StatusConditionData);
                case EffectRefKind.Weather: case EffectRefKind.WeatherList: return typeof(WeatherData);
                case EffectRefKind.Move: return typeof(MoveData);
                case EffectRefKind.Mechanic: return typeof(MechanicData);
                default: return null;
            }
        }

        // Campos que Unity serializa: públicos (no [NonSerialized]) o privados con [SerializeField].
        private static IEnumerable<FieldInfo> SerializedFields(Type type)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (var t = type; t != null && t != typeof(ScriptableObject) && t != typeof(object); t = t.BaseType)
                foreach (var f in t.GetFields(flags))
                {
                    if (f.IsNotSerialized) continue;
                    if (f.IsPublic || f.GetCustomAttribute<SerializeField>() != null) yield return f;
                }
        }
    }
}
