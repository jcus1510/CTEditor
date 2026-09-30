using System.Collections.Generic;
using UnityEditor;
using CTEditor.GameDefinition.Domain.Effects;
using CTEditor.GameDefinition.Infrastructure.Acl;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>Reading and writing an item's EFFECT BLOCKS from the editor (inspector, templates, Excel).</summary>
    public static class ItemEffectsEditing
    {
        /// <summary>The item's blocks (domain).</summary>
        public static List<EffectBlock> Blocks(ItemData d) => d == null ? new List<EffectBlock>() : ItemMapper.Effects(d);

        /// <summary>Replaces all the item's blocks.</summary>
        public static void SetBlocks(SerializedObject so, IList<EffectBlock> blocks)
            => EffectText.WriteAll(so.FindProperty("effects"), blocks);
    }
}
