# 09 · Efectos por bloques («cuándo / si / qué»)

Sistema genérico para describir lo que HACE algo (hoy los **objetos**; después las **habilidades**) sin campos
específicos por objeto ni secciones por generación. Un objeto es una **lista de bloques**:

> **Cuando** [disparador] · **si** [condiciones] → **[acción]** sobre [a quién] · [probabilidad] · [veces por combate] · [se gasta]

## Modelo (dominio)

`GameDefinition/Domain/Effects/EffectBlock.cs`

| Campo | Tipo | Significado |
|---|---|---|
| `Trigger` | `EffectTrigger` | Cuándo se comprueba. |
| `Conditions` | `IReadOnlyList<Condition>` | Todas deben cumplirse (las condiciones de siempre, cap. [05](05-definicion-del-juego.md)). |
| `Action` | `EffectAction` | Qué hace. |
| `Target` | `BlockTarget` (`Self`/`Other`) | Sobre quién actúa (quien lo lleva / el rival). |
| `Ref` | string | Id que necesita la acción: tipo, estado(s) `a\|b`, estadística, clima, movimiento, forma, mecánica. |
| `Amount` | float | PS, %, ×, etapas, turnos, pasos... según la acción. |
| `Threshold` | float | Solo «Con poca vida»: % de PS a partir del cual se activa. |
| `Consumes` | bool | El objeto se gasta al activarse. |
| `Chance` | float 0-100 | Probabilidad (100 = siempre; la tirada solo se hace si es < 100 y DESPUÉS de las condiciones). |
| `MaxPerBattle` | int | Veces por combate (0 = sin límite). Se cuenta por combatiente (`Combatant.EffectUses`). |

### Disparadores (`EffectTrigger`, añadir solo al final)

| Valor | Clave Excel | Cuándo |
|---|---|---|
| `OnUse` | `al_usar` | Se usa desde la mochila (en combate o fuera). |
| `Passive` | `siempre` | Consultas continuas mientras lo lleva (multiplicadores, inmunidades, bloqueos). |
| `OnEntry` | `al_entrar` | Al salir al campo (y al empezar el combate). |
| `EndOfTurn` | `fin_de_turno` | Al final de cada turno. |
| `BeforeHit` | `antes_de_golpe` | Mientras se calcula un golpe que va a recibir. |
| `AfterHit` | `tras_golpe` | Tras recibir un golpe con daño (si sigue en pie). |
| `ContactTaken` | `contacto` | Tras recibir un golpe con contacto (aunque se debilite). |
| `OnDealDamage` | `al_hacer_daño` | Tras golpear con un movimiento que hace daño. |
| `OnStatus` | `al_sufrir_estado` | En cuanto le ponen un estado. |
| `LowHp` | `poca_vida@N` | Cuando sus PS bajan del umbral N %. |
| `OnWalk` | `al_caminar` | Cada paso fuera del combate (aún sin efecto). |

### Acciones (`EffectAction`, añadir solo al final)

