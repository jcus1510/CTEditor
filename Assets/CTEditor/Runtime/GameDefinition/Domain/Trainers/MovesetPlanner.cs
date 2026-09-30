using System;
using System.Collections.Generic;
using System.Linq;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.GameDefinition.Domain.Conditions;

namespace CTEditor.GameDefinition.Domain.Trainers
{
    /// <summary>
    /// Cómo eligen sus movimientos los miembros de un entrenador cuando el autor NO los escribe.
    /// Solo se añaden valores al final (Unity guarda el número).
    /// </summary>
    public enum MovesetStyle
    {
        ByAi,      // según su IA: novato = clásico, listo = equilibrado, experto = fuerte
        Classic,   // los 4 últimos que aprende por nivel (como un salvaje: a veces flojo)
        Balanced,  // su mejor ataque con STAB + cobertura de otro tipo + un buen movimiento de apoyo
        Strong     // los ataques que más daño hacen (con cobertura) y solo apoyo si es muy bueno (dormir, Danza Espada)
    }

    /// <summary>
    /// EL PLANIFICADOR DE MOVIMIENTOS (puro): de todo lo que la especie ha podido aprender hasta su nivel,
    /// elige 4 con cabeza, como haría un jugador:
    ///   1) su mejor ataque (potencia × precisión × STAB × su mejor estadística de ataque);
    ///   2) ataques de OTROS tipos para cubrir debilidades (repetir tipo puntúa poco);
    ///   3) un movimiento de apoyo si merece la pena (dormir, paralizar, Danza Espada, curarse...);
    ///   4) el resto, lo mejor que quede.
    /// Así un entrenador generado automáticamente es un reto de verdad sin escribir movimiento a movimiento.
    /// </summary>
    /// <summary>Opciones extra del planificador.</summary>
    public sealed class PlanOptions
    {
        /// <summary>Movimientos de MT, tutor o huevo que puede usar (según el nivel de IA).</summary>
        public IEnumerable<Move> ExtraMoves;
        /// <summary>¿Busca combinaciones entre sus movimientos?</summary>
        public bool Synergies;
        /// <summary>Cuánto potencia un clima a un tipo (Lluvia → Agua ×1,5). Null = no se sabe.</summary>
        public Func<string, Id<ElementType>, float> WeatherBoost;
    }

    public static class MovesetPlanner
    {
        public static MovesetStyle Resolve(MovesetStyle style, TrainerAi ai)
            => style != MovesetStyle.ByAi ? style
             : ai == TrainerAi.Expert ? MovesetStyle.Strong
             : ai == TrainerAi.Smart ? MovesetStyle.Balanced
             : MovesetStyle.Classic;

        /// <summary>
        /// Elige hasta 4 movimientos. 'learnset' = (movimiento, nivel al que se aprende). Devuelve null con el
        /// estilo Clásico (que lo resuelva la fábrica: los 4 últimos por nivel).
        /// </summary>
        public static List<Id<Move>> Plan(IEnumerable<(Move move, int level)> learnset, IReadOnlyList<Id<ElementType>> types,
            int attack, int spAttack, int level, MovesetStyle style, int slots = 4)
            => Plan(learnset, types, attack, spAttack, level, style, slots, null);

