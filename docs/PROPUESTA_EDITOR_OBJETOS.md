# Propuesta — Editor de objetos por bloques (para todas las generaciones)

> Estado: **propuesta para discutir** (no implementada). Autor: sesión de Claude Code, tras el paso 7.

## 1. Qué falla hoy

Revisados `ItemData.cs`, `ItemDefinition.cs`, `ItemExtras.cs` e `ItemEditorWindow.cs`:

| Problema | Dónde | Por qué choca con «todo personalizable» |
|---|---|---|
| Sección **«Equipado: competición (5.ª y 6.ª gen.)»** | `ItemData` (`[Header]`), `ItemExtras` | Ata los efectos a unas generaciones. Un efecto no «es» de una generación: es una pieza que cualquier objeto puede usar. |
| **~30 campos sueltos con nombre de objeto concreto**: `heldBlackSludge`, `heldAirBalloon`, `heldCuresAnyStatus` (Baya Ziuela), `heldQuickClawChance`, `heldChoiceLock`… | `ItemData` | Cada objeto nuevo del juego original exige un campo nuevo, más código y más columnas de CSV. El autor solo puede combinar lo que ya se programó, con nombres que no dicen *qué hace*. |
| **Ids escritos a mano**: `heldResistBerryType = "fire"`, `curesStatusId = "poison\|toxic"`, `heldSelfStatusEndOfTurn` | `ItemData` | Hay que saberse los ids. Lo correcto es un **desplegable** con los tipos, estados y climas que existen en el proyecto. |
| **«Equipado:» repetido** en cada frase de «Qué hace» | `ItemEditorWindow.Describe` | Ruido: el contexto debería ir en un encabezado («Mientras lo lleva», «Al recibir un golpe»). |
| Curar estados: una casilla «todos» + texto | `curesAllStatus`, `curesStatusId` | Debería ser una lista con desplegable (y «cualquiera» como opción), incluidos estados volátiles (confusión, enamoramiento…). |
| El inspector es el de Unity con `[Header]` | `ItemData` | Todos los campos a la vista aunque el objeto no los use: poco intuitivo. |
| `ItemCategory` es un `enum` fijo | Dominio | Los bolsillos de la mochila no se pueden inventar. |

## 2. La idea: un objeto = ficha + lista de **efectos**; cada efecto = **cuándo + si + qué**

Es la misma gramática que ya usan las condiciones (`Condition`, `ConditionKind`) y que deberían acabar usando las
habilidades: *«**Cuando** [disparador], **si** [condiciones], **entonces** [acción], y [se gasta / no]»*.

```
Baya Caoca  (Equipable · 200 ₽)
┌ Antes de recibir un golpe ─────────────────────────────────────────────┐
│ si  el movimiento es de tipo [Fuego ▼]   y   es muy eficaz (≥ ×2)      │
│ →   daño recibido × [0,5]                           ☑ se gasta         │
└────────────────────────────────────────────────────────────────────────┘
```

Ninguna pieza sabe de generaciones: la 2.ª gen. no tiene Baya Caoca porque **el pack de la 2.ª no la trae**, y las
reglas de generación solo deciden lo global (p. ej. «sin objetos equipados» en la 1.ª).

### 2.1 Ficha del objeto (lo común)

| Campo | Nota |
|---|---|
| id, nombre, nombre en inglés, descripción, icono, precio | Como ahora. |
| **Bolsillo** | Ficha propia `BagPocketData` (Medicinas, Bayas, Bolas, MT, Clave… o los que invente el autor). Sustituye a `ItemCategory` (se migra 1:1). |
| **Dónde se usa** | ☐ desde la mochila en combate · ☐ desde la mochila fuera · ☐ equipado · ☐ al intercambiar (evolución) |
| **Sobre qué** (al usarlo) | un monstruo del equipo · un movimiento de un monstruo · el rival (bolas) · nada (Repelente, Cuerda Huida) |
| **Gasto al usarlo** | se gasta · no se gasta (clave, MT de 5.ª+) · según regla (MT gastables en 1.ª-4.ª: *opción del Ruleset*, no del objeto) |
| **Efectos** | Lista de bloques (abajo). |

