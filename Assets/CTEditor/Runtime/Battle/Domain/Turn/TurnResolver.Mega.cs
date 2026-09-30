using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.Events;
using CTEditor.GameDefinition.Domain.Species;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Rules.Mechanics;
using CTEditor.Battle.Domain.Actions;
using CTEditor.Battle.Domain.Events;

namespace CTEditor.Battle.Domain.Turn
{
    /// <summary>
    /// MEGAEVOLUCIÓN: solo si las reglas tienen activa una mecánica de Megaevolución. El combatiente necesita una regla de
    /// forma «mega» cuya megapiedra lleve equipada (o, sin piedra, que sepa el movimiento que pide: Rayquaza). Cada lado
    /// tiene un máximo por combate (el de la ficha; 0 = sin límite). Ocurre al principio del turno, antes de ordenar las
    /// jugadas (el más rápido primero), y a partir de ahí cuenta ya su nueva Velocidad. Las comprobaciones de fuera del
    /// motor (el objeto clave del jugador, el permiso del entrenador rival) las hace la sesión de combate.
    /// </summary>
    public sealed partial class TurnResolver
    {
        /// <summary>La mecánica de megaevolución activa (null = no hay megas en este juego).</summary>
        public MechanicDefinition MegaMechanic => _rules.Mechanic(MechanicKind.MegaEvolution);

        /// <summary>La regla de megaevolución que 'c' puede usar ahora (null = ninguna).</summary>
        public FormChange MegaRuleFor(Combatant c)
        {
            if (c == null || c.IsFainted || c.HasMegaEvolved) return null;
            foreach (var rule in c.FormChanges)
            {
                if (rule.Trigger != FormTrigger.MegaEvolution || !rule.AppliesFrom(c.FormId) || c.FindForm(rule.To) == null) continue;
                if (rule.Item.Length > 0)
                {
                    if (_rules.Generation.HeldItems && string.Equals(c.HeldItem, rule.Item, StringComparison.OrdinalIgnoreCase)) return rule;
                }
                else if (rule.Move.Length > 0)
                {
                    foreach (var m in c.Moves) if (string.Equals(m.Value, rule.Move, StringComparison.OrdinalIgnoreCase)) return rule;
                }
            }
            return null;
        }

        /// <summary>¿Puede megaevolucionar AHORA el activo de ese lado? (mecánica activa, le quedan megas y tiene su piedra).</summary>
        public bool CanMegaEvolve(Battle battle, bool playerSide)
        {
            var mech = MegaMechanic;
            if (battle == null || battle.IsOver || mech == null) return false;
            if (!mech.Mega.HasUsesLeft(battle.MegasUsed(playerSide))) return false;
            return MegaRuleFor(playerSide ? battle.Player : battle.Enemy) != null;
        }

        // Al principio del turno: las megaevoluciones pedidas, el más rápido primero.
        private void ApplyMegaEvolutions(Battle battle, BattleAction playerAction, BattleAction enemyAction, List<IDomainEvent> events)
        {
            bool p = playerAction is UseMove pm && pm.MegaEvolve, e = enemyAction is UseMove em && em.MegaEvolve;
            if (!p && !e) return;
            bool enemyFirst = e && p && battle.Enemy.Stats.Of(StatId.Speed) > battle.Player.Stats.Of(StatId.Speed);
            if (enemyFirst) { MegaEvolve(battle, false, events); MegaEvolve(battle, true, events); }
            else
            {
                if (p) MegaEvolve(battle, true, events);
                if (e) MegaEvolve(battle, false, events);
            }
        }

        private bool MegaEvolve(Battle battle, bool playerSide, List<IDomainEvent> events)
        {
            if (!CanMegaEvolve(battle, playerSide)) return false;
            var c = playerSide ? battle.Player : battle.Enemy;
            var rule = MegaRuleFor(c);
            var mech = MegaMechanic;
            if (!c.ChangeForm(rule.To, mech.Mega.RevertOnSwitch)) return false;
            c.NoteMegaEvolved();
            battle.NoteMega(playerSide);
            events.Add(new MegaEvolvedEvent(c.Id, c.FormId, c.CurrentForm?.DisplayName ?? "", rule.Item));
            // Su nueva habilidad actúa como al entrar (Mega-Charizard Y hace sol, Mega-Gyarados...).
            var opponent = playerSide ? battle.Enemy : battle.Player;
            if (!opponent.IsFainted)
            {
                ApplyOnEntry(c, opponent, events);
                ApplyGen4OnEntry(c, opponent, events);
            }
            return true;
        }
    }
}
