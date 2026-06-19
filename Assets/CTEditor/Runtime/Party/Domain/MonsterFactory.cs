using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Species;
using CTEditor.GameDefinition.Domain.Rules;

namespace CTEditor.Party.Domain
{
    /// <summary>
    /// FACTORY (Parte B): encapsula la creación COMPLEJA de un MonsterInstance. Crear un individuo
    /// no es solo "new": hay que calcular sus stats desde la base con la fórmula, recortar el nivel
    /// al tope del Ruleset, elegir sus movimientos de arranque y llenarle los PS. El factory reúne
    /// todo eso en un solo lugar para que nadie lo haga a medias.
    ///
    /// Fíjate que el id del individuo se RECIBE como parámetro en vez de generarlo aquí con algo
    /// como Guid.NewGuid(). ¿Por qué? Para que el dominio siga siendo PURO y DETERMINISTA: generar
    /// ids al azar adentro es la misma clase de impureza que UnityEngine.Random o DateTime.Now (J.2).
    /// Quien llama (la capa de aplicación/Bootstrap en producción, o un test) decide de dónde sale
    /// el id. En un test puedes pasar uno fijo y todo es reproducible.
    /// </summary>
    public static class MonsterFactory
    {
        public static MonsterInstance Create(
            Id<MonsterInstance> id,
            Species species,
            int level,
            Ruleset ruleset,
            IStatGrowthFormula growth)
        {
            if (species == null) throw new ArgumentNullException(nameof(species));
            if (ruleset == null) throw new ArgumentNullException(nameof(ruleset));
            if (growth == null) throw new ArgumentNullException(nameof(growth));

            // 1) Nivel recortado al tope de las reglas.
            var lvl = Level.Clamped(level, ruleset.LevelCap);

            // 2) Stats efectivos: por cada stat base de la Species, la fórmula calcula el valor del
            //    individuo a este nivel. Recorremos el StatBlock base (clásicos + inventados por igual)
            //    y construimos un StatBlock nuevo con los resultados.
            var statsBuilder = new StatBlock.Builder();
            foreach (var statId in species.BaseStats.Stats)
            {
                int baseValue = species.BaseStats.Of(statId);
                int computed = growth.Compute(statId, baseValue, lvl.Value);
                statsBuilder.Set(statId, computed);
            }
            var stats = statsBuilder.Build();

            // 3) Movimientos de arranque: los más recientes que ya aprendió a este nivel, hasta el
            //    máximo que permite el Ruleset.
            var moves = DefaultMoves(species, lvl.Value, ruleset.MaxMovesPerMonster);

            // 4) Nace con los PS llenos.
            int maxHp = stats.Of(StatId.Hp);

            return new MonsterInstance(id, species.Id, lvl, stats, maxHp, moves);
        }

        // Toma del learnset los movimientos cuyo nivel de aprendizaje es <= nivel actual, los ordena
        // por nivel, y se queda con los ÚLTIMOS 'maxMoves' (los más recientes). Es la lógica clásica
        // del "set de movimientos inicial".
        private static IReadOnlyList<Id<Move>> DefaultMoves(Species species, int level, int maxMoves)
        {
            var eligible = new List<LearnableMove>();
            foreach (var lm in species.Learnset)
                if (lm.Level <= level)
                    eligible.Add(lm);

            // Ordena ascendente por nivel. La lambda (a, b) => ... es el criterio de comparación.
            eligible.Sort((a, b) => a.Level.CompareTo(b.Level));

            var result = new List<Id<Move>>();
            int start = Math.Max(0, eligible.Count - maxMoves); // ventana de los últimos N
            for (int i = start; i < eligible.Count; i++)
                result.Add(eligible[i].Move);

            return result;
        }
    }
}
