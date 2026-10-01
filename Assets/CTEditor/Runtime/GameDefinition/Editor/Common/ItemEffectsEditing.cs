using System.Collections.Generic;
using UnityEditor;
using CTEditor.GameDefinition.Domain.Effects;
using CTEditor.GameDefinition.Infrastructure.Acl;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>Reading and writing the EFFECT BLOCKS of an item or an ability from the editor (inspector, templates, Excel).</summary>
    public static class ItemEffectsEditing
    {
        /// <summary>The item's blocks (domain).</summary>
        public static List<EffectBlock> Blocks(ItemData d) => d == null ? new List<EffectBlock>() : ItemMapper.Effects(d);

        /// <summary>The ability's blocks (domain).</summary>
        public static List<EffectBlock> Blocks(AbilityData d) => d == null ? new List<EffectBlock>() : AbilityMapper.Effects(d);

        /// <summary>Replaces all the blocks (the «effects» field of an item or an ability).</summary>
        public static void SetBlocks(SerializedObject so, IList<EffectBlock> blocks)
            => EffectTextUnity.WriteAll(so.FindProperty("effects"), blocks);
    }
}
