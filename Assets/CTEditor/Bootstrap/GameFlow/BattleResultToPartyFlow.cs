using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.Battle.Domain;
using CTEditor.Party.Domain;

namespace CTEditor.Bootstrap.GameFlow
{
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
    public sealed class BattleResultToPartyFlow
    {
        private readonly Dictionary<string, Id<MonsterInstance>> _participantToMonster;

        public BattleResultToPartyFlow(Dictionary<string, Id<MonsterInstance>> participantToMonster)
        {
            _participantToMonster = participantToMonster ?? new Dictionary<string, Id<MonsterInstance>>();
        }

        /// <summary>Aplica el resultado del combate al equipo del jugador.</summary>
        public void Apply(BattleResult result, CTEditor.Party.Domain.Party party)
        {
            if (result == null || party == null) return;

            foreach (var pr in result.Participants)
            {
                // ¿Este participante corresponde a un monstruo de NUESTRO equipo? (el rival no.)
                if (!_participantToMonster.TryGetValue(pr.ParticipantId.Value, out var monsterId))
                    continue;

                var monster = FindMember(party, monsterId);
                if (monster == null)
                    continue;

                ApplyHp(monster, pr);
                // Aquí, en el futuro, también: XP ganada, estado alterado persistente, etc.
            }
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
    }
}
