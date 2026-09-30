using System.Collections.Generic;
using System.Linq;
using CTEditor.SharedKernel.Events;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Effects;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Rules.Mechanics;
using CTEditor.Battle.Domain.Actions;
using CTEditor.Battle.Domain.Events;

namespace CTEditor.Battle.Domain.Turn
{
    /// <summary>
    /// MOVIMIENTOS Z: solo si las reglas tienen activa una mecánica de movimientos Z. El combatiente lleva un CRISTAL Z: un
    /// objeto con bloques «movimiento_z» cuyas condiciones dicen qué movimientos convierte (tipo, movimiento concreto,
    /// especie). Una vez por combate y lado (lo que diga la ficha):
    ///   - un movimiento de daño se convierte en el movimiento Z del bloque: su tipo y efectos, con la categoría del
    ///     movimiento base y la potencia de la tabla de la ficha (o la suya, si el Z tiene potencia fija: los exclusivos);
    ///     nunca falla y atraviesa Protección con el % de daño de la ficha;
    ///   - un movimiento de estado hace primero su EFECTO Z (columna «efecto_z») y luego lo suyo.
    /// Gasta los PP del movimiento base. Las comprobaciones de fuera del motor (la Pulsera Z del jugador, el permiso del
    /// entrenador) las hace la sesión de combate, como con la Megaevolución.
    /// </summary>
    public sealed partial class TurnResolver
    {
        /// <summary>La mecánica de movimientos Z activa (null = no hay movimientos Z en este juego).</summary>
        public MechanicDefinition ZMechanic => _rules.Mechanic(MechanicKind.ZMove);

        /// <summary>Z move being resolved now (base move, the Z move to use, the crystal), set by ResolveMove.</summary>
        private (Id<Move> baseMove, Move zMove, string crystal)? _zPending;
        /// <summary>% of the damage that goes through a protection this hit (1 = none: not a Z move against Protect).</summary>
        private float _zProtectFactor = 1f;

        /// <summary>El bloque del cristal Z que convierte 'baseMove' para 'c' (null = ninguno).</summary>
        private (EffectBlock block, string crystal)? ZBlockFor(Combatant c, Move baseMove)
        {
            if (c == null || c.IsFainted || baseMove == null || ZMechanic == null || !TryGetHeldItem(c, out var item)) return null;
            foreach (var b in item.Effects)
                if (b.Trigger == EffectTrigger.Passive && b.Action == EffectAction.ZMove
                    && AllConditions(b.Conditions, c, OpponentOf(c), baseMove))
                    return (b, item.Id);
            return null;
        }

        /// <summary>El movimiento Z en que se convierte 'baseMove' para 'c' (null = no puede). Para los de estado, el propio.</summary>
        public Move ZMoveFor(Combatant c, Id<Move> baseMove)
        {
            if (!_moves.TryGet(baseMove, out var b)) return null;
            var found = ZBlockFor(c, b);
            return found == null ? null : BuildZMove(b, found.Value.block);
        }

        /// <summary>¿Puede el activo de ese lado usar 'move' como movimiento Z ahora? (mecánica, usos que le quedan y cristal).</summary>
        public bool CanZMove(Battle battle, bool playerSide, Id<Move> move)
        {
            var mech = ZMechanic;
            if (battle == null || battle.IsOver || mech == null || !mech.Z.HasUsesLeft(battle.ZMovesUsed(playerSide))) return false;
            return ZMoveFor(playerSide ? battle.Player : battle.Enemy, move) != null;
        }

        /// <summary>¿Puede el activo de ese lado usar ALGÚN movimiento como Z ahora? (para el botón de la interfaz).</summary>
        public bool CanZMove(Battle battle, bool playerSide)
        {
            var c = battle == null ? null : playerSide ? battle.Player : battle.Enemy;
            return c != null && c.Moves.Any(m => CanZMove(battle, playerSide, m));
        }

        private Move BuildZMove(Move b, EffectBlock block)
        {
            if (b.Category == MoveCategory.Status) return b;   // it keeps its data; its Z effect runs first (see RunZStatusEffects)
            _moves.TryGet(new Id<Move>(block.Ref), out var z);
            // A Z move that is itself a status move (Extreme Evoboost from Last Resort) keeps its own category and target.
            bool zStatus = z != null && z.Category == MoveCategory.Status;
            int power = zStatus ? 0 : z != null && z.Power > 1 ? z.Power : ZMechanic.Z.PowerFor(b.Power);
            var tags = new List<string>(z?.Tags ?? new string[0]) { "z" };
            return new Move(z?.Id ?? new Id<Move>(block.Ref.Length > 0 ? block.Ref : b.Id.Value + "_z"), z?.DisplayName ?? block.Ref,
                z?.Type ?? b.Type, zStatus ? MoveCategory.Status : b.Category, power, null, b.MaxPp, 0, zStatus ? z.Target : b.Target, z?.SecondaryEffects,
                makesContact: z != null && z.MakesContact, fixedDamage: z?.FixedDamage ?? FixedDamageKind.None,
                fixedDamageAmount: z?.FixedDamageAmount ?? 0, tags: tags);
        }

        /// <summary>A status move used as Z: its Z effects (they target the user or the foe as written).</summary>
        private void RunZStatusEffects(Combatant actor, Combatant target, Move b, List<IDomainEvent> events)
        {
            if (b.ZEffects.Count == 0) return;
            var z = new Move(b.Id, b.DisplayName, b.Type, MoveCategory.Status, 0, null, 1, 0, b.Target, b.ZEffects);
            ApplyMoveEffects(actor, target, z, 0, events);
        }

        /// <summary>Called by ResolveMoveCore right after paying PP: turns the move into its Z version. Returns the move to use.</summary>
        private Move ApplyPendingZ(Combatant actor, Combatant target, UseMove useMove, Move move, List<IDomainEvent> events)
        {
            if (!_zPending.HasValue || _zPending.Value.baseMove != useMove.Move) return move;
            var (baseId, zMove, crystal) = _zPending.Value;
            _zPending = null;
            _battle.NoteZMove(IsOnPlayerSide(actor));
            events.Add(new ZMoveUsedEvent(actor.Id, baseId, zMove.Id, crystal));
            if (move.Category == MoveCategory.Status)
            {
                RunZStatusEffects(actor, target, move, events);
                return move;
            }
            return zMove;
        }

        /// <summary>Prepares a Z move for ResolveMove (null = it is not used as Z: no crystal, no uses left...).</summary>
        private void PrepareZ(Combatant actor, UseMove useMove)
        {
            _zPending = null;
            if (!useMove.ZMove || _battle == null) return;
            bool side = IsOnPlayerSide(actor);
            if (!CanZMove(_battle, side, useMove.Move) || !_moves.TryGet(useMove.Move, out var b)) return;
            var found = ZBlockFor(actor, b);
            if (found == null) return;
            _zPending = (useMove.Move, BuildZMove(b, found.Value.block), found.Value.crystal);
        }
    }
}
