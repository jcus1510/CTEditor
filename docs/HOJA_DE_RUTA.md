# Hoja de ruta

Plan **paso a paso** de lo que queda antes de los NPC y eventos. Cada tarea tiene su **comprobación**: no se pasa a la
siguiente hasta que la anterior está hecha, probada (pruebas automáticas + lista en Unity) y documentada.

Estados: ✅ hecho · 🔶 en curso · ⬜ pendiente.

---

## Orden general

| # | Bloque | Por qué en este orden |
|---|---|---|
| A | **Fase 5b**: jugar en una ventana y perfiles de prueba | Cierra la «comodidad base»: probar sin salir del editor. |
| B | **Fase 6**: el mundo (corte libre, edición rápida, puertas, autotiles, piezas) | Terminar los mapas antes de poner gente en ellos. |
| C | **Migración**: los editores de Unity pasan a la aplicación | Los NPC y eventos usan especies, objetos, entrenadores...: tienen que estar en la aplicación, con validaciones serias. |
| D | **Packs públicos**: importar y exportar | Lo que se crea se comparte; necesita el formato y las validaciones de C. |
| E | **Fase 8**: NPC y eventos | Con todo lo anterior listo. |

---

## A · Fase 5b — probar sin salir

| Tarea | Estado | Comprobación |
|---|---|---|
| A1. **Jugar dentro de una ventana** (la ventana «Juego»): el mapa se edita en una y se juega en otra; los cambios de tiles, paso y encuentros se ven al momento sin reiniciar | ✅ (`GameWindow`, `PlayScreen` acoplada) | Pintar un tile mientras el jugador anda: aparece; bloquear el paso de una casilla: ya no se puede pisar. |
| A2. **Perfiles de prueba**: con qué se empieza a probar (equipo, nivel, medallas, objetos, interruptores encendidos, hora del día) | ✅ (`TestProfile`, `JsonTestProfileRepository`, `TestProfileTests`) | Dominio `TestProfile` con pruebas; elegir un perfil y que el juego arranque con él. |
| A3. Selector de perfil junto a «Jugar» y en Ctrl+P | ✅ (ventana Juego, menú Jugar, Ctrl+P, `ProfilesDialog`) | Cambiar de perfil y jugar: se nota (otro equipo, otra hora). |
| A4. Documentación (20, DISENO, historial) | ✅ | — |
| A5. **Estados vacíos** (sugerencia 7): icono, título, qué hacer y botón principal | ✅ (`Ui.EmptyState` en Encuentros, Mapas, Capas, Recursos, Problemas) | Ventanas vacías muestran qué hacer con un clic. |

## B · Fase 6 — el mundo

| Tarea | Estado | Comprobación |
|---|---|---|
| B1. **Corte libre**: rectángulos a mano en la imagen, con lupa y precisión de píxel (tiles grandes, objetos sueltos de ripeos) | ✅ (`FreePieceSet`, `FreeSliceDialog`, detectar objetos) | Cortar un tileset ripeado con piezas de distinto tamaño; los mapas las usan. |
| B2. **Edición rápida**: Mayús + clic = línea; X / Y voltear y R girar el sello; Ctrl+Alt+1-9 guardar sello y Alt+1-9 recuperarlo | ✅ (`MapTile` con giro, `TileStamp` volteado/girado/`Line`, `PaintLine`) | Pruebas del dominio (`TileStamp` volteado y girado) + uso en Unity. |
| B3. **Puertas que se enlazan solas**: una puerta en un exterior y su salida en el interior, creadas juntas; moverlas mantiene el enlace | ✅ (`Doors`, `MapObjectsCommand`, herramienta Puerta, salto al jugar) | Entrar y salir jugando; borrar una avisa de la otra (ventana Problemas). |
| B4. **Autotiles**: formato RPG Maker XP (escalado al tile del proyecto), 47 piezas, o detectado por las medidas | ✅ (`AutotileLayout`, `AutotileResolver`; en Corte libre) | Pintar agua y caminos: los bordes salen solos; pruebas de las 47 combinaciones. |
| B5. **Pinceles de terreno y aleatorio** (con pesos) | ✅ (`RandomBrush`, `BrushesDialog`; el terreno son los autotiles) | Pintar hierba con variaciones al azar. |
| B6. **Piezas reutilizables** (casa, árbol grande): guardar una selección como pieza y colocarla | ✅ (`MapPiece`, `JsonMapPieceRepository`, `PiecesDialog`) | Colocar la misma casa en dos pueblos; cambiar la pieza no rompe los mapas. |
| B7. Documentación | ✅ (20, 17, DISENO, 15) | — |

