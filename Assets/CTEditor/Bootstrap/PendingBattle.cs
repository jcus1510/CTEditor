using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Encounters;
using CTEditor.GameDefinition.Domain.Trainers;
using SpeciesDef = CTEditor.GameDefinition.Domain.Species.Species;

namespace CTEditor.Bootstrap
{
    /// <summary>
    /// QUÉ combate hay que lanzar: contra un entrenador, un salvaje de una zona o una especie concreta.
    /// Lo crea quien dispara el combate (un NPC, la hierba alta, el laboratorio de pruebas) y lo consume
    /// la pantalla de combate. El equipo del jugador no va aquí: sale siempre de su partida.
    /// </summary>
    public sealed class BattleRequest
    {
        public TrainerDefinition Trainer { get; private set; }
        public EncounterZone Zone { get; private set; }
        public Id<SpeciesDef>? WildSpecies { get; private set; }
        public int WildLevel { get; private set; }
        /// <summary>Rival escrito a mano en el Inspector (formato antiguo, se sigue aceptando).</summary>
        public OpponentSpec Legacy { get; private set; }

        public bool IsTrainer => Trainer != null || (Legacy != null && !Legacy.IsWild);

        public static BattleRequest AgainstTrainer(TrainerDefinition trainer) => new BattleRequest { Trainer = trainer };
        public static BattleRequest InZone(EncounterZone zone) => new BattleRequest { Zone = zone };
        public static BattleRequest WildOf(Id<SpeciesDef> species, int level) => new BattleRequest { WildSpecies = species, WildLevel = level < 1 ? 1 : level };
        public static BattleRequest FromSpec(OpponentSpec spec) => new BattleRequest { Legacy = spec };
    }

    /// <summary>
    /// Entrega ("handoff") del próximo combate entre el mundo y la escena de combate: un NPC o un
    /// encuentro fija aquí su petición justo antes de cargar la escena; la pantalla de combate la
    /// consume. Si está vacío (escena de prueba), la pantalla usa lo que diga su Inspector.
    /// </summary>
    public static class PendingBattle
    {
        /// <summary>El próximo combate. Lo fija quien lo dispara.</summary>
        public static BattleRequest Request { get; set; }

        /// <summary>Compatibilidad: fijar el rival con el formato antiguo (lista de combatientes).</summary>
        public static OpponentSpec Opponent
        {
            get => Request?.Legacy;
            set => Request = value != null ? BattleRequest.FromSpec(value) : null;
        }

        public static void Clear() => Request = null;
    }
}
