# 14 · Escena, interfaz y controles (Bootstrap)

`Assets/CTEditor/Bootstrap` (asmdef `CTEditor.Bootstrap`) — el borde con Unity en tiempo de juego: monta los
dominios, muestra y pregunta. **La lógica no vive aquí**: la pantalla de combate solo reproduce eventos y pasa
decisiones a `BattleSession`.

## Composición y contenido

| Pieza | Qué hace |
|---|---|
| `GameBootstrap` | Composition root: el único que conoce todos los contextos; traduce assets con la ACL, construye catálogos, fórmulas e instancias, y corre combates reproducidos por el `EventBus`. |
| `ContentLibrary` / `ContentPaths` | Biblioteca estática autocargable desde `Resources` (rutas de `ContentFolders`). |
| `Platform/EventBus`, `Platform/SystemRng` | Implementaciones de `IEventBus` e `IRng` para producción. |
| `PartyHolder` | La partida del jugador en la escena (`PlayerSave`: equipo, PC, mochila, dinero). Arranque: equipo prearmado o inicial; mochila inicial. |
| `PendingBattle` / `BattleRequest` | QUÉ combate lanzar (entrenador, salvaje de zona, especie). Lo crea quien dispara el combate y lo consume la pantalla. |
| `BattlerSpec`, `BattlerBuilder`, `OpponentSpec`, `AiLevel` | Autoría de combatientes concretos (especie + nivel + moveset explícito) y su conversión a individuo / foto. |
| `GameFlow/BattleResultToPartyFlow`, `CaptureToPartyFlow` | Aplican el `BattleResult` y las capturas a la partida. |

## Pantalla de combate (`BattleScreen`)

Muestra la línea de tiempo de eventos con ritmo (mensajes tecleados, barras de PS que bajan), menús Luchar / Mochila /
Equipo / Huir, botón de **Megaevolución** (se crea solo clonando el de volver si la escena no lo tiene), narra
cambios de forma, megas, objetos, climas... Toda validación la hace `BattleSession`.

## Laboratorio de pruebas (`BattleLab` + `Editor/BattleLabSceneBuilder`)

Escena de pruebas creada y cableada por un menú: partida del jugador con un equipo prearmado, combate salvaje por
zona, contra entrenadores, Centro Pokémon, orden del equipo, menús de pausa. Se prueba TODO con la partida real.

## Interfaz del juego (`Interface/`)

| Pieza | Qué hace |
|---|---|
| `UiRoot` (`UiKit`, `UiContent`) | Lienzo de la interfaz, creado solo la primera vez; `ModalOpen` bloquea el resto. |
| `TextBox` | Caja de texto: reparte en cajas de N líneas, teclea, ▼ cuando espera, variables `{jugador}`. |
| `MenuView` | Menú automático desde su ficha (editor de Menús): caja en una esquina, lista o rejilla, cursor, desplazamiento. |
| `Scene/MenuScreen`, `MenuItemView`, `DialogueBoxView` | Menús y caja de texto **diseñados en la escena** con los gráficos del autor; sustituyen a los automáticos con el mismo id (pausa, sí/no, equipo...). Se crean con clic derecho → CTEditor UI (`SceneMenuBuilder`). |
| `MenuOpener` / `MenuStack` / `IMenuPresenter` | Abrir menús (automáticos o de escena) y apilarlos. |
| `PauseMenu`, `FieldScreens` | Menú de pausa y sus pantallas (equipo, mochila, resumen, opciones) usando `FieldActions`. |
| `GameInput` (+ `GameInputDriver`), `KeyboardNavigator` | Botones del juego (Confirmar, Cancelar, Menú, flechas, L/R) con los enlaces del autor (editor de Controles) o los clásicos; teclado y mando. Input System opcional (`CTEDITOR_INPUT_SYSTEM` + `ENABLE_INPUT_SYSTEM`), si no, el Input Manager clásico. |
| `SafeCoroutine` | Corrutinas anidadas a prueba de excepciones (un error no deja el menú abierto para siempre). |
| `TextSafety` | Quita del texto los caracteres que la fuente TMP no tiene (evita «□»). |

El dominio de la interfaz (menús, cursor, navegación espacial, input, texto) está en `Adventure/Domain/Interface`
y se prueba sin Unity (`InterfaceTests`).

## Dependencias de Unity

uGUI (`UnityEngine.UI`), **TextMeshPro** (importar «TMP Essential Resources» la primera vez), Input System (opcional).
