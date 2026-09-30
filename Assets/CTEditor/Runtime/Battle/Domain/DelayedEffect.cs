namespace CTEditor.Battle.Domain
{
    /// <summary>Un efecto que llega dentro de unos turnos a un LADO (Deseo cura; Premonición y Deseo Oculto golpean).</summary>
    internal sealed class DelayedEffect
    {
        public int TurnsLeft;
        public bool Heals;          // true = cura; false = daño
        public int Amount;          // PS (daño ya calculado al usarlo, o curación)
        public string MoveId;       // para narrarlo
        public string SourceName;   // quién lo lanzó (id del participante)
    }
}
