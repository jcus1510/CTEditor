using UnityEngine;

namespace CTEditor.GameDefinition.Infrastructure.ScriptableObjects
{
    /// <summary>
    /// Un MIEMBRO de un equipo diseñado (de un entrenador o de un equipo prearmado del jugador). Es la
    /// misma ficha para ambos: así un NPC se arma exactamente igual que el jugador.
    /// </summary>
    [System.Serializable]
    /// <summary>Género de un miembro de equipo diseñado. Solo se añaden valores al final.</summary>
    public enum MemberGender { Random, Male, Female }

    public sealed class TeamMemberData
    {
        [Tooltip("Especie del miembro.")]
        public SpeciesData species;
        [Tooltip("Nivel (1-100).")]
        [Min(1)] public int level = 5;
        [Tooltip("Movimientos elegidos (hasta 4). Vacío = los últimos que aprende por nivel, como un salvaje.")]
        public MoveData[] moves;
        [Tooltip("Objeto que lleva equipado (Restos, una baya...). Vacío = nada.")]
        [ContentIdReference(typeof(ItemData))] public string heldItem = "";
        [Tooltip("Naturaleza fija. Vacío = una al azar.")]
        public NatureData nature;
        [Tooltip("IVs fijos para todas las estadísticas (0-31). -1 = al azar, como al nacer.")]
        [Range(-1, 31)] public int fixedIvs = -1;
        [Tooltip("Mote (nombre propio). Vacío = el nombre de la especie.")]
        public string nickname = "";
        [Tooltip("Género: al azar (según el % de hembras de su especie), macho o hembra. Las especies sin género lo ignoran.")]
        public MemberGender gender = MemberGender.Random;
    }
}
