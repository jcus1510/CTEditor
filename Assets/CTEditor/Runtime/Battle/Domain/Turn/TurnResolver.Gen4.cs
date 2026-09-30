using System;
using System.Collections.Generic;
using System.Linq;
using CTEditor.SharedKernel.Events;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Abilities;
using CTEditor.GameDefinition.Domain.Battlefield;
using CTEditor.GameDefinition.Domain.Conditions;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Status;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.GameDefinition.Domain.Weather;
using CTEditor.Battle.Domain.Actions;
using CTEditor.Battle.Domain.Events;

namespace CTEditor.Battle.Domain.Turn
{
    /// <summary>
    /// MECÁNICAS DE LA 3.ª Y 4.ª GENERACIÓN (y de las habilidades ocultas). Todo sale de DATOS: las fichas de
    /// habilidades (AbilityExtras), estados (StatusExtras), efectos de lado y movimientos. Aquí solo están
    /// las reglas que los interpretan; el resto del resolvedor las llama en los puntos del turno que tocan.
    /// </summary>
    public sealed partial class TurnResolver
    {
        // Lo que eligió cada lado este turno (Golpe Bajo, Yo Primero).
        private BattleAction _chosenPlayer, _chosenEnemy;
        // Mientras ataca alguien con Rompemoldes, la habilidad de este combatiente no cuenta.
        private Combatant _abilityIgnored;
        // Multiplicador de potencia de un solo uso (Yo Primero ×1,5).
        private float _oneShotPowerBoost = 1f;

        // ================================================================= HABILIDADES

        // ¿Funciona su habilidad ahora? (Bilis la anula; Gas Reactivo anula las de los demás; Rompemoldes la ignora).
        private bool AbilityWorks(Combatant c)
        {
            if (c == null || !c.Ability.HasValue) return false;
            if (ReferenceEquals(c, _abilityIgnored)) return false;
            if (_statuses != null)
                foreach (var v in c.Volatiles)
                    if (TryGetStatus(v.Id, out var d) && d.Extras.SuppressesAbility) return false;
            if (_battle != null && _abilities != null)
                foreach (var other in new[] { _battle.Player, _battle.Enemy })
                    if (!ReferenceEquals(other, c) && other != null && !other.IsFainted && other.Ability.HasValue
                        && _abilities.TryGet(new Id<AbilityDefinition>(other.Ability.Value.Value), out var og) && og.Extras.NeutralizingGas)
                        return false;
            return true;
        }

        /// <summary>Los ganchos 3.ª-4.ª gen. de la habilidad de c (o «nada»).</summary>
        private AbilityExtras X(Combatant c) => TryGetAbility(c, out var a) ? a.Extras : AbilityExtras.None;

        private string AbilityIdOf(Combatant c) => c?.Ability?.Value ?? "";

        private Combatant OpponentOf(Combatant c)
        {
            if (_battle == null || c == null) return null;
            return _battle.PlayerTeam.Find(c.Id) != null ? _battle.Enemy : _battle.Player;
        }

        // ================================================================= TIPOS Y EFICACIA

        /// <summary>Sus tipos AHORA: sin los que un estado le quita (Respiro quita Volador ese turno).</summary>
        private IReadOnlyList<Id<ElementType>> TypesOf(Combatant c)
        {
            if (c == null) return Array.Empty<Id<ElementType>>();
            var types = c.Types;
            if (_statuses == null || c.Volatiles.Count == 0) return types;
            List<Id<ElementType>> list = null;
            foreach (var v in c.Volatiles)
                if (TryGetStatus(v.Id, out var d) && d.Extras.SuppressedType.HasValue)
                {
                    list = list ?? new List<Id<ElementType>>(types);
                    list.Remove(d.Extras.SuppressedType.Value);
                }
            if (list == null) return types;
            if (list.Count == 0) list.Add(new Id<ElementType>("normal")); // Respiro en un Volador puro: queda Normal
            return list;
        }

        /// <summary>El tipo REAL del movimiento: Normalidad lo vuelve Normal; Meteorobola cambia con el clima.</summary>
        private Id<ElementType> MoveTypeOf(Combatant actor, Move move)
        {
            if (move == null) return default;
            if (X(actor).NormalizeMoves) return new Id<ElementType>("normal");
            if (move.TypeByWeather.Count > 0 && WeatherIsActive && _battle.WeatherId != null) return move.TypeIn(_battle.WeatherId);
            // Piel Feérica / Piel Celeste / Piel Helada: los movimientos Normales cambian de tipo.
            var convert = X(actor).ConvertNormalTo;
            if (convert.Length > 0 && string.Equals(move.Type.Value, "normal", StringComparison.OrdinalIgnoreCase)) return new Id<ElementType>(convert);
            return move.Type;
        }

        /// <summary>¿Está en el suelo? (Gravedad en su lado, Arraigo...). Así le afecta Tierra aunque vuele o levite.</summary>
        private bool IsGrounded(Combatant c)
        {
            foreach (var d in FieldConditions()) if (d.GroundsTargets) return true;   // Gravedad: afecta a todo el campo
            return HasVolatileFlag(c, d => d.Extras.Grounded);
        }

