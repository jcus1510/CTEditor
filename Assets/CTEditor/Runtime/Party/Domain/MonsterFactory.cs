using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.SharedKernel.Abstractions;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Species;
using CTEditor.GameDefinition.Domain.Growth;
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
            IStatGrowthFormula growth,
            IReadOnlyList<Id<Move>> chosenMoves = null,
            GrowthCurve curve = null,
            IRng ivRng = null,
            Nature nature = null,
            int? fixedIv = null,
            StatSpread ivOverrides = null)
        {
            if (species == null) throw new ArgumentNullException(nameof(species));
            if (ruleset == null) throw new ArgumentNullException(nameof(ruleset));
            if (growth == null) throw new ArgumentNullException(nameof(growth));

            // 0) REGLAS DE GENERACIÓN: sin naturalezas (1.ª-2.ª gen.) todos son neutros.
            if (!ruleset.Generation.Natures) nature = null;

            // 1) Nivel recortado al tope de las reglas.
            var lvl = Level.Clamped(level, ruleset.LevelCap);

            // 1b) GENÉTICA (Lote 3).
            //     IVs, por prioridad:
            //       - fixedIv: el autor fijó un valor para TODAS las stats (típico de un entrenador
            //         diseñado: "este Gyarados tiene IVs perfectos"). Se recorta a [0, MaxIv].
            //       - ivRng: se tiran al azar 0..MaxIv por stat, como al nacer en los clásicos. El azar
            //         se INYECTA (IRng), así el dominio sigue puro y un test puede fijar la semilla.
            //       - ninguno: sin IVs (se leen como 0). Así todo lo anterior sigue igual.
            //     Si el Ruleset tiene MaxIv = 0 (juego sin IVs), nunca se generan.
            //     EVs: nacen vacíos, con los topes del Ruleset.
            var ivs = BuildIvs(species, ruleset, ivRng, fixedIv, ivOverrides);
            var efforts = new EffortValues(ruleset.MaxEvPerStat, ruleset.MaxEvTotal);

            // 2) Stats efectivos: por cada stat base de la Species, la fórmula calcula el valor del
            //    individuo a este nivel CON su genética (IVs recién decididos, EVs a 0, naturaleza).
            //    Recorremos el StatBlock base (clásicos + inventados por igual).
            var statsBuilder = new StatBlock.Builder();
            foreach (var statId in species.BaseStats.Stats)
            {
                int baseValue = species.BaseStats.Of(statId);
                int iv = ivs != null ? ivs.Of(statId) : 0;
                int naturePct = nature != null ? nature.PercentFor(statId) : 100;
                int computed = growth.Compute(statId, baseValue, lvl.Value, iv, 0, naturePct);
                statsBuilder.Set(statId, computed);
            }
            var stats = statsBuilder.Build();

            // 3) Movimientos. Si quien llama PASÓ un set elegido (el jugador en el editor/equipo),
            //    se respeta (recortado al máximo del Ruleset). Si no, se auto-derivan del learnset por
            //    nivel (lo típico para un monstruo salvaje o un rival generado por nivel).
            var moves = (chosenMoves != null && chosenMoves.Count > 0)
                ? Clamp(chosenMoves, ruleset.MaxMovesPerMonster)
                : DefaultMoves(species, lvl.Value, ruleset.MaxMovesPerMonster);

            // 4) Nace con los PS llenos.
            int maxHp = stats.Of(StatId.Hp);

            // 5) XP inicial coherente con el nivel: si conocemos la curva, arranca justo en el umbral
            //    de su nivel; si no, en 0 (AddExperience se autocorrige luego sin bajar de nivel).
            var startXp = curve != null
                ? new Experience(curve.XpToReachLevel(lvl.Value))
                : Experience.Zero;

            var created = new MonsterInstance(id, species.Id, lvl, stats, maxHp, moves, startXp, ivs, efforts, nature);
            created.SetFriendship(species.BaseFriendship); // amistad inicial de su especie
            // Habilidad 1.ª o 2.ª: como en la 3.ª gen. (que la decidía un número interno), sale de su genética
            // (paridad del IV de PS). Así no gasta tiradas de azar y los combates grabados siguen igual.
            if (species.SecondAbility.HasValue && ivs != null)
                created.SetAbilitySlot(ivs.Of(StatId.Hp) % 2);
            // GÉNERO según el % de hembras de su especie. Tampoco gasta azar: sale de su id y su genética
            // (siempre el mismo para el mismo individuo, distinto entre individuos).
            // Sin géneros en las reglas (1.ª gen.): todos sin género.
            created.SetGender(ruleset.Generation.Genders
                ? GenderText.FromRoll(species.Dex.FemalePercent, GenderRoll(id.Value, ivs))
                : Gender.Genderless);
            return created;
        }

        /// <summary>Número 0-100 estable para decidir el género (hash del id + IVs de Ataque y Defensa).</summary>
        public static float GenderRoll(string id, StatBlock ivs)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in id ?? "") { h ^= c; h *= 16777619; }
                if (ivs != null) { h ^= (uint)(ivs.Of(StatId.Attack) * 31 + ivs.Of(StatId.Defense)); h *= 16777619; }
                return (h % 10000) / 100f;
            }
        }

        // Decide los IVs según la prioridad explicada arriba. Devuelve null si no hay IVs.
        private static StatBlock BuildIvs(Species species, Ruleset ruleset, IRng ivRng, int? fixedIv, StatSpread overrides = null)
        {
            if (ruleset.MaxIv <= 0) return null;          // juego sin IVs
            bool anyOverride = overrides != null && !overrides.IsEmpty;
            if (!fixedIv.HasValue && ivRng == null && !anyOverride) return null;

            var b = new StatBlock.Builder();
            foreach (var statId in species.BaseStats.Stats)
            {
                int? chosen = anyOverride ? overrides.Of(statId) : null;   // IV escrito por el autor para ESTA estadística
                int value = chosen.HasValue ? Math.Max(0, Math.Min(ruleset.MaxIv, chosen.Value))
                    : fixedIv.HasValue ? Math.Max(0, Math.Min(ruleset.MaxIv, fixedIv.Value))
                    : ivRng != null ? ivRng.Next(0, ruleset.MaxIv + 1)   // Next es [min, max) -> +1 para incluir MaxIv
                    : 0;
                b.Set(statId, value);
            }
            return b.Build();
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

            // Recorre de más reciente a más antiguo, sin repetir movimientos (un learnset puede listar
            // el mismo movimiento a dos niveles), y se queda con los últimos N en orden de aprendizaje.
            var result = new List<Id<Move>>();
            for (int i = eligible.Count - 1; i >= 0 && result.Count < maxMoves; i--)
                if (!result.Contains(eligible[i].Move)) result.Insert(0, eligible[i].Move);

            return result;
        }

        // Recorta una lista ELEGIDA al máximo permitido (toma los primeros N, respetando el orden que
        // puso el jugador). Si ya cabe, la devuelve tal cual.
        private static IReadOnlyList<Id<Move>> Clamp(IReadOnlyList<Id<Move>> chosen, int maxMoves)
        {
            if (chosen.Count <= maxMoves) return chosen;
            var result = new List<Id<Move>>(maxMoves);
            for (int i = 0; i < maxMoves; i++)
                result.Add(chosen[i]);
            return result;
        }
    }
}
