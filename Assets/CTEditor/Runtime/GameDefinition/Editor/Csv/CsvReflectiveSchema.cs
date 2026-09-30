using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;
using Object = UnityEngine.Object;

namespace CTEditor.GameDefinition.Editor.Csv
{
    /// <summary>
    /// Esquema de Excel AUTOMÁTICO: mira los campos guardados de la ficha (los que ves en el Inspector) y
    /// crea una columna por campo, sin escribir código por categoría. Si mañana añades un campo a
    /// EstadoData, aparece solo como columna nueva en Excel.
    ///
    /// Cómo se escribe cada tipo de campo en la celda:
    ///   número / texto        tal cual (decimales con coma o punto)
    ///   sí/no                 si | no
    ///   enumeración           su nombre (ej. MediumFast)
    ///   color                 hexadecimal (ej. EE8130)
    ///   referencia a ficha    su id (ej. fire)
    ///   lista                 valores separados por |      (ej. burn|poison)
    ///   lista de "filas"      campos separados por : y filas por |   (ej. attack:1,5|speed:0,5)
    ///
    /// Los campos que no se pueden representar en una celda (imágenes, sonidos, estructuras anidadas
    /// complejas) se saltan: se siguen editando en su ventana.
    /// </summary>
    public sealed class CsvReflectiveSchema<TData> : CsvSchema<TData> where TData : ScriptableObject, IContentAsset
    {
        public CsvReflectiveSchema(string file, string title, string category, int order)
            : base(file, title, category, order)
        {
            foreach (var field in SerializedFields(typeof(TData)))
            {
                if (field.IsDefined(typeof(LegacyFieldAttribute), false)) continue; // campo antiguo: solo para convertir fichas viejas
                var codec = CodecFor(field);
                if (codec == null) continue; // campo no representable en una celda
                var f = field;
                // Cabecera en español (de Etiquetas); el nombre del campo en inglés se acepta como alias.
                string header = Etiquetas.CsvHeader(f.Name);
                string tip = f.GetCustomAttribute<TooltipAttribute>()?.tooltip;
                string help = string.IsNullOrEmpty(tip) ? codec.Hint : $"{tip} [{codec.Hint}]";
                Col(header, $"{Etiquetas.Field(f.Name)}. {help}",
                    d => codec.Format(f.GetValue(d)),
                    (so, cell, ctx) =>
                    {
                        var prop = so.FindProperty(f.Name);
                        if (prop == null) throw new CsvCellException($"No se encontró el campo '{f.Name}'.");
                        codec.Write(prop, cell ?? "", ctx);
                    }, false, header == f.Name ? new string[0] : new[] { f.Name });
            }
        }

        // ---------------- Qué campos se guardan ----------------

        /// <summary>
        /// Los campos que Unity serializa: públicos o con [SerializeField], no estáticos ni [NonSerialized],
        /// en orden de declaración (primero los de las clases base).
        /// </summary>
        internal static List<FieldInfo> SerializedFields(Type type)
        {
            var chain = new List<Type>();
            for (var t = type; t != null && t != typeof(ScriptableObject) && t != typeof(object); t = t.BaseType) chain.Insert(0, t);

            var result = new List<FieldInfo>();
            foreach (var t in chain)
                foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (f.IsDefined(typeof(NonSerializedAttribute), false)) continue;
                    if (!f.IsPublic && !f.IsDefined(typeof(SerializeField), false)) continue;
                    if (f.IsInitOnly || f.IsLiteral) continue;
                    result.Add(f);
                }
            return result;
        }

        // ---------------- Códecs: cómo pasa cada tipo de campo a texto y vuelta ----------------

        private abstract class Codec
        {
            public abstract string Hint { get; }
            public abstract string Format(object value);
            public abstract void Write(SerializedProperty prop, string cell, ImportContext ctx);
        }

        /// <summary>Un valor suelto (número, texto, sí/no, enum, color, referencia a ficha).</summary>
        private sealed class ScalarCodec : Codec
        {
            private readonly Type _type;
            private readonly Type _idRefType; // para textos que guardan el id de otra ficha (avisos)
            private readonly string _what;

            public ScalarCodec(Type type, Type idRefType, string what) { _type = type; _idRefType = idRefType; _what = what; }

            public override string Hint
            {
                get
                {
                    if (_type == typeof(bool)) return "si / no";
                    if (_type.IsEnum) return string.Join(", ", Enum.GetNames(_type).Select(n => $"{n} ({Etiquetas.Enum(n)})"));
                    if (_type == typeof(Color)) return "color hex, ej. EE8130";
                    if (typeof(Object).IsAssignableFrom(_type)) return "id de " + _type.Name.Replace("Data", "");
                    if (_type == typeof(int)) return "número entero";
                    if (_type == typeof(float)) return "número";
                    return _idRefType != null ? "id de " + _idRefType.Name.Replace("Data", "") : "texto";
                }
            }