### 2.2 Disparadores («Cuándo»)

| Grupo | Disparadores |
|---|---|
| Al usarlo | *Al usarlo* (mochila; dentro/fuera según «Dónde se usa») |
| Mientras lo lleva | *Siempre (pasivo)* · *Al entrar al campo* · *Al final del turno* · *Al elegir movimiento* · *Al cambiar/retirarse* |
| Golpes | *Antes de recibir un golpe* · *Al recibir un golpe* · *Al golpear* · *Al recibir daño indirecto* |
| Estado | *Al sufrir un estado* · *Al bajarle una estadística* · *Con PS por debajo de X %* (umbral editable) |
| Aventura | *Al caminar* (Repelente: N pasos) · *Al intercambiar* (evolución; lo deciden las especies) |

### 2.3 Condiciones («Si») — el sistema que ya existe

Se reutilizan `Condition`/`ConditionKind` (tipo del movimiento, categoría, contacto, eficacia, PS %, clima, estado,
tipo propio, puede evolucionar, género, campo…). Cambio clave en el editor: **cada condición que nombra algo muestra
un desplegable** de lo que existe en el proyecto (tipos, estados, climas, campos, etiquetas de movimiento), nunca un id
escrito a mano. Añadir, si hacen falta: *hora del día*, *lugar/zona*, *turno del combate*, *es especie X* (bolas
especiales, Bola Ocaso/Bola Red/Bola Rápida; Polvo Metálico, Bola Luminosa).

### 2.4 Acciones («Qué») — piezas genéricas con parámetros

| Familia | Acciones (parámetros con desplegable) |
|---|---|
| PS | Curar *[fijo / % / todo]* · Perder PS *[%]* · Curar *[%] del daño hecho* · Dañar al atacante *[%]* (con contacto → condición) |
| Estados | **Curar estado *[lista ▼ de estados, o «cualquiera»]*** · Poner estado *[estado ▼]* *[a sí mismo / al otro]* |
| Vida y PP | Revivir *[%]* · Recuperar PP *[n / todos]* *[uno / todos los movimientos]* · Subir PP máx. |
| Estadísticas | Cambiar etapa *[stat ▼]* *[±n]* *[a quién]* · Multiplicar stat *[stat ▼]* *[×]* · +Índice de crítico *[n]* · Precisión propia *[×]* · Precisión del rival *[×]* |
| Daño | Potencia de sus movimientos *[×]* · Daño que hace *[×]* · **Daño recibido *[×]*** · Aguantar con 1 PS |
| Tipos | Inmune al tipo *[tipo ▼]* (Globo Helio: + «hasta recibir un golpe») · Cambiar tipo del movimiento/propio *[tipo ▼]* (placas de Arceus, Multitipo) |
| Orden | Actuar el primero *[%]* · Actuar el último |
| Bloqueos | Solo puede repetir el primer movimiento (Elección) · No puede usar movimientos de estado · No puede huir / cambiar |
| Campo | Duración extra de *[clima ▼ / pantallas / campo ▼]* *[+n turnos]* · Retroceder *[%]* |
| Captura | Bola *[× o «siempre»]* (las condiciones hacen las bolas especiales) |
| Crecimiento | Amistad *[±n]* · EVs *[stat ▼]* *[±n]* · IVs al máximo *[stat ▼ / todas]* · Subir nivel *[n]* · Experiencia *[n]* |
| Formas y mecánicas | Cambiar a forma/variante *[forma ▼]* · Habilita mecánica *[mecánica ▼]* (Megapiedra, Cristal Z, …) |
| Movimientos | Enseñar movimiento *[movimiento ▼]* (MT/MO) · Recordar movimientos |
| Aventura | Huir del combate · Repeler *[n pasos]* · Salir de la cueva · Abrir *[menú ▼]* (objetos clave) |

Cada bloque además: **☐ se gasta al activarse**, **probabilidad %** (opcional) y **veces por combate** (opcional).

