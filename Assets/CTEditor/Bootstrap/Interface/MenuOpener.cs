using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CTEditor.Adventure.Domain.Interface;

namespace CTEditor.Bootstrap
{
    /// <summary>Un menú abierto en pantalla, sea el automático (MenuView) o uno diseñado en la escena (MenuScreen).</summary>
    public interface IMenuPresenter
    {
        bool Done { get; }
        /// <summary>La opción elegida (null = canceló).</summary>
        MenuOption Result { get; }
        /// <summary>Posición de la opción elegida entre las visibles (-1 = canceló). En listas, el índice del elemento.</summary>
        int ResultIndex { get; }
        IEnumerator Wait();
        void Close();
    }

    /// <summary>La pila de menús abiertos: solo el de ARRIBA lee las teclas.</summary>
    public static class MenuStack
    {
        private static readonly List<IMenuPresenter> Open = new List<IMenuPresenter>();

        public static void Push(IMenuPresenter m) { if (m != null && !Open.Contains(m)) Open.Add(m); }
        public static void Remove(IMenuPresenter m) => Open.Remove(m);
        internal static void Clear() => Open.Clear();

        public static bool IsTop(IMenuPresenter m)
        {
            Open.RemoveAll(x => x == null || (x is UnityEngine.Object u && u == null));
            return Open.Count > 0 && ReferenceEquals(Open[Open.Count - 1], m);
        }

        /// <summary>Cierra todos (al interrumpir una pantalla a medias).</summary>
        public static void CloseAll()
        {
            foreach (var m in Open.ToList())
                if (m != null && !(m is UnityEngine.Object u && u == null)) m.Close();
            Open.Clear();
        }
    }

    /// <summary>
    /// ABRE UN MENÚ POR SU ID usando lo mejor que haya:
    ///   1) Si en la escena hay un MenuScreen con ese id (diseñado a mano, con tus gráficos), ese.
    ///   2) Si no, el automático a partir de la ficha del editor de Menús (o la clásica).
    /// Así un juego funciona desde el primer minuto y el autor va sustituyendo menús por los suyos.
    ///
    /// Ids que usa el juego: pausa · si_no · equipo_miembro · equipo_objeto · mochila_objeto · opciones
    /// y las LISTAS: equipo_lista · mochila_lista · olvidar · controles.
    /// </summary>
    public static class MenuOpener
    {
        public static readonly string[] ListIds = { "equipo_lista", "mochila_lista", "olvidar", "controles" };

        /// <summary>
        /// La definición de un menú por su id: la ficha (o la clásica); si solo existe en la ESCENA, una vacía
        /// con ese id (el menú de escena trae sus propias opciones). Null si no existe en ningún sitio.
        /// </summary>
        public static MenuDefinition Definition(string id)
            => UiContent.Menu(id) ?? (MenuScreen.Find(id) != null ? new MenuDefinition(id, id) : null);

        public static IMenuPresenter Open(string id, MenuDefinition fallback = null, Func<string, bool> hasFlag = null,
            Func<MenuOption, bool> enabled = null, IReadOnlyDictionary<string, string> vars = null)
        {
            var scene = MenuScreen.Find(id);
            if (scene != null && !scene.IsList && scene.CanOpen) return scene.Open(hasFlag, enabled, vars);
            var def = fallback ?? UiContent.Menu(id) ?? new MenuDefinition(id, id).Add(new MenuOption("salir", "SALIR", MenuActionKind.Close));
            return MenuView.Open(def, hasFlag, enabled, vars);
        }

        public static IMenuPresenter OpenList(string id, string title, IList<string> labels, MenuAnchor anchor = MenuAnchor.Center,
            Func<int, bool> enabled = null, IList<string> help = null, float width = 640f, int maxRows = 6, bool remember = false)
        {
            var scene = MenuScreen.Find(id);
            if (scene != null && scene.IsList && scene.CanOpen) return scene.OpenList(title, labels, enabled, help, remember);
            return MenuView.OpenList(id, title, labels, anchor, enabled, help, width, maxRows, remember);
        }
    }
}
