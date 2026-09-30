using System.Collections.Generic;
using CTEditor.GameDefinition.Domain.Conditions;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Infrastructure.Acl
{
    /// <summary>ACL de condiciones y modificadores de potencia: datos del editor → dominio.</summary>
    public static class ConditionMapper
    {
        public static List<Condition> ToDomain(ConditionData[] data)
        {
            var list = new List<Condition>();
            if (data == null) return list;
            foreach (var c in data)
                if (c != null) list.Add(new Condition(c.kind, c.subject, c.comparison, c.number, (c.text ?? "").Trim(), c.negate));
            return list;
        }

        public static List<PowerModifier> ToDomain(PowerModifierData[] data)
        {
            var list = new List<PowerModifier>();
            if (data == null) return list;
            foreach (var m in data)
                if (m != null) list.Add(new PowerModifier(m.multiplier, ToDomain(m.conditions)));
            return list;
        }
    }
}
