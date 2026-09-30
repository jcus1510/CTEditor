# 16 · Convenciones

## Idiomas

| Qué | Idioma | Por qué |
|---|---|---|
| **Interfaz del editor y del juego** (ventanas, etiquetas, avisos, mensajes de combate, cabeceras de Excel) | **Español** (de momento) | Es el idioma del autor. |
| **Código**: nombres de clases, métodos, campos, enums | **Inglés** | Para poder traducir la interfaz entera sin tocar el código. |
| Comentarios del código | Inglés en el código nuevo (el código antiguo tiene muchos en español; se respeta) | Coherente con el punto anterior. |
| Esta documentación y el manual del autor | Español | Para el equipo actual. |

**Para poder traducir después**, los textos de interfaz se concentran en pocos sitios:
- `Editor/Common/Etiquetas.cs`: nombre de campo → (etiqueta, cabecera de Excel) y valores de enums → texto.
- `Editor/Common/EffectText.cs`: todo el texto de los efectos por bloques.
- `EditorTheme`, `EditorCatalog`: títulos y descripciones de ventanas.
- Mensajes de combate: `BattleScreen` (narración de eventos); textos del juego: fichas de menús e interfaz.
Evitar escribir textos de interfaz sueltos en la lógica: mejor en estas tablas.

⚠ `Etiquetas` usa el NOMBRE del campo como clave: dos campos con el mismo nombre en fichas distintas comparten
etiqueta y cabecera. Por eso existen nombres como `abilitiesEnabled`, `mechanicKind`, `itemOptions` (evitan chocar
con `items` = «Mochila», etc.). Antes de añadir un campo, buscar si su nombre ya existe en `Etiquetas`.

## Datos serializados

- **Enums: solo se añaden valores AL FINAL** (Unity guarda el número; insertar en medio cambia el significado de las
  fichas existentes).
- **Parámetros nuevos de constructores de dominio: opcionales y al final**, para no romper llamadas.
- Renombrar un campo serializado pierde el dato (usar `[FormerlySerializedAs]` si hace falta).
- Campos antiguos que ya no se editan: `[LegacyField, HideInInspector]` + conversión (ver [09](09-efectos-por-bloques.md)).
- Ids de contenido: minúsculas y guiones bajos (`sp_attack`, `choice_band`). Estadísticas clásicas: `hp, attack,
  defense, sp_attack, sp_defense, speed` (+ `accuracy`, `evasion`).

## Archivos `.meta`

Cada archivo nuevo en `Assets/` necesita su `.meta` con un GUID propio. Fuera de Unity se generan así (C#):
```
fileFormatVersion: 2
guid: <32 caracteres hex nuevos, p. ej. uuid4().hex>
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
```
Para `.md`/`.txt`/`.csv` basta `fileFormatVersion: 2` + `guid` + `TextScriptImporter` (o dejar que Unity los cree).

## Git

- Rama de trabajo **`develop`**; `git push -u origin develop`. **No se crean PR salvo que se pidan.**
- Mensajes de commit en español, descriptivos (título + lista de cambios).
- Cuando los hace Claude, terminan con:
  ```
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  Claude-Session: <enlace de la sesión>
  ```
- No se versionan `Library/`, `*.csproj` generados, `Tools/.cache/` (ver [02](02-estructura-del-repositorio.md)).

## Estilo de código

- Dominio inmutable (propiedades `{ get; }`, `With(...)` para copias), validación en el constructor.
- Todo azar por `IRng`; nada de `UnityEngine` en los `*.Domain`.
- Partials por tema cuando una clase crece (`TurnResolver.*`, `ContentValidator.*`).
- Comentario `/// <summary>` en cada tipo público explicando QUÉ es y POR QUÉ existe.
- Editor: `ContentAssets.Edit(asset, so => ...)` para cambios con Deshacer; `EditorTheme` para el aspecto.

## Recetas

Ver la tabla «Dónde poner algo nuevo» en [03](03-arquitectura.md) y «Añadir una acción nueva» en
[09](09-efectos-por-bloques.md).