| Acción | Clave Excel | Parámetros | ¿El motor la aplica hoy? |
|---|---|---|---|
| `HealHp` / `HealPercent` | `curar N` / `curar N%` | PS / % de PS máx. | Sí (al usar y en instantáneos) |
| `LoseHpPercent` | `perder N%` | % (al_rival: el otro) | Sí (instantáneos) |
| `HealFromDamagePercent` | `curar_del_daño N%` | % del daño hecho | Sí (al hacer daño) |
| `CureStatus` | `curar_estado a,b` | estados (vacío = cualquiera) | Sí |
| `InflictStatus` | `poner_estado id` | estado | Sí (instantáneos) |
| `Revive` | `revivir N%` | % | Sí (al usar) |
| `RestorePp` / `RestorePpAll` | `pp N` / `pp_todos N` | PP (99+ = todos) | Sí (al usar) |
| `ChangeStage` | `etapa stat ±N` | estadística, etapas | Sí (al usar e instantáneos) |
| `MultiplyStat` | `stat stat xN` | estadística, × | Sí (siempre) |
| `CritStage` | `critico +N` | etapas | Sí (siempre) |
| `AccuracyMultiplier` / `EvasionMultiplier` | `precision xN` / `evasion xN` | × | Sí (siempre) |
| `PowerMultiplier` | `potencia xN` | × | Sí (siempre) |
| `DamageDealtMultiplier` / `DamageTakenMultiplier` | `daño xN` / `daño_recibido xN` | × | Sí (siempre / antes de golpe) |
| `SurviveAt1Hp` | `aguantar` | — | Sí (antes de golpe) |
| `ImmuneToType` | `inmune tipo` | tipo | Sí (siempre; Tierra también «no pisa el suelo») |
| `ActFirst` | `primero; prob=N` | probabilidad | Sí (siempre; una tirada por turno) |
| `ChoiceLock` / `BlockStatusMoves` | `eleccion` / `sin_movs_estado` | — | Sí (siempre) |
| `ExtendWeather` / `ExtendScreens` | `clima [id] +N` / `pantallas +N` | clima (vacío = cualquiera), turnos | Sí (siempre) |
| `Flinch` | `retroceso; prob=N` | probabilidad | Sí (instantáneos) |
| `ConsumeItem` | `gastar` | — | Sí (el objeto se gasta: Globo Helio) |
| `Catch` | `captura xN` | × (255 = siempre) | Sí (al usar, sin condiciones) |
| `Friendship` | `amistad ±N` | puntos | Sí (al usar, fuera) |
| `Evs`, `LevelUp`, `TeachMove`, `EscapeBattle`, `Repel` | `evs`, `nivel`, `enseñar`, `huir`, `repelente` | — | **Aún no** (se guardan; el editor avisa) |
| `ChangeForm` | `forma id` | forma | Al usar: las variantes funcionan por `SpeciesData.variantItem` |
| `EnableMechanic` | `mecanica id` | mecánica | Informativo: las megapiedras se leen de la regla de forma de la especie |
| `SetSideCondition` | `poner_lado id` | efecto de lado o campo | Sí (instantáneos; los campos de la 7.ª al entrar) |
| `ZMove` | `movimiento_z id` | movimiento Z | Sí (siempre; cristal Z, ver abajo) |

`EffectRules.IsSupported(bloque)` es la fuente de verdad de la columna de la derecha (la usa el editor para avisar).

## Semántica en el motor (`Battle/Domain/Turn/TurnResolver.Items.cs`)

- Solo funciona si las reglas permiten objetos equipados (`GenerationRules.HeldItems`) y el objeto no está anulado
  (Zoquete, Embargo, Zona Mágica) — `TryGetHeldItem`.
- **Instantáneos** (`RunHeld`): se ELIGEN primero todos los bloques del momento que pasan (condiciones → probabilidad),
  luego, si alguno gasta el objeto, se gasta UNA vez (`HeldItemActivatedEvent` consumido) y después se ejecutan en
  orden. Si ninguno lo gasta, se anuncia la activación antes del primer efecto que haga algo.
- **Bayas** (`IsBerry`): Nerviosismo del rival impide los bloques que gastan la baya; Gula adelanta el umbral de
  «poca vida»; Carrillo añade curación; Picotazo (`EatTargetBerry`) aplica al que la come sus bloques de poca vida y
  de estado.
- `CureStatus` con lista vacía justo al sufrir un estado = estado principal o confusión (Baya Ziuela).
- `LoseHpPercent` respeta Muro Mágico; sobre uno mismo al hacer daño se narra como retroceso (Vidasfera).
- Condiciones del lado que RECIBE el golpe: el tipo del movimiento es el real del atacante (`_hitTypeOverride`).

| Momento | Punto del turno |
|---|---|
| Pasivos | `EffectiveStat` (stats), potencia, crítico, daño hecho, precisión/evasión, inmunidades por tipo, «pisa el suelo», Elección y sin movimientos de estado (`RestrictionFor`), turnos de clima y pantallas, Garra Rápida (`OrderPlays`). |
| Antes de golpe | Por impacto: `ApplyDamageTakenBlocks` (bayas de resistencia...) y `ApplySurviveBlocks` (Banda Focus). |
| Tras golpe / contacto / al hacer daño | `ApplyGen6OnHit`, `ApplyGen6ContactEffects`, `ApplyGen6AfterAttack`. |
| Al sufrir estado | `CheckLum` (tras infligir un estado). |
| Poca vida | `CheckHeldTrigger` tras cada jugada y al final del turno. |
| Fin de turno | `ApplyEndOfTurnHeldItem` (después del clima). |
| Al entrar | `ApplyEntryHeldItem` (dentro de `ApplyOnEntry`). |

Fuera del combate, los bloques «Al usarlo» se leen a través de las vistas de `ItemDefinition` (`HealHp`,
`CuresThis`, `Revives`, `RestorePp`, `FriendshipChange`, `CatchMultiplier`, `BattleStatId/Stages`) desde
`ItemUse`, `BattleItemEffect`, `BattleSession`, `TrainerBrain` y `TeamBuilder`.

