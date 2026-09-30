using F = CTEditor.GameDefinition.Infrastructure.Catalog.ContentFolders;

namespace CTEditor.Bootstrap
{
    /// <summary>
    /// Alias de compatibilidad: las rutas reales viven en ContentFolders (Infraestructura), para que
    /// runtime y editores compartan UNA sola fuente de verdad. Este archivo solo las re-exporta.
    ///
    /// Sintaxis: 'using F = ...' crea un ALIAS de tipo; así escribimos F.Moves en vez del nombre largo.
    /// </summary>
    public static class ContentPaths
    {
        public const string Moves = F.Moves;
        public const string Species = F.Species;
        public const string Status = F.Status;
        public const string Abilities = F.Abilities;
        public const string Items = F.Items;
        public const string Types = F.Types;
        public const string Rulesets = F.Rulesets;
        public const string Curves = F.Curves;
        public const string Natures = F.Natures;
        public const string Weathers = F.Weathers;
        public const string Trainers = F.Trainers;
        public const string Teams = F.Teams;
        public const string Encounters = F.Encounters;
        public const string Hazards = F.Hazards;
        public const string SideConditions = F.SideConditions;
        public const string AiLevels = F.AiLevels;
        public const string EggGroups = F.EggGroups;
        public const string Mechanics = F.Mechanics;
        public const string Sets = F.Sets;
    }
}
