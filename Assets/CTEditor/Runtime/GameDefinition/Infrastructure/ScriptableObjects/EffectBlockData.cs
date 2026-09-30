using System;
using UnityEngine;
using CTEditor.GameDefinition.Domain.Effects;

namespace CTEditor.GameDefinition.Infrastructure.ScriptableObjects
{
    /// <summary>
    /// One EFFECT BLOCK as the author edits it: «when [trigger], if [conditions], then [action]». Items and abilities
    /// keep a list of these. The editor shows each field only when the action needs it (see EffectAction).
    /// </summary>
    [Serializable]
    public sealed class EffectBlockData
    {
        public EffectTrigger trigger = EffectTrigger.Passive;
        public ConditionData[] conditions = new ConditionData[0];
        public EffectAction action = EffectAction.HealPercent;
        public BlockTarget target = BlockTarget.Self;
        [Tooltip("Id que necesita la acción: tipo, estado (varios con |), estadística, clima, movimiento, forma o mecánica.")]
        public string reference = "";
        public float amount;
        [Tooltip("Con «Con poca vida»: se activa con este % de PS o menos.")]
        [Range(0f, 100f)] public float threshold;
        [Tooltip("El objeto se gasta al activarse.")]
        public bool consumes;
        [Tooltip("Probabilidad de activarse (100 = siempre).")]
        [Range(0f, 100f)] public float chance = 100f;
        [Tooltip("Veces por combate (0 = sin límite).")]
        [Min(0)] public int maxPerBattle;
    }

}
