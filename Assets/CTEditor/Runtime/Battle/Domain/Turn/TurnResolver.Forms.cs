using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.Events;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Species;
using CTEditor.Battle.Domain.Events;

namespace CTEditor.Battle.Domain.Turn
{
    /// <summary>
    /// CAMBIOS DE FORMA en combate, según las reglas de la especie (FormChange): al entrar al campo (objeto, clima), antes
    /// o después de usar un movimiento (Aegislash, Meloetta) y al final del turno (Modo Daruma, clima). La megaevolución
    /// no es automática: la pide el entrenador (paso aparte).
    /// </summary>
    public sealed partial class TurnResolver
    {
        // Al pisar el campo: formas por objeto equipado y por clima.
        private void CheckEntryForms(Combatant c, List<IDomainEvent> events)
        {
            if (c == null || c.IsFainted || c.FormChanges.Count == 0) return;
            if (!TryFormChange(c, FormTrigger.HeldItem, null, false, events))
                TryFormChange(c, FormTrigger.Weather, null, false, events);
        }

        // Al final del turno: formas por PS y por clima.
        private void CheckEndOfTurnForms(Combatant c, List<IDomainEvent> events)
        {
            if (c == null || c.IsFainted || c.FormChanges.Count == 0) return;
            if (!TryFormChange(c, FormTrigger.HpBelow, null, false, events)
                && !TryFormChange(c, FormTrigger.HpAtLeast, null, false, events))
                TryFormChange(c, FormTrigger.Weather, null, false, events);
        }

        // Al usar un movimiento: 'after' = false antes de golpear (Aegislash), true después (Meloetta).
        private void CheckMoveForms(Combatant c, Move move, bool after, List<IDomainEvent> events)
        {
            if (c == null || c.IsFainted || move == null || c.FormChanges.Count == 0) return;
            if (!TryFormChange(c, FormTrigger.UseMove, move, after, events))
                TryFormChange(c, FormTrigger.DamagingMove, move, after, events);
        }

        // La PRIMERA regla de ese disparador que se cumple desde la forma actual. true si cambió de forma.
        private bool TryFormChange(Combatant c, FormTrigger trigger, Move move, bool after, List<IDomainEvent> events)
        {
            foreach (var rule in c.FormChanges)
            {
                if (rule.Trigger != trigger || !rule.AppliesFrom(c.FormId) || !FormConditionMet(c, rule, move, after)) continue;
                return ChangeFormOf(c, rule.To, events);
            }
            return false;
        }

        private bool FormConditionMet(Combatant c, FormChange rule, Move move, bool after)
        {
            if (rule.RequiredAbility.Length > 0
                && (!TryGetAbility(c, out var ab) || !string.Equals(ab.Id.Value, rule.RequiredAbility, StringComparison.OrdinalIgnoreCase)))
                return false;
            switch (rule.Trigger)
            {
                case FormTrigger.HeldItem:
                    // El objeto que da la forma funciona aunque haya Zona Mágica (como el Griseo Orbe), pero no si las reglas
                    // del juego no tienen objetos equipados.
                    return _rules.Generation.HeldItems && !string.IsNullOrEmpty(c.HeldItem)
                        && string.Equals(c.HeldItem, rule.Item, StringComparison.OrdinalIgnoreCase);
                case FormTrigger.UseMove:
                    return rule.AfterMove == after && string.Equals(move.Id.Value, rule.Move, StringComparison.OrdinalIgnoreCase);
                case FormTrigger.DamagingMove:
                    return rule.AfterMove == after && _rules.CategoryOf(move) != MoveCategory.Status;
                case FormTrigger.HpBelow:
                    return c.CurrentHp * 100 < c.MaxHp * rule.HpPercent;
                case FormTrigger.HpAtLeast:
                    return c.CurrentHp * 100 >= c.MaxHp * rule.HpPercent;
                case FormTrigger.Weather:
                    return string.Equals(_battle?.WeatherId ?? "", rule.Weather, StringComparison.OrdinalIgnoreCase);
                default:
                    return false;   // MegaEvolution: la pide el entrenador
            }
        }

        /// <summary>Cambia de forma y lo narra. true si cambió.</summary>
        private bool ChangeFormOf(Combatant c, string to, List<IDomainEvent> events)
        {
            string from = c.FormId;
            if (!c.ChangeForm(to)) return false;
            events.Add(new FormChangedEvent(c.Id, from, c.FormId, c.CurrentForm?.DisplayName ?? ""));
            return true;
        }
    }
}
