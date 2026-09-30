using System.Collections.Generic;
using System.Linq;

namespace CTEditor.Adventure.Domain.Interface
{
    /// <summary>
    /// Ids de las ACCIONES DE PANTALLA (opciones «Custom»): las entiende la pantalla que abre el menú.
    /// El autor puede cambiar el texto, el orden o quitar opciones, pero el id dice qué hacen.
    /// </summary>
    public static class ScreenActions
    {
        // Equipo
        public const string Summary = "summary";   // ver resumen
        public const string Swap = "swap";         // mover / cambiar de sitio
        public const string Give = "give";         // dar objeto (abre la mochila)
        public const string Take = "take";         // quitar objeto
        // Mochila
        public const string Use = "use";           // usar objeto (elige miembro)
        public const string GiveFromBag = "give_bag"; // dar este objeto a un miembro
        // Sí / No
        public const string Yes = "yes";
        public const string No = "no";

        /// <summary>Las que entiende cada pantalla (para los desplegables del editor), con su nombre.</summary>
        public static readonly (string id, string name, string screen)[] Known =
        {
            (Summary, "Ver resumen", "Equipo"),
            (Swap, "Mover (cambiar de sitio)", "Equipo"),
            (Give, "Dar un objeto de la mochila", "Equipo"),
            (Take, "Quitar su objeto", "Equipo"),
            (Use, "Usar el objeto", "Mochila"),
            (GiveFromBag, "Dar el objeto a un miembro", "Mochila"),
            ("fight", "Luchar (movimientos)", "Combate"),
            ("bag", "Mochila", "Combate"),
            ("switch", "Cambiar de monstruo", "Combate"),
            ("run", "Huir", "Combate"),
            ("catch", "Capturar (lanza la bola más sencilla)", "Combate"),
            (Yes, "Sí", "Sí/No"),
            (No, "No", "Sí/No"),
        };

        public static string NameOf(string id) => Known.FirstOrDefault(k => k.id == id).name ?? id;
    }

    /// <summary>
    /// LA INTERFAZ CLÁSICA: teclas y menús como en los juegos de la 3ª generación. Es lo que se usa si el
    /// autor no ha creado los suyos («lo predeterminado siempre activo»), y la plantilla de los editores.
    /// </summary>
    public static class ClassicInterface
    {
        // Ids de los menús que busca el juego.
        public const string PauseMenu = "pausa";
        public const string BattleMenu = "combate";
        public const string YesNo = "si_no";
        public const string PartyMember = "equipo_miembro";
        public const string PartyItem = "equipo_objeto";
        public const string BagItem = "mochila_objeto";

        /// <summary>
        /// Teclas clásicas de PC (como los emuladores y los fangames): flechas o WASD; Z/Espacio/Intro =
        /// Confirmar; X/Retroceso = Cancelar; Esc/X = Menú (en el mapa abre el menú; dentro, vuelve); Q/E = L/R.
        /// Mando: cruceta o stick, A = Confirmar, B = Cancelar, Start o Y = Menú, LB/RB = L/R.
        /// </summary>
        public static InputBindings Bindings() => new InputBindings()
            .Bind(GameButton.Up, "UpArrow", "W", "Pad/DpadUp", "Pad/LStickUp")
            .Bind(GameButton.Down, "DownArrow", "S", "Pad/DpadDown", "Pad/LStickDown")
            .Bind(GameButton.Left, "LeftArrow", "A", "Pad/DpadLeft", "Pad/LStickLeft")
            .Bind(GameButton.Right, "RightArrow", "D", "Pad/DpadRight", "Pad/LStickRight")
            .Bind(GameButton.Confirm, "Z", "Space", "Return", "Pad/South")
            .Bind(GameButton.Cancel, "X", "Backspace", "Escape", "Pad/East")
            .Bind(GameButton.Menu, "Escape", "X", "Pad/Start", "Pad/North")
            .Bind(GameButton.Select, "C", "Pad/Select")
            .Bind(GameButton.L, "Q", "Pad/LB")
            .Bind(GameButton.R, "E", "Pad/RB");

        /// <summary>Añade a unos controles los botones de MANDO clásicos (para presets que solo dicen el teclado).</summary>
        public static InputBindings WithClassicPad(this InputBindings b)
        {
            var classic = Bindings();
            foreach (var button in GameButtons.All) b.Replace(button, true, classic.Pad(button));
            return b;
        }

        /// <summary>Todos los menús clásicos (plantillas del editor de menús).</summary>
        public static IReadOnlyList<MenuDefinition> Menus() => new[]
        {
            Pause(), PauseModern(), Battle(), YesNoMenu(), PartyMemberMenu(), PartyItemMenu(), BagItemMenu()
        };

        public static MenuDefinition Find(string id) => Menus().FirstOrDefault(m => m.Id == id);

        public static MenuDefinition Pause() => new MenuDefinition(PauseMenu, "Menú de pausa (clásico)") { Columns = 1, Anchor = MenuAnchor.TopRight }
            .Add(new MenuOption("pokedex", "POKéDEX", MenuActionKind.OpenPokedex, requiredFlag: "tiene_pokedex", help: "Las especies que has visto y capturado."))
            .Add(new MenuOption("equipo", "POKéMON", MenuActionKind.OpenParty, help: "Tu equipo: resumen, orden y objetos."))
            .Add(new MenuOption("mochila", "MOCHILA", MenuActionKind.OpenBag, help: "Tus objetos."))
            .Add(new MenuOption("ficha", "{jugador}", MenuActionKind.OpenTrainerCard, help: "Tu ficha de entrenador."))
            .Add(new MenuOption("guardar", "GUARDAR", MenuActionKind.Save, help: "Guarda la partida."))
            .Add(new MenuOption("opciones", "OPCIONES", MenuActionKind.Options, help: "Velocidad del texto y más."))
            .Add(new MenuOption("salir", "SALIR", MenuActionKind.Close, help: "Cierra este menú."));

