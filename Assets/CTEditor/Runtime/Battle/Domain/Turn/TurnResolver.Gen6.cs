using System;
using System.Collections.Generic;
using System.Linq;
using CTEditor.SharedKernel.Events;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Battlefield;
using CTEditor.GameDefinition.Domain.Effects;
using CTEditor.GameDefinition.Domain.Items;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Status;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.Battle.Domain.Events;

namespace CTEditor.Battle.Domain.Turn
{
    /// <summary>
    /// 5.ª y 6.ª GENERACIÓN: objetos de competición (Elección, Vidasfera, Banda Focus, Chaleco Asalto, Casco Dentado,
    /// Seguro Debilidad, Globo Helio, bayas de resistencia, Ziuela, esferas, Lodo Negro, Garra Rápida...), campos
    /// (Hierba, Niebla, Eléctrico), Zona Extraña / Mágica, protecciones con castigo (Escudo Real, Barrera Espinosa)
    /// y las habilidades nuevas (Alas Vendaval, Baba, Momia, Prestidigitador, Piel Feérica, Carrillo...).
    /// Partial de TurnResolver: aquí solo viven las ayudas; el turno las llama en su sitio.
    /// </summary>
    public sealed partial class TurnResolver
    {
        // ================================================================= OBJETOS

        // El objeto se gasta (Banda Focus, bayas, Seguro Debilidad, Globo Helio...).
        private void UseUpItem(Combatant c, List<IDomainEvent> events)
        {
            var id = c.HeldItem;
            if (id == null) return;
            c.ConsumeHeldItem();
            events.Add(new HeldItemActivatedEvent(c.Id, id, true));
        }

        /// <summary>Turnos del clima que pone 'actor' (las rocas de clima añaden turnos).</summary>
        private int WeatherTurnsFor(Combatant actor, string weatherId, int turns)
        {
            int bonus = HeldWeatherBonus(actor, weatherId);
            if (bonus <= 0) return turns;
            int baseTurns = turns > 0 ? turns
                : _weathers != null && _weathers.TryGet(new Id<GameDefinition.Domain.Weather.WeatherDefinition>(weatherId), out var w) ? w.DefaultTurns : 5;
            return baseTurns <= 0 ? 0 : baseTurns + bonus;   // 0 = permanente: se queda así
        }

        /// <summary>Contacto (lado del defensor): Casco Dentado, Baba y Momia.</summary>
        private void ApplyGen6ContactEffects(Combatant attacker, Combatant defender, Move move, List<IDomainEvent> events)
        {
            if (attacker == null || defender == null || attacker.IsFainted) return;
            RunHeld(defender, EffectTrigger.ContactTaken, attacker, move, events, hitType: move == null ? (Id<ElementType>?)null : MoveTypeOf(attacker, move));
            if (attacker.IsFainted) return;
            var dx = X(defender);
            if (dx.ContactStatDrop.HasValue && dx.ContactStatDropStages != 0)
            {
                events.Add(new AbilityTriggeredEvent(defender.Id, AbilityIdOf(defender), "baba"));
                ChangeStageFrom(defender, attacker, dx.ContactStatDrop.Value, dx.ContactStatDropStages, events);
            }
            if (dx.SpreadsAbilityOnContact && defender.Ability.HasValue && attacker.Ability != defender.Ability)
            {
                attacker.SetAbility(defender.Ability);
                events.Add(new AbilityChangedEvent(attacker.Id, defender.Ability.Value.Value));
            }
        }

        /// <summary>Protecciones con castigo (Escudo Real, Barrera Espinosa) cuando le golpean con contacto.</summary>
        private void PunishProtectContact(Combatant attacker, Combatant defender, Move move, List<IDomainEvent> events)
        {
            if (!move.MakesContact || attacker == null || attacker.IsFainted || _statuses == null) return;
            foreach (var v in defender.Volatiles)
            {
                if (!TryGetStatus(v.Id, out var d) || !d.BlocksIncomingMoves) continue;
                var x = d.Extras;
                if (x.ProtectContactStat.HasValue && x.ProtectContactStages != 0)
                    ChangeStageFrom(defender, attacker, x.ProtectContactStat.Value, x.ProtectContactStages, events);
                if (x.ProtectContactDamagePercent > 0f && !X(attacker).NoIndirectDamage)
                {
                    int dmg = Math.Max(1, (int)(attacker.MaxHp * x.ProtectContactDamagePercent / 100f));
                    attacker.TakeDamage(dmg);
                    events.Add(new StatusDamageEvent(attacker.Id, v.Id, dmg));
                    if (attacker.IsFainted) events.Add(new MonsterFaintedEvent(attacker.Id));
                }
                if (x.ProtectContactStatus.Length > 0 && !attacker.IsFainted)
                    TryInflictStatus(attacker, new StatusId(x.ProtectContactStatus), events, defender);
            }
        }

