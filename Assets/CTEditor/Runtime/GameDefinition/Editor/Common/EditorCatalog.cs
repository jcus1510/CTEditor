using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// CATEGORÍAS de editores. El orden es el de los menús y del Centro de Contenido. Para una categoría
    /// nueva (por ejemplo "Sonido"), se añade aquí y en EditorCatalog.Categories.
    /// </summary>
    public enum EditorCategory
    {
        Creatures,   // especies, evoluciones, habilidades, naturalezas, curvas
        Battle,      // movimientos, tipos, estados, climas, trampas, reglas
        Items,       // objetos (y más adelante tiendas)
        Characters,  // entrenadores, equipos prearmados (y más adelante NPC)
        World,       // zonas salvajes (y más adelante mapas y eventos)
        Interface,   // menús, pantallas, textos (próximamente)
        Tools,       // Excel, validador
        Testing      // simulador, calculadora, escena de pruebas
    }

    /// <summary>
    /// Las RUTAS de los menús de Unity, agrupadas por categoría (CTEditor → Criaturas → Especies...). Están
    /// aquí juntas para que todos los editores usen las mismas y el menú no se desordene al crecer.
    /// Las prioridades separan los grupos con una línea (Unity pone separador cuando saltan 11 o más).
    /// </summary>
    public static class EditorMenus
    {
        public const string Root = "CTEditor/";
        public const string Creatures = Root + "Criaturas/";
        public const string Battle = Root + "Combate/";
        public const string Items = Root + "Objetos/";
        public const string Characters = Root + "Personajes/";
        public const string World = Root + "Mundo/";
        public const string Interface = Root + "Interfaz/";
        public const string Tools = Root + "Herramientas/";
        public const string Testing = Root + "Pruebas/";

        public const string HubPath = Root + "Centro de Contenido";
        public const string BattleScenePath = Testing + "Crear escena de pruebas de combate";

        // Prioridades: una banda por categoría.
        public const int HubOrder = 0;
        public const int CreaturesOrder = 100;
        public const int BattleOrder = 200;
        public const int ItemsOrder = 300;
        public const int CharactersOrder = 400;
        public const int WorldOrder = 500;
        public const int InterfaceOrder = 600;
        public const int ToolsOrder = 800;
        public const int TestingOrder = 900;
    }

    /// <summary>Un editor (o herramienta) del catálogo: cómo se llama, qué hace y cómo se abre.</summary>
    public sealed class EditorEntry
    {
        public EditorCategory Category;
        public string Title;
        public string Icon = "•";
        public string Description = "";
        public Color Color = Color.gray;
        /// <summary>Abre el editor. Null = aún no existe (se muestra como "próximamente").</summary>
        public Action Open;
        /// <summary>Cuántas fichas hay (null = no aplica).</summary>
        public Func<int> Count;
        public int Order;
        /// <summary>Palabras extra para el buscador del Centro de Contenido.</summary>
        public string Keywords = "";

        public bool ComingSoon => Open == null;

        public bool Matches(string search)
        {
            if (string.IsNullOrWhiteSpace(search)) return true;
            string text = (Title + " " + Description + " " + Keywords + " " + EditorCatalog.Info(Category).name).ToLowerInvariant();
            return search.ToLowerInvariant().Split(' ').Where(w => w.Length > 0).All(text.Contains);
        }
    }

    /// <summary>
    /// CATÁLOGO DE EDITORES: la lista única de todo lo que se puede editar, por categorías. Lo usa el
    /// Centro de Contenido (con buscador) y cualquier ventana que quiera enlazar a otra.
    ///
    /// Para AÑADIR un editor nuevo (mapas, menús, tiendas...) basta con registrarlo:
    /// <code>
    /// [InitializeOnLoad] static class MisEditores {
    ///     static MisEditores() => EditorCatalog.Register(new EditorEntry { Category = EditorCategory.World,
    ///         Title = "Mapas", Icon = "🗺", Open = MapEditorWindow.Open, ... });
    /// }
    /// </code>
    /// Si ya había una entrada "próximamente" con el mismo título en la misma categoría, la sustituye.
    /// </summary>
    public static class EditorCatalog
    {
        private static readonly List<EditorEntry> Entries = new List<EditorEntry>();
        private static bool _builtIn;

        /// <summary>Nombre, icono, color y descripción de cada categoría.</summary>
        public static (string name, string icon, Color color, string description) Info(EditorCategory c)
        {
            switch (c)
            {
                case EditorCategory.Creatures: return ("Criaturas", "🧬", EditorTheme.Species, "Especies, evoluciones, habilidades, naturalezas y curvas de experiencia.");
                case EditorCategory.Battle: return ("Combate", "⚔", EditorTheme.Moves, "Movimientos, tipos, estados, climas, trampas de campo y las reglas del juego.");
                case EditorCategory.Items: return ("Objetos", "🎒", EditorTheme.Items, "Medicinas, bolas, piedras evolutivas y objetos equipables.");
                case EditorCategory.Characters: return ("Personajes", "🧑", EditorTheme.Trainers, "Entrenadores rivales (equipo, IA, mochila, frases) y equipos prearmados.");
                case EditorCategory.World: return ("Mundo", "🗺", EditorTheme.Zones, "Zonas salvajes; más adelante mapas, eventos y tiendas.");
                case EditorCategory.Interface: return ("Interfaz", "🖥", EditorTheme.Teams, "Menús, pantallas y controles (flechas, botones) para replicar o reinventar los juegos.");
                case EditorCategory.Tools: return ("Herramientas", "🧰", EditorTheme.Tools, "Excel para editar en bloque y compartir, y el validador de contenido.");
                default: return ("Pruebas", "🧪", EditorTheme.Ok, "Prueba tu juego con el motor real: calculadora, simulador y escena de combate.");
            }
        }

        public static IEnumerable<EditorCategory> Categories => (EditorCategory[])Enum.GetValues(typeof(EditorCategory));

        /// <summary>Registra (o reemplaza) un editor en el catálogo.</summary>
        public static void Register(EditorEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Title)) return;
            EnsureBuiltIn();
            Entries.RemoveAll(e => e.Category == entry.Category && e.Title == entry.Title);
            Entries.Add(entry);
        }

        /// <summary>Quita un editor del catálogo (por categoría y título).</summary>
        public static bool Unregister(EditorCategory category, string title)
        {
            EnsureBuiltIn();
            return Entries.RemoveAll(e => e.Category == category && e.Title == title) > 0;
        }

        /// <summary>Todos los editores, ordenados por categoría y orden.</summary>
        public static IReadOnlyList<EditorEntry> All
        {
            get
            {
                EnsureBuiltIn();
                return Entries.OrderBy(e => e.Category).ThenBy(e => e.ComingSoon).ThenBy(e => e.Order).ToList();
            }
        }

        public static IEnumerable<EditorEntry> In(EditorCategory c) => All.Where(e => e.Category == c);

        // ---------------- Los editores que trae CTEditor ----------------

        private static void EnsureBuiltIn()
        {
            if (_builtIn) return;
            _builtIn = true;

            // Criaturas
            Add(EditorCategory.Creatures, 1, "Especies", "🐾", "Estadísticas, tipos, movimientos que aprende y evoluciones (con calculadora).", EditorTheme.Species, SpeciesEditorWindow.Open, Count<SpeciesData>, "pokemon monstruos");
            Add(EditorCategory.Creatures, 2, "Cadenas evolutivas", "🌱", "Árbol visual: métodos y CONDICIONES combinadas (amistad + de día, nivel + Ataque > Defensa...).", EditorTheme.Species, EvolutionChainWindow.Open, null, "evolucion evoluciones");
            Add(EditorCategory.Creatures, 3, "Habilidades", "✨", "Efectos pasivos: inmunidades, potenciadores, al entrar, al final del turno...", EditorTheme.Abilities, AbilityEditorWindow.Open, Count<AbilityData>);
            Add(EditorCategory.Creatures, 4, "Naturalezas", "🎭", "Las 25 clásicas o las tuyas: qué estadística sube y cuál baja.", EditorTheme.Natures, NatureEditorWindow.Open, Count<NatureData>);
            Add(EditorCategory.Creatures, 6, "Grupos huevo", "🥚", "Los 15 grupos clásicos y qué especies hay en cada uno (para la crianza).", EditorTheme.Species, EggGroupEditorWindow.Open, Count<EggGroupData>, "crianza huevo");
            Add(EditorCategory.Creatures, 5, "Curvas de experiencia", "📈", "Cuánta experiencia hace falta por nivel (6 clásicas, con gráfico).", EditorTheme.Curves, GrowthCurveEditorWindow.Open, Count<GrowthCurveData>, "xp nivel");

            // Combate
            Add(EditorCategory.Battle, 1, "Movimientos", "💥", "Potencia, precisión, efectos, condiciones y plantillas por mecánica.", EditorTheme.Moves, MoveEditorWindow.Open, Count<MoveData>, "ataques");
            Add(EditorCategory.Battle, 2, "Tipos", "🔥", "Los tipos elementales con su color e icono.", EditorTheme.Types, TypeEditorWindow.Open, Count<ElementTypeData>);
            Add(EditorCategory.Battle, 3, "Tabla de tipos", "🧮", "Quién es fuerte o débil contra quién.", EditorTheme.Types, TypeChartWindow.Open, Count<TypeChartData>, "eficacia");
            Add(EditorCategory.Battle, 4, "Estados alterados", "💤", "Principales (dormido, quemado...) y volátiles (confusión, drenadoras...).", EditorTheme.Status, StatusEditorWindow.Open, Count<StatusConditionData>);
            Add(EditorCategory.Battle, 5, "Climas", "🌦", "Lluvia, sol, arena, nieve... y los tuyos.", EditorTheme.Weathers, WeatherEditorWindow.Open, Count<WeatherData>, "tiempo");
            Add(EditorCategory.Battle, 6, "Trampas de campo", "📌", "Púas, Trampa Rocas, Púas Tóxicas, Red Viscosa.", EditorTheme.Hazards, HazardEditorWindow.Open, Count<HazardData>);
            Add(EditorCategory.Battle, 7, "Efectos de lado", "🛡", "Reflejo, Pantalla de Luz, Neblina, Velo Sagrado, Viento Afín... y los tuyos.", EditorTheme.SideConditions, SideConditionEditorWindow.Open, Count<SideConditionData>, "pantallas reflejo");
            Add(EditorCategory.Battle, 9, "Mecánicas especiales", "💎", "Megaevolución (y más adelante movimientos Z, Dinamax...): cómo funciona cada una. Las activas se eligen en las reglas.", EditorTheme.Mechanics, MechanicEditorWindow.Open, Count<MechanicData>, "mega megaevolucion megapiedra");
            Add(EditorCategory.Battle, 8, "Reglas del juego", "📜", "Equipo, niveles, IV/EV, PP, críticos, captura, huida, dinero...", EditorTheme.Rules, RulesetEditorWindow.Open, Count<RulesetData>, "ruleset aventura");

            // Objetos
            Add(EditorCategory.Items, 1, "Objetos", "🧪", "Medicinas, bolas, piedras, objetos de combate y equipables.", EditorTheme.Items, ItemEditorWindow.Open, Count<ItemData>, "pocion mochila");
            Add(EditorCategory.Items, 2, "Tiendas", "🏪", "Qué vende cada tienda y a qué precio.", EditorTheme.Items, null, null);

            // Personajes
            Add(EditorCategory.Characters, 1, "Entrenadores", "🧑", "Equipo, nivel de IA, mochila, premio y frases de cada rival.", EditorTheme.Trainers, TrainerEditorWindow.Open, Count<TrainerData>, "rivales ia lideres");
            Add(EditorCategory.Characters, 2, "Niveles de IA", "🧠", "Los 5 niveles (Novato → Campeón): errores, curación, cambios y a qué movimientos llegan.", EditorTheme.Trainers, AiLevelEditorWindow.Open, Count<AiLevelData>, "dificultad ia reto");
            Add(EditorCategory.Characters, 3, "Equipos prearmados", "👥", "Equipos listos para empezar la partida o para probar.", EditorTheme.Teams, TeamPresetEditorWindow.Open, Count<TeamPresetData>);
            Add(EditorCategory.Characters, 4, "Personajes y diálogos", "💬", "NPC del mapa: sprite, frases y qué hacen al hablarles.", EditorTheme.Trainers, null, null, "npc");

            // Mundo
            Add(EditorCategory.World, 1, "Zonas salvajes", "🌿", "Qué especies salen en cada ruta o cueva, a qué nivel y con qué frecuencia.", EditorTheme.Zones, EncounterZoneEditorWindow.Open, Count<EncounterZoneData>, "encuentros hierba");
            Add(EditorCategory.World, 2, "Mapas", "🗺", "Pinta rutas, pueblos y cuevas; coloca NPC, hierba y salidas.", EditorTheme.Zones, null, null, "tiles tilemap");
            Add(EditorCategory.World, 3, "Eventos", "⚡", "Qué pasa al hablar, pisar o cumplir algo (marcas de la historia, regalos, combates).", EditorTheme.Zones, null, null, "scripts marcas");

            // Interfaz
            Add(EditorCategory.Interface, 1, "Menús", "📋", "Pausa, combate, Sí/No, equipo, mochila... opciones, ORDEN, columnas y vista previa jugable con flechas.", EditorTheme.Interface, MenuEditorWindow.Open, Count<MenuData>, "pausa opciones orden");
            Add(EditorCategory.Interface, 2, "Controles y caja de texto", "🎮", "Qué tecla es cada botón (A, B, Start, cruceta), repetición de flechas y cómo se ve y avanza el texto.", EditorTheme.Interface, ControlsEditorWindow.Open, Count<InterfaceSettingsData>, "flechas teclado teclas mando texto velocidad");
            Add(EditorCategory.Interface, 3, "Pantallas", "🖼", "Diseño de las pantallas de equipo, mochila y resumen (posiciones, fondos).", EditorTheme.Interface, null, null, "diseño layout");

            // Herramientas
            Add(EditorCategory.Tools, 1, "Excel y compartir (CSV)", "📊", "Exporta todo a Excel, edita en bloque e impórtalo; comparte tu configuración.", EditorTheme.Tools, () => Csv.CsvWindow.OpenTab(0), null, "csv importar exportar");
            Add(EditorCategory.Tools, 2, "Validar contenido", "✅", "Busca errores y avisos en todo el contenido.", EditorTheme.Tools, ContentValidationWindow.Open, null, "errores");
            Add(EditorCategory.Tools, 3, "Papelera", "🗑", "Lo que borraste: recupéralo con todas sus referencias o bórralo para siempre.", EditorTheme.Tools, TrashWindow.Open, () => ContentTrash.Count, "borrar recuperar deshacer");

            // Pruebas
            Add(EditorCategory.Testing, 1, "Calculadora de daño", "🎯", "Cuánto quita un movimiento con el cálculo real del combate.", EditorTheme.Ok, DamageCalculatorWindow.Open, null);
            Add(EditorCategory.Testing, 2, "Simulador de combate", "⚔", "Enfrenta dos monstruos turno a turno sin salir del editor.", EditorTheme.Ok, BattleSimulatorWindow.Open, null);
        }

        private static void Add(EditorCategory cat, int order, string title, string icon, string description, Color color, Action open, Func<int> count, string keywords = "")
            => Entries.Add(new EditorEntry { Category = cat, Order = order, Title = title, Icon = icon, Description = description, Color = color, Open = open, Count = count, Keywords = keywords });

        // Cuentas con caché de 2 s (el Centro se redibuja a menudo y buscar en el proyecto es lento).
        private static readonly Dictionary<Type, (double time, int count)> CountCache = new Dictionary<Type, (double, int)>();

        public static int Count<T>() where T : ScriptableObject
        {
            double now = EditorApplication.timeSinceStartup;
            if (CountCache.TryGetValue(typeof(T), out var c) && now - c.time < 2.0) return c.count;
            int n = ContentAssets.LoadAll<T>().Count;
            CountCache[typeof(T)] = (now, n);
            return n;
        }

        public static void ClearCounts() => CountCache.Clear();
    }
}
