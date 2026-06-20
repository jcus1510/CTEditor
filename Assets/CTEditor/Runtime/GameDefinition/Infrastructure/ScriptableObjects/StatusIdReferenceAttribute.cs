using UnityEngine;

namespace CTEditor.GameDefinition.Infrastructure.ScriptableObjects
{
    /// <summary>
    /// Marca un campo string como "id de un estado". Un PropertyDrawer del Editor lo dibuja como un
    /// DESPLEGABLE de los estados existentes, en vez de un cuadro de texto libre — así el autor no
    /// escribe el id a mano (ni lo escribe mal). Es solo una marca; vive aquí (no en Editor) porque
    /// el campo que la usa, en MoveData, también vive aquí. El dibujo, en cambio, sí es de Editor.
    /// </summary>
    public sealed class StatusIdReferenceAttribute : PropertyAttribute
    {
    }
}
