using System;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Infrastructure.Acl
{
    /// <summary>ACL de naturalezas: NatureData (ficha cómoda de Unity) -> Nature (dominio puro).</summary>
    public static class NatureMapper
    {
        public static Nature ToDomain(NatureData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            // Texto vacío = "ninguna". El '(StatId?)null' es necesario para que el operador ?: sepa
            // que ambos lados son del tipo anulable StatId?.
            StatId? boosted = string.IsNullOrWhiteSpace(data.BoostedStatId)
                ? (StatId?)null : new StatId(data.BoostedStatId.Trim());
            StatId? hindered = string.IsNullOrWhiteSpace(data.HinderedStatId)
                ? (StatId?)null : new StatId(data.HinderedStatId.Trim());

            return new Nature(new Id<Nature>(data.Id), data.DisplayName, boosted, hindered, data.BoostPercent);
        }
    }
}