        /// <summary>
        /// Igual, con más opciones: movimientos EXTRA (MT, tutor, huevo) y SINERGIAS. Los extra solo entran si
        /// no son desproporcionados para su nivel (potencia ≤ 30 + 2 × nivel: un Nv.10 no lleva Hiperrayo).
        /// </summary>
        public static List<Id<Move>> Plan(IEnumerable<(Move move, int level)> learnset, IReadOnlyList<Id<ElementType>> types,
            int attack, int spAttack, int level, MovesetStyle style, int slots, PlanOptions options)
        {
            if (style == MovesetStyle.Classic || style == MovesetStyle.ByAi) return null;
            var pool = (learnset ?? Enumerable.Empty<(Move, int)>())
                .Where(x => x.move != null && x.level <= level).Select(x => x.move).ToList();
            if (options?.ExtraMoves != null)
                foreach (var m in options.ExtraMoves)
                    if (m != null && m.Power <= PowerCapFor(level)) pool.Add(m);
            var candidates = pool.GroupBy(m => m.Id).Select(g => g.First()).ToList();
            if (slots < 1) return new List<Id<Move>>();
            if (candidates.Count <= slots) return candidates.Select(m => m.Id).ToList();

            types = types ?? Array.Empty<Id<ElementType>>();
            double best = Math.Max(1, Math.Max(attack, spAttack));
            var offense = candidates.ToDictionary(m => m.Id, m => Offense(m, types, attack / best, spAttack / best, level));
            var support = candidates.ToDictionary(m => m.Id, m => Support(m, attack >= spAttack));

            bool strong = style == MovesetStyle.Strong;
            var chosen = new List<Move>();
            var coveredTypes = new HashSet<Id<ElementType>>();

            // 1) El mejor ataque.
            var top = candidates.Where(m => offense[m.Id] > 0).OrderByDescending(m => offense[m.Id]).FirstOrDefault();
            if (top != null) { chosen.Add(top); coveredTypes.Add(top.Type); }
            double topScore = top != null ? offense[top.Id] : 1;

            // 2) Cobertura: ataques de tipos distintos que no sean mucho peores.
            int maxAttacks = Math.Min(slots, strong ? 3 : 2);
            while (chosen.Count(m => offense[m.Id] > 0) < maxAttacks)
            {
                var next = candidates.Where(m => !chosen.Contains(m) && offense[m.Id] > 0 && !coveredTypes.Contains(m.Type))
                    .OrderByDescending(m => offense[m.Id]).FirstOrDefault();
                if (next == null || offense[next.Id] < topScore * (strong ? 0.30 : 0.35)) break;
                chosen.Add(next); coveredTypes.Add(next.Type);
            }

            // 3) Un buen apoyo.
            var sup = candidates.Where(m => !chosen.Contains(m) && support[m.Id] > 0).OrderByDescending(m => support[m.Id]).FirstOrDefault();
            if (sup != null && chosen.Count < slots && support[sup.Id] >= (strong ? 60 : 35)) chosen.Add(sup);

            // 4) Relleno: lo mejor que quede (repetir tipo o un segundo apoyo cuenta menos).
            while (chosen.Count < slots)
            {
                Move pick = null; double pickScore = 0;
                foreach (var m in candidates)
                {
                    if (chosen.Contains(m)) continue;
                    double o = offense[m.Id], sv = support[m.Id];
                    double score;
                    if (o > 0) score = o / topScore * 100 * (coveredTypes.Contains(m.Type) ? 0.5 : 1.0);
                    else score = sv * (chosen.Any(c => support[c.Id] > 0 && offense[c.Id] <= 0) ? 0.5 : 1.0);
                    if (score > pickScore) { pickScore = score; pick = m; }
                }
                if (pick == null) break;
                chosen.Add(pick);
                if (offense[pick.Id] > 0) coveredTypes.Add(pick.Type);
            }
            // Si aún faltan (todo puntuaba 0, p. ej. Salpicadura), se completa con lo que haya.
            foreach (var m in candidates) if (chosen.Count < slots && !chosen.Contains(m)) chosen.Add(m);

            // 5) SINERGIAS: se prueban cambios de uno en uno mientras el conjunto mejore.
            if (options != null && options.Synergies && chosen.Count >= 2)
                chosen = ImproveWithSynergies(chosen, candidates, offense, support, topScore, options, attack >= spAttack);
            return chosen.Select(m => m.Id).ToList();
        }

        /// <summary>Potencia máxima de un movimiento EXTRA (MT/tutor/huevo) a ese nivel.</summary>
        public static int PowerCapFor(int level) => 30 + 2 * Math.Max(1, level);

        // ---------------- Sinergias ----------------