            public override string Format(object v)
            {
                if (v == null) return "";
                if (_type == typeof(bool)) return (bool)v ? "si" : "no";
                if (_type == typeof(float)) return CsvTable.Number((float)v);
                if (_type == typeof(int)) return ((int)v).ToString();
                if (_type.IsEnum) return Etiquetas.Enum(v.ToString()); // en español (al importar se aceptan ambos)
                if (_type == typeof(Color)) { var c = (Color)v; return $"{Mathf.RoundToInt(c.r * 255):X2}{Mathf.RoundToInt(c.g * 255):X2}{Mathf.RoundToInt(c.b * 255):X2}"; }
                if (v is Object o) return o == null ? "" : o is IContentAsset ca ? ca.Id : o.name;
                return v.ToString();
            }

            public object Parse(string cell, ImportContext ctx)
            {
                cell = cell.Trim();
                if (_type == typeof(string))
                {
                    if (_idRefType != null && cell.Length > 0 && !ctx.Exists(_idRefType, cell))
                        ctx.Warnings.Add($"{_what}: '{cell}' no existe todavía ({_idRefType.Name.Replace("Data", "")}).");
                    return cell;
                }
                if (_type == typeof(int))
                {
                    if (cell.Length == 0) return 0;
                    if (!CsvTable.TryInt(cell, out int i)) throw new CsvCellException($"'{cell}' no es un número entero ({_what}).");
                    return i;
                }
                if (_type == typeof(float))
                {
                    if (cell.Length == 0) return 0f;
                    if (!CsvTable.TryNumber(cell, out float f)) throw new CsvCellException($"'{cell}' no es un número ({_what}).");
                    return f;
                }
                if (_type == typeof(bool))
                {
                    if (!CsvTable.TryBool(cell, out bool b)) throw new CsvCellException($"'{cell}' no es si/no ({_what}).");
                    return b;
                }
                if (_type.IsEnum)
                {
                    foreach (var name in Enum.GetNames(_type))
                        if (string.Equals(name, cell, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(Etiquetas.Enum(name), cell, StringComparison.OrdinalIgnoreCase))
                            return Enum.Parse(_type, name);
                    if (CsvTable.TryInt(cell, out int n) && Enum.IsDefined(_type, n)) return Enum.ToObject(_type, n);
                    throw new CsvCellException($"'{cell}' no es válido en {_what}: usa {string.Join(", ", Enum.GetNames(_type))}.");
                }
                if (_type == typeof(Color))
                {
                    if (cell.Length == 0) return Color.white;
                    try { return TypeChartTools.FromHex(cell.TrimStart('#')); }
                    catch (Exception) { throw new CsvCellException($"'{cell}' no es un color hexadecimal ({_what})."); }
                }
                if (typeof(Object).IsAssignableFrom(_type))
                {
                    if (cell.Length == 0) return null;
                    if (!ctx.Exists(_type, cell)) throw new CsvCellException($"{_what}: '{cell}' no existe ({_type.Name.Replace("Data", "")}).");
                    return ctx.Find(_type, cell); // puede ser null si se crea en esta misma importación
                }
                throw new CsvCellException($"Tipo de campo no soportado ({_what}).");
            }

            public override void Write(SerializedProperty prop, string cell, ImportContext ctx)
            {
                // Celda VACÍA en un número, sí/no, enum o color = se deja como está (en una ficha nueva, su valor
                // por defecto: multiplicadores a 1, etc.). Así una tabla con muchas columnas solo necesita rellenar
                // lo que cambia. En textos y referencias, vacío sí significa «vacío».
                if (string.IsNullOrWhiteSpace(cell) && _type != typeof(string) && !typeof(Object).IsAssignableFrom(_type)) return;
                Assign(prop, Parse(cell, ctx));
            }

            public void Assign(SerializedProperty prop, object value)
            {
                if (_type == typeof(string)) prop.stringValue = (string)value;
                else if (_type == typeof(int)) prop.intValue = (int)value;
                else if (_type == typeof(float)) prop.floatValue = (float)value;
                else if (_type == typeof(bool)) prop.boolValue = (bool)value;
                else if (_type.IsEnum) prop.intValue = Convert.ToInt32(value); // valor del enum (no su índice)
                else if (_type == typeof(Color)) prop.colorValue = (Color)value;
                else prop.objectReferenceValue = value as Object;
            }
        }

