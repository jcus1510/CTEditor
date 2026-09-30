# GameContent — el contenido que crea el AUTOR (separado del código)

Esta carpeta va **al mismo nivel que `CTEditor/`** dentro de `Assets/`. Aquí guardas todo lo que
creas como autor; el motor lo carga **solo**, por id, desde estas carpetas. No se arrastra nada a
ningún componente.

## Estructura (dentro de `Resources/`)

    Assets/
      CTEditor/                 <- el código del motor
      GameContent/
        Resources/
          Moves/      MoveData             (movimientos)
          Species/    SpeciesData          (especies)
          Status/     StatusConditionData  (estados alterados)
          Abilities/  AbilityData          (habilidades)
          Curves/     GrowthCurveData      (curvas de experiencia)
          Natures/    NatureData           (naturalezas)
          Items/      ItemData             (objetos)
          Weathers/   WeatherData          (climas)
          Types/      TypeChartData + ElementTypeData
          Rulesets/   RulesetData          (normalmente uno solo)
          Mechanics/  MechanicData         (mecánicas especiales: Megaevolución...; las activa el Ruleset)
          Trainers/   TrainerData          (entrenadores rivales)
          Teams/      TeamPresetData       (equipos prearmados)
          Encounters/ EncounterZoneData    (zonas salvajes)
          Hazards/    HazardData           (trampas de campo: Púas, Trampa Rocas...)
          SideConditions/ SideConditionData (efectos de lado: Reflejo, Pantalla de Luz, Neblina...)
        Scenes/       PruebaCombate.unity  (la crea el menú CTEditor → Pruebas → Crear escena de pruebas de combate)

> La subcarpeta **`Resources`** es obligatoria (es el mecanismo de carga de Unity). Las carpetas de
> categoría de adentro las define el motor en `ContentPaths`.

## Cómo crear contenido (sin programar)
Abre **CTEditor → Centro de Contenido**. Desde ahí:
- **"Crear el contenido clásico completo"**: crea de una vez los 18 tipos con su tabla, los 8 estados,
  las 25 naturalezas, las 6 curvas de XP, las reglas clásicas y 30 habilidades. Lo que ya exista (por id)
  no se toca, así que puedes pulsarlo sin miedo.
- Cada categoría tiene su **editor** (Tipos, Tabla de Tipos, Estados, Movimientos, Habilidades, Especies,
  Curvas de Experiencia, Naturalezas, Reglas): lista con buscador, crear escribiendo un id, duplicar,
  borrar, plantillas clásicas y validación en vivo. Las fichas nuevas se guardan solas en su carpeta.
- Extras: gráfico comparativo y ritmo de juego en las curvas, calculadora de stats en las especies y
  matriz clicable en la tabla de tipos.

También puedes crear fichas a mano: clic derecho → **Create → CTEditor → …**. Da igual el **nombre de
archivo**: cada catálogo indexa por el **campo `Id`**. **No repitas un Id** dentro de una categoría.

## Cómo se carga
- `ContentLibrary` es **estático**: cualquier escena lo usa sin configurarlo. No hay que ponerlo en la
  escena ni arrastrarlo.
- Carga **perezosa por categoría**: una carpeta no se lee del disco hasta que algo la necesita.

## Mínimo para que un combate funcione
- Un `RulesetData` en `Rulesets/`.
- Un `TypeChartData` en `Types/`.
- Tus `MoveData` en `Moves/` y tus `SpeciesData` en `Species/`.

## Progresión (XP, IVs, EVs, naturalezas)
- **Curvas** (`Curves/`): cada especie elige la suya escribiendo su Id en `Growth Curve Id`. Presets
  clásicos: Fast, MediumFast, MediumSlow, Slow, Erratic, Fluctuating. Si la especie no elige, usa
  MediumFast.
- **Rendimiento al derrotarla** (en cada `SpeciesData`): `Base Exp Yield` (cuánta XP da) y `Ev Yield`
  (qué EVs da, p. ej. `attack` 2).
- **Naturalezas** (`Natures/`): stat que sube, stat que baja y porcentaje. Si la carpeta está vacía,
  todos los monstruos son neutros.
- **Topes** (en el `RulesetData`): `Max Iv` (31), `Max Ev Per Stat` (252), `Max Ev Total` (510).
  Pon un tope a 0 para hacer un juego sin IVs o sin EVs.
- **Por combatiente** (en cada ficha de equipo/rival): naturaleza fija e IVs fijos opcionales
  (`Fixed Ivs = 31` para un entrenador con IVs perfectos; `-1` = al azar).

## PP (puntos de poder)
- Cada movimiento tiene su `Max Pp`; se gastan al usarlo y se guardan en el equipo al acabar el combate.
- En el `RulesetData`: `Use Pp` (desmárcalo para usos ilimitados) y `Struggle Move Id` (Forcejeo: lo que
  se usa cuando no quedan PP en ningún movimiento). La plantilla "struggle" la crea el Centro de Contenido.
- Curarse del todo (centro de curación) restaura PS, estado y PP.

## Excel (CSV) — exportar, importar y compartir
- **CTEditor → Herramientas → Excel y compartir (CSV)**, pestaña **⬆ Exportar**: marca las categorías que quieras
  (o todas) y exporta todo a una carpeta `Excel/` (junto a `Assets`, fuera del proyecto),
  una hoja por categoría (`especies.csv`, `movimientos.csv`, `tipos.csv`, `tabla_tipos.csv`...) más un
  `LEEME.txt` que explica cada columna.
