using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.GameDefinition.Domain.Status;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Infrastructure.Acl
{
    /// <summary>
    /// La ACL para movimientos: MoveData (Unity) -> Move (dominio puro). Muestra dos traducciones
    /// que se repetirán por todo el motor: resolver una referencia cruzada a id, y reconstruir un
    /// nullable a partir de campos amigables.
    /// </summary>
    public static class MoveMapper
    {
        public static Move ToDomain(MoveData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            // Un movimiento sin tipo no se puede mapear: el autor olvidó arrastrar la ficha del tipo.
            // Esto es un fallo "duro" del programador/datos; la validación amigable (L.8) lo cazaría
            // antes con un mensaje claro, pero aquí ponemos la última red.
            if (data.Type == null)
                throw new InvalidOperationException($"El movimiento '{data.Id}' no tiene tipo asignado.");

            // REFERENCIA CRUZADA -> ID: no metemos el ElementType entero, solo su id estable.
            // data.Type es la ficha referenciada; data.Type.Id es su id de texto; lo envolvemos en
            // el Id<ElementType> tipado del dominio.
            var typeId = new Id<ElementType>(data.Type.Id);

            // NULLABLE reconstruido: si "nunca falla", la precisión del dominio es null; si no, es el
            // Percentage con el número del Inspector. El '(Percentage?)' fuerza a que ambas ramas del
            // '?:' tengan el mismo tipo nullable (sin ese casteo el compilador se queja).
            Percentage? accuracy = data.NeverMisses
                ? (Percentage?)null
                : new Percentage(data.Accuracy);

            // EFECTOS SECUNDARIOS: cada sub-ficha -> un MoveEffect del dominio. Las entradas vacías
            // (sin estado) se ignoran.
            var effects = new List<MoveEffect>();
            if (data.SecondaryEffects != null)
            {
                foreach (var e in data.SecondaryEffects)
                {
                    if (e == null || string.IsNullOrWhiteSpace(e.statusId)) continue;
                    effects.Add(new MoveEffect(new Percentage(e.chancePercent), new StatusId(e.statusId)));
                }
            }

            return new Move(
                new Id<Move>(data.Id),
                data.DisplayName,
                typeId,
                data.Category,
                data.Power,
                accuracy,
                data.MaxPp,
                data.Priority,
                data.Target,
                effects);
        }
    }
}
