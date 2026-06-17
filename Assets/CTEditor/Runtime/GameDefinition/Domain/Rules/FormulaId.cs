using System;

namespace CTEditor.GameDefinition.Domain.Rules
{
    /// <summary>
    /// El NOMBRE de una fórmula elegida, como texto estable. NO es la fórmula en sí: es solo su id.
    ///
    /// Aquí está el detalle fino. Una fórmula concreta (la fórmula de daño "clásica", por ejemplo)
    /// es una pieza de Battle, y Battle DEPENDE de GameDefinition (no al revés). Si el Ruleset
    /// —que vive en GameDefinition— guardara directamente un Id&lt;IDamageFormula&gt; tipado a la
    /// interfaz de Battle, crearía un ciclo de dependencias (GameDefinition -> Battle -> GameDefinition).
    /// Es el MISMO problema que tendremos con los efectos.
    ///
    /// La salida: el Ruleset nombra la fórmula con un id de TEXTO ("classic"), sin conocer su tipo C#.
    /// Más tarde, un registro en Infrastructure (Battle) traduce "classic" -> ClassicDamageFormula.
    /// El Ruleset dice QUÉ fórmula usar; no sabe CÓMO está implementada ni dónde vive.
    ///
    /// El costo de esto (sé honesto): se pierde seguridad en compilación. Si escribes "clasic" mal,
    /// el compilador no se entera; el error aparece al resolver el id (o lo atrapa la validación de
    /// autor, L.8). A cambio, ganas el seam para que un creador añada fórmulas nuevas sin tocar el
    /// motor. Es exactamente el trato del Nivel 2 (Parte D): flexibilidad a cambio de type-safety.
    /// </summary>
    public readonly struct FormulaId : IEquatable<FormulaId>
    {
        public string Value { get; }

        public FormulaId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Un FormulaId no puede ser nulo ni vacío.", nameof(value));
            Value = value;
        }

        public bool Equals(FormulaId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is FormulaId other && Equals(other);
        public override int GetHashCode() => Value is null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? "(fórmula vacía)";

        public static bool operator ==(FormulaId a, FormulaId b) => a.Equals(b);
        public static bool operator !=(FormulaId a, FormulaId b) => !a.Equals(b);
    }
}
