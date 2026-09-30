using UnityEngine;

namespace CTEditor.GameDefinition.Infrastructure.ScriptableObjects
{
    /// <summary>
    /// The asset the AUTHOR fills to make an ABILITY: its name and its EFFECT BLOCKS («when / if / then», the same pieces as
    /// items). The ACL (AbilityMapper) turns it into AbilityDefinition (pure domain), which reads what the engine needs from
    /// the blocks. Nothing else: every behaviour of every generation is a block.
    /// </summary>
    [CreateAssetMenu(menuName = "CTEditor/Habilidad", fileName = "NuevaHabilidad")]
    public sealed class AbilityData : ScriptableObject, IContentAsset
    {
        [Tooltip("Id estable de la habilidad (clave de texto, p.ej. \"levitate\").")]
        [SerializeField] private string id;

        [SerializeField] private string displayName;
        [Tooltip("Nombre en INGLÉS (el de Showdown): sirve para importar y exportar equipos. Vacío = se deduce del id.")]
        [SerializeField] private string englishName = "";

        [Tooltip("Qué hace la habilidad: bloques «cuándo / si / qué» (los mismos que los objetos).")]
        [SerializeField] private EffectBlockData[] effects = new EffectBlockData[0];

        public string Id => id;
        public string DisplayName => displayName;
        public string EnglishName => englishName;
        public EffectBlockData[] Effects => effects;
    }
}