        /// <summary>Lista de valores sueltos: "a|b|c".</summary>
        private sealed class ListCodec : Codec
        {
            private readonly ScalarCodec _item;
            public ListCodec(ScalarCodec item) => _item = item;
            public override string Hint => "lista con |: " + _item.Hint;

            public override string Format(object value)
            {
                if (!(value is System.Collections.IEnumerable items)) return "";
                var parts = new List<string>();
                foreach (var v in items) parts.Add(_item.Format(v));
                return string.Join("|", parts);
            }

            public override void Write(SerializedProperty prop, string cell, ImportContext ctx)
            {
                var values = CsvCodecs.SplitList(cell).Select(s => _item.Parse(s, ctx)).ToList();
                prop.arraySize = values.Count;
                for (int i = 0; i < values.Count; i++) _item.Assign(prop.GetArrayElementAtIndex(i), values[i]);
            }
        }

        /// <summary>Lista de "filas" (clases [Serializable] con campos simples): "a:1|b:2".</summary>
        private sealed class RecordListCodec : Codec
        {
            private readonly List<(FieldInfo field, ScalarCodec codec)> _fields;
            public RecordListCodec(List<(FieldInfo, ScalarCodec)> fields) => _fields = fields;
            public override string Hint => "filas con |, campos con ':' en este orden: " + string.Join(":", _fields.Select(f => Etiquetas.CsvHeader(f.field.Name)));

            public override string Format(object value)
            {
                if (!(value is System.Collections.IEnumerable items)) return "";
                var parts = new List<string>();
                foreach (var item in items)
                    if (item != null) parts.Add(string.Join(":", _fields.Select(f => f.codec.Format(f.field.GetValue(item)))));
                return string.Join("|", parts);
            }

            public override void Write(SerializedProperty prop, string cell, ImportContext ctx)
            {
                var rows = CsvCodecs.SplitList(cell);
                var parsed = new List<object[]>();
                foreach (var row in rows)
                {
                    // El último campo se queda con el resto (así una fórmula puede llevar ':' sin romper nada).
                    var parts = row.Split(new[] { ':' }, _fields.Count);
                    if (parts.Length != _fields.Count)
                        throw new CsvCellException($"'{row}' debe tener {_fields.Count} campo(s) separados por ':' ({string.Join(":", _fields.Select(f => f.field.Name))}).");
                    parsed.Add(parts.Select((p, i) => _fields[i].codec.Parse(p, ctx)).ToArray());
                }
                prop.arraySize = parsed.Count;
                for (int r = 0; r < parsed.Count; r++)
                {
                    var el = prop.GetArrayElementAtIndex(r);
                    for (int i = 0; i < _fields.Count; i++)
                        _fields[i].codec.Assign(el.FindPropertyRelative(_fields[i].field.Name), parsed[r][i]);
                }
            }
        }

        private static Codec CodecFor(FieldInfo f)
        {
            var t = f.FieldType;
            var scalar = ScalarFor(t, f);
            if (scalar != null) return scalar;

            Type element = t.IsArray ? t.GetElementType()
                : t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>) ? t.GetGenericArguments()[0] : null;
            if (element == null) return null;

            var itemCodec = ScalarFor(element, f);
            if (itemCodec != null) return new ListCodec(itemCodec);

            // Lista de clases [Serializable] cuyos campos son todos simples.
            if (element.IsClass && element.IsDefined(typeof(SerializableAttribute), false))
            {
                var inner = new List<(FieldInfo, ScalarCodec)>();
                foreach (var sub in SerializedFields(element))
                {
                    var c = ScalarFor(sub.FieldType, sub);
                    if (c == null) return null; // algo anidado: no cabe en una celda
                    inner.Add((sub, c));
                }
                return inner.Count > 0 ? new RecordListCodec(inner) : null;
            }
            return null;
        }

        private static ScalarCodec ScalarFor(Type t, FieldInfo f)
        {
            string what = f.Name;
            if (t == typeof(string))
            {
                Type refType = f.GetCustomAttribute<ContentIdReferenceAttribute>()?.DataType;
                if (refType == null && f.IsDefined(typeof(StatusIdReferenceAttribute), false)) refType = typeof(StatusConditionData);
                return new ScalarCodec(t, refType, what);
            }
            if (t == typeof(int) || t == typeof(float) || t == typeof(bool) || t.IsEnum || t == typeof(Color))
                return new ScalarCodec(t, null, what);
            // Referencias a OTRAS fichas (con id). Imágenes, sonidos, prefabs... no caben en Excel.
            if (typeof(ScriptableObject).IsAssignableFrom(t) && typeof(IContentAsset).IsAssignableFrom(t))
                return new ScalarCodec(t, null, what);
            return null;
        }
    }
}
