namespace CTEditor.GameDefinition.Domain.Effects
{
    /// <summary>
    /// Un EFECTO: una descripción DECLARATIVA de "qué debería pasar" (curar, dar un objeto, aplicar
    /// un estado...). Es solo una marca: NO contiene la lógica de cómo se aplica. El "cómo" lo decide
    /// el EffectDispatcher, traduciendo cada efecto a un COMANDO del contexto dueño (E.2). Esa
    /// separación es lo que evita que un efecto toque internos ajenos y acople todos los contextos.
    ///
    /// UBICACIÓN (el seam que te anticipé): el documento ponía IEffect en Eventing. La bajo aquí, a
    /// GameDefinition.Domain, porque los efectos son CONTENIDO AUTORADO: un objeto los tiene
    /// ("Súper Poción" = [Curar(50)]), un EventScript los tiene, y mañana un movimiento. Como
    /// Eventing depende de GameDefinition (y no al revés), si IEffect viviera en Eventing, el
    /// contenido no podría sostener efectos sin un ciclo. Aquí, todos pueden referenciarlo. La
    /// EJECUCIÓN (el dispatcher) sí vive en Eventing. Anótalo para tu control de versiones.
    /// </summary>
    public interface IEffect
    {
    }
}