## Datos y compatibilidad

- `ItemData` = identidad + dónde se usa + `isBerry` + **`effects`** (`EffectBlockData[]`). No hay otros campos de efecto:
  el sistema antiguo (campos sueltos, `ItemExtras`) se eliminó por completo.
- `ItemMapper.Effects(d)` traduce los bloques al dominio.
- **Plantillas = el pack**: no hay hoja de plantillas aparte. Las plantillas del editor de objetos son los objetos del
  `objetos.csv` de la FUENTE elegida (el pack de la generación o tu carpeta de Excel, `PackTools.Folder`), con todos sus
  efectos. Cada objeto guarda sus efectos DENTRO: usar una plantilla los copia a la ficha.
- **Fuente para generar los packs**: `Tools/datos_fuente/objetos.csv` (los objetos clásicos con TODOS sus efectos; solo lo
  usan los generadores y el verificador, no el editor).
- **Packs**: el `objetos.csv` de cada generación trae TODOS sus objetos con TODOS sus efectos (los de la plantilla, con
  nombre, descripción y precio oficiales). Al importar un pack con «Actualizar también» cada objeto queda completo.

## Editor

- **Inspector** (`ItemDataInspector` + `EffectBlocksGui`): arriba lo común; debajo, tarjetas AGRUPADAS por momento.
  Cada tarjeta: «cuándo ▼», «qué ▼», ▲ ▼ ✕, umbral (poca vida), el parámetro con **desplegable** (tipos, estados con
  selección múltiple, estadísticas, climas, movimientos por letra, mecánicas), cantidad, a quién, condiciones
  («+ Condición», dibujadas como frase), «Se gasta», probabilidad, veces por combate y la FRASE resultante.
- **«✨ Plantillas por piezas»**: añaden bloques hechos con la pieza que cambia elegida de un desplegable (resistir un
  tipo, potenciar un tipo, curar un estado, subir una estadística con poca vida, alargar un clima, MT de un
  movimiento, Elección de una estadística, esferas, Banda Focus, Casco Dentado...). Ajustan categoría y dónde se usa.
- **«Qué hace»** (vista previa de la ventana): frases agrupadas por momento; sin «Equipado:» repetido.
- **Validador**: bola que no se usa en combate, equipable sin efectos, efectos «Al usarlo» en un objeto que no se
  puede usar, efectos que el motor aún no aplica, ids que no existen, parámetros sin elegir.

## Excel (`objetos.csv`, columna `efectos`)

```
fin_de_turno: curar 6,25%
 | antes_de_golpe [si mov.tipo=fire & propio.eficacia>1]: daño_recibido x0,5; se_gasta
 | poca_vida@25: etapa attack +1; se_gasta
 | contacto: perder 16,67%; al_rival
 | siempre: primero; prob=20
 | al_sufrir_estado: curar_estado paralysis,sleep; se_gasta; veces=1
```
Formato por bloque: `cuándo[@umbral] [si condición & condición]: acción [id] [número]; opción; opción`.
Opciones: `se_gasta`, `al_rival`, `a_si_mismo`, `prob=N`, `veces=N`. Errores explicados en español.
`EffectText.Format`/`Parse` (ida y vuelta sin pérdidas, con tests sobre la hoja de plantillas y sobre los seis packs).
`verificar_pack.py` revisa los momentos, las acciones y los ids que nombran los efectos.

## Añadir una acción nueva

1. `EffectAction`: añadir el valor **al final**.
2. Ejecutarla: en `TurnResolver.Items.Execute` (instantánea), en una consulta pasiva (`HeldProduct/HeldSum/HeldHas`
   desde el punto del turno que toque) o fuera del combate (`ItemUse` / `FieldActions`).
3. `EffectRules.IsSupported` (y `InstantActions` si es instantánea).
4. `EffectText`: etiqueta, clave Excel, tipo de id y de cantidad, `ActionsFor`, frase en `Describe`, valor por defecto.
5. Tests en `EffectBlockTests` (motor) y `EffectTextTests` (Excel).

## Habilidades: el mismo sistema

Una habilidad es **una lista de bloques**, igual que un objeto: `AbilityData` solo guarda `id`, `displayName`,
`englishName` y `effects`. El Excel es `id;nombre;nombre_en;efectos`, y el editor muestra las mismas tarjetas (con
`ability: true`: sin «Se gasta», con los momentos y acciones de habilidades y con «Especial»). No hay efectos guardados
fuera de la habilidad.

