using System;
using CTEditor.Adventure.Domain.Interface;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// PUENTE con las herramientas de ESCENA (viven en Bootstrap.Editor, que este ensamblado no ve): el
    /// editor de Menús ofrece «🎨 Crear en la escena» y «🔗 Mapa de menús» si alguien los registró aquí.
    /// </summary>
    public static class SceneMenuHooks
    {
        /// <summary>Crea en la escena abierta un menú editable con gráficos a partir de una definición.</summary>
        public static Action<MenuDefinition> CreateInScene;
        /// <summary>¿Hay ya en la escena un menú con ese id?</summary>
        public static Func<string, bool> ExistsInScene;
        /// <summary>Abre el mapa de menús (grafo para enlazarlos).</summary>
        public static Action OpenFlow;
    }
}
