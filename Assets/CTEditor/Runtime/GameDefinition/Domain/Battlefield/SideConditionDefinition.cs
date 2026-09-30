using System;

namespace CTEditor.GameDefinition.Domain.Battlefield
{
    /// <summary>
    /// Un EFECTO DE LADO: algo que protege (o ayuda) a TODO un bando durante unos turnos, esté quien esté
    /// en el campo. Es contenido: el autor inventa los suyos combinando estas piezas.
    ///
    ///   • Reflejo: daño FÍSICO recibido × 0,5 durante 5 turnos.
    ///   • Pantalla de Luz: daño ESPECIAL recibido × 0,5 durante 5 turnos.
    ///   • Neblina: el rival no puede BAJAR sus estadísticas durante 5 turnos.
    ///   • Velo Sagrado: el rival no puede ponerle ESTADOS durante 5 turnos.
    ///   • Viento Afín: VELOCIDAD × 2 durante 4 turnos.
    ///
    /// Los golpes críticos ignoran las reducciones de daño (como en los juegos).
    /// </summary>
    public sealed class SideConditionDefinition
    {
        public string Id { get; }
        public string DisplayName { get; }
        /// <summary>Cuántos turnos dura (contando el turno en que se pone).</summary>
        public int Turns { get; }
        /// <summary>Multiplicador del daño FÍSICO que recibe este lado (1 = normal; Reflejo 0,5).</summary>
        public float PhysicalDamageMultiplier { get; }
        /// <summary>Multiplicador del daño ESPECIAL que recibe este lado (1 = normal; Pantalla de Luz 0,5).</summary>
        public float SpecialDamageMultiplier { get; }
        /// <summary>El rival no puede bajarle etapas (Neblina).</summary>
        public bool BlocksStatDrops { get; }
        /// <summary>El rival no puede ponerle estados (Velo Sagrado).</summary>
        public bool BlocksStatus { get; }
        /// <summary>Multiplicador de VELOCIDAD de este lado (Viento Afín ×2).</summary>
        public float SpeedMultiplier { get; }

        // --- 3.ª y 4.ª generación ---
        /// <summary>Los más lentos actúan primero (Espacio Raro). Si CUALQUIER lado lo tiene, se invierte el orden.</summary>
        public bool ReversesTurnOrder { get; }
        /// <summary>Multiplica la precisión de los movimientos que se lanzan CONTRA este lado (Gravedad ×5/3).</summary>
        public float AccuracyMultiplier { get; }
        /// <summary>Este lado queda en el suelo: le afecta Tierra aunque vuele o levite (Gravedad).</summary>
        public bool GroundsTargets { get; }
        /// <summary>Los golpes contra este lado no pueden ser críticos (Conjuro).</summary>
        public bool BlocksCrits { get; }
        /// <summary>Daño recibido por este lado según el tipo del movimiento (Chapoteo Lodo: Eléctrico ×0,33). Vacío = nada.</summary>
        public System.Collections.Generic.IReadOnlyDictionary<string, float> TypeDamageMultipliers { get; }

        public SideConditionDefinition(string id, string displayName, int turns = 5,
            float physicalDamageMultiplier = 1f, float specialDamageMultiplier = 1f,
            bool blocksStatDrops = false, bool blocksStatus = false, float speedMultiplier = 1f,
            bool reversesTurnOrder = false, float accuracyMultiplier = 1f, bool groundsTargets = false, bool blocksCrits = false,
            System.Collections.Generic.IReadOnlyDictionary<string, float> typeDamageMultipliers = null)
        {
            ReversesTurnOrder = reversesTurnOrder;
            AccuracyMultiplier = accuracyMultiplier <= 0f ? 1f : accuracyMultiplier;
            GroundsTargets = groundsTargets;
            BlocksCrits = blocksCrits;
            var map = new System.Collections.Generic.Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            if (typeDamageMultipliers != null) foreach (var kv in typeDamageMultipliers) if (!string.IsNullOrWhiteSpace(kv.Key)) map[kv.Key.Trim()] = Math.Max(0f, kv.Value);
            TypeDamageMultipliers = map;
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("El efecto de lado necesita un id.", nameof(id));
            Id = id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName;
            Turns = Math.Max(1, turns);
            PhysicalDamageMultiplier = Math.Max(0f, physicalDamageMultiplier);
            SpecialDamageMultiplier = Math.Max(0f, specialDamageMultiplier);
            BlocksStatDrops = blocksStatDrops;
            BlocksStatus = blocksStatus;
            SpeedMultiplier = speedMultiplier <= 0f ? 1f : speedMultiplier;
        }

        /// <summary>¿Hace algo? (para avisar en el editor de efectos vacíos).</summary>
        public bool DoesSomething => PhysicalDamageMultiplier != 1f || SpecialDamageMultiplier != 1f || BlocksStatDrops || BlocksStatus || SpeedMultiplier != 1f
                                     || ReversesTurnOrder || AccuracyMultiplier != 1f || GroundsTargets || BlocksCrits || TypeDamageMultipliers.Count > 0;
    }
}