        private static List<Move> ImproveWithSynergies(List<Move> chosen, List<Move> candidates, Dictionary<Id<Move>, double> offense,
            Dictionary<Id<Move>, double> support, double topScore, PlanOptions options, bool physical)
        {
            // Solo los candidatos con algo que aportar (los 16 mejores por sí solos + los que forman sinergias).
            var pool = candidates.OrderByDescending(m => Math.Max(offense[m.Id] / topScore * 100, support[m.Id])).Take(16)
                .Concat(candidates.Where(m => SynergyRole(m, options) != 0)).Distinct().ToList();
            double current = SetScore(chosen, offense, support, topScore, options, physical);
            for (int pass = 0; pass < 3; pass++)
            {
                bool improved = false;
                for (int i = 0; i < chosen.Count; i++)
                {
                    Move bestSwap = null; double bestScore = current * 1.02 + 1;   // solo cambios que mejoran de verdad
                    foreach (var c in pool)
                    {
                        if (chosen.Contains(c)) continue;
                        var trial = new List<Move>(chosen) { [i] = c };
                        double sc = SetScore(trial, offense, support, topScore, options, physical);
                        if (sc > bestScore) { bestScore = sc; bestSwap = c; }
                    }
                    if (bestSwap != null) { chosen[i] = bestSwap; current = bestScore; improved = true; }
                }
                if (!improved) break;
            }
            return chosen;
        }

        // Papel de un movimiento en las sinergias (bits): para buscar pareja.
        private const int Sleeper = 1, SleepUser = 2, SelfSleeper = 4, SleepTalker = 8, WeatherSetter = 16,
                          PhysBoost = 32, SpecBoost = 64, BoostPasser = 128, SubMaker = 256;

        private static int SynergyRole(Move m, PlanOptions options)
        {
            int r = 0;
            foreach (var e in m.SecondaryEffects)
            {
                if (e.Kind == MoveEffectKind.InflictStatus && e.Status.Value == "sleep")
                    r |= e.Target == EffectTarget.Opponent ? (e.Chance.AsFraction >= 0.99f ? Sleeper : 0) : SelfSleeper;
                if (e.Kind == MoveEffectKind.SetWeather) r |= WeatherSetter;
                if (e.Kind == MoveEffectKind.ChangeStatStage && e.Target == EffectTarget.Self && e.Stages > 0 && m.Category == MoveCategory.Status)
                {
                    if (e.Stat.Value == "attack") r |= PhysBoost;
                    if (e.Stat.Value == "sp_attack") r |= SpecBoost;
                }
                if (e.Kind == MoveEffectKind.SwitchSelf && e.Stages > 0) r |= BoostPasser;
                if (e.Kind == MoveEffectKind.Substitute) r |= SubMaker;
            }
            foreach (var c in m.Requirements)
                if (c.Kind == ConditionKind.HasStatus && c.Text == "sleep")
                    r |= c.Subject == ConditionSubject.Other ? SleepUser : SleepTalker;
            return r;
        }

        private static string WeatherOf(Move m)
        {
            foreach (var e in m.SecondaryEffects) if (e.Kind == MoveEffectKind.SetWeather) return e.WeatherId;
            return null;
        }

