using UnityEditor;
using CTEditor.GameDefinition.Editor;

namespace CTEditor.Bootstrap.Editor
{
    // Los componentes de escena también se ven en español (mismo inspector que las fichas).
    [CustomEditor(typeof(PartyHolder))] public sealed class PartyHolderInspector : SpanishInspector { }
    [CustomEditor(typeof(BattleScreen))] public sealed class BattleScreenInspector : SpanishInspector { }
    [CustomEditor(typeof(GameBootstrap))] public sealed class GameBootstrapInspector : SpanishInspector { }
    [CustomEditor(typeof(BattleLab))] public sealed class BattleLabInspector : SpanishInspector { }
}