### 2.5 Ejemplos con bloques (sin un solo campo específico)

| Objeto | Bloques |
|---|---|
| Baya Caoca | *Antes de recibir un golpe* · si tipo = [Fuego] y eficacia ≥ 2 → daño recibido ×0,5 · se gasta |
| Baya Ziuela | *Al sufrir un estado* → curar estado [cualquiera] · se gasta |
| Baya Meloc (Antídoto) | *Al usarlo* / *Al sufrir un estado* → curar estado [Envenenado, Gravemente envenenado] |
| Restos | *Al final del turno* → curar 6,25 % |
| Lodo Negro | *Al final del turno* · si es tipo [Veneno] → curar 6,25 % · *Al final del turno* · si NO es tipo [Veneno] → perder 12,5 % |
| Cinta Elección | *Siempre* → stat [Ataque] ×1,5 · *Siempre* → solo repetir el primer movimiento |
| Banda Focus | *Antes de recibir un golpe* · si PS = 100 % → aguantar con 1 PS · se gasta |
| Seguro Debilidad | *Al recibir un golpe* · si eficacia ≥ 2 → etapa [Ataque] +2 · etapa [Atq. Esp.] +2 · se gasta |
| Carbón / Incienso | *Siempre* · si tipo del movimiento = [Fuego] → potencia ×1,2 |
| Poción | *Al usarlo* → curar 20 PS |
| Ataque X | *Al usarlo* (combate) → etapa [Ataque] +2 (1 en gens antiguas: **dato del pack**) |
| Ocaso Ball | *Al usarlo* · si hora = noche o zona = cueva → bola ×3,5; si no → bola ×1 |
| MT26 Terremoto | *Al usarlo* (fuera) · sobre un monstruo → enseñar movimiento [Terremoto] |
| Gardevoirita | *Siempre* → habilita mecánica [Megaevolución] (la forma la indica la especie) |
| Objeto nuevo inventado | «Amuleto Tormenta»: *Al entrar al campo* · si clima = [Lluvia] → etapa [Velocidad] +1 · 1 vez por combate |

### 2.6 El editor (cómo se ve)

1. **Cabecera**: icono, nombre, bolsillo (desplegable), precio, chips «Mochila en combate · Fuera · Equipado».
2. **«Qué hace»** en frases, **agrupadas por disparador** (encabezado «Mientras lo lleva», «Al recibir un golpe»…):
   se acabó el «Equipado:» repetido.
3. **Efectos**: tarjetas plegables; cada una una frase editable
   `[Cuando ▼] si [condición ▼ …] → [acción ▼ + parámetros] ☐ se gasta`, con **+ Condición** y **+ Acción**.
   Solo se ven los parámetros de la acción elegida.
4. **Plantillas paramétricas** (no por generación): «Baya que resiste un ataque de tipo [▼]», «Potenciador de tipo
   [▼] ×[1,2]», «Cura el estado [▼]», «Baya que sube [stat ▼] con poca vida», «Roca de clima [▼]», «Bola [×]»,
   «MT de [movimiento ▼]». Una plantilla solo **genera bloques**: después se pueden tocar.
5. **Biblioteca** (Poción, Restos, Baya Caoca…) = plantillas paramétricas ya rellenas; un pack puede traer todas.
6. Avisos del validador en la misma tarjeta («Cura el estado X, que no existe», «Bola no usable en combate»…).

## 3. Modelo de datos propuesto

**Dominio** (`GameDefinition/Domain/Items`):

