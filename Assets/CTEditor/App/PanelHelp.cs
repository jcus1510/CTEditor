using System.Collections.Generic;
using UnityEngine.UIElements;
using CTEditor.Workspace;

namespace CTEditor.App
{
    /// <summary>
    /// La explicación de cada ventana (botón «i» de su cabecera): para qué sirve, cómo se usa y trucos. Un módulo nuevo
    /// añade la suya con <see cref="Register"/>.
    /// </summary>
    public static class PanelHelp
    {
        private static readonly Dictionary<string, (string what, string[] steps, string[] tips)> Help = new Dictionary<string, (string, string[], string[])>
        {
            [PanelCatalog.Map] = ("Donde se pinta el tramo abierto con los tiles elegidos en Tiles. Lo que ves es lo que se juega.",
                new[] { "Elige un tile (o arrastra un bloque) en Tiles.", "Pinta con el lápiz (B), rectángulo (U) o relleno (G); borra con la goma (E).", "Cuentagotas (I) o clic derecho: coge tiles del mapa.", "Probar aquí (Ctrl+F5): juega desde la casilla del ratón." },
                new[] { "Rueda: zoom · Espacio + arrastrar o botón central: moverse · F: encuadrar.", "Ctrl + clic en un tile: retocarlo en el editor de píxeles.", "Clic derecho en la barra: ponerla arriba, a un lado o abajo." }),
            [PanelCatalog.Palette] = ("El tileset del mapa como paleta, y sus propiedades al estilo RPG Maker XP.",
                new[] { "Modo Pintar (1): clic = un tile; arrastrar = un bloque.", "Paso (2): rojo = no se pasa; junto a un borde = solo ese lado.", "Prioridad (3): 1-5 = se dibuja encima del jugador.", "Terreno (4), arbusto (5), mostrador (6), pieza (7)." },
                new[] { "«+» añade otro tileset al mapa; cada número es una pestaña.", "Retocar abre el tile elegido en el editor de píxeles." }),
            [PanelCatalog.Layers] = ("Las capas del mapa; arriba la que se dibuja encima.",
                new[] { "Con «capas automáticas» cada tile va solo a su capa (suelo, detalles, encima).", "Clic en una capa: pintas solo en ella (las automáticas se apagan; Ctrl+L las vuelve a poner).", "Arrastra el asa para cambiar el orden." },
                new[] { "Ojo: ver u ocultar (solo en el editor). Candado: que no se toque.", "Clic derecho: para qué es la capa. Doble clic: nombre." }),
            [PanelCatalog.World] = ("Todos los tramos exteriores en un lienzo: el mundo continuo por el que se anda sin cargas.",
                new[] { "Arrastra un tramo para moverlo (casilla a casilla).", "«Nuevo tramo…» lo pega al elegido por el lado que digas.", "Doble clic: editarlo." },
                new[] { "Ver / Bloq. / Solo: como las capas, para centrarte en un tramo.", "Mapa de la región: una imagen del mundo para el menú del juego." }),
            [PanelCatalog.Encounters] = ("Qué Pokémon salen en el tramo, como en los juegos originales.",
                new[] { "Zonas: «Todo el tramo» es la tabla general; una zona pintada (un trozo del mapa) manda sobre ella.", "Métodos: hierba, surf, cañas... cada uno con su probabilidad por paso.", "Especies: nivel, peso (frecuencia) y horas; el % real y los pasos de media se calculan solos." },
                new[] { "Icono de pintar de una zona: pintarla en el mapa (clic derecho quita). Pulsarlo otra vez (o Esc) para dejar de pintar.", "Clic en el color de una zona: cambiarlo.", "«⋯» de una especie: condición (interruptor), forma, objeto, variocolor propio, duplicar." }),
            [PanelCatalog.Assets] = ("Las imágenes del proyecto (graficos/): tilesets, personajes, combate...",
                new[] { "Importar: PNG, BMP o DIB (se convierten a PNG), con vista previa y tipo sugerido.", "Cortar…: el asistente de corte (tamaño, desplazamiento, separación).", "Clic en el tipo: cambiarlo (se mueve de carpeta con su corte)." },
                new[] { "Las de 8 columnas de 32 px y las hojas de personaje se cortan solas.", "Retocar: abrir en el editor de píxeles." }),
            [PanelCatalog.PixelEditor] = ("Un pequeño editor de píxeles para corregir tiles y personajes sin salir de la aplicación.",
                new[] { "Lápiz, goma, relleno, línea, rectángulo, cuentagotas, reemplazar color.", "Guardar (Ctrl+S) y el mapa se actualiza solo." },
                new[] { "Desde el mapa: Ctrl + clic en un tile lo abre aquí, limitado a ese tile." }),
            [PanelCatalog.Inspector] = ("Las propiedades del mapa abierto: nombre, tamaño, tramo, tilesets, ambiente y el jugador.",
                new[] { "Tamaño: elige por qué lado se añade o quita.", "Tramo: exterior (en el mundo) o interior; categoría.", "Jugador: dónde empieza y con qué personaje." },
                new[] { "Todo se deshace con Ctrl+Z y se guarda solo." }),
            [PanelCatalog.MapTree] = ("Todos los mapas del proyecto en árbol (como RPG Maker).",
                new[] { "Nuevo mapa…: tipo, categoría, tamaño, lado del mundo y tileset.", "Clic: abrirlo." },
                new[] { "Ctrl+P y escribe su nombre para abrir cualquier mapa al momento." }),
            [PanelCatalog.Problems] = ("Lo que falta o está mal en el proyecto, revisado solo tras cada cambio.",
                new[] { "Rojo: errores (algo no funcionará). Amarillo: avisos. Gris: consejos.", "Clic en un problema: abre su mapa." },
                new[] { "Se vuelve a revisar un momento después de cada cambio; «Revisar» lo hace ya." }),
        };

        public static void Register(string panelId, string what, string[] steps, string[] tips) => Help[panelId] = (what, steps, tips);

        public static bool Has(string panelId) => panelId != null && Help.ContainsKey(panelId);

        public static void Show(AppShell shell, string panelId)
        {
            if (!Help.TryGetValue(panelId, out var h)) return;
            var info = PanelCatalog.Find(panelId);
            var d = shell.ShowDialog(info?.Label ?? panelId, 40);
            var head = Ui.Row(10);
            if (IconArt.Has(info?.Icon)) head.Add(Icons.Element(info.Icon, Ui.IconSize * 1.6f, Ui.C("acento")));
            head.Add(Ui.Text(h.what, 1.02f, wrap: true).Grow());
            d.Body.Add(head);
            d.Body.Add(Section("Cómo se usa", h.steps));
            d.Body.Add(Section("Trucos", h.tips));
            d.Buttons.Add(Ui.Button("Entendido", () => shell.CloseDialog(d), Ui.ButtonKind.Primary));
        }

        private static VisualElement Section(string title, string[] lines)
        {
            var box = Ui.Column(6).Margin(0, 6, 0, 0);
            box.Add(Ui.Text(title.ToUpperInvariant(), 0.78f, dim: true, bold: true));
            int n = 1;
            foreach (var l in lines ?? new string[0])
            {
                var row = Ui.Row(8);
                row.style.alignItems = Align.FlexStart;
                var num = Ui.Text(n++.ToString(), 0.8f, bold: true).Colored("acento").NoShrink();
                num.style.width = 14;
                row.With(num, Ui.Text(l, 0.92f, wrap: true).Grow());
                box.Add(row);
            }
            return box;
        }
    }
}
