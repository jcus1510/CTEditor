using System;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Infrastructure.Acl
{
    /// <summary>
    /// La ACL (Anti-Corruption Layer) para tipos: el TRADUCTOR de una sola dirección que convierte
    /// la ficha de Unity (ElementTypeData) en el objeto de dominio puro (ElementType).
    ///
    /// ¿Por qué existe esta capa en vez de que el dominio use el ScriptableObject directo? Porque si
    /// el dominio dependiera de ElementTypeData, dependería de UnityEngine, y se acabó la pureza: no
    /// podrías testear sin Unity (A.3), ni mover el dominio fuera del motor. La ACL es la aduana que
    /// deja pasar los DATOS pero deja afuera la dependencia de Unity. El dominio recibe un ElementType
    /// limpio y nunca se entera de que vino de un asset.
    ///
    /// Es una clase 'static' con métodos 'static': no guarda estado, solo transforma. (Más adelante,
    /// si la traducción necesitara contexto, podríamos hacerla una instancia; por ahora no hace falta.)
    /// </summary>
    public static class ElementTypeMapper
    {
        public static ElementType ToDomain(ElementTypeData data)
        {
            // 'data == null' en Unity también detecta el asset "destruido/faltante", no solo el null
            // de C#: ScriptableObject sobrecarga el operador == para eso. Por eso se compara con null
            // y no con 'is null' aquí.
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            // El paso clave: del string crudo del asset construimos el Id<ElementType> tipado del
            // dominio. A partir de aquí, "abajo" todo es puro y fuerte de tipos.
            return new ElementType(new Id<ElementType>(data.Id), data.DisplayName);
        }
    }
}