## C · Migración de los editores de Unity a la aplicación

Hoy hay **36 ventanas de Unity** (especies, movimientos, habilidades, objetos, tipos, naturalezas, grupos huevo,
curvas, estados, climas, campos, peligros, mecánicas, reglas, entrenadores, plantillas, equipos, sets, Showdown,
cambio de generación, papelera, validación, CSV, calculadora de daño, simulador de combate, torneo de IA, menús,
controles...). Pasan a la aplicación **con más rigor**: nada se rompe al borrar, renombrar o cambiar de generación.

| Tarea | Estado | Comprobación |
|---|---|---|
| C0. **Inventario**: cada ventana de Unity → su ventana en la aplicación, qué datos toca y qué valida | ✅ (tabla de abajo) | Tabla en este documento revisada contigo. |
| C1. **Una sola fuente de datos**: el contenido vive en archivos abiertos del proyecto (`datos/*.csv` / JSON), leídos por repositorios con **esquema** (columnas, tipos, obligatorios) y errores con fila y columna | ✅ (`CTEditor.Content`: `ContentSchemas`, `ContentDatabase`, `ContentChecks`) | Abrir datos con errores a propósito: se dicen todos, ninguno rompe la aplicación. |
| C2. **Índice de referencias** (quién usa qué: una especie en encuentros, entrenadores, evoluciones, sets...) | ⬜ | Pruebas: el índice encuentra todas las referencias de cada tipo de contenido. |
| C3. **Operaciones seguras**: borrar avisa de quién lo usa (cancelar, sustituir por otro o quitar las referencias); renombrar un id lo cambia en todas partes; **papelera** para recuperar lo borrado; todo con Ctrl+Z | ✅ (`ContentSession` + diálogo de borrar, cambiar id, ¿quién lo usa?, ventana Papelera) | Borrar Pikachu usado en una ruta y un entrenador: avisa, y al sustituirlo por Raichu todo sigue funcionando. |
| C4. **Validación** completa (la de `ContentValidator`) como comprobaciones de la ventana Problemas | ✅ `ContentChecks` en Problemas (clic = abrir la ficha); 🔶 faltan las reglas de mecánica de `ContentValidator` | Los mismos avisos que en Unity, con clic = ir al sitio. |
| C5. Ventanas en este orden: **Especies** (formas, evoluciones, aprendizaje, árbol de familia) → **Movimientos** → **Habilidades** (bloques) → **Objetos** (bloques) → **Tipos y tabla de tipos** → Naturalezas, grupos huevo, curvas → Estados, climas, campos, peligros → **Entrenadores**, equipos, plantillas y sets (con Showdown) → **Reglas y mecánicas** → Menús y controles | ⬜ | Cada una: crear, cambiar, duplicar, borrar con referencias, deshacer; lo mismo que en Unity o más. |
| C6. **Cambio de generación** con **vista previa** de lo que cambia (diferencias), copia de seguridad automática antes y vuelta atrás | ✅ (`PackChange`, `ApplyPack`, `GenerationDialog`; un Ctrl+Z lo deja igual) | Pasar un proyecto de Gen 3 a Gen 7 y volver: queda igual. |
| C7. Herramientas: calculadora de daño, simulador de combate, torneo de IA | ⬜ | Mismos resultados que en Unity con la misma semilla. |
| C8. Las ventanas de Unity se quedan solo para desarrollo (no para el autor) | ⬜ | Todo el contenido se edita sin abrir Unity. |


### C0 · Inventario (36 ventanas de Unity → aplicación)

Diseño: se respeta la distribución de cada ventana de Unity (lista a la izquierda con buscador, filtros y marcas de
color; ficha a la derecha por secciones; vista previa debajo). El encabezado de color pasa a ser el tema de la
aplicación; la «Guía rápida / Cómo usar» va al botón «i»; las acciones (nuevo, duplicar, borrar, ¿quién lo usa?,
renombrar, plantillas) se agrupan en la barra de la ventana. Datos: `datos/*.csv` (el formato de los packs y de Excel).