- Edita en Excel y vuelve a la ventana: **Analizar** muestra filas nuevas, cambios (antes → después),
  errores y avisos **sin tocar nada**; **Aplicar** guarda antes una copia de seguridad en `Excel/copias/`.
- Borrar una fila no borra la ficha. Puedes quitar columnas: solo se aplican las que estén.
- Pestaña **⬇ Importar**: elige una **carpeta** entera o **archivos sueltos** que te haya pasado otra
  persona. Si el archivo no se llama como los nuestros, la categoría se adivina por sus cabeceras (y
  puedes corregirla en el desplegable).
- Las cabeceras ahora están **en español** (`nombre`, `tipo`, `potencia`…). Los CSV antiguos con
  cabeceras en inglés se siguen importando sin problema.
- Pestaña **? Ayuda de columnas**: qué significa cada columna y ejemplos.

## Objetos, mochila y evoluciones
- **CTEditor → Objetos → Todos los objetos**: 119 plantillas clásicas (pociones, revivir, antídotos, éteres, bolas, piedras
  evolutivas, objetos X, Restos, Carbón y demás objetos que potencian un tipo, bayas). Pulsa
  «Crear los 119 objetos clásicos» (también los de competición de la 5.ª-6.ª gen.) o «✨ Crear TODO» en el Centro de Contenido.
- Un objeto puede: curar PS (fijos o %), curar todos los estados o solo algunos (`poison|toxic`),
  revivir con un % de PS, recuperar PP (uno o todos los movimientos), cambiar la amistad, ser una bola
  (multiplicador de captura), subir etapas en combate (Ataque X) o hacer algo **equipado**:
  potenciar movimientos con condiciones, curar al final de cada turno o activarse (y consumirse)
  al bajar de cierto % de PS.
- **Mochila**: el `PartyHolder` trae la `Mochila inicial` (por defecto 5 Pociones y 10 Poké Balls). Los
  botones Mochila y Capturar de la pantalla de combate usan los objetos que elijas.
- **Evoluciones** (ficha de especie o ventana de cadenas evolutivas): por **nivel**, por **objeto**
  (piedra), por **amistad** (220 por defecto) o por **intercambio** (con objeto equipado opcional).
  En Excel: `charmeleon@16 | ninetales@objeto:fire_stone | pikachu@amistad:220 | alakazam@intercambio`.
- Tras ganar un combate, si sube de nivel y cumple la condición, evoluciona y se avisa en pantalla.

## Combates con reglas reales y escena de pruebas
- **CTEditor → Pruebas → Crear escena de pruebas de combate** (o el botón 🎮 del Centro de Contenido): crea
  `Scenes/PruebaCombate.unity` ya montada. Pulsa Play y verás el **Laboratorio**:
  - ⚔ Combate contra el entrenador elegido (◀ ▶ para cambiar), 🌿 combate salvaje en la zona elegida.
  - 🏥 Centro (cura PS, estados y PP), elegir un miembro (◆) y ponerlo primero, dejar/sacar del PC, reiniciar la partida.
  - 🎒 **Mochila y equipo fuera del combate** (sobre el miembro elegido): ◀ ▶ para elegir objeto, **Usar**
    (Poción, Revivir, Antídoto, Éter, piedras evolutivas...), **Dar para llevar** y **Quitar objeto** (vuelve a
    la mochila). Con una piedra evoluciona al momento; si la especie nueva aprende algo y ya sabe 4, se pregunta
    cuál olvidar. **📋 Resumen**: estadísticas (▲▼ por la naturaleza), IV/EV, habilidad, movimientos con PP y la
    experiencia que falta para subir. Los objetos solo se gastan si hacen algo.
  - Tras cada combate verás tu equipo ACTUALIZADO: niveles, PS, experiencia, capturas y dinero.
- **La partida del jugador** («Partida del jugador» en la escena) empieza con un **equipo prearmado**, **desde
  cero** (un inicial) o con la lista escrita a mano. El combate recibe fotos del equipo y al terminar todo vuelve
  a la partida.
- **Reglas del combate (como en los juegos):**
  - Sale el primero del equipo que puede luchar. Si cae, eliges quién sale; el entrenador saca al siguiente.
  - Huir de un salvaje: si eres igual o más rápido, escapas; si no, depende de la velocidad y de los intentos.
    De un entrenador no se huye, y sus monstruos no se capturan (se explica y no pierdes el turno).
  - Captura con la fórmula clásica y sus **sacudidas**: ratio de captura de la especie, PS, estado y bola.
    Con el equipo lleno, el capturado va al **PC**.
  - La experiencia se aplica **al momento**: sube de nivel en mitad del combate (con sus nuevas estadísticas),
    aprende movimientos y, si ya sabe 4, eliges cuál olvidar (o ninguno).
  - Al ganar a un entrenador: su frase y el premio (dinero base × nivel de su último monstruo).
  - Al perder: pierdes el 50% del dinero y vuelves curado al Centro.
  - Al terminar, si toca, **evoluciona** (puedes cancelarlo).
