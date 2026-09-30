# 18 · Pendientes y hoja de ruta

## Decididos con el autor

1. ~~Habilidades por bloques~~ ✅ (hecho: [09](09-efectos-por-bloques.md)).
2. **Efectos «Al usarlo» que aún no aplica el motor**: `TeachMove` (MT; si se gasta lo decide la regla
   `GenerationRules.MachinesConsumable`, ya existe), `Evs` (vitaminas / bayas reductoras), `LevelUp` (Caramelo Raro),
   `EscapeBattle` (Poké Muñeco), `Repel` + disparador `OnWalk`, y condiciones en los bloques «Al usarlo» (bolas
   especiales: Ocaso Ball de noche, Red Ball contra Agua/Bicho...). Hoy se guardan y el editor avisa.
3. ~~Paso 8 — 7.ª generación~~ ✅ (pack Gen7, variantes de Alola, movimientos Z). Queda de la 7.ª:
   - 18 habilidades solo con nombre: Huida/Retirada (`wimp_out`, `emergency_exit`), Remoto, Voz Fluida, Primer
     Auxilio, Banco, Fuerte Afecto, Agrupamiento, Corrosión, Regia Presencia, Revés, Pareja de Baile, Batería,
     Cuerpo Vívido, Receptor, Reacción Química, Ultraimpulso, Sistema Alfa.
   - Ultraexplosión (Necrozma), formas de Mimikyu (Disfraz) y Greninja Ash.
   - «Protector de Alola» (Guardian of Alola) se aproxima a «quita la mitad de los PS».
   - ~50 sets de Smogon descartados por formas que el pack aún no tiene.

## Ideas abiertas (propuestas, sin decidir)

- **Bolsillos de la mochila como fichas** (`BagPocketData`) en lugar del enum `ItemCategory`.
- Condiciones nuevas para bloques: hora del día, zona, turno del combate (movimiento y especie concretos ya existen).
- Crianza (los grupos huevo ya existen).
- Movimientos por bloques (el mismo sistema que objetos y habilidades para sus efectos secundarios).
- Efectos de movimientos y habilidades por generación (hoy el motor aplica los de la 6.ª: ver `INFORME.txt` de cada pack).

## Problemas conocidos

- La clave `"Other"` de `Etiquetas` vale «Rival» (objetivo de un efecto). El editor de objetos ya muestra «Otros» para
  `ItemCategory.Other`, pero el inspector genérico de la ficha (desplegable de categoría) aún dice «Rival».
- 9 tests fallan fuera de Unity por diseño ([15](15-pruebas-y-verificacion.md)).
- Bootstrap no se compila fuera de Unity sin *stubs* (usa APIs de Unity 6).
- El código del editor se prueba fuera de Unity con DLL de Unity 2021: probar siempre en Unity antes de dar por
  terminado un cambio visual (tarjetas de efectos, asistentes).