        /// <summary>¿Le protege algo de ESTE movimiento? (las protecciones «solo daño» dejan pasar los de estado).</summary>
        private bool IsProtectedFrom(Combatant target, Move move)
        {
            if (_statuses == null) return false;
            foreach (var v in target.Volatiles)
                if (TryGetStatus(v.Id, out var d) && d.BlocksIncomingMoves && (!d.Extras.ProtectOnlyDamaging || move.DealsDirectDamage)) return true;
            return false;
        }

        // ================================================================= CAMPOS Y EFECTOS DE CAMPO

        /// <summary>¿Pisa el suelo para los CAMPOS? (no si es Volador, levita, lleva Globo o está bajo Levitón), salvo Gravedad.</summary>
        private bool TouchesGround(Combatant c)
        {
            if (c == null) return false;
            if (IsGrounded(c)) return true;
            if (ContainsType(TypesOf(c), new Id<ElementType>("flying"))) return false;
            if (TryGetAbility(c, out var ab) && ab.IsImmuneToType(new Id<ElementType>("ground"))) return false;
            if (HeldImmuneTo(c, new Id<ElementType>("ground"))) return false;
            if (HasVolatileFlag(c, d => ContainsType(d.Extras.TypeImmunities, new Id<ElementType>("ground")))) return false;
            return true;
        }

        /// <summary>¿Un campo hace fallar la prioridad contra él? (Campo Psíquico, a quien pisa el suelo).</summary>
        private bool TerrainBlocksPriority(Combatant target)
        {
            if (!TouchesGround(target)) return false;
            foreach (var d in FieldConditions()) if (d.BlocksPriorityOnGrounded) return true;
            return false;
        }

