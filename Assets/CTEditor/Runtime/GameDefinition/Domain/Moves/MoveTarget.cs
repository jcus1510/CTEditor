namespace CTEditor.GameDefinition.Domain.Moves
{
    /// <summary>
    /// A QUIÉN apunta un movimiento. También es un conjunto cerrado por ahora: en combate 1v1
    /// (L.1) la mayoría serán SingleEnemy o Self; dejamos los multi-objetivo listos para cuando
    /// el formato lo permita. Si algún día hace falta un objetivo nuevo, se agrega aquí (barato:
    /// es solo una opción más). Lo dejamos cerrado porque, igual que la categoría, el motor de
    /// combate "razona" sobre estos casos.
    /// </summary>
    public enum MoveTarget
    {
        /// <summary>Un solo rival (el caso típico de un ataque).</summary>
        SingleEnemy,

        /// <summary>El propio usuario (p.ej. subirse el ataque).</summary>
        Self,

        /// <summary>Todos los rivales.</summary>
        AllEnemies,

        /// <summary>Un aliado (formatos 2v2+).</summary>
        Ally,

        /// <summary>Todos en el campo.</summary>
        Everyone
    }
}
