# 12 · Excel (CSV)

`Runtime/GameDefinition/Editor/Csv` — TODO el contenido se exporta a hojas CSV, se edita en Excel (o LibreOffice /
Google Sheets) y se vuelve a importar. Sirve para editar en bloque, compartir y como formato de los **packs**.

## Formato de archivo (`CsvTable`)

- Escribe con **`;`** y **coma decimal**, en **UTF-8 con BOM** (Excel en español lo abre bien).
- Lee `;` o `,` (lo detecta por la cabecera) y números con coma o punto.
- Una fila por ficha, identificada por la columna **`id`**. Solo se aplican las columnas presentes: se pueden quitar
  columnas que no se quieran tocar. Borrar una fila NO borra la ficha.

## Esquemas (`CsvSchemas`)

- `CsvSchema<T>`: columnas escritas a mano (`.Col(cabecera, ayuda, leer, escribir, diferido, alias)`) — especies,
  movimientos, tipos, sets, entrenadores, zonas, equipos y columnas especiales de objetos/habilidades.
- `CsvReflectiveSchema<T>`: automático, **una columna por campo** serializado de la ficha (cabecera de `Etiquetas`, el
  nombre del campo en inglés vale como alias). Se salta los campos `[LegacyField]`. Si se añade un campo a una ficha,
  aparece solo en Excel.
- `TypeChartCsvSchema`: la tabla de tipos como matriz (`atacante\defensor`).

Orden de importación (lo que otros referencian va antes):

| Orden | Hoja | Orden | Hoja |
|---|---|---|---|
| 10 | `tipos.csv` | 50 | `curvas.csv` |
| 15 | `tabla_tipos.csv`, `grupos_huevo.csv` | 58 | `mecanicas.csv` |
| 20 | `estados.csv` | 60 | `reglas.csv` |
| 25-27 | `climas.csv`, `trampas.csv`, `efectos_lado.csv` | 70 | `movimientos.csv` |
| 30 | `habilidades.csv` | 80 | `especies.csv` |
| 35 | `objetos.csv` | 85 | `sets.csv` |
| 40 | `naturalezas.csv` | 88 / 90 / 92 / 95 | `niveles_ia.csv`, `entrenadores.csv`, `zonas.csv`, `equipos.csv` |

## Importar en dos fases (`CsvImporter`, `CsvWindow`)

1. **Analizar** (no toca nada): para cada fila, NUEVA / CAMBIOS (campo a campo) / IGUAL / ERRORES / avisos; columnas
   desconocidas. Primero se registran todos los ids que existirán (una hoja puede referenciar fichas que crea otra).
2. **Aplicar**: copia de seguridad (exporta todo a `Excel/copias/copia_<fecha>`), crea/actualiza por orden, guarda y
   **reenlaza** referencias por id. Un fallo en un archivo no bloquea los demás.
- Modos (`ImportMode`): **solo crear lo que falta** / **crear y actualizar**.
- Detección de categoría: por **nombre de archivo**; si no, por **cabeceras** (la categoría con más columnas
  reconocidas, pero NUNCA si la mayoría de columnas no son de esa categoría: entonces se pide elegirla).
- La ventana **Excel y compartir** tiene tres pestañas: exportar (todo o algunas categorías, con `LEEME.txt`),
  importar (carpeta o archivos sueltos) y compartir.

## Formatos de celda

| Qué | Formato | Ejemplo |
|---|---|---|
| Lista | `a\|b\|c` | `fire\|flying` |
| Aprendizaje | `nivel:movimiento\|…` | `1:tackle\|7:ember` |
| Evoluciones | `especie@nivel`, `@objeto:id`, `@amistad:N`, `@intercambio`, condiciones | `charmeleon@16 \| ninetales@objeto:fire_stone` |
| EVs que da | `stat:cantidad\|…` | `attack:1\|speed:2` |
| Efectos de movimiento | `clave:valor@prob [si condiciones]` | `estado:burn@10 \| stat_propio:attack:+2 [si propio.vida<=50]` |
| Condiciones | `propio./rival./mov.` + comparación, unidas con `&` | `rival.vida<50 & clima=rain` |
| Potencia con condiciones | `xN [si …] \| …` | `x2 [si propio.estado]` |
| Equipo (entrenadores / prearmados) | `especie@nivel%g[mov/mov]{objeto}~naturaleza!habilidad(EVs)#iv(IVs)"mote"` | `garchomp@62[earthquake/dragon_claw]{choice_scarf}~jolly!rough_skin(252 Atq/4 DefE/252 Vel)` |
| Zona | `especie@min-max:frecuencia` | `pidgey@2-5:50 \| pikachu@3:5` |
| Formas (especies) | `id;nombre;t1/t2;atq/def/atqe/defe/vel;habilidad;vuelve` | `zen;Modo Daruma;fire/psychic;30/105/140/105/55;zen_mode;vuelve` |
| Cambios de forma | `desde>hasta:disparador[:valor][;con=hab][;despues][;sabe=mov]` | `>zen:ps_bajo:50;con=zen_mode` |
| **Efectos de objeto** | `cuándo[@umbral] [si …]: acción [id] [n]; opciones` | `poca_vida@25: etapa attack +1; se_gasta` ([09](09-efectos-por-bloques.md)) |
| Sets (`sets.csv`) | alternativas con `,`, huecos con `/` | `movimientos = earthquake/dragon_claw,outrage/stone_edge/swords_dance` |

Todos los formatos tienen ida y vuelta sin pérdidas (tests en `CsvTests`, `EffectTextTests`, `ShowdownTests`...).
Los errores se explican en español con la fila y la columna.
