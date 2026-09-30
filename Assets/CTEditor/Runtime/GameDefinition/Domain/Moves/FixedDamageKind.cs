namespace CTEditor.GameDefinition.Domain.Moves
{
    /// <summary>
    /// DAÑO ESPECIAL: movimientos cuyo daño NO sale de la fórmula (potencia × stats). Ignoran STAB,
    /// críticos, azar y la efectividad de tipos (solo respetan la INMUNIDAD: Bomba Sónica no afecta a
    /// un Fantasma). El orden importa: Unity guarda el número, así que solo se añaden al final.
    /// </summary>
    public enum FixedDamageKind
    {
        None,          // daño normal por fórmula
        Fixed,         // siempre la misma cantidad de PS (Bomba Sónica 20, Furia Dragón 40)
        UserLevel,     // tanto daño como el NIVEL del usuario (Sísmico, Tinieblas)
        HalfTargetHp,  // la mitad de los PS ACTUALES del objetivo (Superdiente)
        OneHitKo,      // debilita de un golpe; falla si el objetivo tiene más nivel (Guillotina, Fisura)
        ReturnPhysical, // devuelve el daño FÍSICO recibido este turno × (FixedDamageAmount %, 0 = 200 %) (Contraataque)
        ReturnSpecial,  // devuelve el daño ESPECIAL recibido este turno × (FixedDamageAmount %, 0 = 200 %) (Manto Espejo)
        Bide,           // aguanta 2 turnos y devuelve todo el daño recibido × (FixedDamageAmount %, 0 = 200 %) (Venganza)
        // --- 3.ª y 4.ª generación (solo AL FINAL) ---
        Endeavor,       // deja al objetivo con los mismos PS que el usuario (Esfuerzo)
        ReturnAny,      // devuelve el ÚLTIMO daño recibido este turno (físico o especial) × % (0 = 150 %) (Repr. Metal)
        // --- 5.ª generación ---
        UserHp          // tanto daño como los PS actuales del usuario (Sacrificio; el usuario se debilita con «debilitarse»)
    }
}
