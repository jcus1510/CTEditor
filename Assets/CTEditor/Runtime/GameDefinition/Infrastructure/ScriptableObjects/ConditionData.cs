using System;
using UnityEngine;
using CTEditor.GameDefinition.Domain.Conditions;

namespace CTEditor.GameDefinition.Infrastructure.ScriptableObjects
{
    /// <summary>
    /// Una CONDICIÓN tal como la edita el autor (se dibuja como una frase: "si el rival tiene menos del
    /// 50% de vida"). La traduce ConditionMapper al dominio.
    ///   - kind: qué se pregunta.       - subject: de quién (propio = dueño de la ficha, rival = el otro).
    ///   - comparison + number: para números (vida, nivel, amistad...).
    ///   - text: para ids (estado, tipo, clima, stat, etiqueta, categoría).
    ///   - negate: invierte ("NO tiene estado").
    /// </summary>
    [Serializable]
    public sealed class ConditionData
    {
        public ConditionKind kind = ConditionKind.HpPercent;
        public ConditionSubject subject = ConditionSubject.Other;
        public Comparison comparison = Comparison.LessOrEqual;
        public float number = 50f;
        public string text = "";
        public bool negate = false;
    }

    /// <summary>
    /// Un MODIFICADOR DE POTENCIA editable: multiplica la potencia cuando se cumplen TODAS sus condiciones
    /// (sin condiciones = siempre). Ej.: ×2 si propio tiene estado (Fachada).
    /// </summary>
    [Serializable]
    public sealed class PowerModifierData
    {
        [Min(0f)] public float multiplier = 1.5f;
        public ConditionData[] conditions = new ConditionData[0];
    }
}
