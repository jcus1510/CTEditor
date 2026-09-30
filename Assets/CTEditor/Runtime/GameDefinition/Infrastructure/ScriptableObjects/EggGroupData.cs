using UnityEngine;

namespace CTEditor.GameDefinition.Infrastructure.ScriptableObjects
{
    /// <summary>
    /// Un GRUPO HUEVO (Monstruo, Agua 1, Dragón...): dos especies pueden criar juntas si comparten alguno.
    /// Se asignan en la ficha de cada especie (hasta 2). «Desconocido» (no_eggs) = no puede criar.
    /// La crianza llegará más adelante: de momento sirve para organizar y consultar.
    /// </summary>
    [CreateAssetMenu(menuName = "CTEditor/Grupo huevo", fileName = "GrupoHuevo")]
    public sealed class EggGroupData : ScriptableObject, IContentAsset
    {
        [SerializeField] private string id;
        [Tooltip("Nombre visible (Monstruo, Agua 1...).")]
        [SerializeField] private string displayName;
        [Tooltip("Color de la etiqueta en los editores.")]
        [SerializeField] private Color color = new Color(0.6f, 0.75f, 0.9f);
        [Tooltip("Si está marcado, las especies de este grupo NO pueden criar (como «Desconocido»).")]
        [SerializeField] private bool cannotBreed;
        [Tooltip("Descripción para el autor.")]
        [SerializeField, TextArea(2, 3)] private string description = "";

        public string Id => id;
        public string DisplayName => displayName;
        public Color Color => color;
        public bool CannotBreed => cannotBreed;
        public string Description => description;
    }
}
