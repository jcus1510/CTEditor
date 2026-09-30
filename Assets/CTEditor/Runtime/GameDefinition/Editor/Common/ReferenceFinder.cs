using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
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
            string targetId = target is IContentAsset c ? c.Id : null;

            foreach (var type in ContentTypes)
                foreach (var owner in ContentAssets.LoadAll(type))
                    found.AddRange(FindIn(owner, target));
            return found;
        }

        /// <summary>Referencias a 'target' dentro de UNA ficha concreta (útil para tests y herramientas).</summary>
        public static List<ContentReference> FindIn(ScriptableObject owner, ScriptableObject target)
        {
            var found = new List<ContentReference>();
            if (owner == null || target == null || owner == target) return found;
            Walk(owner, owner.GetType(), "", target, target is IContentAsset c ? c.Id : null, owner, found, 0);
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

        // ---------------- Recorrido por reflexión ----------------

        private static void Walk(object obj, Type type, string path, ScriptableObject target, string targetId,
            ScriptableObject owner, List<ContentReference> found, int depth)
        {
            if (obj == null || depth > 6) return;

            foreach (var field in SerializedFields(type))
            {
                string fieldPath = path.Length == 0 ? field.Name : path + "." + field.Name;
                object value = field.GetValue(obj);
                Type ft = field.FieldType;

                // Arrays y listas: se revisa cada elemento con los atributos del campo.
                if (ft.IsArray || (ft.IsGenericType && ft.GetGenericTypeDefinition() == typeof(List<>)))
                {
                    if (!(value is IList list)) continue;
                    Type et = ft.IsArray ? ft.GetElementType() : ft.GetGenericArguments()[0];
                    for (int i = 0; i < list.Count; i++)
                        CheckValue(list[i], et, field, $"{fieldPath}.Array.data[{i}]", target, targetId, owner, found, depth);
                    continue;
                }
                CheckValue(value, ft, field, fieldPath, target, targetId, owner, found, depth);
            }
        }

        private static void CheckValue(object value, Type type, FieldInfo field, string path, ScriptableObject target,
            string targetId, ScriptableObject owner, List<ContentReference> found, int depth)
        {
            if (value == null) return;

            if (typeof(Object).IsAssignableFrom(type))
            {
                if (ReferenceEquals(value, target))
                    found.Add(new ContentReference { Owner = owner, PropertyPath = path, ById = false });
                return;
            }

            if (type == typeof(string))
            {
                if (targetId != null && (string)value == targetId && PointsTo(field, target.GetType()))
                    found.Add(new ContentReference { Owner = owner, PropertyPath = path, ById = true });
                return;
            }

            // Estructuras y clases [Serializable] anidadas (entradas de learnset, efectos de movimiento...).
            if (!type.IsPrimitive && !type.IsEnum && type.IsDefined(typeof(SerializableAttribute), false))
                Walk(value, type, path, target, targetId, owner, found, depth + 1);
        }

        // ¿Este campo de texto guarda ids del tipo de ficha buscado?
        private static bool PointsTo(FieldInfo field, Type targetType)
        {
            var content = field.GetCustomAttribute<ContentIdReferenceAttribute>();
            if (content != null) return content.DataType == targetType;
            if (field.GetCustomAttribute<StatusIdReferenceAttribute>() != null) return targetType == typeof(StatusConditionData);
            return false;
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
