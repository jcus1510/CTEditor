# 13 · Packs y herramientas Python

## Packs (`Assets/GameContent/Packs/GenN`)

Un pack = una generación **fiel a sí misma**, en hojas CSV + `INFORME.txt` (qué es fiel y qué se aproxima).

| Hoja | Gen1 | Gen2 | Gen3+ |
|---|---|---|---|
| `especies.csv`, `movimientos.csv`, `objetos.csv`, `tipos.csv`, `tabla_tipos.csv`, `entrenadores.csv`, `sets.csv` | ✔ | ✔ | ✔ |
| `grupos_huevo.csv` | — | ✔ | ✔ |
| `habilidades.csv`, `naturalezas.csv` | — | — | ✔ |

Contenido: 151/251/386/493/649/721 especies con los datos de su época (PokeAPI deshaciendo cambios posteriores),
movimientos con tipo/potencia/precisión/PP/prioridad de entonces (categoría por tipo hasta la 3.ª), aprendizaje de su
juego de referencia + MT/tutor/huevo, habilidades (3.ª+, ocultas 5.ª+), tabla de tipos de la generación, objetos
(salvo MT) **con todos sus efectos** (los de `Assets/GameContent/Plantillas/objetos.csv`), entrenadores de los juegos, formas y variantes, **48 megas**
con su megapiedra (Gen6), nombres en inglés (`nombre_en`) y sets de Smogon.

**Importar**: Centro de Contenido → Pack → «📦 Importar». Primero se crea la base que el pack no trae desde las
plantillas del código (estados, climas, efectos de lado, trampas, Forcejeo, curvas, menús,
controles, niveles de IA); **si el pack trae la hoja de una categoría, manda el pack**. Para pasar TODO el proyecto a
otra generación: Herramientas → **Cambiar de generación** ([11](11-editor.md)).

## Herramientas (`Tools/verificar_pack/`, Python 3 sin librerías extra)

| Script | Qué hace |
|---|---|
| `generar_packs.py [--gen N]` | Genera los packs Gen1…Gen6 desde PokeAPI (caché en `Tools/.cache/pokeapi`) y `Tools/datos_fuente/`. |
| `generar_base.py <carpeta> --gen N` | Hojas base de un pack: tipos, tabla de tipos, naturalezas, grupos huevo. |
| `formas.py` | Formas de combate, variantes y megas (lo usa `generar_packs.py`). |
| `generar_sets.py [--gen N]` | `sets.csv` desde Smogon (`pkmn.github.io/smogon/data/{sets,stats}/genNformato.json`; OU, Ubers, UU, RU, NU, PU, LC), puntuación por uso real; ids únicos por (especie, formato). Caché en `Tools/.cache/smogon`. |
| `verificar_pack.py <carpeta> [--pokeapi --gen N]` | Formato, referencias (a la hoja y a la base que crean las plantillas) y fidelidad con PokeAPI. Sale con código 1 si hay ERRORES. Estado actual: **0 errores en Gen1…Gen6** (los avisos son movimientos especiales de líderes, intencionados). |
| `pokeapi.py`, `csvlib.py` | Descarga/caché de PokeAPI; CSV idéntico a `CsvTable.cs` (`;`, coma decimal, BOM). |

Datos que PokeAPI no tiene (efectos de movimientos, configuración de habilidades, Pokédex, entrenadores): en
`Tools/datos_fuente/`. Se edita allí y se regenera.

## Flujo para cambiar datos de los packs

1. Editar los CSV (Excel o scripts) o `datos_fuente/`, y regenerar si hace falta.
2. `python3 Tools/verificar_pack/verificar_pack.py Assets/GameContent/Packs/GenN --pokeapi --gen N` hasta 0 errores.
3. En Unity: importar (Centro de Contenido o Excel), **Validar contenido** y, si se tocaron IAs, **Torneo de IAs**.

## Problemas conocidos de las fuentes

- PokeAPI: descripciones en español de los grupos de versión 15-16 desalineadas → se usan las de vg ≥ 17.
- Megapiedras: su generación sale de `item_game_indices`, no del grupo de versión.
- Smogon: «Camerupt» y «Camerupt-Mega» son la misma especie aquí → contador por (especie, formato).
- Nidoran♀/♂ normalizan igual → nombres en inglés «Nidoran-F» / «Nidoran-M».
