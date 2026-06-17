namespace CTEditor.GameDefinition.Domain.Moves
{
    /// <summary>
    /// La categoría de un movimiento. Un 'enum' es un conjunto CERRADO de opciones con nombre:
    /// aquí solo existen estas tres y el autor elige una de un menú (no puede inventar categorías).
    ///
    /// ¿Por qué los TIPOS son abiertos (ElementType, contenido) pero la CATEGORÍA es cerrada?
    /// Porque la categoría no es una etiqueta: DECIDE LÓGICA. La fórmula de daño la lee para saber
    /// qué stats usar (Físico -> Ataque/Defensa; Especial -> Ataque Esp./Defensa Esp.; Estado -> no
    /// pega). Abrir la categoría significaría abrir las entrañas de la fórmula, que es caro (Parte D,
    /// Nivel 3 acoplado a la fórmula). Los tipos, en cambio, son solo una clave en una tabla: abrirlos
    /// es barato. La libertad no es uniforme; sigue al costo. (Más abajo te explico el seam para
    /// abrir esto cuando llegue el momento.)
    /// </summary>
    public enum MoveCategory
    {
        /// <summary>Pega usando Ataque vs Defensa.</summary>
        Physical,

        /// <summary>Pega usando Ataque Especial vs Defensa Especial.</summary>
        Special,

        /// <summary>No hace daño directo: su efecto son cambios de estado/stats (vía efectos).</summary>
        Status
    }
}
