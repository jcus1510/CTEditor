using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;

namespace CTEditor.GameDefinition.Domain.Catalog
{
    /// <summary>
    /// Un CATÁLOGO de contenido: la pieza que responde "dame la ficha con id X".
    ///
    /// Es la otra mitad de la idea de "referenciar por id" (M.4): si una Species guarda
    /// Id&lt;Move&gt; en vez del Move entero, alguien tiene que poder convertir ese id en el objeto
    /// real cuando haga falta. Ese alguien es el catálogo.
    ///
    /// Aquí solo está el CONTRATO (la interface). La implementación de verdad —la que lee los
    /// ScriptableObjects, los mapea a dominio puro vía la ACL, y eventualmente los carga de forma
    /// perezosa por id sin meterlos todos en memoria de golpe (Parte O)— vivirá en Infrastructure
    /// (J.2). El dominio pide "dame X por id" y le da igual de dónde sale: de RAM, de disco o de
    /// un asset de Unity. Esa ignorancia es justo lo que mantiene puro al dominio.
    ///
    /// DECISIÓN DE DISEÑO (te la comento por transparencia): el documento listaba dos interfaces
    /// con nombre, ISpeciesCatalog e IMoveCatalog. Las reemplazo por UNA genérica, ICatalog&lt;T&gt;,
    /// porque "buscar contenido por Id&lt;T&gt;" es idéntico para toda ficha: una sola interface sirve
    /// para ICatalog&lt;Species&gt;, ICatalog&lt;Move&gt;, ICatalog&lt;ElementType&gt;... Si algún tipo de
    /// contenido necesitara consultas propias (p.ej. "todas las especies de tipo Fuego"), ahí sí
    /// crearíamos una interface específica que extienda esta. Antes no: J.6.
    ///
    /// 'where T : class' = restringe el genérico a tipos por referencia (clases). Nuestras fichas
    /// (Species, Move...) son clases, y además esto permite que el 'out T item' valga null sin
    /// problemas cuando no se encuentra.
    /// </summary>
    public interface ICatalog<T> where T : class
    {
        /// <summary>
        /// Busca SIN lanzar excepción. Devuelve true y entrega la ficha por 'out' si existe;
        /// false (e item = null) si no. Es la vía segura para cuando no sabes si el id está.
        /// </summary>
        bool TryGet(Id<T> id, out T item);

        /// <summary>
        /// Devuelve la ficha o LANZA si no existe (KeyNotFoundException). Para el camino feliz:
        /// cuando la validación ya garantizó que el id existe y un fallo sería un bug, no un caso normal.
        /// </summary>
        T Get(Id<T> id);

        /// <summary>¿Existe una ficha con este id?</summary>
        bool Contains(Id<T> id);

        /// <summary>
        /// Todas las fichas del catálogo. Útil para el editor y la validación (recorrer todo).
        /// En runtime, lo normal es pedir por id (TryGet/Get); recorrer todo es cosa de design-time.
        /// </summary>
        IReadOnlyCollection<T> All { get; }
    }
}
