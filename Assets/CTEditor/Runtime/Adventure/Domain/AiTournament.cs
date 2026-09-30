using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.Abstractions;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Trainers;
using CTEditor.Battle.Domain;
using CTEditor.Battle.Domain.Actions;
using CTEditor.Battle.Domain.AI;

namespace CTEditor.Adventure.Domain
{
    /// <summary>Resultado de un duelo entre dos IAs (desde el punto de vista de la IA «A»).</summary>
    public sealed class DuelResult
    {
        public int Wins, Losses, Draws;
        public long Turns;
        public int Games => Wins + Losses + Draws;
        /// <summary>% de victorias de A (un empate cuenta medio).</summary>
        public double WinRate => Games == 0 ? 0 : (Wins + 0.5 * Draws) / Games;
        public double AverageTurns => Games == 0 ? 0 : (double)Turns / Games;
        public readonly List<string> Problems = new List<string>();
    }

    /// <summary>
    /// TORNEO DE IAs: enfrenta dos IAs con el MISMO equipo (combate espejo) muchas veces, con el motor real, y
    /// mide quién gana. Sirve para comprobar con números que cada nivel es más fuerte que el anterior.
    ///
    /// Cómo se juega cada combate: una IA es el RIVAL (TrainerBrain completo: objetos, cambios, predicción, memoria)
    /// y la otra hace de «jugador» con el mismo tipo de DECISIÓN de su nivel (al azar / más fuerte / experto), pero
    /// sin objetos ni cambios voluntarios. Para que sea justo, la mitad de los combates se juegan con los lados
    /// cambiados. Cada IA arma su equipo según su bloque 🎒 (movimientos MT/tutor/huevo, objetos, entrenamiento).
    /// </summary>
    public static class AiTournament
    {
        /// <summary>Turnos máximos de un combate: si nadie gana antes, es empate.</summary>
        public const int MaxTurns = 300;

        /// <summary>
        /// Un duelo de 'games' combates entre la IA de 'a' y la de 'b', con el equipo de 'team' (se usa igual en
        /// los dos lados). Devuelve el resultado de A. 'onProgress' (0-1) sirve para una barra de progreso; si
        /// devuelve true, se cancela.
        /// </summary>
        public static DuelResult Duel(GameData data, TrainerDefinition team, (int level, string profileId) a, (int level, string profileId) b,
            int games, int seed, Func<float, bool> onProgress = null)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (team == null) throw new ArgumentNullException(nameof(team));
            var result = new DuelResult();
            var ta = WithAi(team, "A", a.level, a.profileId);
            var tb = WithAi(team, "B", b.level, b.profileId);
            var rng = new SeededRng(seed);
            for (int g = 0; g < games; g++)
            {
                if (onProgress != null && onProgress((float)g / Math.Max(1, games))) break;
                bool aIsRival = g % 2 == 0;   // mitad y mitad: así la ventaja del lado se compensa
                try
                {
                    var (outcome, turns) = Play(data, aIsRival ? tb : ta, aIsRival ? ta : tb, rng);
                    result.Turns += turns;
                    if (outcome == BattleOutcome.InProgress) result.Draws++;
                    else
                    {
                        bool playerWon = outcome == BattleOutcome.PlayerWon;
                        bool aWon = aIsRival ? !playerWon : playerWon;
                        if (aWon) result.Wins++; else result.Losses++;
                    }
                }
                catch (Exception e)
                {
                    if (result.Problems.Count < 5) result.Problems.Add(e.Message);
                    result.Draws++;
                }
            }
            return result;
        }

        /// <summary>Una copia del entrenador con otra IA (mismo equipo). 'profileId' vacío = la de su nivel.</summary>
        public static TrainerDefinition WithAi(TrainerDefinition t, string suffix, int level, string profileId)
            => new TrainerDefinition(t.Id + "_" + suffix, t.DisplayName, t.Team, t.TrainerClass, t.Ai, t.BaseMoney,
                t.IntroLine, t.DefeatLine, t.VictoryLine, t.AiSettings, level, profileId ?? "");