- Todo esto se cambia en **Reglas del juego → Aventura** (plantillas Clásica, Fácil y Reto).
- **CTEditor → Personajes → Entrenadores**, **Equipos prearmados** y **Zonas salvajes**: editores con ejemplos clásicos
  (Joven Joaquín, Brock, Misty, el Rival; Ruta 1, Bosque Verde, Cueva; un inicial, un equipo de ruta y otro
  de nivel 50). También van a Excel: `entrenadores.csv`, `equipos.csv`, `zonas.csv`
  (equipo: `pidgey@5 | onix@14[tackle/rock_throw]{oran_berry}`; zona: `pidgey@2-5:50 | rattata@2-4:50`).
- Las especies tienen **ratio de captura** (el pack de 1ª generación trae el oficial) y los estados cuánto
  facilitan la captura (dormido y congelado ×2,5; paralizado, envenenado y quemado ×1,5).

## Interfaz en español
- Todos los editores, el Inspector, los menús de creación (*Crear → CTEditor → …*) y los mensajes del
  combate están en español. Cada editor tiene una **guía de primeros pasos** plegable y colores por
  categoría. (Más adelante se podrá cambiar a inglés.)
- Si una ventana es estrecha, marca **«📝 Ver la ayuda de cada campo debajo de él»** arriba del Inspector: las
  explicaciones se escriben completas bajo cada campo (además de verse al pasar el ratón).

## Packs por generación (`Packs/Gen1/` … `Packs/Gen6/`)
- Un pack por generación, **fiel a ella**: especies, estadísticas, tipos, movimientos (tipo, potencia, precisión, PP,
  prioridad; físico/especial según el tipo hasta la 3.ª), habilidades (desde la 3.ª), aprendizaje de su juego, tabla de
  tipos, naturalezas y grupos huevo cuando existían, y sus entrenadores. Cada uno trae un `INFORME.txt`.
- Centro de Contenido → desplegable **Pack** para elegirlo y **📦 Importar** («solo lo que falta» o «actualizar también»,
  sin borrar nada). La base que el pack no trae (estados, climas, efectos de lado, objetos, niveles de IA, Forcejeo) la
  crean las plantillas del código; si el pack trae la hoja de una categoría, manda el pack.
- Los packs se generan con `Tools/verificar_pack/generar_packs.py` desde `Tools/datos_fuente/` y PokeAPI, y se
  comprueban con `Tools/verificar_pack/verificar_pack.py` (ver `Tools/README.md`).
- Los efectos de movimientos y habilidades aún son los de la 6.ª gen. (ver «Aproximaciones» en el INFORME de cada
  pack). Las reglas de cada generación se ponen en **Reglas del juego → Reglas de generación** (ver abajo).

## Formas y variantes
- **Formas de COMBATE** (dentro de la especie): cambian tipos, estadísticas (menos los PS) o habilidad EN MITAD del combate
  y al acabar vuelven a la normal. Qué las provoca (editable, se pueden combinar varias reglas):
  llevar un objeto (Giratina, Arceus, Kyogre/Groudon primigenios), usar un movimiento (Meloetta, antes o después),
  usar cualquier ataque (Aegislash), PS por debajo / desde un % (Modo Daruma), un clima (Castform, Cherrim) y la
  megaevolución (la pide el entrenador). Cada regla puede pedir una habilidad. «Vuelve al retirarse» = la forma se pierde
  al cambiar.
- **VARIANTES**: especies completas enlazadas con «Es forma de» (Rotom Lavado, Deoxys Ataque, Shaymin Cielo, los Tótem,
  Kyurem Negro/Blanco, Hoopa Desatado...). «Objeto que cambia a esta variante»: usado fuera del combate la cambia (y la
  devuelve a la base); vacío = con un personaje del mapa (`FieldActions.ChangeVariant`, para los eventos).
- Editores: ficha de especie → «Formas y variantes» (+ Forma de combate con plantilla, + Variante); **Criaturas →
  🌳 Árbol de familia**: evoluciones, insignias ⚔ de las formas (se editan en el panel) y variantes punteadas debajo de su
  base.
- Excel (`especies.csv`): `forma_de`, `objeto_variante`, `formas` (`id;nombre;tipo1/tipo2;atq/def/atq_esp/def_esp/vel;habilidad;vuelve`)
  y `cambios_forma` (`desde>hasta:disparador[:valor][;con=habilidad][;despues]`; disparadores objeto, movimiento, ataque,
  ps_bajo, ps_desde, clima, mega). Los packs Gen3-Gen6 las traen de PokeAPI.

## Reglas de generación y mecánicas especiales
- **Reglas del juego → Reglas de generación**: botones «1.ª gen.» … «9.ª gen.» / «Moderno» ponen todas las perillas de
  golpe, y después se retoca cada una:
  - **Categoría por tipo** (1.ª-3.ª): físico o especial lo decide el tipo del movimiento (lista de tipos especiales
    editable); los de estado siguen siendo de estado.
  - **Especial único** (1.ª): lo que sube o baja el Ataque Especial también mueve la Defensa Especial.
  - **Habilidades** (desde la 3.ª), **objetos equipados** (desde la 2.ª), **naturalezas** (desde la 3.ª) y **géneros**
    (desde la 2.ª): apagadas, el motor las ignora (nadie lleva objetos, todos neutros y sin género).
