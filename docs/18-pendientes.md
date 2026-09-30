# 18 · Pendientes y hoja de ruta

## Decididos con el autor

1. **Habilidades por bloques** (siguiente). Mismo modelo, editor y Excel que los objetos: `AbilityData.effects`,
   conversión automática de `AbilityData`/`AbilityExtras` a bloques, el motor leyendo los bloques de la habilidad en los
   mismos puntos del turno. Faltarán disparadores/acciones propios de habilidades (Intimidación = al entrar → etapa
   al rival; clima al entrar; cambiar de tipo; absorber un tipo; copiar/suprimir habilidades...). Probabilidad y veces
   por combate configurables en todos (decisión del autor).
2. **Efectos «Al usarlo» que aún no aplica el motor**: `TeachMove` (MT; si se gasta lo decide la regla
   `GenerationRules.MachinesConsumable`, ya existe), `Evs` (vitaminas / bayas reductoras), `LevelUp` (Caramelo Raro),
   `EscapeBattle` (Poké Muñeco), `Repel` + disparador `OnWalk`, y condiciones en los bloques «Al usarlo» (bolas
   especiales: Ocaso Ball de noche, Red Ball contra Agua/Bicho...). Hoy se guardan y el editor avisa.
3. **Paso 8 — 7.ª generación**: variantes de Alola como variantes enlazadas (`FormOf`), **movimientos Z** (nueva
   `MechanicKind` + Cristales Z como objetos con `EnableMechanic`), pack Gen7 (`generar_packs.py --gen 7`,
   `formas.py`, `generar_sets.py --gen 7`), reglas de la 7.ª (Megaevolución sigue activa).

## Ideas abiertas (propuestas, sin decidir)

- **Bolsillos de la mochila como fichas** (`BagPocketData`) en lugar del enum `ItemCategory`.
- Condiciones nuevas para bloques: hora del día, zona, turno del combate, especie concreta.
- Crianza (los grupos huevo ya existen).
- Efectos de movimientos y habilidades por generación (hoy el motor aplica los de la 6.ª: ver `INFORME.txt` de cada pack).

## Problemas conocidos

- La clave `"Other"` de `Etiquetas` vale «Rival» (objetivo de un efecto). El editor de objetos ya muestra «Otros» para
  `ItemCategory.Other`, pero el inspector genérico de la ficha (desplegable de categoría) aún dice «Rival».
- 8 tests fallan fuera de Unity por diseño ([15](15-pruebas-y-verificacion.md)).
- Bootstrap no se compila fuera de Unity sin *stubs* (usa APIs de Unity 6).
- El código del editor se prueba fuera de Unity con DLL de Unity 2021: probar siempre en Unity antes de dar por
  terminado un cambio visual (tarjetas de efectos, asistentes).