        /// <summary>¿Un campo impide ponerle este estado? (Niebla: todos; Eléctrico: dormir).</summary>
        private bool TerrainBlocksStatus(Combatant target, StatusId statusId)
        {
            if (!TouchesGround(target)) return false;
            foreach (var d in FieldConditions())
                foreach (var s in d.GroundedStatusBlock)
                    if (s == "*" || string.Equals(s, statusId.Value, StringComparison.OrdinalIgnoreCase)
                        || statusId.Value == "drowsy" && string.Equals(s, "sleep", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Potencia extra por campo (Campo Eléctrico: Eléctrico ×1,5 si el atacante pisa el suelo).</summary>
        private double TerrainPowerMultiplier(Combatant actor, Id<ElementType> type)
        {
            double m = 1.0;
            if (!TouchesGround(actor)) return m;
            foreach (var d in FieldConditions())
                if (d.TypePowerMultipliers.TryGetValue(type.Value ?? "", out var f)) m *= f;
            return m;
        }

        /// <summary>Zona Extraña: Defensa ↔ Def. Esp. para el daño.</summary>
        private bool DefensesSwapped()
        {
            foreach (var d in FieldConditions()) if (d.SwapsDefenses) return true;
            return false;
        }

        /// <summary>¿Está activo ese efecto de campo en algún lado? (condición «campo=X»).</summary>
        private bool FieldConditionActive(string id)
        {
            if (_battle == null || string.IsNullOrEmpty(id)) return false;
            foreach (var team in new[] { _battle.PlayerTeam, _battle.EnemyTeam })
                foreach (var k in team.SideConditions.Keys)
                    if (string.Equals(k, id, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Zona Mágica: ningún objeto equipado funciona.</summary>
        private bool ItemsSuppressed()
        {
            foreach (var d in FieldConditions()) if (d.SuppressesItems) return true;
            return false;
        }

        /// <summary>Al poner un efecto con grupo (campos), se quitan los demás del mismo grupo en los dos lados.</summary>
        private void ClearGroup(string group, string keep, BattleTeam keepTeam, List<IDomainEvent> events)
        {
            if (string.IsNullOrEmpty(group) || _battle == null || _sideConditions == null) return;
            foreach (var team in new[] { _battle.PlayerTeam, _battle.EnemyTeam })
            {
                bool own = ReferenceEquals(team, keepTeam);
                var removed = team.RemoveSideConditions(id => !(own && id == keep) && _sideConditions.TryGet(new Id<SideConditionDefinition>(id), out var d)
                                                             && string.Equals(d.Group, group, StringComparison.OrdinalIgnoreCase));
                foreach (var id in removed) events.Add(new SideConditionEndedEvent(ReferenceEquals(team, _battle.PlayerTeam), id));
            }
        }

        // ================================================================= FIN DE TURNO

        private void ApplyGen6EndOfTurn(Battle battle, List<IDomainEvent> events)
        {
            foreach (var c in new[] { battle.Player, battle.Enemy })
            {
                if (c == null || c.IsFainted) continue;
                // Campo de Hierba: los que pisan el suelo recuperan PS.
                foreach (var d in FieldConditions())
                    if (d.EndOfTurnHealPercent > 0f && TouchesGround(c) && c.CurrentHp < c.MaxHp && !HasVolatileFlag(c, s => s.Extras.BlocksHealing))
                    {
                        int before = c.CurrentHp;
                        c.HealHp(Math.Max(1, (int)(c.MaxHp * d.EndOfTurnHealPercent / 100f)));
                        if (c.CurrentHp > before) events.Add(new HpRestoredEvent(c.Id, c.CurrentHp - before));
                    }
            }
        }

        // ================================================================= EFECTOS NUEVOS DE MOVIMIENTO

        private bool ApplyGen6Effect(Combatant actor, Combatant target, Move move, MoveEffect effect, Combatant who, List<IDomainEvent> events)
        {
            switch (effect.Kind)
            {
                case MoveEffectKind.GiveAbility:
                    if (who == null || who == actor || !actor.Ability.HasValue || who.Ability == actor.Ability) { events.Add(new MoveFailedEvent(actor.Id)); return true; }
                    who.SetAbility(actor.Ability);
                    events.Add(new AbilityChangedEvent(who.Id, actor.Ability.Value.Value));
                    return true;
                case MoveEffectKind.CopyTypes:
                {
                    if (who == null || who == actor) { events.Add(new MoveFailedEvent(actor.Id)); return true; }
                    var types = TypesOf(who).ToList();
                    actor.ChangeTypes(types);
                    events.Add(new TypeChangedEvent(actor.Id, types));
                    return true;
                }
                case MoveEffectKind.GiveItem:
                {
                    if (who == null || who == actor || actor.HeldItem == null || who.HeldItem != null) { events.Add(new MoveFailedEvent(actor.Id)); return true; }
                    var item = actor.TakeHeldItem();
                    who.SetHeldItem(item);
                    events.Add(new ItemTransferredEvent(actor.Id, who.Id, item));
                    return true;
                }
                case MoveEffectKind.AddType:
                {
                    if (who == null || who.IsFainted || string.IsNullOrEmpty(effect.TypeId)) return true;
                    var t = new Id<ElementType>(effect.TypeId);
                    var types = TypesOf(who).Where(x => x != t).ToList();
                    if (types.Count == TypesOf(who).Count && ContainsType(TypesOf(who), t)) { events.Add(new MoveFailedEvent(actor.Id)); return true; }
                    types.Add(t);
                    who.ChangeTypes(types);
                    events.Add(new TypeChangedEvent(who.Id, types));
                    return true;
                }
                case MoveEffectKind.InvertStages:
                {
                    if (who == null || who.IsFainted) return true;
                    var inverted = new Dictionary<StatId, int>();
                    foreach (var kv in who.StagesSnapshot()) inverted[kv.Key] = -kv.Value;
                    who.SetStages(inverted);
                    events.Add(new StatsSwappedEvent(who.Id, null, false));
                    return true;
                }
            }
            return false;
        }

        // ================================================================= TIPOS ESPECIALES DE MOVIMIENTO

        /// <summary>
        /// Etiquetas de tipos: «tipo_extra:flying» (Plancha: también cuenta como Volador) y «eficaz_contra:water»
        /// (Liofilización: muy eficaz contra Agua). Devuelve el multiplicador extra contra ese objetivo.
        /// </summary>
        private float TagEffectiveness(Move move, Combatant target, float current)
        {
            if (move.Tags.Count == 0 || current <= 0f) return current;
            float m = current;
            foreach (var tag in move.Tags)
            {
                if (tag.StartsWith("tipo_extra:", StringComparison.OrdinalIgnoreCase))
                {
                    var extra = new Id<ElementType>(tag.Substring(11).Trim());
                    foreach (var t in TypesOf(target)) m *= _typeChart.Effectiveness(extra, t).Multiplier;
                }
                else if (tag.StartsWith("eficaz_contra:", StringComparison.OrdinalIgnoreCase))
                {
                    var against = new Id<ElementType>(tag.Substring(14).Trim());
                    var type = move.Type;
                    foreach (var t in TypesOf(target))
                        if (t == against)
                        {
                            float was = _typeChart.Effectiveness(type, t).Multiplier;
                            if (was > 0f) m = m / was * 2f;
                        }
                }
            }
            return m;
        }
    }
}