        /// <summary>
        /// Un combate: 'player' lo pilota su decisión de movimientos; 'rival' es un TrainerBrain completo.
        /// Devuelve el resultado (InProgress = empate por límite de turnos) y los turnos jugados.
        /// </summary>
        public static (BattleOutcome outcome, int turns) Play(GameData data, TrainerDefinition player, TrainerDefinition rival, IRng rng)
        {
            // Partida nueva en cada combate: la memoria de la IA (revanchas) no se arrastra entre combates.
            var save = new PlayerSave(data.Ruleset, "Torneo");
            foreach (var mon in TeamBuilder.TrainerTeam(player, data, rng)) save.Receive(mon, true);
            if (!save.CanBattle) throw new InvalidOperationException($"El equipo de '{player.Id}' no tiene ningún monstruo válido.");

            var s = BattleSession.Against(data, save, rival, rng);
            s.Begin();
            var profile = data.AiProfileFor(player);
            var pilot = Pilot(profile, data, s, rng);
            var random = new SimpleBattleAI(rng) { CanUse = s.Resolver.CanChooseMove };

            int turns = 0, stuck = 0, guard = 0;
            while (s.Phase != SessionPhase.Finished && turns < MaxTurns && stuck < 20 && guard++ < MaxTurns * 20)
            {
                switch (s.Phase)
                {
                    case SessionPhase.ChooseAction:
                        turns++;
                        var ai = profile.MistakePercent > 0 && rng.NextFloat() * 100f < profile.MistakePercent ? random : pilot;
                        stuck = SubmitAction(s, ai) ? 0 : stuck + 1;
                        break;
                    case SessionPhase.ChooseReplacement:
                        stuck = Replace(s) ? 0 : stuck + 1;
                        break;
                    case SessionPhase.LearnMove:
                        s.AnswerLearnMove(-1);
                        break;
                    case SessionPhase.Evolution:
                        s.AnswerEvolution(false);
                        break;
                    default:
                        stuck++;
                        break;
                }
            }
            return (s.Phase == SessionPhase.Finished ? s.Outcome : BattleOutcome.InProgress, turns);
        }

        // La decisión de movimientos del nivel, para el lado del «jugador».
        private static IBattleAI Pilot(AiProfile profile, GameData data, BattleSession s, IRng rng)
        {
            switch (profile.Brain)
            {
                case MoveBrain.Random: return new SimpleBattleAI(rng) { CanUse = s.Resolver.CanChooseMove };
                case MoveBrain.Aggressive: return new AggressiveBattleAI(rng, data.Moves, data.TypeChart) { CanUse = s.Resolver.CanChooseMove };
                default: return new ExpertBattleAI(rng, data.Moves, s.Resolver, s.Battle);
            }
        }

        private static bool SubmitAction(BattleSession s, IBattleAI ai)
        {
            var self = s.Battle.Player;
            var foe = s.Battle.Enemy;
            PlayerChoice choice = null;
            var action = ai.ChooseAction(self, foe);
            if (action is UseMove use)
            {
                for (int i = 0; i < self.Moves.Count; i++)
                    if (self.Moves[i].Equals(use.Move)) { choice = PlayerChoice.Fight(i); break; }
            }
            else if (action is SwitchMonster sw)
            {
                var members = s.Battle.PlayerTeam.Members;
                for (int i = 0; i < members.Count; i++)
                    if (members[i].Id.Equals(sw.Target)) { choice = PlayerChoice.Switch(i); break; }
            }
            if (choice != null && s.Submit(choice).Accepted) return true;
            // Lo que eligió no vale (sin PP, bloqueado...): el primer movimiento que se acepte.
            for (int i = 0; i < Math.Max(1, self.Moves.Count); i++)
                if (s.Submit(PlayerChoice.Fight(i)).Accepted) return true;
            return false;
        }

        private static bool Replace(BattleSession s)
        {
            var members = s.Battle.PlayerTeam.Members;
            for (int i = 0; i < members.Count; i++)
                if (!members[i].IsFainted && s.ChooseReplacement(i).Accepted) return true;
            return false;
        }

        /// <summary>Azar con semilla (mismo número = mismos combates: resultados repetibles).</summary>
        private sealed class SeededRng : IRng
        {
            private readonly Random _r;
            public SeededRng(int seed) => _r = new Random(seed);
            public int Next(int minInclusive, int maxExclusive) => _r.Next(minInclusive, maxExclusive);
            public float NextFloat() => (float)_r.NextDouble();
        }
    }
}
