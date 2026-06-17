using System;

namespace CTEditor.SharedKernel.Abstractions
{
    /// <summary>
    /// Tiempo INYECTABLE (J.2). El dominio nunca llama a DateTime.Now (eso es infraestructura);
    /// pide la hora a través de esta interfaz. Útil, p.ej., para encuentros nocturnos.
    /// En tests inyectas un reloj fijo y el comportamiento se vuelve reproducible.
    /// </summary>
    public interface IClock
    {
        DateTimeOffset Now { get; }
    }
}