        /// <summary>
        /// EFICACIA de un movimiento contra un objetivo con todas las reglas: tipos actuales, Intrépido,
        /// Profecía/Gran Ojo, Gravedad, habilidades que anulan un tipo (Levitación), estados que dan inmunidad
        /// (Levitón) y Superguarda. 'abilityImmune' = lo anuló una habilidad o un estado (no la tabla).
        /// </summary>
        private float EffectivenessAgainst(Combatant actor, Combatant target, Move move, out bool abilityImmune)
        {
            abilityImmune = false;
            if (target == null) return 1f;
            var type = MoveTypeOf(actor, move);
            // «ignora_inmunidad» (Mil Flechas): alcanza aunque el objetivo sea inmune por tipo, habilidad o Globo.
            bool grounded = IsGrounded(target) || move.HasTag("ignora_inmunidad");
            var ignoreFor = X(actor).IgnoresImmunityFor;
            bool identified = false;
            IReadOnlyList<Id<ElementType>> hittableBy = null;
            if (_statuses != null)
                foreach (var v in target.Volatiles)
                    if (TryGetStatus(v.Id, out var sd) && sd.Extras.Identified) { identified = true; hittableBy = sd.Extras.HittableByTypes; }

            float mult = 1f;
            foreach (var t in TypesOf(target))
            {
                float e = _typeChart.Effectiveness(type, t).Multiplier;
                if (e <= 0f)
                {
                    bool pierce = ContainsType(ignoreFor, type)
                                  || identified && hittableBy != null && ContainsType(hittableBy, type)
                                  || grounded && type.Value == "ground";
                    if (pierce) e = 1f;
                }
                mult *= e;
            }

            // Habilidad del objetivo que anula el tipo (Levitación, Absorbe Agua...). En el suelo, Levitación no sirve.
            if (TryGetAbility(target, out var ab) && ab.IsImmuneToType(type) && !(grounded && type.Value == "ground"))
            { abilityImmune = true; return 0f; }
            // Estados que dan inmunidad (Levitón: Tierra).
            if (!(grounded && type.Value == "ground") && HasVolatileFlag(target, d => ContainsType(d.Extras.TypeImmunities, type)))
            { abilityImmune = true; return 0f; }
            // Globo Helio: inmune a Tierra (salvo en el suelo por Gravedad...).
            if (type.Value == "ground" && !grounded && HeldX(target).AirBalloon) { abilityImmune = true; return 0f; }
            // Plancha (también Volador) y Liofilización (muy eficaz contra Agua).
            mult = TagEffectiveness(move, target, mult);
            // Superguarda: solo los muy eficaces.
            if (move.DealsDirectDamage && mult > 0f && mult <= 1f && X(target).OnlySuperEffectiveHits)
            { abilityImmune = true; return 0f; }
            return mult;
        }

        // ================================================================= CLIMA

        /// <summary>¿El clima hace algo ahora? (Aclimatación y Bucle Aire lo anulan mientras están en el campo).</summary>
        private bool WeatherIsActive
        {
            get
            {
                if (_battle == null || _battle.WeatherId == null) return false;
                foreach (var c in new[] { _battle.Player, _battle.Enemy })
                    if (c != null && !c.IsFainted && X(c).SuppressesWeather) return false;
                return true;
            }
        }