- **Combate → 💎 Mecánicas especiales**: fichas de mecánica (hoy, Megaevolución: megas por combate —1 oficial, 0 sin
  límite—, objeto clave del jugador —Megapulsera— y si vuelve a su forma al retirarse). Puedes tener varias fichas y
  **activar en las Reglas** las que quieras, incluso varias a la vez (Excel: `mecanicas.csv`, columna `mecanicas` de
  `reglas.csv`). Una ficha que no está activa no hace nada.

## Niveles de IA (Lote E + Lote F)
Siete niveles, cada uno una ficha editable (CTEditor → Personajes → **Niveles de IA**) hecha de **4 bloques combinables**:
🧠 **Conocimiento** (qué sabe de ti), 🎯 **Decisión** (cómo elige), 🛡️ **Gestión** (curas y cambios) y 🎒 **Equipo**
(movimientos, objetos equipados y entrenamiento).
| Nivel | Para | Cómo juega |
|---|---|---|
| 1 Novato | Joven, Cazabichos | movimientos al azar, se olvida de sus objetos, clásico (4 últimos) |
| 2 Aficionado | entrenadores de ruta | el golpe más fuerte, 20 % de despistes, usa **MT** |
| 3 Veterano | Entrenador guay, Team Rocket | calcula el daño real, **curación inteligente**, + **tutor**, sinergias |
| 4 Élite | Líderes | casi sin fallos, **cambia de monstruo**, + **movimientos huevo**, objetos equipados |
| 5 Campeón | Alto Mando, Campeón, Rojo | sin fallos, Restaurar Todo, **MEMORIA**: en la revancha recuerda tus movimientos y tus IVs/EVs estimados |
| 6 Maestro | Campeones de Teselia/Kalos, Maestros de torre | **PREDICE** (castiga tus cambios, se protege del KO, cambia al que resiste tu golpe), sets y **objetos de competición**, IVs 31 + EVs + naturaleza |
| 7 Injusto | Retos especiales (Benga, Dana) | **lo sabe TODO desde el principio**: tus movimientos, IVs y EVs; predice siempre |
- **Conocimiento**: *Nada* (supone el mejor ataque de tu tipo y stats normales), *Combate* (aprende mientras lucha: tus
  movimientos y, por el daño, tus stats), *Memoria* (lo mismo, guardado en tu partida para la revancha), *Todo* (injusto).
- **Estimación de IVs/EVs**: cada golpe limpio (sin crítico ni daño fijo) le dice si tu Ataque/Defensa es mayor o menor de lo
  «normal» (IVs 20, EVs 85); lo guarda como un factor por estadística que sirve aunque subas de nivel.
- **Objetos de competición** (Maestro/Injusto): Cinta/Gafas/Pañuelo Elección, Vidasfera, Banda Focus, Chaleco Asalto, Casco
  Dentado, Mineral Evolutivo, Lodo Negro, Restos... elegidos según el Pokémon.
- **Curación inteligente**: no se cura si puede debilitarte antes, ni si tu golpe le quita más de lo que cura; solo
  cuando la cura le da al menos un golpe más de vida.
- **Sinergias**: Hipnosis + Comesueños, Descanso + Sonámbulo, Danza Lluvia + ataques de Agua, mejoras + Relevo...
- **Mochila**: edítala en el entrenador (objeto, cantidad, ✕); si está vacía usa la de su nivel.
- **🎲 Preparar un reto**: genera un entrenador del nivel que elijas (tamaño, niveles, tipo), con la etapa de evolución
  correcta para su nivel y el «as» al final.
- Filtros de la lista de entrenadores: nivel de IA, nivel del equipo y clase; orden por nombre, IA, nivel, clase o premio.

## Género (Lote F)
- Cada individuo es **macho, hembra o sin género** según el «% de hembras» de su especie (fijo por individuo). Se ve ♂/♀ en
  el combate y en el resumen.
- En entrenadores y equipos prearmados puedes fijarlo (Al azar / Macho / Hembra). En Excel: `gardevoir@50%h`, `gallade@50%m`.
- Lo usan **Atracción** (solo entre géneros opuestos: opción «Solo al género opuesto» del estado), **Rivalidad** (condiciones
  `mismo_genero` / `genero_opuesto`) y las **evoluciones** de un solo sexo (`gallade@objeto:dawn_stone+genero:macho`).

## 5.ª y 6.ª generación: objetos, campos y más (Lote F)
- **Objetos de competición** (plantillas en CTEditor → Objetos): multiplicar estadísticas con condiciones, bloquear en el primer
  movimiento (Elección), perder PS al atacar (Vidasfera), aguantar desde PS llenos (Banda Focus), sin movimientos de estado
  (Chaleco Asalto), dañar al que toca (Casco Dentado), subir etapas al recibir un golpe muy eficaz (Seguro Debilidad), Globo Helio,
  crítico, precisión, bayas de resistencia (18 tipos), Baya Ziuela, esferas, Garra Rápida, Campana Concha, rocas de clima,
  Refleluz, Lodo Negro, Cinta Experto... Todo combinable para inventar objetos nuevos. En Excel: `equipado_stats`
  (`attack:x1,5`) y `equipado_al_recibir_golpe` (`attack:+2 [si propio.eficacia>1]`).
