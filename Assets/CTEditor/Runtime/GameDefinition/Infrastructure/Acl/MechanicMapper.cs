using System;
using CTEditor.GameDefinition.Domain.Rules.Mechanics;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Infrastructure.Acl
{
    /// <summary>La ACL para mecánicas: MechanicData (Unity) -> MechanicDefinition (dominio puro).</summary>
    public static class MechanicMapper
    {
        public static MechanicDefinition ToDomain(MechanicData d)
        {
            if (d == null) throw new ArgumentNullException(nameof(d));
            return new MechanicDefinition(d.Id, d.DisplayName, d.Kind,
                d.Kind == MechanicKind.MegaEvolution
                    ? new MegaEvolutionSettings(d.MegaMaxPerBattle, d.MegaRequiredKeyItem, d.MegaRevertOnSwitch)
                    : null);
        }
    }
}
