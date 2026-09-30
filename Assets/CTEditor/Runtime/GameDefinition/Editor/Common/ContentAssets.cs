using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CTEditor.GameDefinition.Infrastructure.Catalog;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;
using Object = UnityEngine.Object;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// Utilidades de EDITOR para trabajar con fichas de contenido: listarlas, buscarlas por id y
    /// crearlas directamente en su carpeta de GameContent (sin diálogos de "guardar como": el autor
    /// escribe un id y la ficha aparece en su sitio, lista para cargarse en el juego).
    ///
    /// 'using Object = UnityEngine.Object;' evita la ambigüedad con System.Object (ambos se llaman
    /// "Object" y aquí importamos los dos namespaces).
    /// </summary>
    public static class ContentAssets
    {
        // CACHÉ: buscar fichas en el proyecto (FindAssets) es lento y los editores lo hacen al repintar,
        // es decir, en cada tecla. Se guarda la lista por tipo y se vacía sola cuando cambia algún asset
        // (ContentAssetsWatcher) o cuando creamos/movemos/borramos fichas desde aquí.
        private static readonly Dictionary<Type, List<ScriptableObject>> Cache = new Dictionary<Type, List<ScriptableObject>>();

        /// <summary>Olvida la caché (tras crear, borrar, mover o importar fichas).</summary>
        public static void ClearCache() { Cache.Clear(); Version++; }

        /// <summary>Sube cada vez que cambia algún asset: otras cachés del editor lo usan para saber si caducaron.</summary>
        public static int Version { get; private set; }

        private static List<ScriptableObject> Cached(Type type)
        {
            if (Cache.TryGetValue(type, out var list))
            {
                if (list.TrueForAll(a => a != null)) return list;   // alguna se destruyó: se vuelve a buscar
            }
            list = new List<ScriptableObject>();
            foreach (var guid in AssetDatabase.FindAssets("t:" + type.Name))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (ContentTrash.IsTrashedPath(path)) continue;
                var asset = AssetDatabase.LoadAssetAtPath(path, type) as ScriptableObject;
                if (asset != null) list.Add(asset);
            }
            Cache[type] = list;
            return list;
        }

        /// <summary>Todas las fichas de un tipo en el proyecto (las de la PAPELERA no cuentan). Copia: se puede ordenar.</summary>
        public static List<T> LoadAll<T>() where T : ScriptableObject
        {
            var list = new List<T>();
            foreach (var a in Cached(typeof(T))) if (a is T t) list.Add(t);
            return list;
        }

        /// <summary>Versión no genérica (para el desplegable, que recibe el tipo en un atributo).</summary>
        public static List<ScriptableObject> LoadAll(Type type)
            => type == null ? new List<ScriptableObject>() : new List<ScriptableObject>(Cached(type));

        /// <summary>Busca una ficha por su id (null si no existe).</summary>
        public static T FindById<T>(string id) where T : ScriptableObject, IContentAsset
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            foreach (var a in LoadAll<T>())
                if (a.Id == id) return a;
            return null;
        }

        /// <summary>El id de una ficha, o su nombre de archivo si aún no tiene id.</summary>
        public static string KeyOf(Object asset)
        {
            if (asset == null) return "";
            if (asset is IContentAsset c && !string.IsNullOrWhiteSpace(c.Id)) return c.Id;
            return asset.name;
        }

        /// <summary>Etiqueta amigable: "Nombre (id)", o solo el id si no hay nombre.</summary>
        public static string Label(Object asset)
        {
            if (asset == null) return "";
            if (asset is IContentAsset c)
            {
                string id = string.IsNullOrWhiteSpace(c.Id) ? asset.name : c.Id;
                return string.IsNullOrWhiteSpace(c.DisplayName) || c.DisplayName == id ? id : $"{c.DisplayName} ({id})";
            }
            return asset.name;
        }

        /// <summary>Crea (si faltan) las carpetas de una ruta tipo "Assets/GameContent/Resources/Curves".</summary>
        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parts = path.Split('/');
            string current = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        /// <summary>
        /// Crea una ficha nueva en GameContent/Resources/&lt;categoría&gt;, con id y nombre ya puestos, y
        /// deja que 'init' rellene el resto de campos (así los presets crean fichas completas).
        /// Si ya existe un archivo con ese nombre, Unity genera uno único ("fire 1.asset").
        ///
        /// La ficha se rellena ENTERA en memoria y solo al final se guarda como archivo: si 'init' falla
        /// (o algo se interrumpe), no queda en el proyecto una ficha en blanco, sin id ni nombre.
        /// </summary>
        public static T Create<T>(string category, string id, string displayName, Action<SerializedObject> init = null)
            where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            try
            {
                var so = new SerializedObject(asset);
                SetString(so, "id", id);
                SetString(so, "displayName", displayName);
                init?.Invoke(so);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            catch
            {
                Object.DestroyImmediate(asset);
                throw;
            }

            string folder = ContentFolders.PathOf(category);
            EnsureFolder(folder);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{SafeFileName(id)}.asset");
            AssetDatabase.CreateAsset(asset, path);
            ClearCache();

            EditorUtility.SetDirty(asset);
            return asset;
        }

        /// <summary>Crea la ficha SOLO si no hay ninguna con ese id. Devuelve true si la creó.</summary>
        public static bool CreateIfMissing<T>(string category, string id, string displayName, Action<SerializedObject> init = null)
            where T : ScriptableObject, IContentAsset
        {
            if (FindById<T>(id) != null) return false;
            Create<T>(category, id, displayName, init);
            return true;
        }

        /// <summary>Asigna un campo de texto si existe (algunas fichas no tienen todos los campos).</summary>
        public static void SetString(SerializedObject so, string field, string value)
        {
            var p = so.FindProperty(field);
            if (p != null) p.stringValue = value ?? "";
        }

        /// <summary>Edita una ficha existente de forma segura (con Deshacer) y la marca como modificada.</summary>
        public static void Edit(Object asset, Action<SerializedObject> edit)
        {
            if (asset == null || edit == null) return;
            var so = new SerializedObject(asset);
            so.Update();
            edit(so);
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(asset);
        }

        private static string SafeFileName(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return "Nuevo";
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) id = id.Replace(c, '_');
            return id.Trim();
        }
    }

    /// <summary>Nombres en español de las stats clásicas (para desplegables, tablas y gráficos).</summary>
    public static class StatLabels
    {
        public static readonly string[] ClassicIds = { "hp", "attack", "defense", "sp_attack", "sp_defense", "speed" };
        public static readonly string[] ClassicNames = { "PS", "Ataque", "Defensa", "Atq. Esp.", "Def. Esp.", "Velocidad" };

        /// <summary>Nombre legible de una stat ("attack" -> "Ataque"); si es inventada, su propio id.</summary>
        public static string NameOf(string statId)
        {
            int i = Array.IndexOf(ClassicIds, statId);
            if (i >= 0) return ClassicNames[i];
            if (statId == "accuracy") return "Precisión";
            if (statId == "evasion") return "Evasión";
            return statId;
        }
    }

    /// <summary>Vacía la caché de fichas cuando Unity importa, borra o mueve assets (también al cambiar de rama o pegar archivos).</summary>
    public sealed class ContentAssetsWatcher : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (imported.Length + deleted.Length + moved.Length > 0) ContentAssets.ClearCache();
        }
    }
}
