# 04 · Núcleo compartido (SharedKernel)

`Runtime/SharedKernel` — sin dependencias. Solo mecanismos, nada de reglas de juego.

| Tipo | Archivo | Para qué |
|---|---|---|
| `Id<T>` | `ValueObjects/Id.cs` | Id tipado (`Id<Species>`, `Id<Move>`...). Un id de especie no se puede pasar donde va uno de movimiento. Es un `struct` con `Value` (string). |
| `Chance` | `ValueObjects/Chance.cs` | Probabilidad validada (0-1). |
| `Percentage` | `ValueObjects/Percentage.cs` | Porcentaje validado (0-100); lo usan los estados. |
| `Result` | `ValueObjects/Result.cs` | Resultado de una operación que puede fallar con un motivo (sin excepciones para lo esperable: «el equipo está lleno»). |
| `IRng` | `Abstractions/IRng.cs` | Azar inyectable. **El dominio nunca usa `UnityEngine.Random` ni `System.Random` directamente.** En producción: `SystemRng` (Bootstrap/Platform); en tests: `SeededRng` (determinista). |
| `IClock` | `Abstractions/IClock.cs` | Hora inyectable (evoluciones de día/noche, encuentros). |
| `IDomainEvent` | `Events/IDomainEvent.cs` | Marca de «hecho que ya ocurrió» (inmutable, nombre en pasado). |
| `IEventBus`, `IEventHandler` | `Events/` | Tablón de anuncios; la implementación (`EventBus`) vive en Bootstrap/Platform. |

**Regla**: todo lo aleatorio del motor pasa por `IRng`. Por eso un combate con la misma semilla y las mismas
elecciones es **idéntico** (tests, replays, torneo de IAs). Cuidado al tocar el motor: añadir una tirada de azar en un
sitio nuevo cambia la secuencia de los combates de los tests con semilla. Las tiradas «opcionales» (probabilidad de un
bloque de efecto) solo se hacen si la probabilidad es menor que 100 %.