**Momentos nuevos** (también valen para objetos): `al_retirarse`, `al_absorber` (una inmunidad anula un golpe),
`al_debilitar` (debilita a un rival), `al_bajarle_stat` (el rival le baja una estadística) y `al_retroceder`.

**Acciones nuevas**: `inmune_estado`, `potencia_recibida`, `stab`, `prioridad`, `sin_bajadas`, `poner_clima`,
`inmune_clima`, `cambiar_tipo`, `daño_critico`, `prob_secundarios`, `etapas_x`, `peso`, `normal_a_tipo`, `avisar`,
`atrapar`, `atrapar_en_suelo`, `ignora_inmunidad`, `inmune_etiqueta`, `bloquea_etiqueta`, `anular` y **`especial`**.
Las listas se escriben con comas (`poner_estado poison,paralysis,sleep`), porque `|` separa bloques.

**Especiales** (`AbilityEffects.Specials`): lo que es único de una habilidad y no se descompone en piezas (Rastro,
Impostor, Ausente, Rompemoldes, Gas Reactivo, Espejo Mágico, Gula `umbral_bayas 50%`...). En Excel se escriben por su
clave: `al_entrar: rastro`, `siempre: umbral_bayas 50%`. Cada uno tiene etiqueta y ayuda en español.

**Cómo lo usa el motor** (`AbilityEffects.Read`): cada bloque con una «forma clásica» se traduce a lo que el motor ya
consulta (las propiedades de `AbilityDefinition` y `AbilityExtras`), así el combate es idéntico al de antes. Ejemplos:

| Habilidad | Bloques |
|---|---|
| Intimidación | `al_entrar: etapa attack -1; al_rival` |
| Mar Llamas | `siempre [si mov.tipo=fire & propio.vida<=33]: daño x1,5` |
| Efecto Espora | `contacto: poner_estado poison,paralysis,sleep; al_rival; prob=30` |
| Piel Seca | `siempre: inmune water \| al_absorber: curar 25% \| siempre [si mov.tipo=fire]: potencia_recibida x1,25 \| fin_de_turno [si clima=rain]: curar 12,5% \| fin_de_turno [si clima=sun]: perder 12,5%` |
| Robustez | `antes_de_golpe [si propio.vida>=100]: aguantar \| siempre: sin_ko_directo` |
| Predicción | `siempre [si clima=rain]: cambiar_tipo water \| siempre [si clima=sun]: cambiar_tipo fire \| ...` |

Los bloques que **no** tienen forma clásica (`AbilityDefinition.GenericEffects`: «al entrar: curar 10 %», un
multiplicador con probabilidad o con límite de veces...) **se ejecutan como los de un objeto equipado**
(`TurnResolver.Items`, `EffectSource`), con su probabilidad y sus veces por combate. Así el autor puede inventar
habilidades combinando piezas. `AbilityEffects.IsSupported` dice qué hace el motor con cada bloque (el editor avisa si
algo se guarda pero no tiene efecto).

**Verificación de la conversión**: las 190 habilidades se construyeron con las columnas antiguas y con los bloques
nuevos, y se compararon **todas** sus propiedades: salen idénticas. Tests: `EffectTextTests` (todas las habilidades de
todos los packs se leen, se reescriben igual, no dejan bloques genéricos y el motor las soporta) y `AbilityBlockTests`
(bloques libres en combate, veces por combate, momento «al debilitar»).

## Cristales Z y condiciones nuevas (7.ª)

Un **cristal Z** es un objeto con bloques `siempre [condiciones]: movimiento_z <id del Z>`. Las condiciones dicen qué
movimientos convierte: `mov.tipo=fire` (los genéricos), `mov.id=thunderbolt` (un movimiento concreto) y
`propio.especie=pikachu` (los exclusivos; `pikachu_` vale para todas sus formas). Ejemplos:

```
Firium Z:    siempre [si mov.tipo=fire]: movimiento_z inferno_overdrive
Pikanium Z:  siempre [si mov.id=volt_tackle & propio.especie=pikachu]: movimiento_z catastropika
```

Condiciones nuevas: `mov.id=x` (`MoveIs`) y `propio.especie=x` / `rival.especie=x` (`IsSpecies`), útiles también para
objetos y habilidades normales. La mecánica en sí (usos por combate, Pulsera Z, tabla de potencias, % que atraviesa
Protección) está en la ficha de la mecánica, cap. [06](06-combate.md).
