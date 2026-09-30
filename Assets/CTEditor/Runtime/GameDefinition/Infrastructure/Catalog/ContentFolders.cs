namespace CTEditor.GameDefinition.Infrastructure.Catalog
{
    /// <summary>
    /// ÚNICA fuente de verdad de "dónde vive cada categoría de contenido". La usan el runtime (para
    /// cargar con Resources.LoadAll) y los EDITORES (para crear assets nuevos en su sitio correcto).
    ///
    /// Antes estaba en Bootstrap/ContentPaths, pero el Editor no puede referenciar Bootstrap (su
    /// asmdef no lo incluye). Al vivir aquí, en Infraestructura, ambos lados leen lo mismo y nunca
    /// se desincronizan. ContentPaths sigue existiendo como alias para no romper código viejo.
    /// </summary>
    public static class ContentFolders
    {
        /// <summary>Carpeta física (para el Editor) donde vive el contenido cargable.</summary>
        public const string ResourcesRoot = "Assets/GameContent/Resources";

        // Rutas RELATIVAS a Resources (lo que recibe Resources.LoadAll).
        public const string Moves = "Moves";
        public const string Species = "Species";
        public const string Status = "Status";
        public const string Abilities = "Abilities";
        public const string Items = "Items";
        public const string Types = "Types";
        public const string Rulesets = "Rulesets";
        public const string Curves = "Curves";
        public const string Natures = "Natures";
        public const string Weathers = "Weathers";
        public const string Trainers = "Trainers";
        public const string Teams = "Teams";
        public const string Encounters = "Encounters";
        public const string Hazards = "Hazards";
        public const string SideConditions = "SideConditions";
        public const string Menus = "Menus";
        public const string AiLevels = "AiLevels";
        public const string EggGroups = "EggGroups";
        /// <summary>Ajustes de interfaz (teclas, texto, colores): normalmente una sola ficha.</summary>
        public const string Interface = "Interface";

        /// <summary>Ruta física de una categoría (p. ej. "Assets/GameContent/Resources/Curves").</summary>
        public static string PathOf(string category) => ResourcesRoot + "/" + category;
    }
}
