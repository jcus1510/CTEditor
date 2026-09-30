using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.Battle.Domain;
using CTEditor.Party.Domain;
using CTEditor.GameDefinition.Domain.Species;
using CTEditor.GameDefinition.Domain.Growth;
using CTEditor.GameDefinition.Domain.Catalog;

namespace CTEditor.Bootstrap.GameFlow
{
    /// <summary>Anuncio de subida de nivel para la presentación ("¡X subió al Nv. 12!").</summary>
    public readonly struct LevelUpInfo
    {
        /// <summary>Id del monstruo (== id de participante, tal como lo armó el PartyHolder).</summary>
        public string MonsterId { get; }
        public int LevelsGained { get; }
        public int NewLevel { get; }

        /// <summary>Si además EVOLUCIONÓ, la especie nueva (id). Null = no evolucionó.</summary>
        public string EvolvedTo { get; }

        public LevelUpInfo(string monsterId, int levelsGained, int newLevel, string evolvedTo = null)
        {
            EvolvedTo = evolvedTo;
            MonsterId = monsterId;
            LevelsGained = levelsGained;
            NewLevel = newLevel;
        }
    }
    /// <summary>
    /// La mitad de "resultados-out" del ciclo de combate (J.5). Battle nunca tocó a Party: trabajó
    /// sobre SNAPSHOTS y, al terminar, entregó un BattleResult con el estado final de cada
    /// participante (por su id de combate). Este flujo, que vive en el composition root y por eso SÍ
    /// puede conocer ambos contextos, traduce esos resultados de vuelta a los MonsterInstance reales
    /// del equipo, respetando sus invariantes (no se tocan PS por debajo de 0, etc.).
    ///
    /// El mapeo participante-de-combate -> monstruo-del-equipo se arma al crear los snapshots (cuando
    /// el orquestador sabe de qué MonsterInstance salió cada BattleParticipant) y se conserva hasta
    /// aquí. Así el id de combate (efímero) nunca se filtra a Party; solo se usa para reencontrar al
    /// individuo correcto.
    /// </summary>
    [System.Obsolete("Sustituido por CTEditor.Adventure.Domain.BattleSession, que aplica todo a la partida (niveles, capturas, dinero, evoluciones).")]
    public sealed class BattleResultToPartyFlow
    {
        private readonly Dictionary<string, Id<MonsterInstance>> _participantToMonster;
        private readonly IReadOnlyDictionary<string, Species> _speciesByMonster; // para stats base + curva
        private readonly ICatalog<GrowthCurve> _curves;                          // curvas autoradas
        private readonly IStatGrowthFormula _growth;                             // recálculo de stats
        private readonly ICatalog<Species> _allSpecies;                          // para evolucionar (opcional)
        private readonly System.Action<string, Species> _onEvolved;             // avisa a quien guarda las especies

        // Curva por defecto si la especie no eligió una (la "normal" de los clásicos).
        private static GrowthCurve _defaultCurve;
        private static GrowthCurve DefaultCurve =>
            _defaultCurve ?? (_defaultCurve = GrowthCurvePresets.MediumFast(
                new Id<GrowthCurve>("default_medium_fast"), "Medium Fast (default)", 100));

        public BattleResultToPartyFlow(
            Dictionary<string, Id<MonsterInstance>> participantToMonster,
            IReadOnlyDictionary<string, Species> speciesByMonster = null,
            ICatalog<GrowthCurve> curves = null,
            IStatGrowthFormula growth = null,
            ICatalog<Species> allSpecies = null,
            System.Action<string, Species> onEvolved = null)
        {
            _allSpecies = allSpecies;
            _onEvolved = onEvolved;
            _participantToMonster = participantToMonster ?? new Dictionary<string, Id<MonsterInstance>>();
            _speciesByMonster = speciesByMonster;
            _curves = curves;
            _growth = growth;
        }

        /// <summary>
        /// Aplica el resultado del combate al equipo del jugador (PS, estado y XP/nivel), y devuelve
        /// las subidas de nivel para que la presentación las anuncie.
        /// </summary>
        public IReadOnlyList<LevelUpInfo> Apply(BattleResult result, CTEditor.Party.Domain.Party party)
        {
            var levelUps = new List<LevelUpInfo>();
            if (result == null || party == null) return levelUps;

            foreach (var pr in result.Participants)
            {
                // ¿Este participante corresponde a un monstruo de NUESTRO equipo? (el rival no.)
                if (!_participantToMonster.TryGetValue(pr.ParticipantId.Value, out var monsterId))
                    continue;

                var monster = FindMember(party, monsterId);
                if (monster == null)
                    continue;

                ApplyHp(monster, pr);
                ApplyStatus(monster, pr);
                if (pr.FinalPp != null) monster.SetCurrentPp(pr.FinalPp); // los PP gastados persisten
                monster.SetHeldItem(pr.FinalHeldItem);                     // la baya comida ya no está
            }

            // EVs ganados (Lote 3): ANTES que la XP, así si además sube de nivel, el recálculo de
            // stats de la subida ya incluye los EVs nuevos. Como en Gen V+, los stats se recalculan
            // al terminar CADA combate (no hay que esperar a subir de nivel para notar los EVs).
            ApplyEffort(result, party);

            // XP ganada: se aplica DESPUÉS de PS/estado, con la curva de cada especie. El propio
            // MonsterInstance sube de nivel y recalcula sus stats (dominio puro; aquí solo cableamos).
            ApplyExperience(result, party, levelUps);

            return levelUps;
        }

