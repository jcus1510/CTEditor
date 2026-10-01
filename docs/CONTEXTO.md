# CTEditor — resumen de una página

> La documentación completa, capítulo por capítulo, está en [`docs/README.md`](README.md). Esto es solo el resumen.

- **Qué es**: motor de combates por turnos estilo Pokémon + editor completo en **Unity 6 (6000.3.7f1)**. Premisa:
  **todo es editable**; las generaciones son datos (packs) y reglas, nunca código ([01](01-vision-y-premisas.md)).
- **Arquitectura**: DDD estricto; dominios puros sin Unity (SharedKernel, GameDefinition.Domain, Battle, Party,
  Adventure, Eventing, Art, World, Editing, Project, Workspace) + Infrastructure (ScriptableObjects y mappers) + Editor + Bootstrap ([03](03-arquitectura.md)).
- **Datos**: fichas `*Data` → mapper (ACL) → dominio → `GameData` → `BattleSession` → `TurnResolver` → eventos → UI.
- **Objetos y habilidades**: listas de **efectos por bloques** «cuándo / si / qué» con probabilidad, veces por combate y
  gasto, guardadas DENTRO de cada ficha ([09](09-efectos-por-bloques.md)).
- **Excel**: todo se exporta/importa en CSV (`;`, coma decimal), con análisis previo y copia de seguridad ([12](12-excel-csv.md)).
- **Packs**: Gen1…Gen7 fieles, generados con Python desde PokeAPI y Smogon y verificados ([13](13-packs-y-herramientas.md)).
- **Verificar sin Unity**: `dotnet test Tools/probar_dominio` (275 ✔) y `dotnet test Tools/compilar_unity`
  (354 ✔, 9 fallos conocidos que necesitan Unity) ([15](15-pruebas-y-verificacion.md)).
- **Idiomas**: interfaz en español; código (identificadores y comentarios nuevos) en inglés; textos de interfaz
  centralizados (`Etiquetas`, `EffectText`) para traducir ([16](16-convenciones.md)).
- **Git**: rama `develop`, sin PR salvo que se pidan, commits en español ([16](16-convenciones.md)).
- **Siguiente**: la **aplicación CTEditor** (hecha con Unity): recursos y corte de tilesets, editor de mapas con
  ▶ jugar al instante, editor de píxeles, NPC y eventos por nodos ([PROPUESTA_APLICACION](PROPUESTA_APLICACION.md)).
  La aplicación ya abre y se pueden hacer mapas y jugarlos (fases 2-4): CTEditor → Aplicación → Abrir la aplicación
  ([20](20-aplicacion.md)). Arquitectura por capas de la aplicación y cómo añadir módulos: [03](03-arquitectura.md).
  Plan de fases (5-12) en la sección 8 de la propuesta.
  Las generaciones quedan en pausa ([18](18-pendientes.md)).
- **Migrar**: [19](19-migrar.md).