- **Campos** (efectos de lado con grupo «campo», solo uno a la vez): Hierba (cura 1/16 y Planta ×1,5), Eléctrico (sin dormir y
  Eléctrico ×1,5) y Niebla (sin estados). **Zona Extraña** (Defensa ↔ Def. Esp.) y **Zona Mágica** (sin objetos).
- **Protecciones con castigo**: Escudo Real (solo ataques con daño, −2 Ataque al que toca) y Barrera Espinosa (1/8 de PS).
- **Etiquetas del motor** para movimientos: `ignora_etapas` (Espada Santa), `ignora_inmunidad` (Mil Flechas),
  `tipo_extra:flying` (Plancha), `eficaz_contra:water` (Liofilización), además de `rompe_proteccion`.
- **Condiciones nuevas**: `campo=grassy_terrain` y `propio.puede_evolucionar` (Mineral Evolutivo).

## Editores: zoom, filtros y grupos huevo (Lote E)
- **Zoom** 80 %–160 % en todos los editores: botones «A− 100 % A+» o Ctrl + rueda / Ctrl + / Ctrl − / Ctrl 0.
- **Especies**: filtra por tipo, grupo huevo, legendario y rango de Pokédex; ordena por Nº, nombre, peso, altura, total de stats o tipo.
- **Grupos huevo** (CTEditor → Criaturas): 15 grupos clásicos con color; ves qué especies pertenecen a cada uno.

## Movimientos automáticos de los entrenadores
Si un miembro del equipo no tiene movimientos escritos, **«Movimientos automáticos»** decide cuáles lleva:
- **Según su IA** (por defecto): novato = clásico, listo = equilibrado, experto = fuerte.
- **Clásico**: los 4 últimos que aprende por nivel (como un salvaje: a veces flojo).
- **Equilibrado**: su mejor ataque con STAB + otro tipo para cubrir + un buen apoyo (dormir, paralizar, Danza Espada...).
- **Fuerte**: los ataques que más daño hacen con cobertura; apoyo solo si es muy bueno.
La vista previa del equipo enseña exactamente los que elegirá. En Excel: columna `movimientos_auto` (ia/clasico/equilibrado/fuerte).

## Movimientos con requisitos
Columna `requisitos` (o «Requisitos» en la ficha): si no se cumplen, el movimiento **falla**. Comesueños y Pesadilla:
`rival.estado=sleep` (solo contra un rival dormido). Se escriben igual que las condiciones de potencia.

## Estados principales y volátiles (Lote B)
- Cada estado es **principal** (quemado, parálisis, sueño, veneno, congelado: solo uno a la vez) o
  **volátil** (confusión, atrapado, drenadoras, protegido, aguante...: se SUMAN al principal y entre sí).
  Se decide con la casilla `Is Volatile` de la ficha. Al retirarse, los volátiles y las etapas se van.
- Comportamientos que puedes combinar en cualquier estado: no poder cambiarse/huir, bloquear los
  ataques del rival, aguantar con 1 PS, que el daño por turno cure al rival, duración al azar (mín-máx),
  "más difícil si se repite" y tipos inmunes.
- Los estados solo impiden USAR MOVIMIENTOS: cambiarse, huir y usar objetos siempre se puede.