        // Suma los EVs de cada participante (MonsterInstance respeta los topes del Ruleset) y luego
        // recalcula UNA vez los stats de cada monstruo que ganó algo.
        private void ApplyEffort(BattleResult result, CTEditor.Party.Domain.Party party)
        {
            if (_speciesByMonster == null || result.EvAwards == null || result.EvAwards.Count == 0) return;
            var growth = _growth ?? new ClassicStatGrowthFormula();
            var touched = new HashSet<string>();

            foreach (var award in result.EvAwards)
            {
                if (award.Amount <= 0) continue;
                if (!_participantToMonster.TryGetValue(award.ParticipantId.Value, out var monsterId))
                    continue;

                var monster = FindMember(party, monsterId);
                if (monster == null) continue;

                if (monster.AddEffort(award.Stat, award.Amount) > 0)
                    touched.Add(monsterId.Value);
            }

            foreach (var id in touched)
            {
                var monster = FindMember(party, new Id<MonsterInstance>(id));
                if (monster != null && _speciesByMonster.TryGetValue(id, out var species) && species != null)
                    monster.RecomputeStats(species.BaseStats, growth);
            }
        }

        private void ApplyExperience(BattleResult result, CTEditor.Party.Domain.Party party, List<LevelUpInfo> levelUps)
        {
            if (_speciesByMonster == null || result.XpAwards == null) return;
            var growth = _growth ?? new ClassicStatGrowthFormula();

            foreach (var award in result.XpAwards)
            {
                if (award.Amount <= 0) continue;
                if (!_participantToMonster.TryGetValue(award.ParticipantId.Value, out var monsterId))
                    continue;

                var monster = FindMember(party, monsterId);
                if (monster == null) continue;
                if (!_speciesByMonster.TryGetValue(monsterId.Value, out var species) || species == null)
                    continue; // sin la especie no hay stats base para recalcular

                var curve = ResolveCurve(species);
                var res = monster.AddExperience(award.Amount, curve, species.BaseStats, growth);
                if (!res.LeveledUp) continue;

                // EVOLUCIÓN al subir de nivel (por nivel o por amistad), si hay catálogo de especies.
                string evolvedTo = null;
                var evo = _allSpecies != null ? EvolutionRules.Find(monster, species, EvolutionTrigger.LevelUp) : null;
                if (evo != null && _allSpecies.TryGet(evo.Target, out var next))
                {
                    monster.Evolve(next.Id, next.BaseStats, growth);
                    evolvedTo = next.Id.Value;
                    _onEvolved?.Invoke(monsterId.Value, next);
                }
                levelUps.Add(new LevelUpInfo(monsterId.Value, res.LevelsGained, res.NewLevel, evolvedTo));
            }
        }

        // La curva de la especie (por id, desde el catálogo autorado) o la por defecto.
        private GrowthCurve ResolveCurve(Species species)
        {
            if (species.GrowthCurveId.HasValue && _curves != null &&
                _curves.TryGet(species.GrowthCurveId.Value, out var curve))
                return curve;
            return DefaultCurve;
        }

        private static MonsterInstance FindMember(CTEditor.Party.Domain.Party party, Id<MonsterInstance> id)
        {
            foreach (var m in party.Members)
                if (m.Id == id)
                    return m;
            return null;
        }

        // Lleva los PS del individuo a su valor final del combate, usando solo las mutaciones públicas
        // del MonsterInstance (que protegen sus límites). Calculamos el delta y curamos o dañamos.
        private static void ApplyHp(MonsterInstance monster, ParticipantResult pr)
        {
            int target = pr.Fainted ? 0 : pr.FinalHp;
            int delta = target - monster.CurrentHp;

            if (delta < 0)
                monster.TakeDamage(-delta);
            else if (delta > 0)
                monster.Heal(delta);
        }

        // Persiste el estado alterado final. Un debilitado queda sin estado; si terminó sano (porque
        // se curó en combate) también se limpia. Los estados volátiles (confusión) no llegan aquí.
        private static void ApplyStatus(MonsterInstance monster, ParticipantResult pr)
        {
            if (pr.Fainted)
            {
                monster.ClearStatus();
                return;
            }

            if (pr.FinalStatus.HasValue)
                monster.SetStatus(pr.FinalStatus.Value);
            else
                monster.ClearStatus();
        }
    }
}
