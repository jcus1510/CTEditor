using System;
using UnityEngine;

namespace CTEditor.GameDefinition.Infrastructure.ScriptableObjects
{
    /// <summary>
    /// Marca un campo de texto que guarda el ID de otra ficha. En el Inspector se dibuja como un
    /// DESPLEGABLE con todas las fichas existentes de ese tipo ("Rápida (fast)", "Lenta (slow)"...),
    /// en vez de obligar al autor a escribir el id a mano y equivocarse.
    ///
    /// Uso:  [ContentIdReference(typeof(GrowthCurveData))] [SerializeField] private string growthCurveId;
    ///
    /// Sigue guardando TEXTO (el id): el dominio no cambia, solo mejora la experiencia de edición.
    /// Funciona también con arrays (Unity aplica el dibujo a cada elemento).
    /// </summary>
    public sealed class ContentIdReferenceAttribute : PropertyAttribute
    {
        /// <summary>Tipo de ficha a listar (debe implementar IContentAsset).</summary>
        public Type DataType { get; }

        public ContentIdReferenceAttribute(Type dataType) => DataType = dataType;
    }

    /// <summary>
    /// Marca un campo de texto que guarda el id de una STAT ("attack", "speed"...). Se dibuja como un
    /// desplegable con las 6 clásicas en español, más la opción de escribir una stat inventada.
    /// </summary>
    public sealed class StatIdReferenceAttribute : PropertyAttribute { }
}
