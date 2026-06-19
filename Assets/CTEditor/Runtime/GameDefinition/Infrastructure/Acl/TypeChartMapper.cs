using System;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Infrastructure.Acl
{
    /// <summary>
    /// La ACL para la tabla de tipos: TypeChartData (Unity) -> TypeChart (dominio puro). Usa el
    /// Builder del dominio para ir registrando cada cruce; los huecos vacíos se saltan.
    /// </summary>
    public static class TypeChartMapper
    {
        public static TypeChart ToDomain(TypeChartData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            var builder = new TypeChart.Builder();
            if (data.Matchups != null)
            {
                foreach (var m in data.Matchups)
                {
                    // Si al autor le faltó arrastrar uno de los dos tipos, ese cruce no se puede armar.
                    if (m.attacking == null || m.defending == null) continue;

                    builder.Set(
                        new Id<ElementType>(m.attacking.Id),
                        new Id<ElementType>(m.defending.Id),
                        m.multiplier);
                }
            }
            return builder.Build();
        }
    }
}