| Ventana de Unity | En la aplicación | Datos | Valida / extra |
|---|---|---|---|
| Especies (+ calculadora de stats) | **Especies** | especies.csv | tipos, habilidades, curva, aprende/MT/tutor/huevo, evoluciones; barras de stats y total |
| Árbol de familia (formas y variantes) | pestaña de Especies | especies.csv (evoluciona, forma_de, formas) | evoluciones a especies que existen |
| Movimientos | **Movimientos** | movimientos.csv | tipo, categoría, potencia/precisión/PP, efectos |
| Habilidades (bloques) | **Habilidades** | habilidades.csv | efectos |
| Objetos (bloques) | **Objetos** | objetos.csv | efectos, precio |
| Tipos + tabla de tipos | **Tipos** (con la matriz) | tipos.csv, tabla_tipos.csv | columnas = tipos que existen; números |
| Naturalezas (tabla 5×5) | **Naturalezas** | naturalezas.csv | sube / baja |
| Grupos huevo | **Grupos huevo** | grupos_huevo.csv | especies de cada grupo |
| Curvas de experiencia (gráfico) | **Curvas** | curvas.csv (+ las 6 clásicas) | — |
| Estados, Climas, Efectos de lado, Trampas | **Combate → …** | estados/climas/efectos_lado/trampas.csv | tipos inmunes, se convierte en |
| Entrenadores, Equipos prearmados | **Entrenadores**, **Equipos** | entrenadores.csv, equipos.csv | equipo (especie, movimientos, objeto), mochila |
| Plantillas de entrenadores, Sets, Elegir set, Showdown | dentro de Entrenadores | sets.csv | especie, objeto, habilidad, naturaleza, movimientos |
| Niveles de IA | **Niveles de IA** | (por definir en C5) | — |
| Reglas del juego, Mecánicas especiales | **Reglas** | reglas.csv | — |
| Menús, Mapa de menús, Controles y texto | **Interfaz** (último de C5) | (por definir) | — |
| Zonas salvajes | ya en la aplicación: **Encuentros** de cada mapa | mapas | especies que existen |
| Validar contenido | ventana **Problemas** (C4) | todo | `ContentChecks` |
| Excel / compartir (CSV) | innecesaria: los datos ya son CSV; exportar/importar → bloque D | — | — |
| Papelera | **Papelera** (C3) | `datos/papelera.csv` | recuperar |
| Cambiar de generación | **Cambiar de generación** (C6) | packs | vista previa y copia |
| Calculadora de daño, Simulador, Torneo de IA | **Pruebas** (C7) | — | mismo motor |
| Centro de contenido | pantalla de inicio / packs | — | — |
| Escenas de prueba (BattleLab, menús de escena) | se quedan en Unity (desarrollo, C8) | — | — |

## D · Packs públicos

| Tarea | Estado | Comprobación |
|---|---|---|
| D1. **Formato de pack**: un `.ctpack` (zip) con `pack.json` (id, nombre, versión, generación, autor, licencia, depende de, contenido, sumas de comprobación) | ⬜ | Pruebas: escribir y leer; un pack dañado se rechaza con el motivo. |
| D2. **Exportar**: elegir qué contenido (todo, o especies, movimientos...) y sus imágenes; incluye lo que haga falta (referencias) | ⬜ | Exportar solo 3 especies: el pack lleva también sus movimientos y habilidades. |
| D3. **Importar** con **vista previa**: qué se añade, qué cambia, qué choca; para cada choque, saltar, sustituir, renombrar o combinar; copia de seguridad antes | ⬜ | Importar dos veces el mismo pack no duplica nada; importar uno que choca deja elegir. |
| D4. **Validación al importar** (esquema + referencias + Problemas) | ⬜ | Un pack con una especie que usa un movimiento que no trae: avisa antes de importar. |
| D5. Packs instalados y **actualizaciones** (versión nueva de un pack) | ⬜ | Actualizar Gen7 1.0 → 1.1 conserva tus cambios locales o te deja elegir. |
| D6. Los packs Gen1…Gen7 actuales pasan a este formato | ⬜ | Crear un proyecto con cada uno: la ventana Problemas queda limpia. |

## E · Fase 8 — NPC y eventos

Se planifica en detalle al terminar D (recetas arrastrables, bloques, grafo de nodos y guion de diálogos sobre un
mismo modelo).

---

## Hecho recientemente (para no repetir)

- Fase 4.5 (mundo, encuentros, capas automáticas), pulidos 1-6, fase 5a (copias de seguridad, Ctrl+P, Problemas),
  encuentros como tabla, selector de especies con filtros, ayuda «i», selector de color.
- Sugerencias de diseño aplicadas: 1 (la «i» parpadea tres veces la primera vez), 2 (tabla más estrecha y arrastrar
  especies), 3 (simulador en su ventana), 4 (plantillas al crear zona), 5 (aviso de terreno, apagado por defecto),
  6 (títulos de sección iguales), 8 (densidad cómoda/compacta), 9 (animaciones rápidas y desactivables), 10 (color
  de acento manteniendo el tema). Pendiente de confirmar: 7 (estados vacíos).
