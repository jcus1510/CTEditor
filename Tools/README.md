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
- `verificar_pack/verificar_pack.py`, `verificar_pack/generar_base.py` — las dos herramientas.

Pokémon y sus nombres son marcas de Nintendo / Game Freak / The Pokémon Company: proyecto personal y educativo.
