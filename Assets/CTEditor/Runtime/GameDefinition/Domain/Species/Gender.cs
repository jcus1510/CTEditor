namespace CTEditor.GameDefinition.Domain.Species
{
    /// <summary>
    /// GÉNERO de un individuo. Sale al azar según el «% de hembras» de su Pokédex (o «sin género»).
    /// Importa en Atracción y Gran Encanto (solo al género contrario), Rivalidad, Seducción y algunas
    /// evoluciones (Gallade solo macho, Froslass solo hembra...). Solo se añaden valores al final.
    /// </summary>
    public enum Gender
    {
        Genderless,  // sin género (Magnemite, legendarios...)
        Male,        // ♂
        Female       // ♀
    }

    public static class GenderText
    {
        public static string Symbol(Gender g) => g == Gender.Male ? "♂" : g == Gender.Female ? "♀" : "";
        public static string Name(Gender g) => g == Gender.Male ? "macho" : g == Gender.Female ? "hembra" : "sin género";

        /// <summary>¿Pueden atraerse? (géneros opuestos y ninguno sin género).</summary>
        public static bool Opposite(Gender a, Gender b) => a != Gender.Genderless && b != Gender.Genderless && a != b;

        /// <summary>Género a partir de un sorteo 0-100 y el % de hembras (-1 = sin género).</summary>
        public static Gender FromRoll(float femalePercent, float roll0to100)
            => femalePercent < 0f ? Gender.Genderless : roll0to100 < femalePercent ? Gender.Female : Gender.Male;
    }
}
