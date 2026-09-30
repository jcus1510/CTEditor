using UnityEngine;

namespace CTEditor.Bootstrap
{
    /// <summary>
    /// El RIVAL de un combate, como un solo concepto. Distingue lo que mecánicamente importa:
    ///  - SALVAJE (wild = true): normalmente un solo monstruo, y SE PUEDE capturar.
    ///  - ENTRENADOR (wild = false): un equipo de uno o más, y NO se puede capturar (hay un dueño).
    ///
    /// Así construir encuentros por el mapa es trivial: cada encuentro/entrenador es un OpponentSpec.
    /// </summary>
    [System.Serializable]
    public sealed class OpponentSpec
    {
        [Tooltip("Los combatientes del rival (salvaje: normalmente 1; entrenador: su equipo).")]
        [SerializeField] private BattlerSpec[] members;

        [Tooltip("¿Es un monstruo salvaje? Si sí, se puede capturar. Si no, es un entrenador (no capturable).")]
        [SerializeField] private bool wild = true;

        public BattlerSpec[] Members => members;
        public bool IsWild => wild;
    }
}
