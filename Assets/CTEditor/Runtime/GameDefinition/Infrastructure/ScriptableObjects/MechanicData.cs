using UnityEngine;
using CTEditor.GameDefinition.Domain.Rules.Mechanics;

namespace CTEditor.GameDefinition.Infrastructure.ScriptableObjects
{
    /// <summary>
    /// Ficha de una MECÁNICA ESPECIAL (Megaevolución y, más adelante, movimientos Z, Dinamax...). El TIPO dice cómo
    /// funciona (eso es código del motor); la ficha dice cómo está configurada. Puedes tener varias del mismo tipo
    /// (p. ej. «Megaevolución oficial» y «Megas sin límite») y activar en las REGLAS las que quieras, incluso varias
    /// a la vez. La traduce MechanicMapper al dominio (MechanicDefinition).
    /// </summary>
    [CreateAssetMenu(menuName = "CTEditor/Mecánica especial", fileName = "Mecanica")]
    public sealed class MechanicData : ScriptableObject, IContentAsset
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [Tooltip("Qué mecánica es. Cada tipo lo ejecuta el motor; aquí solo se configura.")]
        [SerializeField] private MechanicKind mechanicKind = MechanicKind.MegaEvolution;
        [Tooltip("Para qué sirve (texto de ayuda en el editor).")]
        [SerializeField, TextArea(2, 4)] private string description = "";

        [Header("💎 Megaevolución")]
        [Tooltip("Cuántas megaevoluciones puede hacer cada lado por combate. 1 = oficial. 0 = sin límite.")]
        [SerializeField, Min(0)] private int megaMaxPerBattle = 1;
        [Tooltip("Objeto clave que el JUGADOR necesita en la mochila para megaevolucionar (oficial: Megapulsera). Vacío = no hace falta. " +
                 "Los entrenadores rivales no lo necesitan: se decide en su ficha con «Puede megaevolucionar».")]
        [ContentIdReference(typeof(ItemData)), SerializeField] private string megaRequiredKeyItem = "mega_ring";
        [Tooltip("Marcado: al retirarse del combate vuelve a su forma normal. Oficial: desmarcado (se queda megaevolucionado).")]
        [SerializeField] private bool megaRevertOnSwitch = false;

        [Header("⚡ Movimientos Z")]
        [Tooltip("Cuántos movimientos Z puede usar cada lado por combate. 1 = oficial. 0 = sin límite.")]
        [SerializeField, Min(0)] private int zMaxPerBattle = 1;
        [Tooltip("Objeto clave que el JUGADOR necesita en la mochila (oficial: Pulsera Z). Vacío = no hace falta.")]
        [ContentIdReference(typeof(ItemData)), SerializeField] private string zRequiredKeyItem = "z_ring";
        [Tooltip("% del daño de un movimiento Z que atraviesa Protección (oficial: 25).")]
        [SerializeField, Range(0f, 100f)] private float zProtectDamagePercent = 25f;
        [Tooltip("Potencia Z según la potencia del movimiento: «hasta:potencia» separados por |, «*» = el resto. " +
                 "Oficial: 55:100|65:120|75:140|85:160|95:175|100:180|110:185|125:190|130:195|*:200")]
        [SerializeField] private string zPowerTable = ZMoveSettings.OfficialTable;

        public string Id => id;
        public string DisplayName => displayName;
        public MechanicKind Kind => mechanicKind;
        public string Description => description;
        public int MegaMaxPerBattle => megaMaxPerBattle;
        public string MegaRequiredKeyItem => megaRequiredKeyItem;
        public bool MegaRevertOnSwitch => megaRevertOnSwitch;
        public int ZMaxPerBattle => zMaxPerBattle;
        public string ZRequiredKeyItem => zRequiredKeyItem;
        public float ZProtectDamagePercent => zProtectDamagePercent;
        public string ZPowerTable => zPowerTable;
    }
}
