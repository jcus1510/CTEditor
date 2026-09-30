# 01 · Visión y premisas

## Qué es CTEditor

CTEditor es un **motor de combates por turnos al estilo Pokémon + un editor completo** dentro de **Unity 6
(6000.3.7f1)**. Con él un autor crea su juego (especies, movimientos, tipos, estados, habilidades, objetos,
entrenadores, zonas, menús, reglas...) **sin programar**, desde ventanas del editor o desde hojas de Excel, y lo prueba
con el motor real (escena de pruebas, simulador, calculadora de daño, torneo de IAs).

- Repositorio: `jcus1510/CTEditor` · rama de trabajo **`develop`**.
- Idioma de la interfaz del editor y del juego: **español** (preparado para traducirse, ver [16](16-convenciones.md)).

## La premisa: TODO es editable

> «Estancar todo en unas generaciones específicas va en contra de la personalización total.»

Consecuencias de diseño que se repiten en todo el proyecto:

1. **Las generaciones son DATOS y REGLAS, nunca código.** No hay `if (gen == 5)`. Una generación es:
   - un **pack** de contenido (`Assets/GameContent/Packs/GenN`), y
   - una **plantilla de reglas** (`GenerationRules`: categoría por tipo, «Especial» único, habilidades, objetos
     equipados, naturalezas, géneros, MT que se gastan) + **mecánicas** activables (Megaevolución...).
   El autor puede mezclar (1.ª gen. con habilidades, 6.ª sin megas...).
2. **Las piezas son genéricas y combinables.** Un objeto no «es Baya Ziuela»: es «al sufrir un estado → curar
   cualquier estado → se gasta». Un estado no es un `if`: es una ficha con daño por turno, probabilidad de impedir la
   acción, duración, etc. Los tipos, las estadísticas, las naturalezas y las curvas son contenido, no enums.
3. **Desplegables, no ids a mano.** Donde el autor elige algo que existe (un tipo, un estado, un movimiento), el
   editor ofrece la lista del proyecto.
4. **Nada se pierde.** Borrar = mover a la **papelera** (recuperable con todas sus referencias); importar = copia de
   seguridad antes; cambiar de generación = copia + papelera en grupo.
5. **El motor manda.** Lo que el editor enseña (vista previa, calculadora, «Sugerir según la IA», simulador) usa el
   MISMO código que el juego, así que no hay sorpresas.
6. **El juego nunca se rompe por contenido incompleto.** Lo que no existe se ignora; el **validador** lo avisa.

## Qué hay hoy (resumen)

- Combate 1 contra 1 completo: fórmula clásica, críticos configurables, estados principales y volátiles, climas,
  campos, trampas, efectos de lado, habilidades, objetos, formas, Megaevolución, cambios, captura, XP y EVs.
- 7 niveles de IA (Novato → Injusto) con memoria del rival, predicción, sets de Smogon y megas.
- Partida: equipo, PC, mochila, dinero, derrota, evoluciones, aprender movimientos, acciones fuera del combate.
- Editor: ~30 ventanas en español, Centro de Contenido, papelera, validador, Excel, Showdown, asistente «Cambiar de
  generación», packs Gen1…Gen7 fieles a su generación.
- Interfaz del juego: menús y caja de texto editables (o diseñados en la escena), controles configurables.

Lo pendiente está en [18](18-pendientes.md).