        /// <summary>Variación: rejilla de 2 columnas abajo, sin Pokédex (para juegos propios).</summary>
        public static MenuDefinition PauseModern() => new MenuDefinition("pausa_rejilla", "Menú de pausa (rejilla 2×3)") { Columns = 2, Anchor = MenuAnchor.BottomFull }
            .Add(new MenuOption("equipo", "Equipo", MenuActionKind.OpenParty))
            .Add(new MenuOption("mochila", "Mochila", MenuActionKind.OpenBag))
            .Add(new MenuOption("ficha", "{jugador}", MenuActionKind.OpenTrainerCard))
            .Add(new MenuOption("guardar", "Guardar", MenuActionKind.Save))
            .Add(new MenuOption("opciones", "Opciones", MenuActionKind.Options))
            .Add(new MenuOption("salir", "Cerrar", MenuActionKind.Close));

        /// <summary>El menú de acciones del combate (2×2, como en la 3ª gen.).</summary>
        public static MenuDefinition Battle() => new MenuDefinition(BattleMenu, "Combate: ¿qué hará?") { Columns = 2, Anchor = MenuAnchor.BottomRight, CancelCloses = false }
            .Add(new MenuOption("luchar", "LUCHAR", MenuActionKind.ScreenAction, "fight"))
            .Add(new MenuOption("mochila", "MOCHILA", MenuActionKind.ScreenAction, "bag"))
            .Add(new MenuOption("equipo", "POKéMON", MenuActionKind.ScreenAction, "switch"))
            .Add(new MenuOption("huir", "HUIR", MenuActionKind.ScreenAction, "run"));

        public static MenuDefinition YesNoMenu() => new MenuDefinition(YesNo, "Sí / No") { Columns = 1, Anchor = MenuAnchor.BottomRight, CancelCloses = false, CancelOptionId = ScreenActions.No, RememberCursor = false }
            .Add(new MenuOption(ScreenActions.Yes, "SÍ", MenuActionKind.ScreenAction, ScreenActions.Yes))
            .Add(new MenuOption(ScreenActions.No, "NO", MenuActionKind.ScreenAction, ScreenActions.No));

        public static MenuDefinition PartyMemberMenu() => new MenuDefinition(PartyMember, "Equipo: miembro elegido") { Columns = 1, Anchor = MenuAnchor.BottomRight, RememberCursor = false }
            .Add(new MenuOption(ScreenActions.Summary, "DATOS", MenuActionKind.ScreenAction, ScreenActions.Summary))
            .Add(new MenuOption(ScreenActions.Swap, "MOVER", MenuActionKind.ScreenAction, ScreenActions.Swap))
            .Add(new MenuOption("objeto", "OBJETO", MenuActionKind.OpenMenu, PartyItem))
            .Add(new MenuOption("salir", "SALIR", MenuActionKind.Close));

        public static MenuDefinition PartyItemMenu() => new MenuDefinition(PartyItem, "Equipo: objeto") { Columns = 1, Anchor = MenuAnchor.BottomRight, RememberCursor = false }
            .Add(new MenuOption(ScreenActions.Give, "DAR", MenuActionKind.ScreenAction, ScreenActions.Give))
            .Add(new MenuOption(ScreenActions.Take, "QUITAR", MenuActionKind.ScreenAction, ScreenActions.Take))
            .Add(new MenuOption("salir", "SALIR", MenuActionKind.Close));

        public static MenuDefinition BagItemMenu() => new MenuDefinition(BagItem, "Mochila: objeto elegido") { Columns = 1, Anchor = MenuAnchor.BottomRight, RememberCursor = false }
            .Add(new MenuOption(ScreenActions.Use, "USAR", MenuActionKind.ScreenAction, ScreenActions.Use))
            .Add(new MenuOption(ScreenActions.GiveFromBag, "DAR", MenuActionKind.ScreenAction, ScreenActions.GiveFromBag))
            .Add(new MenuOption("salir", "SALIR", MenuActionKind.Close));

        /// <summary>Nombre en español de cada acción (editores).</summary>
        public static string ActionName(MenuActionKind k)
        {
            switch (k)
            {
                case MenuActionKind.Close: return "Cerrar el menú";
                case MenuActionKind.OpenMenu: return "Abrir otro menú";
                case MenuActionKind.OpenParty: return "Abrir el equipo";
                case MenuActionKind.OpenBag: return "Abrir la mochila";
                case MenuActionKind.OpenSummary: return "Resumen del primero";
                case MenuActionKind.OpenPokedex: return "Pokédex (próximamente)";
                case MenuActionKind.OpenTrainerCard: return "Ficha de entrenador";
                case MenuActionKind.Save: return "Guardar (próximamente)";
                case MenuActionKind.Options: return "Opciones";
                default: return "Acción de la pantalla";
            }
        }

        public static string AnchorName(MenuAnchor a)
        {
            switch (a)
            {
                case MenuAnchor.TopRight: return "Arriba a la derecha";
                case MenuAnchor.BottomRight: return "Abajo a la derecha";
                case MenuAnchor.BottomLeft: return "Abajo a la izquierda";
                case MenuAnchor.TopLeft: return "Arriba a la izquierda";
                case MenuAnchor.Center: return "Centro";
                default: return "Abajo, a lo ancho";
            }
        }
    }
}
