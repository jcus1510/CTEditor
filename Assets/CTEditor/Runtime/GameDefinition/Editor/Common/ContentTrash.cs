using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CTEditor.GameDefinition.Infrastructure.Catalog;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// PAPELERA DE CONTENIDO. «Borrar» ya no destruye la ficha: la MUEVE a Assets/GameContent/Papelera/&lt;categoría&gt;.
    ///
    /// ¿Por qué así? Unity recuerda cada ficha por su GUID interno, y mover un archivo NO cambia su GUID.
    /// Por eso, al RECUPERARLA, todas las referencias (learnsets, entrenadores, zonas...) vuelven a
    /// funcionar solas: no hay que reparar nada.
    ///
    /// Mientras está en la papelera:
    ///   • El juego NO la carga (está fuera de la carpeta Resources).
    ///   • Los editores y el validador NO la listan (ContentAssets la ignora).
    ///   • El validador AVISA si alguna ficha viva todavía la usa.
    /// «Borrar para siempre» y «Vaciar la papelera» sí la destruyen (con aviso de quién la usa).
    /// </summary>
    public static class ContentTrash
    {
        public const string Root = "Assets/GameContent/Papelera";

        /// <summary>
        /// Las carpetas de GRUPO de la papelera empiezan por este signo («~Cambio a Gen3 2026-09-30»): lo que se manda a la
        /// papelera de una vez (un cambio de generación) queda junto y se recupera o se borra en bloque.
        /// </summary>
        public const string GroupPrefix = "~";

        /// <summary>Grupo de una ficha de la papelera ("" si no está en ninguno).</summary>
        public static string GroupOfPath(string assetPath)
        {
            if (!IsTrashedPath(assetPath)) return "";
            string rest = assetPath.Replace('\\', '/').Substring(Root.Length + 1);
            return rest.StartsWith(GroupPrefix) && rest.IndexOf('/') > 0 ? rest.Substring(GroupPrefix.Length, rest.IndexOf('/') - GroupPrefix.Length) : "";
        }

        /// <summary>¿Esta ruta de asset está dentro de la papelera? (puro: se prueba sin Unity)</summary>
        public static bool IsTrashedPath(string assetPath)
            => !string.IsNullOrEmpty(assetPath) && assetPath.Replace('\\', '/').StartsWith(Root + "/", StringComparison.OrdinalIgnoreCase);

        /// <summary>Categoría de una ficha según su carpeta ("Assets/GameContent/Resources/Moves/x.asset" → "Moves").</summary>
        public static string CategoryOfPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return "";
            string p = assetPath.Replace('\\', '/');
            foreach (var root in new[] { ContentFolders.ResourcesRoot + "/", Root + "/" })
                if (p.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    string rest = p.Substring(root.Length);
                    // En la papelera, la carpeta de grupo no es parte de la categoría.
                    if (root == Root + "/" && rest.StartsWith(GroupPrefix) && rest.IndexOf('/') > 0) rest = rest.Substring(rest.IndexOf('/') + 1);
                    int slash = rest.LastIndexOf('/');
                    return slash > 0 ? rest.Substring(0, slash) : "";
                }
            return "";
        }

        /// <summary>Todas las fichas que hay ahora en la papelera.</summary>
        public static List<ScriptableObject> Items()
        {
            var list = new List<ScriptableObject>();
            if (!AssetDatabase.IsValidFolder(Root)) return list;
            foreach (var guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { Root }))
            {
                var a = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (a != null) list.Add(a);
            }
            return list;
        }

        public static int Count => Items().Count;

        /// <summary>Manda la ficha a la papelera. Devuelve "" si fue bien, o el motivo del fallo.</summary>
        public static string MoveToTrash(ScriptableObject asset) => MoveToTrash(asset, null, true);

        /// <summary>
        /// Manda la ficha a la papelera, dentro del GRUPO 'group' si se da (carpeta «~grupo»). 'save' = guardar y refrescar
        /// al terminar (en bloque, mejor false y guardar una vez al final).
        /// </summary>
        public static string MoveToTrash(ScriptableObject asset, string group, bool save)
        {
            if (asset == null) return "No hay ficha.";
            string path = AssetDatabase.GetAssetPath(asset);
            if (IsTrashedPath(path)) return "Ya está en la papelera.";
            string category = CategoryOfPath(path);
            string root = string.IsNullOrWhiteSpace(group) ? Root : Root + "/" + GroupPrefix + SafeGroup(group);
            string folder = string.IsNullOrEmpty(category) ? root : root + "/" + category;
            ContentAssets.EnsureFolder(folder);
            string target = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + Path.GetFileName(path));
            string error = AssetDatabase.MoveAsset(path, target);
            if (save) { AssetDatabase.SaveAssets(); ContentAssets.ClearCache(); }
            return error ?? "";
        }

        private static string SafeGroup(string group)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) group = group.Replace(c, '-');
            return group.Replace('/', '-').Trim();
        }

        /// <summary>
        /// Saca la ficha de la papelera y la devuelve a su carpeta. Si mientras tanto creaste otra con el
        /// MISMO id, no se recupera (tendrías dos): antes hay que renombrar o borrar una de ellas.
        /// </summary>
        public static string Restore(ScriptableObject asset)
        {
            if (asset == null) return "No hay ficha.";
            string path = AssetDatabase.GetAssetPath(asset);
            if (!IsTrashedPath(path)) return "No está en la papelera.";
            if (asset is IContentAsset c && !string.IsNullOrWhiteSpace(c.Id))
            {
                var twin = ContentAssets.LoadAll(asset.GetType()).FirstOrDefault(o => o is IContentAsset x && x.Id == c.Id);
                if (twin != null)
                    return $"Ya existe otra ficha con el id '{c.Id}' ({AssetDatabase.GetAssetPath(twin)}). " +
                           "Si la creaste después de borrar, usa «Cambiar referencias a la recuperada» o renombra una de las dos.";
            }
            string category = CategoryOfPath(path);
            string folder = ContentFolders.PathOf(category);
            ContentAssets.EnsureFolder(folder);
            string target = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + Path.GetFileName(path));
            string error = AssetDatabase.MoveAsset(path, target);
            AssetDatabase.SaveAssets();
            ContentAssets.ClearCache();
            return error ?? "";
        }

        /// <summary>
        /// Caso típico: borraste «confusion», creaste otra «confusion» nueva y las especies se quedaron
        /// apuntando a la vieja (en la papelera). Esto cambia TODAS esas referencias a la ficha viva con el
        /// mismo id y deja la vieja lista para borrar. Devuelve cuántas cambió.
        /// </summary>
        public static int RedirectToLiveTwin(ScriptableObject trashed)
        {
            if (!(trashed is IContentAsset c) || string.IsNullOrWhiteSpace(c.Id)) return 0;
            var twin = ContentAssets.LoadAll(trashed.GetType()).FirstOrDefault(o => o is IContentAsset x && x.Id == c.Id);
            if (twin == null) return 0;
            int n = 0;
            foreach (var r in ReferenceFinder.FindReferencesTo(trashed))
            {
                if (r.ById) continue; // por id ya apunta al vivo (mismo id)
                ContentAssets.Edit(r.Owner, so =>
                {
                    var p = so.FindProperty(r.PropertyPath);
                    if (p != null && p.objectReferenceValue == trashed) { p.objectReferenceValue = twin; n++; }
                });
            }
            AssetDatabase.SaveAssets();
            return n;
        }

        /// <summary>¿Hay una ficha viva con el mismo id y tipo?</summary>
        public static bool HasLiveTwin(ScriptableObject trashed)
            => trashed is IContentAsset c && !string.IsNullOrWhiteSpace(c.Id)
               && ContentAssets.LoadAll(trashed.GetType()).Any(o => o is IContentAsset x && x.Id == c.Id);

        /// <summary>Borra DE VERDAD (no se puede deshacer).</summary>
        public static void DeleteForever(ScriptableObject asset)
        {
            if (asset == null) return;
            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(asset));
            AssetDatabase.SaveAssets();
            ContentAssets.ClearCache();
        }

        /// <summary>Texto con quién usa una ficha (para los diálogos): "Bulbasaur → learnset[3].move" ...</summary>
        public static string DescribeUsers(List<ContentReference> refs, int max = 8)
        {
            if (refs == null || refs.Count == 0) return "Nadie la usa.";
            var lines = refs.Take(max).Select(r => $"• {ContentAssets.Label(r.Owner)}  →  {r.Field}").ToList();
            if (refs.Count > max) lines.Add($"… y {refs.Count - max} más");
            return string.Join("\n", lines);
        }
    }
}
