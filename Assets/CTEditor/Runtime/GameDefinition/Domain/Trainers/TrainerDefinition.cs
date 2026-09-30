using System;
using System.Collections.Generic;

namespace CTEditor.GameDefinition.Domain.Trainers
{
    /// <summary>
    /// NIVEL DE IA de un entrenador rival. Solo se añaden valores AL FINAL (Unity guarda el número).
    /// </summary>
    public enum TrainerAi
    {
        Random,  // NOVATO: movimientos al azar; a veces se le olvida usar sus objetos
        Smart,   // LISTO: el golpe que más daño hace, evita lo que no afecta; usa sus objetos a tiempo
        Expert   // EXPERTO: calcula el daño real, remata si puede, usa mejoras y estados con cabeza y CAMBIA de monstruo si pierde el duelo
    }

    /// <summary>
    /// Ajustes de la IA de un entrenador: si usa objetos (y cuáles lleva en su mochila), cuándo se cura
    /// y si puede cambiar de monstruo. Por defecto: usa objetos, se cura con un 25 % de PS o menos y
    /// puede cambiar (solo lo hace la IA Experta).
    /// </summary>
    public sealed class TrainerAiSettings
    {
        /// <summary>¿Usa objetos de su mochila en combate?</summary>
        public bool UseItems { get; }
        /// <summary>Su mochila: objetos y cantidad (Hiperpoción ×2...). Se gastan durante el combate.</summary>
        public IReadOnlyList<(string itemId, int quantity)> Items { get; }
        /// <summary>Se cura cuando su monstruo activo tiene este % de PS o menos.</summary>
        public int HealBelowPercent { get; }
        /// <summary>¿Puede retirar a su monstruo para sacar otro mejor? (lo decide la IA Experta).</summary>
        public bool CanSwitch { get; }
        /// <summary>Cómo elige movimientos cuando no se escriben (por defecto, según su IA).</summary>
        public MovesetStyle Moveset { get; }

        public TrainerAiSettings(bool useItems = true, IReadOnlyList<(string itemId, int quantity)> items = null,
            int healBelowPercent = 25, bool canSwitch = true, MovesetStyle moveset = MovesetStyle.ByAi)
        {
            Moveset = moveset;
            UseItems = useItems;
            var list = new List<(string, int)>();
            if (items != null)
                foreach (var (id, qty) in items)
                    if (!string.IsNullOrWhiteSpace(id) && qty > 0) list.Add((id.Trim(), qty));
            Items = list;
            HealBelowPercent = Math.Max(1, Math.Min(100, healBelowPercent));
            CanSwitch = canSwitch;
        }

        /// <summary>Los ajustes clásicos: usa objetos (si lleva), se cura al 25 %, puede cambiar.</summary>
        public static readonly TrainerAiSettings Default = new TrainerAiSettings();
    }

    /// <summary>
    /// Un ENTRENADOR rival: nombre, clase ("Cazabichos", "Líder de gimnasio"...), su equipo, cómo
    /// piensa y cuánto dinero paga al perder. Contenido inmutable creado por el autor.
    ///
    /// Dinero clásico: premio = BaseMoney × nivel de su ÚLTIMO monstruo (así un entrenador de nivel
    /// alto paga más).
    /// </summary>
    public sealed class TrainerDefinition
    {
        public string Id { get; }
        public string DisplayName { get; }
        public string TrainerClass { get; }
        public IReadOnlyList<TeamMemberSpec> Team { get; }
        public TrainerAi Ai { get; }

        private readonly int _aiLevel;
        /// <summary>Nivel de IA 1-5 (Novato … Campeón). Si no se eligió, sale de la IA antigua (novato 1, listo 2, experto 4).</summary>
        public int AiLevel => _aiLevel > 0 ? _aiLevel : AiProfile.LevelFromLegacy(Ai);
        /// <summary>Objetos, curación y cambios de la IA.</summary>
        public TrainerAiSettings AiSettings { get; }
        public int BaseMoney { get; }

        /// <summary>Lo que dice al empezar ("¡Mis bichos son los mejores!").</summary>
        public string IntroLine { get; }
        /// <summary>Lo que dice al PERDER.</summary>
        public string DefeatLine { get; }
        /// <summary>Lo que dice si GANA él.</summary>
        public string VictoryLine { get; }

        public TrainerDefinition(string id, string displayName, IReadOnlyList<TeamMemberSpec> team,
            string trainerClass = "", TrainerAi ai = TrainerAi.Smart, int baseMoney = 20,
            string introLine = "", string defeatLine = "", string victoryLine = "", TrainerAiSettings aiSettings = null,
            int aiLevel = 0)
        {
            _aiLevel = Math.Max(0, Math.Min(AiProfile.MaxLevel, aiLevel));
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("El entrenador necesita un id.", nameof(id));
            Id = id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName;
            TrainerClass = trainerClass ?? "";
            Team = team == null ? Array.Empty<TeamMemberSpec>() : new List<TeamMemberSpec>(team);
            Ai = ai;
            AiSettings = aiSettings ?? TrainerAiSettings.Default;
            BaseMoney = Math.Max(0, baseMoney);
            IntroLine = introLine ?? "";
            DefeatLine = defeatLine ?? "";
            VictoryLine = victoryLine ?? "";
        }

        /// <summary>"Cazabichos Pepe" (o solo el nombre si no tiene clase).</summary>
        public string FullName => string.IsNullOrWhiteSpace(TrainerClass) ? DisplayName : TrainerClass + " " + DisplayName;

        /// <summary>Nivel más alto de su equipo (para filtrar y para la dificultad).</summary>
        public int MaxTeamLevel { get { int m = 0; foreach (var t in Team) m = Math.Max(m, t.Level); return m; } }

        /// <summary>Premio al ganarle: BaseMoney × nivel del último miembro de su equipo.</summary>
        public int PrizeMoney => Team.Count == 0 ? 0 : BaseMoney * Team[Team.Count - 1].Level;
    }
}
