using System.Collections.Generic;
using System.Linq;
using CTEditor.Adventure.Domain.Interface;
using CTEditor.GameDefinition.Infrastructure.Acl;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>Validación de la INTERFAZ: menús (opciones, submenús, ids) y ajustes (teclas).</summary>
    public static partial class ContentValidator
    {
        private static void ValidateInterface(List<ValidationIssue> issues)
        {
            var menus = LoadAll<MenuData>();
            var ids = CollectIds(menus, m => m.Id, "menú", issues);
            // Un submenú puede ser uno del autor o uno clásico que el juego trae de serie.
            // También los menús diseñados en la escena abierta (si las herramientas de escena están cargadas).
            bool Exists(string id) => ids.Contains(id) || ClassicInterface.Find(id) != null
                                      || (SceneMenuHooks.ExistsInScene != null && SceneMenuHooks.ExistsInScene(id));
            foreach (var m in menus)
                foreach (var p in InterfaceMapper.ToDomain(m).Problems(Exists))
                    issues.Add(Warning($"Menú '{ContentAssets.Label(m)}': {p}", m));

            var settings = LoadAll<InterfaceSettingsData>();
            if (settings.Count > 1 && settings.All(s => s.Id != "ajustes"))
                issues.Add(Warning("Hay varias fichas de ajustes de interfaz y ninguna tiene el id «ajustes»: el juego usará la primera.", settings[0]));
            foreach (var s in settings)
            {
                // Sin completar con las clásicas: lo que ve el autor es lo que hay en la ficha.
                var b = new InputBindings();
                foreach (var x in s.Bindings.Where(x => x != null)) b.Bind(x.button, x.keys ?? new string[0]);
                foreach (var p in b.Problems())
                    issues.Add(Warning($"Controles '{ContentAssets.Label(s)}': {p}" + (p.Contains("no tiene ninguna tecla") ? " (el juego usará las clásicas)" : ""), s));
                if (!string.IsNullOrWhiteSpace(s.PauseMenuId) && !Exists(s.PauseMenuId))
                    issues.Add(Error($"Controles '{ContentAssets.Label(s)}': el menú de pausa «{s.PauseMenuId}» no existe.", s));
            }
        }
    }
}