        /// <summary>Puntuación de un CONJUNTO de movimientos (ataques, cobertura, apoyo y sinergias).</summary>
        public static double SetScore(IReadOnlyList<Move> set, Dictionary<Id<Move>, double> offense, Dictionary<Id<Move>, double> support,
            double topScore, PlanOptions options, bool physical)
        {
            var roles = set.Select(m => SynergyRole(m, options)).ToList();
            int all = roles.Aggregate(0, (a, b) => a | b);
            string weather = set.Select(WeatherOf).FirstOrDefault(w => !string.IsNullOrEmpty(w));

            double score = 0;
            var types = new HashSet<Id<ElementType>>();
            var attacks = new List<(Move m, double v)>();
            foreach (var m in set)
            {
                double v = offense[m.Id] / Math.Max(1e-6, topScore) * 100;
                // Comesueños con alguien que duerme al rival: ya no está «castigado».
                if (v > 0 && m.Requirements.Count > 0 && (all & Sleeper) != 0 && (SynergyRole(m, options) & SleepUser) != 0) v /= 0.3 * 1.1;
                // Ataques potenciados por el clima que el propio set pone (Danza Lluvia + Agua).
                if (v > 0 && weather != null && options?.WeatherBoost != null) v *= Math.Max(1f, options.WeatherBoost(weather, m.Type));
                if (v > 0 && weather != null && m.PowerModifiers.Any(pm => pm.Conditions.Any(c => c.Kind == ConditionKind.Weather && c.Text == weather))) v *= 1.4;
                if (v > 0) attacks.Add((m, v));
            }
            attacks.Sort((a, b) => b.v.CompareTo(a.v));
            for (int i = 0; i < attacks.Count; i++)
            {
                var (m, v) = attacks[i];
                score += i == 0 ? v : types.Contains(m.Type) ? v * 0.25 : v * 0.6;
                types.Add(m.Type);
            }
            if (attacks.Count == 0) score -= 100;   // sin ningún ataque no se gana

            var sups = set.Where(m => offense[m.Id] <= 0).Select(m => support[m.Id]).OrderByDescending(x => x).ToList();
            for (int i = 0; i < sups.Count; i++) score += sups[i] * (i == 0 ? 0.7 : 0.35);

            // Parejas que se potencian.
            if ((all & Sleeper) != 0 && (all & SleepUser) != 0) score += 45;          // Hipnosis + Comesueños
            if ((all & SelfSleeper) != 0 && (all & SleepTalker) != 0) score += 40;    // Descanso + Sonámbulo/Ronquido
            if ((all & PhysBoost) != 0) score += 12 * attacks.Count(a => a.m.Category == MoveCategory.Physical && a.v > 20);
            if ((all & SpecBoost) != 0) score += 12 * attacks.Count(a => a.m.Category == MoveCategory.Special && a.v > 20);
            if ((all & BoostPasser) != 0 && (all & (PhysBoost | SpecBoost)) != 0) score += 25;   // Danza + Relevo
            if ((all & SubMaker) != 0 && (all & (PhysBoost | SpecBoost | Sleeper)) != 0) score += 15;
            // Mejoras que no encajan con cómo ataca (Danza Espada en un especial) no suman.
            if ((all & PhysBoost) != 0 && !attacks.Any(a => a.m.Category == MoveCategory.Physical)) score -= 30;
            if ((all & SpecBoost) != 0 && !attacks.Any(a => a.m.Category == MoveCategory.Special)) score -= 30;
            // Dos del mismo papel sobran (dos climas, dos «duermes»).
            foreach (int role in new[] { Sleeper, WeatherSetter, SelfSleeper })
                if (roles.Count(r => (r & role) != 0) > 1) score -= 40;
            return score;
        }

        /// <summary>Valor ofensivo esperado de un movimiento para esta especie (0 = no hace daño).</summary>
        public static double Offense(Move m, IReadOnlyList<Id<ElementType>> types, double atkFactor, double spaFactor, int level)
        {
            if (m == null || !m.DealsDirectDamage) return 0;
            double power = m.Power;
            switch (m.FixedDamage)
            {
                case FixedDamageKind.UserLevel: power = Math.Max(40, level); break;
                case FixedDamageKind.Fixed: power = Math.Max(20, m.FixedDamageAmount * 1.2); break;
                case FixedDamageKind.HalfTargetHp: power = 60; break;
                case FixedDamageKind.OneHitKo: power = 45; break;
                case FixedDamageKind.ReturnPhysical: case FixedDamageKind.ReturnSpecial: case FixedDamageKind.Bide: power = 35; break;
            }
            if (m.PowerFormula != null) power = Math.Max(power, 60);
            if (power <= 0) return 0;

            double hits = (m.MinHits + m.MaxHits) / 2.0;
            double acc = m.NeverMisses ? 1.0 : m.Accuracy.Value.AsFraction;
            double stab = types.Contains(m.Type) ? 1.5 : 1.0;
            // Las fórmulas del daño multiplican por la estadística: un físico con poco Ataque rinde menos.
            double stat = m.FixedDamage != FixedDamageKind.None ? 1.0 : m.Category == MoveCategory.Physical ? atkFactor : spaFactor;
            double penalty = 1.0;
            if (m.TwoTurn != TwoTurnKind.None) penalty *= 0.55;
            if (m.Priority > 0) penalty *= 1.05;
            double bonus = 0;
            foreach (var e in m.SecondaryEffects)
            {
                double chance = e.Chance.AsFraction;
                switch (e.Kind)
                {
                    case MoveEffectKind.Recoil: penalty *= 1 - e.Amount.AsFraction * 0.5; break;
                    case MoveEffectKind.RecoilMaxHp: penalty *= e.Amount.AsFraction >= 0.99 ? 0.25 : 0.8; break;  // Explosión
                    case MoveEffectKind.Rampage: penalty *= 0.85; break;
                    case MoveEffectKind.Drain: bonus += 0.15; break;
                    case MoveEffectKind.InflictStatus when e.Target == EffectTarget.Opponent: bonus += chance * 0.5; break;
                    case MoveEffectKind.Flinch: bonus += chance * 0.3; break;
                    case MoveEffectKind.ChangeStatStage when e.Target == EffectTarget.Opponent && e.Stages < 0: bonus += chance * 0.2; break;
                    case MoveEffectKind.ChangeStatStage when e.Target == EffectTarget.Self && e.Stages < 0: penalty *= 0.85; break;
                    case MoveEffectKind.SwitchSelf: bonus += 0.05; break;
                }
            }
            if (m.Requirements.Count > 0) penalty *= 0.3;   // Comesueños: solo con el rival dormido
            return power * hits * acc * stab * stat * penalty * (1 + bonus);
        }

