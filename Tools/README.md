# Tools — verificar y generar datos FUERA de Unity

Scripts de Python 3 (sin librerías extra) para comprobar los packs y tus hojas de Excel **antes** de importarlos en
Unity. Están fuera de `Assets/`, así que Unity no los importa. Leen los CSV **igual que el editor** (`CsvTable.cs`).

Los datos de referencia vienen de [PokeAPI](https://github.com/PokeAPI/pokeapi) (carpeta `data/v2/csv`). La primera
vez se descargan a `Tools/.cache/pokeapi` (ignorada por git); después funciona sin conexión.

## verificar_pack.py — ¿está bien este pack?

```bash
python3 Tools/verificar_pack/verificar_pack.py Assets/GameContent/Packs/Gen1-6
python3 Tools/verificar_pack/verificar_pack.py Assets/GameContent/Packs/Gen1-6 --pokeapi --gen 6
python3 Tools/verificar_pack/verificar_pack.py Excel                    # tu carpeta de Excel exportada
```

| Comprobación | Qué mira |
|---|---|
| Formato | Filas y columnas como las lee el editor, ids vacíos o repetidos, filas con celdas de más o de menos. |
| Referencias | Que exista todo lo que se nombra (en el pack o en la base que crean las plantillas del código): movimientos de especies y entrenadores, evoluciones y sus objetos, habilidades, tipos, curvas, grupos huevo, naturalezas, objetos equipados y de mochila, efectos de movimientos (estados, climas, trampas, campos), niveles de IA. |
| `--pokeapi --gen N` | Estadísticas base y tipos de las especies, tipo/categoría/potencia/precisión/PP/prioridad de los movimientos y la tabla de tipos **de la generación N** (deshace con el historial de PokeAPI los cambios posteriores). Movimientos escritos en los equipos de entrenadores: que la especie los pueda aprender en algún juego hasta la gen N. |

- **ERRORES**: hay que corregirlos (el script termina con código 1: sirve antes de un commit o en CI).
- **AVISOS**: revisa si son intencionados (p. ej. movimientos especiales de un líder del juego original).

## generar_packs.py — un pack FIEL a cada generación (Gen1 … Gen6)

```bash
python3 Tools/verificar_pack/generar_packs.py          # los 6
python3 Tools/verificar_pack/generar_packs.py --gen 3  # solo la 3.ª
```

Crea `Assets/GameContent/Packs/GenN` = «el juego» de esa generación: 151/251/386/493/649/721 especies con los datos
que tenían entonces (PokeAPI, deshaciendo los cambios posteriores):

| | Qué es fiel a la generación |
|---|---|
| Especies | Tipos, estadísticas (1.ª gen.: una sola Especial), habilidades (desde la 3.ª; ocultas desde la 5.ª), aprendizaje por nivel del juego de referencia (Rojo/Azul, Cristal, Esmeralda, Platino, N2/B2, ROZA) y MT/tutor/huevo de sus juegos, grupos huevo (desde la 2.ª), sin géneros en la 1.ª. |
| Movimientos | Solo los que existen, con tipo, potencia, precisión, PP y prioridad de entonces; físico/especial **según el tipo** hasta la 3.ª gen.; Maldición «???» (typeless) en la 2.ª-4.ª. |
| Nombres en inglés | Columna `nombre_en` (el nombre de Showdown) en especies —variantes al estilo «Rotom-Wash», «Nidoran-F»—, movimientos, habilidades, objetos y naturalezas: sirve para importar y exportar equipos en formato Showdown. |
| Formas y variantes | Formas de combate (Castform, Cherrim, Darmanitan, Meloetta, Aegislash, Giratina, Arceus, Kyogre/Groudon primigenios) con qué las provoca, y variantes como especies con `forma_de` (Deoxys, Wormadam, Rotom, Shaymin, Basculin, Tótem, Kyurem, Keldeo, Pumpkaboo/Gourgeist, Hoopa) con sus datos de PokeAPI (`formas.py`). |
| Tabla de tipos | La de la generación (sin Siniestro/Acero en la 1.ª, sin Hada hasta la 6.ª, Fantasma→Acero ×0,5 hasta la 5.ª...). |
| Objetos | Todos los de la generación (nombre, descripción y precio oficiales) salvo las MT y los que ya tienen efecto en las plantillas del código: Balls, objetos clave, bayas, placas, Megapiedras, mails... (los nuevos, solo con sus datos). |
| Entrenadores | Los de los juegos hasta esa generación, con movimientos válidos (sin objetos en la 1.ª, sin naturalezas antes de la 3.ª), especialistas de tipo, Ases del Frente (5.ª y 6.ª) y el Laboratorio de IA. |

Lo que PokeAPI no tiene (efectos de movimientos, configuración de habilidades, Pokédex, entrenadores) sale de
**`Tools/datos_fuente/`**: edita allí y vuelve a generar. Cada pack trae un `INFORME.txt` con lo que es fiel y lo
que aún se aproxima (el motor aplica efectos y reglas de la 6.ª gen.; se ajustará con las mecánicas por generación).

En Unity: **Centro de Contenido → Pack** (desplegable) para elegir cuál importar.

## generar_base.py — las hojas base de un pack

```bash
python3 Tools/verificar_pack/generar_base.py Assets/GameContent/Packs/Gen1-6 --gen 6
```

Escribe `tipos.csv`, `tabla_tipos.csv` (la de esa generación), `naturalezas.csv` (desde la 3.ª) y `grupos_huevo.csv`
(desde la 2.ª). Con ellas el pack trae su propia base y el Centro de Contenido no la saca de las plantillas del
código («el pack manda»).

## Flujo recomendado para un lote de datos

1. Edita los CSV (Excel o scripts).
2. `verificar_pack.py <carpeta> --pokeapi --gen N` hasta que no haya errores.
3. Importa en Unity (Centro de Contenido → «📦 Importar» o Herramientas → Excel y compartir).
4. En Unity: «Validar contenido» y, si tocaste niveles de IA, Pruebas → 🏆 Torneo de IAs.

## Archivos

- `verificar_pack/csvlib.py` — lectura/escritura de CSV idéntica a `CsvTable.cs`.
- `verificar_pack/pokeapi.py` — descarga y caché de PokeAPI; valores por generación.
- `verificar_pack/verificar_pack.py`, `generar_packs.py`, `generar_base.py` — las herramientas.
- `datos_fuente/` — datos maestros (efectos, habilidades, Pokédex, entrenadores) y los INFORME de los packs antiguos.

Pokémon y sus nombres son marcas de Nintendo / Game Freak / The Pokémon Company: proyecto personal y educativo.

## probar_dominio — compilar el dominio y pasar sus tests sin abrir Unity

```bash
dotnet test Tools/probar_dominio
```

Compila con .NET 8 el código puro del juego (SharedKernel, GameDefinition.Domain, Battle, Party, Adventure...) y ejecuta
los tests de `Tests/EditMode` que no dependen de Unity. Sirve para comprobar un cambio del motor en segundos. Los tests
que usan el editor o las fichas (Excel, catálogo, papelera...) están excluidos en el `.csproj`: esos se pasan en el
Test Runner de Unity. Si creas un test nuevo que use Unity, añádelo a la lista `Exclude`.
