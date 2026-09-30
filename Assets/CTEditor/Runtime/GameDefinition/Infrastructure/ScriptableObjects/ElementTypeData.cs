using UnityEngine;

namespace CTEditor.GameDefinition.Infrastructure.ScriptableObjects
{
    /// <summary>
    /// La FICHA que rellena el autor para definir un tipo (Fuego, Agua...). Es un ScriptableObject.
    ///
    /// ¿Qué es un ScriptableObject? Un contenedor de DATOS de Unity que se guarda como un asset
    /// (un archivo .asset) y se edita en el Inspector, sin necesidad de escribir código. Es la forma
    /// en que tu autor NO-programador crea contenido (A.9). No es un MonoBehaviour: no va pegado a un
    /// objeto de la escena ni "corre" en el juego; solo guarda datos.
    ///
    /// OJO — esta clase vive en INFRASTRUCTURE, no en el dominio. Es la primera que importa Unity
    /// ('using UnityEngine'). El dominio (ElementType) JAMÁS la verá: el traductor (la ACL, ver
    /// ElementTypeMapper) la convierte a dominio puro. Así la regla de dependencias se respeta: lo
    /// de afuera (esto) conoce lo de adentro (ElementType), nunca al revés.
    /// </summary>
    // [CreateAssetMenu] añade una opción al menú "Create" de Unity para que el autor genere este
    // asset con clic derecho en el Project. menuName = dónde aparece; fileName = nombre por defecto.
    [CreateAssetMenu(menuName = "CTEditor/Tipo", fileName = "NuevoTipo")]
    public sealed class ElementTypeData : ScriptableObject, IContentAsset
    {
        // [SerializeField] + private = el campo se EDITA en el Inspector pero queda privado al código.
        // Es el patrón limpio: editable por el autor, de solo lectura para el resto del programa
        // (lo exponemos con una propiedad 'get' más abajo).
        [SerializeField] private string id;
        [SerializeField] private string displayName;

        [Tooltip("Color del tipo (lo usan los editores y la interfaz: chips de tipo, tabla de tipos).")]
        [SerializeField] private Color color = new Color(0.62f, 0.62f, 0.62f, 1f);

        /// <summary>
        /// Id ESTABLE del tipo (M.4). Es DISTINTO del nombre del archivo .asset: el archivo se puede
        /// renombrar sin romper nada, pero este id no debería cambiar, porque las demás fichas y los
        /// guardados apuntan a él. El autor lo escribe una vez (p.ej. "fire") y lo deja quieto.
        /// </summary>
        public string Id => id;

        public string DisplayName => displayName;
        public Color Color => color;
    }
}
