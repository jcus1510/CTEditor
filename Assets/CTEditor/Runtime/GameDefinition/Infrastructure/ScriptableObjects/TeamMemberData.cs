using UnityEngine;

namespace CTEditor.GameDefinition.Infrastructure.ScriptableObjects
{
    /// <summary>Género de un miembro de equipo diseñado. Solo se añaden valores al final.</summary>
    public enum MemberGender { Random, Male, Female }

    /// <summary>
    /// Un MIEMBRO de un equipo diseñado (de un entrenador o de un equipo prearmado del jugador). Es la
    /// misma ficha para ambos: así un NPC se arma exactamente igual que el jugador.
    ///
    /// Además de las REFERENCIAS (especie, movimientos, naturaleza) guarda sus IDS. Una referencia de Unity
    /// apunta al archivo (GUID): si la especie se borra y se vuelve a crear (o se reimporta un pack), la
    /// referencia se rompe, pero el id sigue valiendo. El juego usa el id si la referencia falta, y
    /// «🔗 Reenlazar por id» (Centro de Contenido / validador) vuelve a enlazar las referencias.
    ///
    /// OJO: [System.Serializable] tiene que ir JUSTO encima de la clase. Si no, Unity no guarda los equipos.
    /// </summary>
    [System.Serializable]
    public sealed class TeamMemberData
    {
        [Tooltip("Especie del miembro.")]
        public SpeciesData species;
        [Tooltip("Nivel (1-100).")]
        [Min(1)] public int level = 5;
        [Tooltip("Movimientos elegidos (hasta 4). Vacío = los que elija su nivel de IA (o los últimos que aprende por nivel).")]
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

        // --- Ids de respaldo (se rellenan solos; ver arriba) ---
        [HideInInspector] public string speciesId = "";
        [HideInInspector] public string[] moveIds = new string[0];
        [HideInInspector] public string natureId = "";

        /// <summary>Id de la especie: el de la referencia si existe; si se rompió, el guardado.</summary>
        public string SpeciesKey => species != null && !string.IsNullOrWhiteSpace(species.Id) ? species.Id : (speciesId ?? "").Trim();

        /// <summary>Id de la naturaleza (referencia o respaldo). Vacío = al azar.</summary>
        public string NatureKey => nature != null && !string.IsNullOrWhiteSpace(nature.Id) ? nature.Id : (natureId ?? "").Trim();

        /// <summary>Ids de los movimientos, en orden: cada hueco usa su referencia o, si se rompió, su id guardado.</summary>
        public string[] MoveKeys()
        {
            int n = System.Math.Max(moves?.Length ?? 0, moveIds?.Length ?? 0);
            var list = new System.Collections.Generic.List<string>();
            for (int i = 0; i < n; i++)
            {
                var m = moves != null && i < moves.Length ? moves[i] : null;
                string key = m != null && !string.IsNullOrWhiteSpace(m.Id) ? m.Id
                           : moveIds != null && i < moveIds.Length ? (moveIds[i] ?? "").Trim() : "";
                if (key.Length > 0) list.Add(key);
            }
            return list.ToArray();
        }

        /// <summary>
        /// Copia los ids de las referencias vivas a los campos de respaldo. Las referencias rotas NO borran el id
        /// guardado (es justo lo que permite recuperarlas). Se llama desde OnValidate de las fichas que tienen equipos.
        /// </summary>
        public void SyncIds()
        {
            if (species != null && !string.IsNullOrWhiteSpace(species.Id)) speciesId = species.Id;
            if (nature != null && !string.IsNullOrWhiteSpace(nature.Id)) natureId = nature.Id;
            int n = moves?.Length ?? 0;
            var ids = new string[n];
            for (int i = 0; i < n; i++)
            {
                var m = moves[i];
                ids[i] = m != null && !string.IsNullOrWhiteSpace(m.Id) ? m.Id
                       : moveIds != null && i < moveIds.Length ? moveIds[i] ?? "" : "";
            }
            // Una referencia rota deja su HUECO (null) en la lista, así que su id se conserva arriba. Una lista
            // VACÍA es intencional («que elija la IA»): vacía también los ids.
            moveIds = ids;
        }

        /// <summary>Sincroniza los ids de todo un equipo (null-safe).</summary>
        public static void SyncIds(TeamMemberData[] team)
        {
            if (team == null) return;
            foreach (var m in team) m?.SyncIds();
        }
    }
}
