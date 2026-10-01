using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CTEditor.GameDefinition.Domain.Conditions;
using CTEditor.GameDefinition.Domain.Effects;
using CTEditor.GameDefinition.Domain.Items;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;
using CTEditor.GameDefinition.Text;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// ITEM inspector: the common fields (name, pocket, price, where it is used) and then its EFFECTS as cards.
    /// «Plantillas por piezas» add ready-made blocks with a dropdown for the piece that changes
    /// («Baya que resiste un ataque de tipo ▸ Fuego»).
    /// </summary>
    [CustomEditor(typeof(ItemData))]
    public sealed class ItemDataInspector : SpanishInspector
    {
        protected override bool DrawsItself(string propertyName) => propertyName == "effects";

        protected override void DrawCustom()
        {
            EditorGUILayout.Space(4);
            if (GUILayout.Button("✨ Plantillas por piezas…", GUILayout.Height(22))) TemplatesMenu(serializedObject).ShowAsContext();
            EffectBlocksGui.Draw(serializedObject.FindProperty("effects"), EditorTheme.Items);
        }

        // ---------------- Piece templates ----------------

        private sealed class Piece
        {
            public string Menu;
            public EffectRefKind Choice;             // what the author picks (None = nothing to pick)
            public bool ChoiceAllowsAny;             // «cualquiera» as first option
            public Func<string, EffectBlock[]> Blocks;
            public Action<SerializedObject> Setup;   // category, where it is used...
        }

        private static Condition MoveType(string t) => new Condition(ConditionKind.MoveType, text: t);

        private static void Use(SerializedObject so, bool inBattle, bool outside, bool consumable = true)
        {
            so.FindProperty("usableInBattle").boolValue = inBattle;
            so.FindProperty("usableOutsideBattle").boolValue = outside;
            so.FindProperty("consumable").boolValue = consumable;
        }
        private static void HeldOnly(SerializedObject so) => Use(so, false, false, false);
        private static void Berry(SerializedObject so) { HeldOnly(so); so.FindProperty("isBerry").boolValue = true; so.FindProperty("category").intValue = (int)ItemCategory.Berry; }
        private static void Cat(SerializedObject so, ItemCategory c) => so.FindProperty("category").intValue = (int)c;

        private static readonly Piece[] Pieces =
        {
            new Piece { Menu = "Al usarlo/Medicina: cura PS", Blocks = _ => new[] { new EffectBlock(EffectTrigger.OnUse, EffectAction.HealHp, 20) },
                Setup = so => { Use(so, true, true); Cat(so, ItemCategory.Medicine); } },
            new Piece { Menu = "Al usarlo/Medicina: cura un % de PS", Blocks = _ => new[] { new EffectBlock(EffectTrigger.OnUse, EffectAction.HealPercent, 100) },
                Setup = so => { Use(so, true, true); Cat(so, ItemCategory.Medicine); } },
            new Piece { Menu = "Al usarlo/Cura el estado…", Choice = EffectRefKind.Status, ChoiceAllowsAny = true,
                Blocks = s => new[] { new EffectBlock(EffectTrigger.OnUse, EffectAction.CureStatus, 0, s) },
                Setup = so => { Use(so, true, true); Cat(so, ItemCategory.StatusCure); } },
            new Piece { Menu = "Al usarlo/Revivir", Blocks = _ => new[] { new EffectBlock(EffectTrigger.OnUse, EffectAction.Revive, 50) },
                Setup = so => { Use(so, true, true); Cat(so, ItemCategory.Revive); } },
            new Piece { Menu = "Al usarlo/Recupera PP", Blocks = _ => new[] { new EffectBlock(EffectTrigger.OnUse, EffectAction.RestorePp, 10) },
                Setup = so => { Use(so, true, true); Cat(so, ItemCategory.PpRestore); } },
            new Piece { Menu = "Al usarlo/Bola de captura", Blocks = _ => new[] { new EffectBlock(EffectTrigger.OnUse, EffectAction.Catch, 1.5f) },
                Setup = so => { Use(so, true, false); Cat(so, ItemCategory.Ball); } },
            new Piece { Menu = "Al usarlo/Sube una etapa en combate…", Choice = EffectRefKind.Stat,
                Blocks = s => new[] { new EffectBlock(EffectTrigger.OnUse, EffectAction.ChangeStage, 2, s) },
                Setup = so => { Use(so, true, false); Cat(so, ItemCategory.BattleBoost); } },
            new Piece { Menu = "Al usarlo/MT: enseña un movimiento…", Choice = EffectRefKind.Move,
                Blocks = s => new[] { new EffectBlock(EffectTrigger.OnUse, EffectAction.TeachMove, 0, s) },
                Setup = so => { Use(so, false, true); Cat(so, ItemCategory.Machine); } },
            new Piece { Menu = "Al usarlo/Sube la amistad", Blocks = _ => new[] { new EffectBlock(EffectTrigger.OnUse, EffectAction.Friendship, 10) },
                Setup = so => { Use(so, false, true); Cat(so, ItemCategory.Vitamin); } },

            new Piece { Menu = "Equipado/Potencia los movimientos de tipo…", Choice = EffectRefKind.Type,
                Blocks = s => new[] { new EffectBlock(EffectTrigger.Passive, EffectAction.PowerMultiplier, 1.2f, conditions: new[] { MoveType(s) }) },
                Setup = so => { HeldOnly(so); Cat(so, ItemCategory.Held); } },
            new Piece { Menu = "Equipado/Multiplica una estadística…", Choice = EffectRefKind.Stat,
                Blocks = s => new[] { new EffectBlock(EffectTrigger.Passive, EffectAction.MultiplyStat, 1.5f, s) },
                Setup = so => { HeldOnly(so); Cat(so, ItemCategory.Held); } },
            new Piece { Menu = "Equipado/Estadística ×1,5 pero solo repite un movimiento (Elección)…", Choice = EffectRefKind.Stat,
                Blocks = s => new[] { new EffectBlock(EffectTrigger.Passive, EffectAction.MultiplyStat, 1.5f, s), new EffectBlock(EffectTrigger.Passive, EffectAction.ChoiceLock) },
                Setup = so => { HeldOnly(so); Cat(so, ItemCategory.Held); } },
            new Piece { Menu = "Equipado/Cura un % cada turno", Blocks = _ => new[] { new EffectBlock(EffectTrigger.EndOfTurn, EffectAction.HealPercent, 6.25f) },
                Setup = so => { HeldOnly(so); Cat(so, ItemCategory.Held); } },
            new Piece { Menu = "Equipado/Inmune a un tipo…", Choice = EffectRefKind.Type,
                Blocks = s => new[] { new EffectBlock(EffectTrigger.Passive, EffectAction.ImmuneToType, 0, s) },
                Setup = so => { HeldOnly(so); Cat(so, ItemCategory.Held); } },
            new Piece { Menu = "Equipado/Alarga un clima…", Choice = EffectRefKind.Weather, ChoiceAllowsAny = true,
                Blocks = s => new[] { new EffectBlock(EffectTrigger.Passive, EffectAction.ExtendWeather, 3, s) },
                Setup = so => { HeldOnly(so); Cat(so, ItemCategory.Held); } },
            new Piece { Menu = "Equipado/Se pone un estado al final del turno…", Choice = EffectRefKind.Status,
                Blocks = s => new[] { new EffectBlock(EffectTrigger.EndOfTurn, EffectAction.InflictStatus, 0, s) },
                Setup = so => { HeldOnly(so); Cat(so, ItemCategory.Held); } },
            new Piece { Menu = "Equipado/Aguanta con 1 PS si tenía la vida llena", Blocks = _ => new[] {
                    new EffectBlock(EffectTrigger.BeforeHit, EffectAction.SurviveAt1Hp, conditions: new[] { ItemEffects.FullHp() }, consumes: true) },
                Setup = so => { HeldOnly(so); Cat(so, ItemCategory.Held); } },
            new Piece { Menu = "Equipado/Daña a quien le golpea con contacto", Blocks = _ => new[] {
                    new EffectBlock(EffectTrigger.ContactTaken, EffectAction.LoseHpPercent, 16.67f, target: BlockTarget.Other) },
                Setup = so => { HeldOnly(so); Cat(so, ItemCategory.Held); } },

            new Piece { Menu = "Bayas/Resiste un ataque de tipo…", Choice = EffectRefKind.Type,
                Blocks = s => new[] { new EffectBlock(EffectTrigger.BeforeHit, EffectAction.DamageTakenMultiplier, 0.5f,
                    conditions: new[] { MoveType(s), ItemEffects.SuperEffectiveOn(ConditionSubject.Self) }, consumes: true) },
                Setup = Berry },
            new Piece { Menu = "Bayas/Cura PS con poca vida", Blocks = _ => new[] {
                    new EffectBlock(EffectTrigger.LowHp, EffectAction.HealPercent, 25, threshold: 50, consumes: true) },
                Setup = Berry },
            new Piece { Menu = "Bayas/Sube una estadística con poca vida…", Choice = EffectRefKind.Stat,
                Blocks = s => new[] { new EffectBlock(EffectTrigger.LowHp, EffectAction.ChangeStage, 1, s, threshold: 25, consumes: true) },
                Setup = Berry },
            new Piece { Menu = "Bayas/Cura un estado en cuanto lo sufre…", Choice = EffectRefKind.Status, ChoiceAllowsAny = true,
                Blocks = s => new[] { new EffectBlock(EffectTrigger.OnStatus, EffectAction.CureStatus, 0, s, consumes: true) },
                Setup = Berry },
        };

        private static GenericMenu TemplatesMenu(SerializedObject so)
        {
            var menu = new GenericMenu();
            foreach (var piece in Pieces)
            {
                var p = piece;
                if (p.Choice == EffectRefKind.None) { menu.AddItem(new GUIContent(p.Menu), false, () => Apply(so, p, "")); continue; }
                string root = p.Menu.TrimEnd('…');
                var options = ChoiceOptions(p.Choice);
                if (p.ChoiceAllowsAny) menu.AddItem(new GUIContent(root + "/(cualquiera)"), false, () => Apply(so, p, ""));
                foreach (var (id, name) in options)
                {
                    string first = p.Choice == EffectRefKind.Move && name.Length > 0 ? char.ToUpperInvariant(name[0]) + "/" : "";
                    var pick = id;
                    menu.AddItem(new GUIContent(root + "/" + first + name), false, () => Apply(so, p, pick));
                }
                if (options.Count == 0) menu.AddDisabledItem(new GUIContent(root + "/(no hay ninguno en el proyecto)"));
            }
            return menu;
        }

        private static List<(string id, string name)> ChoiceOptions(EffectRefKind k)
        {
            IEnumerable<IContentAsset> assets;
            switch (k)
            {
                case EffectRefKind.Type: assets = ContentAssets.LoadAll<ElementTypeData>(); break;
                case EffectRefKind.Status: assets = ContentAssets.LoadAll<StatusConditionData>(); break;
                case EffectRefKind.Weather: assets = ContentAssets.LoadAll<WeatherData>(); break;
                case EffectRefKind.Move: assets = ContentAssets.LoadAll<MoveData>(); break;
                case EffectRefKind.Stat:
                    return StatLabels.ClassicIds.Concat(new[] { "accuracy", "evasion" }).Select(id => (id, StatLabels.NameOf(id))).ToList();
                default: return new List<(string, string)>();
            }
            return assets.Where(a => !string.IsNullOrWhiteSpace(a.Id)).Select(a => (a.Id, ContentAssets.Label((UnityEngine.Object)a)))
                .OrderBy(x => x.Item2, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private static void Apply(SerializedObject so, Piece p, string choice)
        {
            so.Update();
            var arr = so.FindProperty("effects");
            foreach (var b in p.Blocks(choice)) EffectBlocksGui.Append(arr, b);
            p.Setup?.Invoke(so);
            so.ApplyModifiedProperties();
        }
    }
}
