# 19 · Cómo migrar el proyecto

Lista paso a paso para llevar CTEditor a otro repositorio, máquina, versión de Unity o persona sin perder nada.

## 1. Qué hay que llevarse

| Imprescindible | Opcional / regenerable |
|---|---|
| `Assets/` **con todos los `.meta`** (código, fichas, packs, papelera) | `Library/` (Unity la regenera) |
| `Packages/manifest.json` (+ `packages-lock.json`) y `ProjectSettings/` | `Tools/.cache/` (se vuelve a descargar) |
| `Tools/` (scripts, `datos_fuente/`, los dos `.csproj`) y `docs/` | `Excel/` (exportación; pero `Excel/copias/` son copias de seguridad: guárdalas si importan) |
| `.gitignore` (con las negaciones de `Tools/*/*.csproj`) | `*.csproj`/`*.sln` generados por Unity |

⚠ **Nunca copiar `Assets/` sin los `.meta`**: se pierden todas las referencias entre fichas (especies ↔ movimientos,
entrenadores ↔ especies...). Si pasa, **Herramientas → Reenlazar referencias por id** recupera los equipos y zonas
(guardan ids de respaldo), pero no todo.

## 2. Nuevo repositorio

```bash
git clone <origen> CTEditor && cd CTEditor
git remote set-url origin <destino> && git push -u origin develop
```
Rama de trabajo: `develop`. La historia completa (PR #1-#16 y los pasos) va incluida.

## 3. Abrir en Unity

1. Unity **6000.3.7f1** (ver `ProjectSettings/ProjectVersion.txt`). Otra versión 6.x debería valer; Unity 2022 o
   anterior NO (el código usa APIs de Unity 6 en Bootstrap).
2. Primera apertura: «Window → TextMeshPro → Import TMP Essential Resources».
3. Esperar la compilación: **la Consola no debe tener errores rojos**. Si los hay, Unity usaría código viejo ([15](15-pruebas-y-verificacion.md)).
4. **CTEditor → Centro de Contenido**: si el proyecto está vacío, elegir pack e importar (o «✨ Crear TODO»).
5. **Herramientas → Validar contenido** y **Test Runner → EditMode → Run All**.
6. Si los objetos o las habilidades vienen de una versión antigua (sin efectos): Centro de Contenido → Pack → Importar →
   **«Actualizar también»**, o en el editor de objetos «↻ Actualizar desde las plantillas» (usa el pack elegido como fuente).

## 4. Entorno sin Unity (CI, otra persona, una IA)

```bash
apt-get update && apt-get install -y dotnet-sdk-8.0 python3
dotnet test Tools/probar_dominio          # dominio: todo verde
dotnet test Tools/compilar_unity          # todo Runtime + tests: solo los 9 fallos conocidos
python3 Tools/verificar_pack/verificar_pack.py Assets/GameContent/Packs/Gen6 --pokeapi --gen 6
```
`compilar_unity` descarga `Unity3D.SDK`, NUnit y el SDK de tests de NuGet (necesita red la primera vez).
Los generadores necesitan red para PokeAPI (`raw.githubusercontent.com`) y Smogon (`pkmn.github.io`).

## 5. Mover el contenido de un proyecto a otro (sin el código)

1. En el origen: **Herramientas → Excel y compartir → Exportar** (todo o las categorías que quieras).
2. En el destino: **Importar** esa carpeta («crear y actualizar»), revisar el análisis y **Aplicar** (hace copia antes).
3. Las fichas se crean con ids iguales; las referencias se resuelven por id y se reenlazan.
Los equipos también se pueden pasar en formato **Showdown** (entrenadores / equipos prearmados).

## 6. Retomar el trabajo (persona o sesión de IA nueva)

1. Leer [README](README.md) → [01](01-vision-y-premisas.md) → [03](03-arquitectura.md) → [16](16-convenciones.md) → [18](18-pendientes.md).
2. Ejecutar las comprobaciones del punto 4.
3. Seguir las convenciones de idioma: interfaz en español, código en inglés, commits en español en `develop`.
4. Antes de cada commit, la lista de [15](15-pruebas-y-verificacion.md).