## Condiciones, potencia y clima (Lote A)
- **Condiciones** (en efectos y en modificadores de potencia): vida, estado, tipo, clima, amistad, nivel,
  diferencia de nivel, etapa de una stat, "ya actuó", tipo/categoría/potencia/contacto/etiqueta del
  movimiento y azar. Sin condiciones = siempre. Cada una se ve como una frase ("→ Si el rival tiene como
  mucho 50% de vida").
- **Potencia**: modificadores ×N con condiciones (Fachada), fórmula opcional (Estallido: `150 * vida / 100`),
  y las habilidades pueden potenciar al atacar (Experto, Puño Férreo) o reducir al recibir (Peluche).
- **Stats del daño**: por categoría en las Reglas, y por movimiento (Psicocarga, Juego Sucio, stats inventadas).
- **Críticos**: tabla y multiplicador en las Reglas (plantillas: moderna, 6ª gen., 2ª-5ª gen., sin críticos).
- **Curar**: "Heal" cura a quien diga el objetivo (también al rival); "CureStatus" quita un estado.
- **Clima** (`Weathers/`, CTEditor → Combate → Climas): lluvia, sol, arena y nieve de serie; potencia tipos, daña cada
  turno salvo a los inmunes y dura N turnos. Se activa con el efecto "SetWeather".
- **Amistad** (0-255): cada especie tiene su amistad inicial (clásico 70); la usan fórmulas y condiciones.

## Trampas de campo y cambios forzados
- **CTEditor → Combate → Trampas de campo**: «Crear las 4 trampas clásicas» (Púas, Trampa Rocas, Púas Tóxicas, Red Viscosa)
  o inventa las tuyas. Cada trampa se pone en el lado del RIVAL y afecta a cada monstruo que ENTRE en ese lado:
  - **Capas**: cuántas veces se puede poner (Púas 3, Púas Tóxicas 2). Con el máximo, el movimiento falla.
  - **Daño por capa** (% de los PS máximos), que puede **multiplicarse por la eficacia de un tipo** (Trampa Rocas
    usa Roca: a un Fuego/Volador le quita el 50%).
  - **Estado por capa** (1 capa envenena, 2 envenenan gravemente) y **subir/bajar una estadística** (Red Viscosa: −1 Velocidad).
  - **Quién se libra**: tipos inmunes (Volador) y quien sea inmune a un tipo por su habilidad (Levitación).
  - **Quién la retira** al entrar (un tipo Veneno se lleva las Púas Tóxicas).
- **Movimientos** (Plantillas clásicas): Púas, Trampa Rocas, Púas Tóxicas, Red Viscosa (efecto «Poner una trampa»),
  Giro Rápido (quita las de tu lado) y Despejar (quita las de ambos lados) con el efecto «Quitar trampas».
  En Excel: `trampa:spikes`, `quitar_trampas`, `quitar_trampas_rival` (se puede añadir `:id`). Tabla `trampas.csv`.
- **Cambios forzados** (efecto «Obligar a cambiar», `forzar_cambio`): Rugido, Remolino y Cola Dragón (prioridad −6).
  Contra un salvaje **terminan el combate**; contra un entrenador, sale otro de su equipo **al azar**, que pisa las
  trampas y no actúa ese turno. Sin nadie en reserva, falla. El pack de 1ª gen. ya trae Rugido y Remolino así.
- Si un relevo cae por las trampas al entrar, cuenta como debilitado (experiencia) y el entrenador saca al siguiente.
- Las trampas desaparecen al acabar el combate.

## Organización de los editores (por categorías)
- El menú **CTEditor** se agrupa por categorías: **Criaturas** (especies, cadenas evolutivas, habilidades, naturalezas,
  curvas), **Combate** (movimientos, tipos, tabla de tipos, estados, climas, trampas, reglas), **Objetos**, **Personajes**
  (entrenadores, equipos prearmados), **Mundo** (zonas salvajes), **Interfaz**, **Herramientas** (Excel, validador) y
  **Pruebas** (calculadora, simulador, escena de combate).
- El **Centro de Contenido** muestra las mismas categorías, plegables y con **buscador** («evolución», «ia», «pociones»...).
  Los editores que llegarán (Mapas, Eventos, Tiendas, Personajes y diálogos, Menús, Pantallas y controles) ya tienen su
  sitio marcado como «Pronto».
- Cada editor tiene arriba **🏠** (volver al Centro) e **Ir a… ▾** (saltar a cualquier otro editor).

## IA de los entrenadores
- **Nivel de IA** (CTEditor → Personajes → Entrenadores, botones de un clic):
  - 🎲 **Novato**: movimientos al azar; se acuerda de sus objetos solo la mitad de las veces.
  - 🎯 **Listo**: el golpe que más daño hace (potencia × eficacia × mismo tipo); usa sus objetos a tiempo.
  - 🧠 **Experto**: calcula el daño REAL (estadísticas, etapas, habilidades, clima), remata cuando puede, usa mejoras y
    estados con cabeza, no malgasta curaciones si lo van a debilitar igual y **cambia de monstruo** si pierde el duelo.
- **Usa objetos** (interruptor) y **Mochila**: qué lleva y cuántos (Hiperpoción ×2, Cura Total, Ataque X...). Se cura con
  «Se cura con PS ≤ (%)» (25 por defecto), quita estados y usa mejoras al empezar. Lo que gasta no se pierde de su ficha.
- **Puede cambiar de monstruo** (solo la IA Experta; como mucho cada 3 turnos).
- La vista previa enseña su equipo con **estadísticas aproximadas**, habilidad, movimientos y un **análisis**: niveles,
  tipos y a qué tipos es débil la mayoría del equipo.
- En Excel: `ia` (novato/listo/experto), `usa_objetos`, `mochila` (`hyper_potion:2 | full_heal:1`), `curar_bajo`, `puede_cambiar`.

## Evoluciones con varias condiciones
- En **Cadenas evolutivas**, pulsa **✎** sobre una flecha: método (por nivel, **al subir de nivel con condiciones**, por
  amistad, con un objeto, por intercambio) y **condiciones extra** que deben cumplirse TODAS a la vez:
  nivel mínimo, amistad, objeto equipado, sabe un movimiento, sabe un movimiento de un tipo, momento del día (día, noche,
  mañana, atardecer), lugar, clima del mapa, Ataque frente a Defensa, especie o tipo en el equipo, naturaleza,
  «según su personalidad» (un % fijo por individuo) y marcas de la partida. «Al revés» invierte una condición
  (por ejemplo, «SIN llevar la Piedra Eterna»).
- **Plantillas clásicas**: Espeon/Umbreon (amistad + día/noche), Hitmonlee/Hitmonchan (nivel + Ataque/Defensa),
  Weavile (objeto + noche), Ambipom (movimiento), Sylveon (amistad + movimiento Hada), Mantine (especie en el equipo),
  Pangoro (tipo en el equipo), Goodra (lluvia), Silcoon/Cascoon (50 %) y evoluciones por evento.
- En Excel se añaden con `+`: `espeon@amistad+hora:dia`, `hitmonlee@20+stats:atq>def`,
  `weavile@subir+lleva:razor_claw+hora:noche`, `raichu@objeto:thunder_stone+!lleva:everstone`, `silcoon@7+azar:50`.
- **Hora del juego**: en «Partida del jugador», usa el reloj del ordenador o una hora fija (para probar día y noche).
  También tiene un lugar de inicio. Las marcas de la historia se guardan en la partida (las usarán los eventos).

## Combate completo: mecánicas especiales
Todas llevan plantilla en **Movimientos** (grupos «Efectos de lado», «Apoyo», «Devolver daño», «Encadenar»,
«Llamar y copiar» y «Cambiarse») y se escriben en Excel en la columna `efectos` o en `daño_especial`.

| Mecánica | Movimientos clásicos | Excel |
|---|---|---|
| Efectos de lado (CTEditor → Combate → Efectos de lado) | Reflejo, Pantalla de Luz, Velo Aurora, Neblina, Velo Sagrado, Viento Afín | `lado:reflect` · `lado_rival:id` |
| Sustituto (recibe los golpes y bloquea estados y bajadas del rival) | Sustituto | `sustituto` (`sustituto:25` = % de PS) |
| Reiniciar etapas | Niebla | `reiniciar_etapas` · `reiniciar_etapas_rival` |
| Más críticos | Foco Energía | `foco` (`foco:2`) |
| Anular / repetir | Anulación, Otra Vez | `anular:4` · `otra_vez:3` |
| Devolver daño | Contraataque, Manto Espejo, Venganza | `daño_especial`: `devolver_fisico`, `devolver_especial`, `venganza` |
| Encadenar turnos | Saña, Danza Pétalo, Enfado · Furia | `desenfreno:3:confusion` · `furia` |
| Llamar y copiar | Metrónomo, Espejo, Mimético, Transformación, Conversión | `metronomo` · `espejo` · `mimetico` · `transformarse` · `cambiar_tipo(:tipo)` |
| Cambiarse uno mismo | Ida y Vuelta, Voltiocambio, Relevo, Teletransporte | `cambio_propio` · `relevo` · `teletransporte` |

- **Efectos de lado:** duran unos turnos y protegen a todo el bando (los golpes críticos atraviesan Reflejo y Pantalla de Luz).
- **Ida y Vuelta / Relevo del jugador:** el combate se pausa y eliges **quién entra**; luego el turno sigue (el rival
  golpea al que entró). Relevo pasa las etapas, el sustituto y el crítico extra. El rival elige al azar.
- **Metrónomo** nunca elige Forcejeo ni movimientos que llaman o copian a otros; marca con la etiqueta `no_metronomo`
  los que quieras excluir.
- **Mimético y Transformación** solo duran mientras siga en el campo: la partida nunca pierde sus movimientos.
- El pack de 1ª generación ya importa **todos** sus movimientos con efecto (0 sin soporte).


## Actualizar y restaurar sin borrar (plantillas y pack)

**Nunca hace falta borrar una ficha para volver a importarla.** Borrar rompe las referencias
(learnsets, entrenadores, zonas...) aunque luego crees otra con el mismo id.

- **Todas las listas con plantillas** (movimientos, estados, objetos, habilidades, trampas, efectos de lado,
  entrenadores, equipos, zonas, climas, naturalezas, curvas): botón **«↻ Actualizar desde las plantillas ▾»**
  → *Todas* o *Por sección*, siempre con confirmación. En cada ficha: **«↺ Restaurar su plantilla»**.
- **Especies y Movimientos**: botón **«📦 Pack 1ª gen. ▾»**
  - *Importar lo que FALTA*: recupera lo que borraste; lo tuyo no se toca.
  - *Actualizar TODOS desde el pack*: abre la ventana de Excel con los cambios fila a fila antes de Aplicar.
  - En cada ficha: **«↺ Restaurar desde el pack 1ª gen.»** (te enseña qué cambia).
- **«🔧 Reparar N especies»** aparece si alguna especie tiene huecos vacíos en su learnset
  (lo que pasa al borrar un movimiento y volver a crearlo, p. ej. *Confusión*).
- **Centro de Contenido → «📦 Importar la 1ª generación»** pregunta ahora: *Solo lo que falta* o *Actualizar también*.

## Papelera y fuente de datos propia

- **Borrar ya no destruye**: el botón *Borrar* te enseña **quién usa la ficha** y ofrece **🗑 A la papelera** (recomendado) o *Borrar para siempre*.
- **Herramientas → Papelera**: *↩ Recuperar* (vuelve a su carpeta y **todas sus referencias funcionan otra vez**, porque Unity la recuerda por su GUID),
  *⇄ Pasar referencias a la nueva* (si ya creaste otra con el mismo id), *✖ Borrar para siempre* y *🧹 Vaciar*.
  Mientras está en la papelera el juego no la carga y el validador avisa si alguien la sigue usando.
- **«📦 … ▾» → Fuente de datos**: *Pack 1ª generación* o **Mi carpeta de Excel** (la que exportas). Restaurar, actualizar y reparar usan esa fuente.

## Interfaz y controles

**Botones del juego** (como una consola): Arriba/Abajo/Izquierda/Derecha, **Confirmar (A)**, **Cancelar (B)**, **Menú (Start)** y **Especial (Select)**.
Por defecto: flechas o WASD · Z/Espacio/Intro · X/Retroceso/Esc · Esc/X · C. Con el **Input System** el **mando** funciona solo.

- **CTEditor → Interfaz → Controles y caja de texto** (ficha «ajustes»): pulsa **+ tecla** y aprieta la tecla; avisos de teclas repetidas;
  repetición al mantener; velocidad, líneas y colores de la **caja de texto** con vista previa que teclea.
- **CTEditor → Interfaz → Menús**: pausa, combate (2×2), Sí/No, equipo y mochila. Opciones **en orden (▲▼)**, columnas, esquina de la pantalla,
  acción de cada opción (abrir equipo/mochila/otro menú, cerrar o acción de pantalla) y **marcas** para mostrar u ocultar
  (POKéDEX solo con `tiene_pokedex`). La **vista previa es jugable** con flechas, Confirmar y Cancelar, y entra en los submenús.
- En el juego:
  - **Menú de pausa** (botón Menú): POKéMON (datos, mover, dar/quitar objeto), MOCHILA (usar, dar; aprender movimientos),
    ficha, opciones (velocidad del texto). Guardar y Pokédex llegan en sus bloques.
  - **Combate y Laboratorio con teclado**: flechas entre botones (►), Confirmar pulsa, Cancelar = Atrás, Confirmar avanza el texto (mantener = rápido).
    Si creas el menú **«combate»**, sus textos, qué botones salen y su **orden** se aplican a la pantalla de combate.
  - Variables en textos y menús: `{jugador}`, `{dinero}`, `{hora}`, `{equipo}`. Una **línea en blanco** empieza caja nueva.
- Todo funciona **sin tocar la escena**: el lienzo de menús se crea solo. Sin fichas propias se usa la interfaz clásica.

## Menús con tus gráficos (en la escena)

- **Crear**: clic derecho en la Jerarquía → **CTEditor UI** → *Set clásico completo* (o uno a uno: pausa, Sí/No, equipo, mochila,
  listas y **caja de texto**). También desde el editor de Menús: **🎨 Crear en la escena**. Van en el lienzo «Menús del juego (CTEditor)».
- **Editar**: son UI normal de Unity. Cambia la imagen del marco y del fondo, la fuente y el texto de cada opción, posiciones y tamaños.
  En cada opción (**MenuItemView**): qué hace y cómo se ve (imagen normal / elegida / desactivada, tintes, marcador, tamaño).
  ¿Las quieres donde tú digas? Quita el *Grid/Vertical Layout Group* y muévelas: con navegación **Automática** las flechas van a la más cercana.
- El juego usa el menú de la escena **en lugar** del automático con el mismo **id**. Lo que no esté en la escena sigue funcionando con el automático.
- **Listas** (equipo_lista, mochila_lista, olvidar): una fila de **plantilla** que se copia para cada elemento.
- **Caja de texto** (DialogueBoxView): todos los mensajes del juego salen en ella.

## Mapa de menús

**CTEditor → Interfaz → Mapa de menús**: cada menú es una caja y las flechas dicen **qué opción abre qué**
(como las cadenas evolutivas). Para enlazar: pulsa el **●** de una opción y luego la caja de destino
(otro menú o una pantalla: Equipo, Mochila, Opciones, Cerrar...). Clic derecho en el ● = acciones de pantalla.
Funciona con los menús de la escena (🎨), las fichas (📋) y los clásicos (◌).

## Teclas y mando (autor y jugador)

- Cada botón del juego es un **identificador** (Confirmar, Cancelar, Menú, flechas, Especial, **L**, **R**) con sus teclas **y** botones de mando
  (`Pad/South` = A, `Pad/Start`, `Pad/DpadUp`, `Pad/LStickUp`...). En el editor: **+ tecla** (pulsa la tecla) y **+ mando ▾**.
- El **jugador** los cambia en **Menú → OPCIONES → CONTROLES**: elige el botón, «Cambiar tecla» o «Cambiar botón del mando» y pulsa el nuevo.
  Se guarda solo; «Por defecto» vuelve a lo del autor. Si choca con otro botón, se intercambian; un botón imprescindible nunca se queda vacío.
- Los textos quitan solos los símbolos que la fuente no tiene (emojis, ▶, ◆, ₽...): adiós a los avisos «character not found» y a los □.

## Novedades de los editores (Lote D)
- **Escribir ya no va lento**: los editores guardan en memoria la lista de fichas y validan 0,6 s después de dejar de escribir.
- **Cadenas evolutivas**: las tarjetas se **arrastran** por su nombre (se recuerda dónde las pones; «Ordenar tarjetas» las recoloca)
  y el panel de la derecha es ancho y con barra, para editar cómodamente las condiciones.
- **Mapa de menús por capas**: elige «Capa N» para ver solo ese nivel y el siguiente; las flechas a «Cerrar» se ocultan
  (actívalas con su casilla); clic en el título de una caja = resalta solo sus flechas.
- **Tabla de tipos**: columnas y filas alineadas; sección **«Revisar la tabla»** compara con la actual, la de 2ª-5ª gen. o la de 1ª
  y la pone con un botón (borra lo distinto entre los tipos de esa época; tus tipos inventados no se tocan).
- **Combate**: las barras de PS bajan y suben **en orden**, evento a evento (primero el objeto, luego el más rápido, luego el
  otro, luego quemaduras/veneno/clima); al debilitarse se ve llegar a 0.
