# 17 · Historial

Resumen de lo construido, en orden (ver `git log` para el detalle).

## Base (PR #1-#16 en GitHub)

SharedKernel → definiciones (stats, tipos, movimientos, catálogo, reglas, especies) → fichas de Unity + mappers →
Party (instancias, fábrica, niveles) → Battle (fórmula de daño, acciones, eventos, turno) → crecimiento clásico →
tests EditMode → Eventing y contratos → bootstrap y escena de prueba → IA básica → primeras ventanas de editor
(movimientos, estados, habilidades).

## Lotes (antes de la serie de pasos)

- **Lote 1-4**: XP, curvas (6 clásicas + personalizadas), EVs, IVs, naturalezas, fórmula clásica verificada.
- **Lote A**: condiciones genéricas, potencia con modificadores y fórmula, estadísticas del daño, críticos
  configurables, curar a otros, clima, amistad, dado compartido.
- **Lote B**: estados volátiles (atrapar, drenadoras, protección, aguante) apilables con el principal.
- **Cierre del combate**: efectos de lado, Sustituto, Niebla, Foco Energía, Anulación, Otra Vez, Contraataque, Manto
  Espejo, Venganza, Saña, Furia, Metrónomo, Espejo, Mimético, Transformación, Conversión, trampas y cambios forzados.
- **Lote D**: Pokédex, movimientos con requisitos, planificador de movimientos, tablas de tipos por época, zoom/filtros.
- **Lote E**: mecánicas de 3.ª-4.ª gen., niveles de IA, curación inteligente, «Preparar un reto», MT/tutor/huevo, grupos huevo.
- **Lote F**: IAs Maestro e Injusto, memoria y predicción, género, 5.ª-6.ª gen. (objetos de competición, campos, Zona
  Mágica, Escudo Real, Alas Vendaval, Piel Feérica, Rivalidad, Baba...).
- Interfaz: menús con gráficos propios, mapa de menús, controles y mando, caja de texto.
- Excel robusto (un fallo no bloquea), ids de respaldo y reenlazado, papelera.
- Packs Gen1…Gen6 fieles; «✨ Sugerir según la IA»; plantillas de entrenadores; objetos de cada generación; torneo de IAs.

## Plan de 8 pasos

| Paso | Qué | Estado |
|---|---|---|
| 1 | Reglas de generación y fichas de mecánica en el Ruleset | ✅ |
| 2 | Formas de combate, variantes enlazadas y Árbol de familia | ✅ |
| 3 | Megaevolución (motor, jugador, IA por nivel, 48 megas de la 6.ª) | ✅ |
| 4 | EVs, IVs y habilidad elegida por miembro de equipo | ✅ |
| 5 | Importar/exportar en formato Showdown + nombres en inglés | ✅ |
| 6 | Sets de competición de Smogon (IA por nivel, fijo o cambiante, selector) | ✅ |
| 7 | Asistente «Cambiar de generación» (papelera por grupos, copia, Adaptar a las reglas) | ✅ |
| 8 | 7.ª generación (variantes de Alola, movimientos Z, pack Gen7) | ⏳ |

## Después del paso 7

- **Arreglo crítico**: `Handles.DrawDashedLine` (no existe en Unity) impedía compilar el editor desde el paso 2; Unity
  seguía con el código viejo. Cambiado a `DrawDottedLine` y creada `Tools/compilar_unity` (compila y prueba TODO sin
  Unity). La detección de CSV por cabeceras ya no adivina si la mayoría de columnas no encajan.
- **Objetos por EFECTOS** (bloques «cuándo / si / qué»): dominio, motor, datos, editor con tarjetas y desplegables,
  plantillas por piezas, Excel `efectos`, conversión automática de objetos antiguos, regla «las MT se gastan». Ver [09](09-efectos-por-bloques.md).
- **Documentación** completa por capítulos (esta carpeta).