        private bool WeatherImmune(Combatant c, string weatherId)
        {
            var x = X(c);
            if (x.NoIndirectDamage) return true;
            foreach (var w in x.WeatherImmunities)
                if (w == "*" || string.Equals(w, weatherId, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // ================================================================= PRECISIÓN

        /// <summary>¿Acierta? Tiene en cuenta Indefenso, Fijar Blanco, semi-invulnerables (Vuelo, Excavar), Gravedad, habilidades y etapas.</summary>
        private bool RollHit(Combatant actor, Combatant target, Move move)
        {
            bool noGuard = X(actor).NoGuard || target != null && X(target).NoGuard;
            // Fijar Blanco / Telépata: el próximo acierta seguro (y se gasta).
            bool sure = false;
            if (_statuses != null)
                foreach (var v in new List<ActiveVolatileStatus>(actor.Volatiles))
                    if (TryGetStatus(v.Id, out var d) && d.Extras.SureHit) { sure = true; actor.RemoveVolatile(v.Id); }
            if (noGuard || sure) return true;

            // Semi-invulnerable: el objetivo está en el aire, bajo tierra o bajo el agua cargando su movimiento.
            if (target != null && target != actor && target.ChargingMove.HasValue && _moves.TryGet(target.ChargingMove.Value, out var charging)
                && charging.HasTag("semiinvulnerable"))
                return false;

            if (move.NeverMisses) return true;
            float acc = move.Accuracy.Value.AsFraction * HitStageMultiplier(actor, target, move);
            if (target != null && target != actor && move.Target != MoveTarget.Self)
            {
                foreach (var d in FieldConditions()) acc *= d.AccuracyMultiplier;   // Gravedad: todo el campo
                foreach (var m in X(target).EvasionModifiers)
                    if (AllConditions(m.Conditions, target, actor, move)) acc *= m.Multiplier;
            }
            foreach (var m in X(actor).AccuracyModifiers)
                if (AllConditions(m.Conditions, actor, target, move)) acc *= m.Multiplier;
            // Objetos: Lupa / Telescopio (atacante) y Polvo Brillo / Incienso Lax (objetivo).
            acc *= HeldX(actor).AccuracyMultiplier;
            if (target != null && target != actor && move.Target != MoveTarget.Self) acc *= HeldX(target).EvasionMultiplier;
            return _rng.NextFloat() < acc;
        }

        // ================================================================= RESTRICCIONES AL ELEGIR

        /// <summary>
        /// ¿Puede usar ESTE movimiento ahora? (Mofa, Tormento, Cerca, Anticura, Humedad). Si no, devuelve el
        /// motivo (id del estado o de la habilidad). Lo usan el turno y la IA.
        /// </summary>
        public string RestrictionFor(Combatant actor, Id<Move> moveId)
        {
            if (actor == null || !_moves.TryGet(moveId, out var move)) return null;
            if (_statuses != null)
                foreach (var v in actor.Volatiles)
                {
                    if (!TryGetStatus(v.Id, out var d)) continue;
                    var x = d.Extras;
                    if (x.BlocksStatusMoves && move.Category == MoveCategory.Status) return v.Id.Value;
                    if (x.BlocksRepeatedMove && actor.LastMoveUsed.HasValue && actor.LastMoveUsed.Value == moveId
                        && (!_struggleMove.HasValue || moveId != _struggleMove.Value)) return v.Id.Value;
                    if (x.BlocksHealing && IsHealingMove(move)) return v.Id.Value;
                }
            // Objetos Elección: solo el primer movimiento que usó. Chaleco Asalto: nada de movimientos de estado.
            bool struggle = _struggleMove.HasValue && moveId == _struggleMove.Value;
            var hx = HeldX(actor);
            if (!struggle && hx.ChoiceLock && actor.ChoiceLockedMove.HasValue && actor.ChoiceLockedMove.Value != moveId
                && actor.IndexOfMove(actor.ChoiceLockedMove.Value) >= 0) return actor.HeldItem;
            if (!struggle && hx.BlocksStatusMoves && move.Category == MoveCategory.Status) return actor.HeldItem;
            // Cerca: el rival no puede usar los movimientos que conoce quien la usó.
            var opp = OpponentOf(actor);
            if (opp != null && !opp.IsFainted && opp.IndexOfMove(moveId) >= 0 && HasVolatileFlag(opp, d => d.Extras.Imprisons))
                return "imprison";
            // Humedad: nadie usa movimientos con esas etiquetas.
            if (_battle != null)
                foreach (var c in new[] { _battle.Player, _battle.Enemy })
                    if (c != null && !c.IsFainted)
                        foreach (var tag in X(c).BlocksMoveTagsForAll)
                            if (move.HasTag(tag)) return AbilityIdOf(c);
            return null;
        }

        private static bool IsHealingMove(Move move)
        {
            foreach (var e in move.SecondaryEffects)
                if ((e.Kind == MoveEffectKind.Heal || e.Kind == MoveEffectKind.HealSelf) && e.Target == EffectTarget.Self
                    || e.Kind == MoveEffectKind.DelayedHeal || e.Kind == MoveEffectKind.UseStockpile && e.Amount.Value > 0)
                    return true;
            return false;
        }

        /// <summary>¿Se puede elegir el movimiento del hueco 'index'? (PP, Anulación y las restricciones de la 4.ª gen.)</summary>
        public bool CanChooseMove(Combatant actor, int index)
        {
            if (actor == null || !actor.CanChoose(index)) return false;
            return RestrictionFor(actor, actor.Moves[index]) == null;
        }

        // ================================================================= ENTRADA AL CAMPO

        // Lo nuevo de la 4.ª gen. al entrar: Deseo Cura, clima de habilidad, Rastro, Impostor, Descarga, avisos, Predicción.
        private void ApplyGen4OnEntry(Combatant entering, Combatant opponent, List<IDomainEvent> events)
        {
            if (entering == null || entering.IsFainted) return;
            var team = SideOf(entering);
            if (team != null && team.PendingHealingWish)
            {
                team.PendingHealingWish = false;
                int before = entering.CurrentHp;
                entering.HealHp(entering.MaxHp);
                if (entering.Status.HasValue) { var was = entering.Status.Value; entering.ClearStatus(); events.Add(new StatusFadedEvent(entering.Id, was)); }
                if (entering.CurrentHp > before) events.Add(new HpRestoredEvent(entering.Id, entering.CurrentHp - before));
            }

            if (!TryGetAbility(entering, out var ab)) return;
            var x = ab.Extras;
            string id = ab.Id.Value;

            if (x.TraceOnEntry && opponent != null && !opponent.IsFainted && opponent.Ability.HasValue && AbilityWorks(opponent)
                && _abilities.TryGet(new Id<AbilityDefinition>(opponent.Ability.Value.Value), out var oppAb) && !oppAb.Extras.TraceOnEntry)
            {
                entering.SetAbility(opponent.Ability);
                events.Add(new AbilityChangedEvent(entering.Id, opponent.Ability.Value.Value));
                // La habilidad copiada también «entra» (Intimidación copiada intimida).
                ApplyOnEntry(entering, opponent, events);
                return;
            }
            if (x.TransformOnEntry && opponent != null && !opponent.IsFainted && !opponent.HasSubstitute && !opponent.IsTransformed && !entering.IsTransformed)
            {
                entering.TransformInto(opponent, 5);
                events.Add(new TransformedEvent(entering.Id, opponent.Id));
            }
            if (!string.IsNullOrEmpty(x.OnEntryWeather) && _battle != null
                && !string.Equals(_battle.WeatherId, x.OnEntryWeather, StringComparison.OrdinalIgnoreCase))
            {
                events.Add(new AbilityTriggeredEvent(entering.Id, id, "clima"));
                if (x.OnEntryWeatherTurns > 0) StartWeather(x.OnEntryWeather, WeatherTurnsFor(entering, x.OnEntryWeather, x.OnEntryWeatherTurns), events);
                else
                {
                    _battle.SetWeather(x.OnEntryWeather, 0);   // 0 = hasta que otro lo cambie
                    events.Add(new WeatherStartedEvent(x.OnEntryWeather, 0));
                }
            }
            if (x.DownloadOnEntry && opponent != null && !opponent.IsFainted)
            {
                var stat = EffectiveStat(opponent, StatId.Defense) < EffectiveStat(opponent, StatId.SpDefense) ? StatId.Attack : StatId.SpAttack;
                ChangeStageFrom(entering, entering, stat, 1, events);
            }
            if (!string.IsNullOrEmpty(x.AnnounceOnEntry) && opponent != null && !opponent.IsFainted)
                events.Add(new AbilityTriggeredEvent(entering.Id, id, Announce(x.AnnounceOnEntry, entering, opponent)));
            if (x.NeutralizingGas) events.Add(new AbilityTriggeredEvent(entering.Id, id, "gas"));
            if (x.Unnerve) events.Add(new AbilityTriggeredEvent(entering.Id, id, "nervios"));
            if (x.Pressure) events.Add(new AbilityTriggeredEvent(entering.Id, id, "presion"));
            if (x.MoldBreaker) events.Add(new AbilityTriggeredEvent(entering.Id, id, "rompemoldes"));
            UpdateWeatherType(entering, events);
        }

        // Cacheo (objeto), Anticipación (peligro), Alerta (movimiento más fuerte).
        private string Announce(string kind, Combatant self, Combatant opponent)
        {
            switch (kind.ToLowerInvariant())
            {
                case "objeto": return "objeto:" + (opponent.HeldItem ?? "");
                case "peligro":
                    foreach (var m in opponent.Moves)
                        if (_moves.TryGet(m, out var mv) && mv.DealsDirectDamage
                            && (mv.FixedDamage == FixedDamageKind.OneHitKo || _typeChart.Effectiveness(mv.Type, TypesOf(self)).Multiplier > 1f))
                            return "peligro:si";
                    return "peligro:no";
                case "movimiento":
                    Move best = null;
                    foreach (var m in opponent.Moves)
                        if (_moves.TryGet(m, out var mv) && (best == null || mv.Power > best.Power)) best = mv;
                    return "movimiento:" + (best?.Id.Value ?? "");
                default: return kind;
            }
        }

        // Predicción: su tipo cambia con el clima.
        private void UpdateWeatherType(Combatant c, List<IDomainEvent> events)
        {
            if (c == null || c.IsFainted) return;
            var x = X(c);
            if (x.TypeByWeather.Count == 0) return;
            Id<ElementType>? want = null;
            if (WeatherIsActive)
                foreach (var (w, t) in x.TypeByWeather)
                    if (string.Equals(w, _battle.WeatherId, StringComparison.OrdinalIgnoreCase)) { want = t; break; }
            var current = c.Types;
            if (want.HasValue)
            {
                if (current.Count == 1 && current[0] == want.Value) return;
                c.ChangeTypes(new[] { want.Value });
                events.Add(new TypeChangedEvent(c.Id, new[] { want.Value }));
            }
        }

        // ================================================================= ETAPAS

        /// <summary>
        /// Cambia una etapa respetando Simple (×2), Respondón (al revés), Cuerpo Puro, Vista Lince / Corte Fuerte
        /// y Competitivo/Tenacidad. 'source' = quien lo provoca (para saber si viene del rival). Devuelve lo aplicado.
        /// </summary>
        private int ChangeStageFrom(Combatant source, Combatant who, StatId stat, int stages, List<IDomainEvent> events)
        {
            if (who == null || who.IsFainted || stages == 0) return 0;
            var x = X(who);
            if (x.InvertsStages) stages = -stages;
            if (x.StageMultiplier > 1) stages *= x.StageMultiplier;
            bool fromOpponent = source != null && !ReferenceEquals(source, who);
            if (stages < 0 && fromOpponent)
            {
                if (TryGetAbility(who, out var ab) && ab.PreventsStatReduction) return 0;
                foreach (var s in x.PreventedStatDrops) if (s == stat) { events.Add(new AbilityTriggeredEvent(who.Id, AbilityIdOf(who), "protege")); return 0; }
            }
            int applied = who.ChangeStage(stat, stages);
            if (applied != 0) events.Add(new StatStageChangedEvent(who.Id, stat, applied));
            // Competitivo / Tenacidad: si el rival le baja algo, sube lo suyo.
            if (applied < 0 && fromOpponent && x.OnStatDroppedStat.HasValue && x.OnStatDroppedStages != 0)
            {
                events.Add(new AbilityTriggeredEvent(who.Id, AbilityIdOf(who), "competitivo"));
                int b = who.ChangeStage(x.OnStatDroppedStat.Value, x.OnStatDroppedStages);
                if (b != 0) events.Add(new StatStageChangedEvent(who.Id, x.OnStatDroppedStat.Value, b));
            }
            return applied;
        }

        // ================================================================= DESPUÉS DE UN GOLPE

        // Reacciones del objetivo tras recibir un golpe que hizo daño (y del atacante si lo debilitó).
        private void AfterHit(Combatant actor, Combatant target, Move move, int damage, bool wasCrit, List<IDomainEvent> events)
        {
            if (target == null || target == actor || damage <= 0) return;
            var tx = X(target);
            var type = MoveTypeOf(actor, move);

            if (!target.IsFainted)
            {
                // Cambio Color: pasa a ser del tipo del golpe.
                if (tx.ColorChange && !ContainsType(target.Types, type))
                {
                    target.ChangeTypes(new[] { type });
                    events.Add(new TypeChangedEvent(target.Id, new[] { type }));
                }
                // Irascible: un crítico le sube el Ataque al máximo.
                if (wasCrit && tx.CritMaxesAttack)
                {
                    events.Add(new AbilityTriggeredEvent(target.Id, AbilityIdOf(target), "irascible"));
                    int applied = target.ChangeStage(StatId.Attack, 12);
                    if (applied != 0) events.Add(new StatStageChangedEvent(target.Id, StatId.Attack, applied));
                }
                // Armadura Frágil, Justiciero, Cobardía...
                foreach (var oh in tx.OnHitStats)
                    if (AllConditions(oh.Conditions, target, actor, move))
                    {
                        int applied = target.ChangeStage(oh.Stat, oh.Stages);
                        if (applied != 0) events.Add(new StatStageChangedEvent(target.Id, oh.Stat, applied));
                    }
                // Cuerpo Maldito: puede anular el movimiento que le golpeó.
                if (tx.DisableOnHitChance > 0f && !actor.DisabledMove.HasValue && actor.IndexOfMove(move.Id) >= 0
                    && _rng.NextFloat() * 100f < tx.DisableOnHitChance)
                {
                    actor.Disable(move.Id, 4);
                    events.Add(new MoveDisabledEvent(actor.Id, move.Id));
                }
            }

            // Si lo DEBILITÓ: Mismo Destino, Rabia, Resquicio, Autoestima.
            if (target.IsFainted)
            {
                if (_statuses != null && !actor.IsFainted)
                    foreach (var v in target.Volatiles)
                    {
                        if (!TryGetStatus(v.Id, out var d)) continue;
                        if (d.Extras.DestinyBond)
                        {
                            events.Add(new DestinyBondEvent(target.Id, actor.Id, false));
                            actor.TakeDamage(actor.CurrentHp);
                            events.Add(new MonsterFaintedEvent(actor.Id));
                            break;
                        }
                        if (d.Extras.Grudge)
                        {
                            int slot = actor.IndexOfMove(move.Id);
                            if (slot >= 0) { EnsurePp(actor); while (actor.PpAt(slot) > 0) actor.TryConsumePp(move.Id); }
                            events.Add(new DestinyBondEvent(target.Id, actor.Id, true));
                        }
                    }
                if (move.MakesContact && tx.AftermathPercent > 0f && !actor.IsFainted && !X(actor).NoIndirectDamage)
                {
                    events.Add(new AbilityTriggeredEvent(target.Id, AbilityIdOf(target), "resquicio"));
                    DamagePercent(actor, tx.AftermathPercent, events);
                }
                var ax = X(actor);
                if (!actor.IsFainted && ax.OnKoStat.HasValue && ax.OnKoStages != 0)
                {
                    events.Add(new AbilityTriggeredEvent(actor.Id, AbilityIdOf(actor), "autoestima"));
                    int applied = actor.ChangeStage(ax.OnKoStat.Value, ax.OnKoStages);
                    if (applied != 0) events.Add(new StatStageChangedEvent(actor.Id, ax.OnKoStat.Value, applied));
                }
            }
        }

        // Pierde un % de sus PS máx. (daño indirecto: Piel Tosca, Resquicio...). Emite el daño como «de estado».
        /// <summary>«Estado» ficticio de los daños que no vienen de un estado (Piel Tosca, Resquicio, Lodo Líquido...).</summary>
        public static readonly StatusId InjuryStatus = new StatusId("herida");

        private void DamagePercent(Combatant who, float percent, List<IDomainEvent> events, string sourceId = "")
        {
            if (who == null || who.IsFainted || percent <= 0f) return;
            int amount = Math.Max(1, (int)(who.MaxHp * percent / 100f));
            who.TakeDamage(amount);
            events.Add(new StatusDamageEvent(who.Id, InjuryStatus, amount));
            if (who.IsFainted) events.Add(new MonsterFaintedEvent(who.Id));
        }

        // Contacto, del lado del ATACANTE y del DEFENSOR: Piel Tosca, Toque Tóxico, Hurto, Efecto Espora...
        private void ApplyGen4ContactEffects(Combatant attacker, Combatant defender, List<IDomainEvent> events)
        {
            var dx = X(defender);
            if (dx.ContactDamagePercent > 0f && !attacker.IsFainted && !X(attacker).NoIndirectDamage)
            {
                events.Add(new AbilityTriggeredEvent(defender.Id, AbilityIdOf(defender), "piel_tosca"));
                DamagePercent(attacker, dx.ContactDamagePercent, events);
            }
            var ax = X(attacker);
            if (ax.OffensiveContactStatus.HasValue && !defender.IsFainted && _rng.NextFloat() * 100f < ax.OffensiveContactChance)
                TryInflictStatus(defender, ax.OffensiveContactStatus.Value, events, attacker);
            // Hurto: le quita el objeto a quien le golpea (si él no lleva).
            if (dx.Pickpocket && !defender.IsFainted && defender.HeldItem == null && attacker.HeldItem != null && !X(attacker).StickyHold)
            {
                var item = attacker.TakeHeldItem();
                defender.SetHeldItem(item);
                events.Add(new ItemTransferredEvent(attacker.Id, defender.Id, item));
            }
        }

        // ================================================================= EFECTOS NUEVOS DE MOVIMIENTOS

        // Devuelve true si el efecto era de la 4.ª gen. (y ya se aplicó).
        private bool ApplyGen4Effect(Combatant actor, Combatant target, Move move, MoveEffect effect, Combatant who, int damageDealt,
            List<IDomainEvent> events)
        {
            switch (effect.Kind)
            {
                case MoveEffectKind.ClearSideConditions:
                {
                    var team = SideOf(who);
                    if (team == null) return true;
                    var ids = string.IsNullOrEmpty(effect.Text) ? null : new HashSet<string>(effect.Text.Split('|').Select(t => t.Trim()));
                    var removed = team.RemoveSideConditions(id =>
                        ids != null ? ids.Contains(id)
                        : _sideConditions != null && _sideConditions.TryGet(new Id<SideConditionDefinition>(id), out var d)
                          && (d.PhysicalDamageMultiplier < 1f || d.SpecialDamageMultiplier < 1f));
                    foreach (var id in removed) events.Add(new SideConditionEndedEvent(ReferenceEquals(team, _battle.PlayerTeam), id));
                    return true;
                }
                case MoveEffectKind.RemoveItem:
                    if (who == null || who.IsFainted || who.HeldItem == null) return true;
                    if (X(who).StickyHold) { events.Add(new AbilityTriggeredEvent(who.Id, AbilityIdOf(who), "viscosidad")); return true; }
                    events.Add(new ItemTransferredEvent(who.Id, null, who.TakeHeldItem()));
                    return true;
                case MoveEffectKind.StealItem:
                    if (who == null || who == actor || actor.IsFainted || who.HeldItem == null || actor.HeldItem != null) return true;
                    if (X(who).StickyHold) { events.Add(new AbilityTriggeredEvent(who.Id, AbilityIdOf(who), "viscosidad")); return true; }
                    { var item = who.TakeHeldItem(); actor.SetHeldItem(item); events.Add(new ItemTransferredEvent(who.Id, actor.Id, item)); }
                    return true;
                case MoveEffectKind.SwapItems:
                {
                    if (who == null || who == actor || actor.HeldItem == null && who.HeldItem == null || X(who).StickyHold)
                    { events.Add(new MoveFailedEvent(actor.Id)); return true; }
                    var mine = actor.HeldItem; var theirs = who.HeldItem;
                    actor.SetHeldItem(theirs); who.SetHeldItem(mine);
                    if (mine != null) events.Add(new ItemTransferredEvent(actor.Id, who.Id, mine));
                    if (theirs != null) events.Add(new ItemTransferredEvent(who.Id, actor.Id, theirs));
                    return true;
                }
                case MoveEffectKind.ConsumeTargetBerry:
                {
                    if (who == null || who.IsFainted || actor.IsFainted || !TryGetHeldItem(who, out var berry) || berry.HeldTriggerHpPercent <= 0f) return true;
                    who.TakeHeldItem();
                    events.Add(new ItemTransferredEvent(who.Id, actor.Id, berry.Id));
                    int before = actor.CurrentHp;
                    actor.HealHp(berry.HeldTriggerHealHp + (int)(actor.MaxHp * berry.HeldTriggerHealPercent / 100f));
                    if (actor.CurrentHp > before) events.Add(new HpRestoredEvent(actor.Id, actor.CurrentHp - before));
                    return true;
                }
                case MoveEffectKind.RestoreItem:
                {
                    var item = actor.ConsumedItem;
                    if (!actor.RestoreConsumedItem()) { events.Add(new MoveFailedEvent(actor.Id)); return true; }
                    events.Add(new ItemRestoredEvent(actor.Id, item));
                    return true;
                }
                case MoveEffectKind.CopyAbility:
                    if (who == null || who == actor || !who.Ability.HasValue || actor.Ability == who.Ability) { events.Add(new MoveFailedEvent(actor.Id)); return true; }
                    actor.SetAbility(who.Ability);
                    events.Add(new AbilityChangedEvent(actor.Id, who.Ability.Value.Value));
                    return true;
                case MoveEffectKind.SwapAbility:
                {
                    if (who == null || who == actor) { events.Add(new MoveFailedEvent(actor.Id)); return true; }
                    var mine = actor.Ability; var theirs = who.Ability;
                    actor.SetAbility(theirs); who.SetAbility(mine);
                    events.Add(new AbilityChangedEvent(actor.Id, theirs?.Value ?? ""));
                    events.Add(new AbilityChangedEvent(who.Id, mine?.Value ?? ""));
                    return true;
                }
                case MoveEffectKind.SetAbility:
                    if (who == null || who.IsFainted || string.IsNullOrEmpty(effect.Text) || who.Ability.HasValue && who.Ability.Value.Value == effect.Text)
                    { events.Add(new MoveFailedEvent(actor.Id)); return true; }
                    who.SetAbility(new AbilityId(effect.Text));
                    events.Add(new AbilityChangedEvent(who.Id, effect.Text));
                    return true;
                case MoveEffectKind.DelayedHeal:
                {
                    var team = SideOf(actor);
                    if (team == null || team.Delayed.Any(d => d.Heals)) { events.Add(new MoveFailedEvent(actor.Id)); return true; }
                    float pct = effect.Amount.Value > 0 ? effect.Amount.AsFraction : 0.5f;
                    team.Delayed.Add(new DelayedEffect { TurnsLeft = Math.Max(1, effect.Turns), Heals = true, Amount = Math.Max(1, (int)(actor.MaxHp * pct)), MoveId = move.Id.Value, SourceName = actor.Id.Value });
                    events.Add(new DelayedEffectSetEvent(actor.Id, move.Id, true));
                    return true;
                }
                case MoveEffectKind.Stockpile:
                    if (!actor.AddStockpile()) { events.Add(new MoveFailedEvent(actor.Id)); return true; }
                    events.Add(new StockpileEvent(actor.Id, actor.Stockpile));
                    return true;
                case MoveEffectKind.UseStockpile:
                {
                    int n = actor.ClearStockpile();
                    if (effect.Amount.Value > 0 && n > 0) HealPercent(actor, n >= 3 ? 1f : n == 2 ? 0.5f : 0.25f, events);
                    events.Add(new StockpileEvent(actor.Id, 0));
                    return true;
                }
                case MoveEffectKind.TransferStatus:
                {
                    if (who == null || who == actor || !actor.Status.HasValue || who.Status.HasValue) { events.Add(new MoveFailedEvent(actor.Id)); return true; }
                    var st = actor.Status.Value;
                    TryInflictStatus(who, st, events, actor);
                    if (who.Status.HasValue && who.Status.Value == st) { actor.ClearStatus(); events.Add(new StatusFadedEvent(actor.Id, st)); }
                    return true;
                }
                case MoveEffectKind.RaiseRandomStat:
                {
                    var stats = new[] { StatId.Attack, StatId.Defense, StatId.SpAttack, StatId.SpDefense, StatId.Speed, StatId.Accuracy, StatId.Evasion }
                        .Where(s => who.GetStage(s) < Combatant.MaxStage).ToList();
                    if (stats.Count == 0) { events.Add(new MoveFailedEvent(actor.Id)); return true; }
                    ChangeStageFrom(actor, who, stats[_rng.Next(0, stats.Count)], effect.Stages != 0 ? effect.Stages : 2, events);
                    return true;
                }
                case MoveEffectKind.SwapOwnStats:
                {
                    var parts = (string.IsNullOrEmpty(effect.Text) ? "attack,defense" : effect.Text).Split(',');
                    if (parts.Length != 2) return true;
                    actor.SwapBaseStats(new StatId(parts[0].Trim()), new StatId(parts[1].Trim()));
                    events.Add(new StatsSwappedEvent(actor.Id, null, false));
                    return true;
                }
                case MoveEffectKind.SwapStages:
                {
                    if (who == null || who == actor) return true;
                    var list = string.IsNullOrEmpty(effect.Text) ? null : effect.Text.Split(',').Select(t => new StatId(t.Trim())).ToList();
                    var mine = actor.StagesSnapshot(); var theirs = who.StagesSnapshot();
                    var newMine = new Dictionary<StatId, int>(mine); var newTheirs = new Dictionary<StatId, int>(theirs);
                    var keys = list ?? mine.Keys.Concat(theirs.Keys).Distinct().ToList();
                    foreach (var k in keys)
                    {
                        newMine[k] = theirs.TryGetValue(k, out var t) ? t : 0;
                        newTheirs[k] = mine.TryGetValue(k, out var m) ? m : 0;
                    }
                    actor.SetStages(newMine); who.SetStages(newTheirs);
                    events.Add(new StatsSwappedEvent(actor.Id, who.Id, false));
                    return true;
                }
                case MoveEffectKind.CopyStages:
                    if (who == null || who == actor) return true;
                    actor.SetStages(who.StagesSnapshot());
                    events.Add(new StatsSwappedEvent(actor.Id, who.Id, true));
                    return true;
                case MoveEffectKind.SelfFaint:
                    if (actor.IsFainted) return true;
                    actor.TakeDamage(actor.CurrentHp);
                    events.Add(new MonsterFaintedEvent(actor.Id));
                    return true;
                case MoveEffectKind.HealNextSwitchIn:
                {
                    var team = SideOf(actor);
                    if (team != null) team.PendingHealingWish = true;
                    return true;
                }
                case MoveEffectKind.PainSplit:
                {
                    if (who == null || who == actor || who.IsFainted || actor.IsFainted) return true;
                    int avg = (actor.CurrentHp + who.CurrentHp) / 2;
                    int da = avg - actor.CurrentHp, db = avg - who.CurrentHp;
                    if (da > 0) actor.HealHp(da); else actor.TakeDamage(-da);
                    if (db > 0) who.HealHp(db); else who.TakeDamage(-db);
                    if (da != 0) events.Add(new HpChangedEvent(actor.Id, da));
                    if (db != 0) events.Add(new HpChangedEvent(who.Id, db));
                    return true;
                }
                case MoveEffectKind.CallTeamMove:
                {
                    var team = SideOf(actor);
                    var pool = new List<Id<Move>>();
                    if (team != null)
                        foreach (var m in team.Members)
                            if (!ReferenceEquals(m, actor))
                                foreach (var mv in m.Moves)
                                    if (_moves.TryGet(mv, out var mm) && !IsCallingMove(mm) && !HasEffect(mm, MoveEffectKind.CallTeamMove)) pool.Add(mv);
                    CallMove(actor, target, move, pool.Count == 0 ? (Id<Move>?)null : pool[_rng.Next(0, pool.Count)], events);
                    return true;
                }
                case MoveEffectKind.CallMove:
                    CallMove(actor, target, move, string.IsNullOrEmpty(effect.Text) || !_moves.Contains(new Id<Move>(effect.Text)) ? (Id<Move>?)null : new Id<Move>(effect.Text), events);
                    return true;
                case MoveEffectKind.CallTargetMove:
                {
                    var chosen = target == null ? null : (IsOnPlayerSide(target) ? _chosenPlayer : _chosenEnemy) as UseMove;
                    if (chosen == null || target.ActedThisTurn || !_moves.TryGet(chosen.Move, out var tm) || !tm.DealsDirectDamage || IsCallingMove(tm))
                    { events.Add(new MoveFailedEvent(actor.Id)); return true; }
                    _oneShotPowerBoost = 1.5f;
                    try { CallMove(actor, target, move, chosen.Move, events); }
                    finally { _oneShotPowerBoost = 1f; }
                    return true;
                }
                case MoveEffectKind.CallOwnMove:
                {
                    // SONÁMBULO: otro de SUS movimientos al azar (no los que llaman a otros ni los de cargar).
                    var pool = new List<Id<Move>>();
                    foreach (var mv in actor.Moves)
                        if (mv != move.Id && _moves.TryGet(mv, out var mm) && !IsCallingMove(mm) && mm.TwoTurn != TwoTurnKind.Charge
                            && !mm.HasTag("no_sonambulo"))
                            pool.Add(mv);
                    CallMove(actor, target, move, pool.Count == 0 ? (Id<Move>?)null : pool[_rng.Next(0, pool.Count)], events);
                    return true;
                }
                case MoveEffectKind.TeamCureStatus:
                {
                    var team = SideOf(actor);
                    if (team == null) return true;
                    foreach (var m in team.Members)
                        if (!m.IsFainted && m.Status.HasValue) { var was = m.Status.Value; m.ClearStatus(); events.Add(new StatusFadedEvent(m.Id, was)); }
                    return true;
                }
                case MoveEffectKind.DelayedDamage:
                {
                    if (target == null || target == actor) return true;
                    var team = SideOf(target);
                    if (team == null || team.Delayed.Any(d => !d.Heals)) { events.Add(new MoveFailedEvent(actor.Id)); return true; }
                    // El daño se calcula AHORA (con las estadísticas actuales), como en la 4.ª gen.
                    var h = PrepareHit(actor, target, move);
                    int dmg = h.effectiveness <= 0f ? 0 : ComputeHit(actor, target, move, h.effectiveness, h.stab, h.stabMult, h.attackStat, h.defenseStat, h.power, _rng).Damage;
                    team.Delayed.Add(new DelayedEffect { TurnsLeft = Math.Max(1, effect.Turns > 0 ? effect.Turns : 2), Heals = false, Amount = dmg, MoveId = move.Id.Value, SourceName = actor.Id.Value });
                    events.Add(new DelayedEffectSetEvent(actor.Id, move.Id, false));
                    return true;
                }
            }
            return false;
        }

        // ================================================================= FIN DE TURNO

        private void ApplyGen4EndOfTurn(Battle battle, List<IDomainEvent> events)
        {
            // Efectos retardados (Deseo, Premonición).
            foreach (var team in new[] { battle.PlayerTeam, battle.EnemyTeam })
                foreach (var d in new List<DelayedEffect>(team.Delayed))
                {
                    if (--d.TurnsLeft > 0) continue;
                    team.Delayed.Remove(d);
                    var who = team.Active;
                    if (who == null || who.IsFainted) continue;
                    events.Add(new DelayedEffectTriggeredEvent(who.Id, new Id<Move>(d.MoveId), d.Heals));
                    if (d.Heals)
                    {
                        int before = who.CurrentHp;
                        who.HealHp(d.Amount);
                        if (who.CurrentHp > before) events.Add(new HpRestoredEvent(who.Id, who.CurrentHp - before));
                    }
                    else
                    {
                        int dmg = Math.Min(d.Amount, who.CurrentHp);
                        who.TakeDamage(d.Amount);
                        events.Add(new DamageDealtEvent(who.Id, dmg, 1f));
                        if (who.IsFainted) events.Add(new MonsterFaintedEvent(who.Id));
                    }
                }

            foreach (var c in new[] { battle.Player, battle.Enemy })
            {
                if (c == null || c.IsFainted) continue;
                var x = X(c);
                string id = AbilityIdOf(c);
                // PS según el clima (Cura Lluvia, Piel Seca, Poder Solar).
                if (WeatherIsActive)
                    foreach (var (w, pct) in x.WeatherHpChanges)
                        if (string.Equals(w, battle.WeatherId, StringComparison.OrdinalIgnoreCase) && pct != 0f)
                        {
                            events.Add(new AbilityTriggeredEvent(c.Id, id, "clima_ps"));
                            if (pct > 0f && !HasVolatileFlag(c, d => d.Extras.BlocksHealing)) HealPercent(c, pct / 100f, events);
                            else if (pct < 0f && !x.NoIndirectDamage) DamagePercent(c, -pct, events);
                        }
                if (c.IsFainted) continue;
                // Hidratación: con su clima se cura el estado.
                if (!string.IsNullOrEmpty(x.CuresStatusInWeather) && WeatherIsActive && c.Status.HasValue
                    && string.Equals(x.CuresStatusInWeather, battle.WeatherId, StringComparison.OrdinalIgnoreCase))
                {
                    var was = c.Status.Value; c.ClearStatus();
                    events.Add(new AbilityTriggeredEvent(c.Id, id, "cura"));
                    events.Add(new StatusFadedEvent(c.Id, was));
                }
                // Mal Sueño: el rival dormido pierde PS.
                var opp = OpponentOf(c);
                if (x.BadDreamsPercent > 0f && opp != null && !opp.IsFainted && opp.Status.HasValue && opp.Status.Value.Value == "sleep" && !X(opp).NoIndirectDamage)
                {
                    events.Add(new AbilityTriggeredEvent(c.Id, id, "mal_sueño"));
                    DamagePercent(opp, x.BadDreamsPercent, events);
                }
                // Cosecha: puede recuperar la baya (con sol, siempre).
                if (x.HarvestChance > 0f && c.HeldItem == null && !string.IsNullOrEmpty(c.ConsumedItem))
                {
                    bool sun = WeatherIsActive && string.Equals(battle.WeatherId, "sun", StringComparison.OrdinalIgnoreCase);
                    if (sun || _rng.NextFloat() * 100f < x.HarvestChance)
                    {
                        var item = c.ConsumedItem;
                        if (c.RestoreConsumedItem()) { events.Add(new AbilityTriggeredEvent(c.Id, id, "cosecha")); events.Add(new ItemRestoredEvent(c.Id, item)); }
                    }
                }
                // Veleta: sube mucho una al azar y baja otra.
                if (x.Moody)
                {
                    var all = new[] { StatId.Attack, StatId.Defense, StatId.SpAttack, StatId.SpDefense, StatId.Speed };
                    var up = all.Where(s => c.GetStage(s) < Combatant.MaxStage).ToList();
                    if (up.Count > 0)
                    {
                        var u = up[_rng.Next(0, up.Count)];
                        events.Add(new AbilityTriggeredEvent(c.Id, id, "veleta"));
                        int a = c.ChangeStage(u, 2); if (a != 0) events.Add(new StatStageChangedEvent(c.Id, u, a));
                        var down = all.Where(s => s != u && c.GetStage(s) > Combatant.MinStage).ToList();
                        if (down.Count > 0) { var dn = down[_rng.Next(0, down.Count)]; int b = c.ChangeStage(dn, -1); if (b != 0) events.Add(new StatStageChangedEvent(c.Id, dn, b)); }
                    }
                }
                UpdateWeatherType(c, events);
            }

            // Turnos en el campo (Sorpresa, Inicio Lento).
            if (!battle.Player.IsFainted) battle.Player.TickTurnOnField();
            if (!battle.Enemy.IsFainted) battle.Enemy.TickTurnOnField();
        }

        // ================================================================= PESO

        private double WeightOf(Combatant c) => c == null ? 0 : c.WeightKg * X(c).WeightMultiplier;

        private static int PositiveStages(Combatant c)
        {
            int sum = 0;
            foreach (var kv in c.StagesSnapshot()) if (kv.Value > 0) sum += kv.Value;
            return sum;
        }

        // ¿El otro eligió un ataque que hace daño este turno? (Golpe Bajo)
        private bool ChoseAttack(Combatant c)
        {
            var chosen = c == null ? null : (IsOnPlayerSide(c) ? _chosenPlayer : _chosenEnemy) as UseMove;
            return chosen != null && _moves.TryGet(chosen.Move, out var m) && m.DealsDirectDamage;
        }

        // ¿Es un efecto «secundario» (con probabilidad) de un movimiento de daño?
        private static bool IsSecondary(Move move, MoveEffect e)
            => move.DealsDirectDamage && e.Chance.Value < 100f
               && (e.Kind == MoveEffectKind.InflictStatus || e.Kind == MoveEffectKind.ChangeStatStage || e.Kind == MoveEffectKind.Flinch);

        private static bool HasSecondary(Move move)
        {
            foreach (var e in move.SecondaryEffects) if (IsSecondary(move, e)) return true;
            return false;
        }
    }
}
