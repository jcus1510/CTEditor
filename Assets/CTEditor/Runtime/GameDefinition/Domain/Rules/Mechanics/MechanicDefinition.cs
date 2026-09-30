using System;

namespace CTEditor.GameDefinition.Domain.Rules.Mechanics
{
    /// <summary>
    /// Los TIPOS de mecánica especial que el motor sabe ejecutar. Cada tipo es código (cómo funciona); cada FICHA de
    /// mecánica es un dato (cómo está configurada). Se pueden tener varias fichas del mismo tipo y activar varias a
    /// la vez en el Ruleset. Los tipos futuros (movimientos Z, Dinamax, Teracristal...) se añaden aquí.
    /// </summary>
    public enum MechanicKind
    {
        /// <summary>Megaevolución: con su megapiedra, cambia de forma en combate (una vez por combate, por defecto).</summary>
        MegaEvolution = 0,
    }

    /// <summary>Ajustes de la megaevolución (editables en su ficha).</summary>
    public sealed class MegaEvolutionSettings
    {
        /// <summary>Cuántas megaevoluciones puede hacer cada lado por combate. 1 = oficial; 0 = sin límite.</summary>
        public int MaxPerBattle { get; }

        /// <summary>Objeto clave que el JUGADOR necesita en la mochila (Megapulsera). Vacío = no hace falta.</summary>
        public string RequiredKeyItem { get; }

        /// <summary>true = al retirarse vuelve a su forma normal. Oficial: false (se queda megaevolucionado).</summary>
        public bool RevertOnSwitch { get; }

        public MegaEvolutionSettings(int maxPerBattle = 1, string requiredKeyItem = "mega_ring", bool revertOnSwitch = false)
        {
            MaxPerBattle = maxPerBattle < 0 ? 0 : maxPerBattle;
            RequiredKeyItem = requiredKeyItem?.Trim() ?? "";
            RevertOnSwitch = revertOnSwitch;
        }

        public static MegaEvolutionSettings Official => new MegaEvolutionSettings();

        /// <summary>¿Queda alguna megaevolución para un lado que ya hizo 'used'?</summary>
        public bool HasUsesLeft(int used) => MaxPerBattle == 0 || used < MaxPerBattle;
    }

    /// <summary>
    /// Una MECÁNICA ESPECIAL configurada (una ficha). El Ruleset guarda la lista de las que están activas; el motor
    /// comprueba en combate si hay alguna de un tipo antes de permitirla. Inmutable.
    /// </summary>
    public sealed class MechanicDefinition
    {
        public string Id { get; }
        public string DisplayName { get; }
        public MechanicKind Kind { get; }

        /// <summary>Ajustes si es de tipo <see cref="MechanicKind.MegaEvolution"/> (si no, null).</summary>
        public MegaEvolutionSettings Mega { get; }

        public MechanicDefinition(string id, string displayName, MechanicKind kind, MegaEvolutionSettings mega = null)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Una mecánica necesita id.", nameof(id));
            Id = id.Trim();
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Id : displayName;
            Kind = kind;
            Mega = kind == MechanicKind.MegaEvolution ? mega ?? MegaEvolutionSettings.Official : null;
        }

        /// <summary>La megaevolución oficial (6.ª-7.ª gen.).</summary>
        public static MechanicDefinition OfficialMega => new MechanicDefinition("mega_evolution", "Megaevolución", MechanicKind.MegaEvolution);
    }
}
