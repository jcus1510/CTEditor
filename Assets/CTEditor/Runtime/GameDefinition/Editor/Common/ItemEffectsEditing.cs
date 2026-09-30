using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using CTEditor.GameDefinition.Domain.Effects;
using CTEditor.GameDefinition.Infrastructure.Acl;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// Moves items from the OLD per-feature fields to EFFECT BLOCKS. The item editor does it on its own when it opens an old
    /// item; the menu converts every item at once. The behaviour does not change (same conversion as the engine uses).
    /// </summary>
    public static class ItemEffectsEditing
    {
        /// <summary>Blocks the item has now (its effects + whatever is left in the old fields).</summary>
        public static List<EffectBlock> Blocks(ItemData d) => d == null ? new List<EffectBlock>() : ItemMapper.Effects(d);

        /// <summary>Converts one item (true if it had old data). Undo-able.</summary>
        public static bool Convert(ItemData d)
        {
            if (d == null || !d.HasLegacyEffects) return false;
            var blocks = Blocks(d);
            ContentAssets.Edit(d, so => SetBlocks(so, blocks));
            return true;
        }

        /// <summary>Replaces all the item's blocks and empties the old fields (so nothing counts twice).</summary>
        public static void SetBlocks(SerializedObject so, IList<EffectBlock> blocks)
        {
            EffectText.WriteAll(so.FindProperty("effects"), blocks);
            ClearLegacy(so);
        }

        [MenuItem(EditorMenus.Items + "Convertir objetos antiguos a efectos", false, EditorMenus.ItemsOrder + 50)]
        public static void ConvertAllFromMenu()
        {
            int n = ConvertAll();
            EditorUtility.DisplayDialog("Objetos", n == 0 ? "Todos los objetos ya usan efectos." : $"{n} objetos convertidos a efectos (se comportan igual).", "Vale");
        }

        public static int ConvertAll()
        {
            int n = 0;
            foreach (var d in ContentAssets.LoadAll<ItemData>()) if (Convert(d)) n++;
            if (n > 0) AssetDatabase.SaveAssets();
            return n;
        }

        /// <summary>Puts every OLD field back to «does nothing».</summary>
        public static void ClearLegacy(SerializedObject so)
        {
            void I(string f, int v) { var p = so.FindProperty(f); if (p != null) p.intValue = v; }
            void F(string f, float v) { var p = so.FindProperty(f); if (p != null) p.floatValue = v; }
            void B(string f, bool v) { var p = so.FindProperty(f); if (p != null) p.boolValue = v; }
            void S(string f, string v) { var p = so.FindProperty(f); if (p != null) p.stringValue = v; }
            void Arr(string f) { var p = so.FindProperty(f); if (p != null) p.arraySize = 0; }
            I("healHp", 0); F("healPercent", 0); B("curesAllStatus", false); S("curesStatusId", "");
            B("revives", false); F("reviveHpPercent", 50); I("restorePp", 0); B("restorePpAllMoves", false);
            I("friendshipChange", 0); F("catchMultiplier", 0); S("battleStatId", ""); I("battleStages", 0);
            Arr("heldPowerModifiers");
            F("heldEndOfTurnHealPercent", 0); F("heldTriggerHpPercent", 0); I("heldTriggerHealHp", 0);
            F("heldTriggerHealPercent", 0); B("heldConsumedOnTrigger", true);
            Arr("heldStatMultipliers"); Arr("heldOnHitStats");
            B("heldChoiceLock", false); F("heldAttackRecoilPercent", 0); B("heldSurviveFromFullHp", false);
            F("heldContactDamagePercent", 0); B("heldOnHitConsumed", false); B("heldAirBalloon", false);
            B("heldBlocksStatusMoves", false); I("heldCritStageBonus", 0); F("heldAccuracyMultiplier", 1f);
            F("heldEvasionMultiplier", 1f); S("heldResistBerryType", ""); B("heldCuresAnyStatus", false);
            S("heldSelfStatusEndOfTurn", ""); F("heldFlinchChance", 0); F("heldHealOnDamagePercent", 0);
            I("heldWeatherTurnsBonus", 0); I("heldScreenTurnsBonus", 0); B("heldBlackSludge", false);
            F("heldQuickClawChance", 0); F("heldSuperEffectiveBoost", 1f);
        }
    }
}