        /// <summary>Valor de apoyo (0-100) de un movimiento sin daño directo.</summary>
        public static double Support(Move m, bool physicalAttacker)
        {
            if (m == null || m.DealsDirectDamage) return 0;
            double acc = m.NeverMisses ? 1.0 : m.Accuracy.Value.AsFraction;
            double best = 0;
            foreach (var e in m.SecondaryEffects)
            {
                double v = 0;
                switch (e.Kind)
                {
                    case MoveEffectKind.InflictStatus when e.Target == EffectTarget.Opponent:
                        string st = e.Status.Value ?? "";
                        v = st == "sleep" ? 90 : st == "paralysis" ? 70 : st == "toxic" ? 60 : st == "burn" ? (physicalAttacker ? 45 : 55)
                          : st == "poison" ? 45 : st == "confusion" ? 40 : st == "leech_seed" ? 50 : st == "trapped" ? 20 : 30;
                        v *= acc;
                        break;
                    case MoveEffectKind.ChangeStatStage when e.Target == EffectTarget.Self && e.Stages > 0:
                        string stat = e.Stat.Value ?? "";
                        bool myAttack = physicalAttacker ? stat == "attack" : stat == "sp_attack";
                        v = myAttack ? (e.Stages >= 2 ? 75 : 50) : stat == "speed" ? (e.Stages >= 2 ? 45 : 30)
                          : stat == "evasion" ? 35 : (e.Stages >= 2 ? 35 : 25);
                        break;
                    case MoveEffectKind.ChangeStatStage when e.Target == EffectTarget.Opponent && e.Stages < 0:
                        v = (e.Stat.Value == "accuracy" ? 25 : Math.Abs(e.Stages) >= 2 ? 30 : 15) * acc;
                        break;
                    case MoveEffectKind.HealSelf: case MoveEffectKind.Heal when e.Target == EffectTarget.Self:
                        v = e.Amount.AsFraction >= 0.5 ? 55 : 35; break;
                    case MoveEffectKind.SetSideCondition: v = 40; break;
                    case MoveEffectKind.Substitute: v = 40; break;
                    case MoveEffectKind.SetHazard: v = 35; break;
                    case MoveEffectKind.CritBoost: v = 25; break;
                    case MoveEffectKind.DisableMove: case MoveEffectKind.Encore: v = 30; break;
                    case MoveEffectKind.SetWeather: v = 25; break;
                    case MoveEffectKind.Transform: case MoveEffectKind.CallRandomMove: v = 20; break;
                    case MoveEffectKind.ForceSwitch: case MoveEffectKind.Teleport: v = 10; break;
                    default: v = 15; break;
                }
                best = Math.Max(best, v);
            }
            if (m.SecondaryEffects.Count == 0) best = 0;   // Salpicadura: nada
            if (m.Requirements.Count > 0) best *= 0.3;
            return best;
        }
    }
}
