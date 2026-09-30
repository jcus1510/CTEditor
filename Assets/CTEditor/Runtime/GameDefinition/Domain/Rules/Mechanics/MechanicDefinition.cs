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
        /// <summary>Movimientos Z: con su cristal Z, un movimiento se convierte en su versión Z (una vez por combate).</summary>
        ZMove = 1,
    }

    /// <summary>Ajustes de los movimientos Z (editables en su ficha).</summary>
    public sealed class ZMoveSettings
    {
        /// <summary>Cuántos movimientos Z puede usar cada lado por combate. 1 = oficial; 0 = sin límite.</summary>
        public int MaxPerBattle { get; }

        /// <summary>Objeto clave que el JUGADOR necesita en la mochila (Pulsera Z). Vacío = no hace falta.</summary>
        public string RequiredKeyItem { get; }

        /// <summary>% del daño que atraviesa Protección y similares (oficial: 25).</summary>
        public float ProtectDamagePercent { get; }

        /// <summary>Potencia Z según la potencia del movimiento base: (hasta esta potencia, potencia Z), en orden.</summary>
        public System.Collections.Generic.IReadOnlyList<(int upTo, int zPower)> PowerTable { get; }

        public const string OfficialTable = "55:100|65:120|75:140|85:160|95:175|100:180|110:185|125:190|130:195|*:200";

        public ZMoveSettings(int maxPerBattle = 1, string requiredKeyItem = "z_ring", float protectDamagePercent = 25f, string powerTable = OfficialTable)
        {
            MaxPerBattle = maxPerBattle < 0 ? 0 : maxPerBattle;
            RequiredKeyItem = requiredKeyItem?.Trim() ?? "";
            ProtectDamagePercent = Math.Max(0f, Math.Min(100f, protectDamagePercent));
            PowerTable = ParseTable(string.IsNullOrWhiteSpace(powerTable) ? OfficialTable : powerTable);
        }

        public static ZMoveSettings Official => new ZMoveSettings();

        public bool HasUsesLeft(int used) => MaxPerBattle == 0 || used < MaxPerBattle;

        /// <summary>Potencia Z de un movimiento de potencia 'basePower' (tabla «hasta:potencia», «*» = el resto).</summary>
        public int PowerFor(int basePower)
        {
            foreach (var (upTo, z) in PowerTable) if (basePower <= upTo) return z;
            return PowerTable.Count > 0 ? PowerTable[PowerTable.Count - 1].zPower : 100;
        }

        private static System.Collections.Generic.List<(int, int)> ParseTable(string text)
        {
            var list = new System.Collections.Generic.List<(int, int)>();
            foreach (var part in text.Split('|'))
            {
                var kv = part.Split(':');
                if (kv.Length != 2 || !int.TryParse(kv[1].Trim(), out int z)) continue;
                string k = kv[0].Trim();
                int upTo = k == "*" ? int.MaxValue : int.TryParse(k, out int v) ? v : -1;
                if (upTo >= 0) list.Add((upTo, z));
            }
            list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            return list;
        }
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

        /// <summary>Ajustes si es de tipo <see cref="MechanicKind.ZMove"/> (si no, null).</summary>
        public ZMoveSettings Z { get; }

        public MechanicDefinition(string id, string displayName, MechanicKind kind, MegaEvolutionSettings mega = null, ZMoveSettings z = null)
        {
            Z = kind == MechanicKind.ZMove ? z ?? ZMoveSettings.Official : null;
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Una mecánica necesita id.", nameof(id));
            Id = id.Trim();
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Id : displayName;
            Kind = kind;
            Mega = kind == MechanicKind.MegaEvolution ? mega ?? MegaEvolutionSettings.Official : null;
        }

        /// <summary>La megaevolución oficial (6.ª-7.ª gen.).</summary>
        public static MechanicDefinition OfficialMega => new MechanicDefinition("mega_evolution", "Megaevolución", MechanicKind.MegaEvolution);

        /// <summary>Los movimientos Z oficiales (7.ª gen.).</summary>
        public static MechanicDefinition OfficialZ => new MechanicDefinition("z_moves", "Movimientos Z", MechanicKind.ZMove);
    }
}