```csharp
public enum ItemTrigger { OnUse, Passive, OnEnter, EndOfTurn, OnChooseMove, OnSwitchOut, BeforeHit, AfterHit,
                          OnDealDamage, OnIndirectDamage, OnStatus, OnStatDrop, HpBelow, OnWalk, OnTrade }   // solo se añade AL FINAL
public enum ItemAction  { Heal, LoseHp, HealFromDamage, DamageAttacker, CureStatus, InflictStatus, Revive, RestorePp,
                          RaisePpMax, ChangeStage, MultiplyStat, CritStage, AccuracyMul, EvasionMul, PowerMul,
                          DamageDealtMul, DamageTakenMul, SurviveAt1, ImmuneToType, ChangeType, ActFirstChance,
                          ActLast, ChoiceLock, BlockStatusMoves, BlockSwitch, ExtendWeather, ExtendScreens,
                          ExtendField, FlinchChance, Catch, Friendship, Evs, MaxIvs, LevelUp, Experience,
                          ChangeForm, EnableMechanic, TeachMove, EscapeBattle, Repel, EscapeRope, OpenMenu }
public sealed class ItemEffect
{
    public ItemTrigger Trigger; public IReadOnlyList<Condition> Conditions;
    public ItemAction Action; public ConditionSubject Target;   // a sí mismo / al otro
    public string Ref;        // id del tipo / estado / stat / clima / movimiento / forma / mecánica (según la acción)
    public float Amount;      // n, %, ×
    public bool Consumes; public float Chance = 100; public int MaxPerBattle;
}
// ItemDefinition: Id, nombres, Pocket, UsableInBattle/Outside/Held, Target, Consumable, IReadOnlyList<ItemEffect> Effects
```

**Unity** (`ItemData`): `ItemEffectData[] effects` con los mismos campos serializables (struct plano, sin
`SerializeReference`: más robusto con Unity y CSV) + `ConditionData[]` (ya existe su drawer). `ItemMapper` traduce.

**Motor**: un `ItemEffectRunner` en `Battle.Domain` que el `TurnResolver` llama en cada punto de disparo (los mismos
enganches que hoy usan `ItemExtras`: `BeforeHit` = donde se aplican Baya/Banda Focus, `EndOfTurn` = Restos, etc.).
Fuera de combate, `FieldActions` ejecuta los *Al usarlo* / *Al caminar*. **El mismo motor sirve después para las
habilidades** (hoy `AbilityExtras` tiene el mismo problema de campos sueltos).

**CSV** (`objetos.csv`): una columna `efectos` legible y reversible, un bloque por `|`:

```
antes_de_golpe[si movimiento.tipo=fire; eficacia>=2]: daño_recibido x0,5; se_gasta
fin_de_turno[si propio.tipo=poison]: curar 6,25% | fin_de_turno[si propio.tipo!=poison]: perder 12,5%
al_usar: curar_estado poison,toxic
```

Se usan los **ids** en el CSV (como el resto de hojas), pero el editor siempre muestra nombres y desplegables.

## 4. Migración sin romper nada (por fases)

1. **Dominio + motor** con tests: `ItemEffect`, `ItemEffectRunner`; conversión *campos viejos → bloques* en el mapper.
   Test de oro: cada objeto de la biblioteca actual se comporta igual con bloques que con campos (combates de prueba).
2. **`ItemData.effects`** + menú «Convertir objetos al formato por bloques» (lee los campos viejos, escribe bloques,
   deja copia en Excel/copias). Los campos viejos quedan ocultos y solo lectura.
3. **Nuevo editor** (tarjetas, desplegables, plantillas paramétricas, «Qué hace» agrupado).
4. **CSV**: columna `efectos`; las columnas viejas siguen aceptándose como alias (se convierten al importar);
   `generar_packs.py` escribe `efectos`; `verificar_pack.py` valida sus referencias.
5. **Limpieza**: fuera `ItemExtras` y los ~30 campos sueltos; `ItemCategory` → `BagPocketData`.
6. (Después) **Habilidades** con el mismo motor.

## 5. Preguntas para decidir

1. ¿Bolsillos como fichas editables (`BagPocketData`) o basta con el enum actual?
2. ¿Las MT gastables (1.ª-4.ª) como opción de las **reglas** (propuesta) o del objeto?
3. ¿Hacemos a la vez las habilidades con el mismo motor, o primero objetos?
4. ¿Queremos «veces por combate» y «probabilidad» en todos los bloques (más potente) o solo donde haga falta?
