# CTEditor — resumen de una página

> La documentación completa, capítulo por capítulo, está en [`docs/README.md`](README.md). Esto es solo el resumen.

- **Qué es**: motor de combates por turnos estilo Pokémon + editor completo en **Unity 6 (6000.3.7f1)**. Premisa:
  **todo es editable**; las generaciones son datos (packs) y reglas, nunca código ([01](01-vision-y-premisas.md)).
- **Arquitectura**: DDD estricto; dominios puros sin Unity (SharedKernel, GameDefinition.Domain, Battle, Party,
  Adventure, Eventing) + Infrastructure (ScriptableObjects y mappers) + Editor + Bootstrap ([03](03-arquitectura.md)).
- **Datos**: fichas `*Data` → mapper (ACL) → dominio → `GameData` → `BattleSession` → `TurnResolver` → eventos → UI.
- **Objetos y habilidades**: listas de **efectos por bloques** «cuándo / si / qué» con probabilidad, veces por combate y
  gasto, guardadas DENTRO de cada ficha ([09](09-efectos-por-bloques.md)).
- **Excel**: todo se exporta/importa en CSV (`;`, coma decimal), con análisis previo y copia de seguridad ([12](12-excel-csv.md)).
- **Packs**: Gen1…Gen6 fieles, generados con Python desde PokeAPI y Smogon y verificados ([13](13-packs-y-herramientas.md)).
- **Verificar sin Unity**: `dotnet test Tools/probar_dominio` (221 ✔) y `dotnet test Tools/compilar_unity`
  (300 ✔, 9 fallos conocidos que necesitan Unity) ([15](15-pruebas-y-verificacion.md)).
- **Idiomas**: interfaz en español; código (identificadores y comentarios nuevos) en inglés; textos de interfaz
  centralizados (`Etiquetas`, `EffectText`) para traducir ([16](16-convenciones.md)).
- **Git**: rama `develop`, sin PR salvo que se pidan, commits en español ([16](16-convenciones.md)).
- **Siguiente**: paso 8 (7.ª gen.), efectos «Al usarlo» pendientes (MT, vitaminas, Caramelo Raro, repelentes),
  paso 8 (7.ª gen.) ([18](18-pendientes.md)).
- **Migrar**: [19](19-migrar.md).
