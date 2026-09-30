using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Abilities;
using CTEditor.GameDefinition.Domain.Items;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Status;
using CTEditor.Party.Domain;
using SpeciesDef = CTEditor.GameDefinition.Domain.Species.Species;

namespace CTEditor.Adventure.Domain
{
    /// <summary>
    /// Lo que pasó al hacer algo con el equipo FUERA del combate: si se hizo, las frases para la
    /// interfaz, si hubo evolución y los movimientos que quiere aprender y no caben (hay que preguntar).
    /// </summary>
    public sealed class FieldResult
    {
        public bool Done { get; internal set; }
        public List<string> Messages { get; } = new List<string>();
        /// <summary>Si evolucionó: de qué especie a cuál.</summary>
        public (Id<SpeciesDef> from, Id<SpeciesDef> to)? Evolved { get; internal set; }
        /// <summary>Movimientos que quiere aprender pero ya sabe 4: pregunta con FieldActions.LearnMove.</summary>
        public List<Id<Move>> PendingMoves { get; } = new List<Id<Move>>();

        internal static FieldResult Fail(string message)
        {
            var r = new FieldResult();
            r.Messages.Add(message);
            return r;
        }

        public override string ToString() => string.Join(" ", Messages);
    }

    /// <summary>
    /// USAR EL EQUIPO Y LA MOCHILA FUERA DEL COMBATE (el menú de pausa de los juegos):
    ///   • Usar un objeto de la mochila sobre un miembro (Poción, Revivir, Antídoto, Éter, piedras...).
    ///   • Evolucionar con una piedra (y aprender lo que la especie nueva aprende a su nivel).
    ///   • Dar un objeto para que lo lleve equipado, o quitárselo (vuelve a la mochila).
    ///   • Cambiar el orden del equipo (el primero es el que sale a combatir).
    ///   • Ver el RESUMEN de un monstruo: estadísticas, IV/EV, naturaleza, movimientos con PP...
    /// Dominio puro: sin Unity. La interfaz solo llama y enseña los mensajes.
    /// </summary>
    public static class FieldActions
    {
        // ---------------- Qué se puede usar ----------------

        /// <summary>Objetos de la mochila que se pueden usar fuera del combate (con su cantidad).</summary>
        public static List<(ItemDefinition item, int count)> UsableItems(GameData data, PlayerSave save)
        {
            var list = new List<(ItemDefinition, int)>();
            foreach (var (id, count) in save.Bag.Contents())
                if (count > 0 && data.TryGetItem(id, out var item) && item.UsableOutsideBattle) list.Add((item, count));
            return list;
        }

        /// <summary>Objetos de la mochila que se pueden dar para llevar equipados (todos menos los clave).</summary>
        public static List<(ItemDefinition item, int count)> GivableItems(GameData data, PlayerSave save)
        {
            var list = new List<(ItemDefinition, int)>();
            if (!data.Ruleset.Generation.HeldItems) return list;   // reglas de generación: sin objetos equipados
            foreach (var (id, count) in save.Bag.Contents())
                if (count > 0 && data.TryGetItem(id, out var item) && CanBeHeld(item)) list.Add((item, count));
            return list;
        }

        /// <summary>¿Se puede llevar equipado? Como en los juegos: todo menos los objetos clave.</summary>
        public static bool CanBeHeld(ItemDefinition item) => item != null && item.Category != ItemCategory.Key;

        // ---------------- Usar un objeto ----------------

        /// <summary>
        /// Usa un objeto de la mochila sobre el miembro 'partyIndex'. Solo se gasta si hizo algo.
        /// Si es una piedra evolutiva que le sirve, evoluciona en el acto.
        /// </summary>
        public static FieldResult UseItem(GameData data, PlayerSave save, int partyIndex, string itemId)
        {
            if (!TryMember(save, partyIndex, out var mon, out var fail)) return fail;
            if (!data.TryGetItem(itemId, out var item)) return FieldResult.Fail($"El objeto '{itemId}' no existe.");
            if (!save.Bag.Has(item.Id)) return FieldResult.Fail($"No te quedan {item.DisplayName}.");

            var species = data.SpeciesOf(mon);

            // VARIANTES: un objeto que cambia de variante (Gracídea, Espejo Veraz...) no se gasta.
            var variant = VariantTargetFor(data, species, item.Id);
            if (variant != null)
            {
                var vr = new FieldResult { Done = true };
                string vn = data.NameOf(mon);
                vr.Messages.Add($"Usaste {item.DisplayName} en {vn}.");
                ChangeInto(data, mon, variant, vr, vn);
                return vr;
            }

            var use = ItemUse.UseOn(item, mon, save.Bag, species, slot => slot < mon.Moves.Count ? data.MaxPpOf(mon.Moves[slot]) : 1,
                data.EvolutionContextFor(save));

            var r = new FieldResult { Done = use.Used };
            string name = data.NameOf(mon);
            r.Messages.Add($"Usaste {item.DisplayName} en {name}.");
            if (!use.Used) { r.Messages.Clear(); r.Messages.Add($"{item.DisplayName}: {string.Join(" ", use.Messages)}"); return r; }
            r.Messages.AddRange(use.Messages);

            if (use.Evolution != null)
            {
                if (!data.TryGetSpecies(use.Evolution.Target, out var target))
                {
                    // Contenido roto: no evoluciona y se devuelve el objeto.
                    if (item.Consumable) save.Bag.Add(item.Id);
                    r.Messages.Add($"Iba a evolucionar en '{use.Evolution.Target.Value}', pero esa especie no existe.");
                    r.Done = use.Messages.Count > 0;
                    return r;
                }
                Evolve(data, mon, target, r, name);
            }
            return r;
        }

        /// <summary>
        /// ¿A qué variante cambia 'species' con este objeto? Sobre la especie base (o otra variante de su familia) → la
        /// variante que se activa con ese objeto; sobre esa misma variante → vuelve a la base. Null = el objeto no cambia
        /// de variante a esta especie.
        /// </summary>
        public static SpeciesDef VariantTargetFor(GameData data, SpeciesDef species, string itemId)
        {
            if (species == null || string.IsNullOrWhiteSpace(itemId)) return null;
            var root = species.FormOf ?? species.Id;
            if (species.FormOf.HasValue && string.Equals(species.VariantItem, itemId, StringComparison.OrdinalIgnoreCase))
                return data.TryGetSpecies(root, out var baseSpecies) ? baseSpecies : null;
            foreach (var s in data.Species.All)
                if (s.FormOf.HasValue && s.FormOf.Value == root && s.Id != species.Id
                    && string.Equals(s.VariantItem, itemId, StringComparison.OrdinalIgnoreCase))
                    return s;
            return null;
        }

        /// <summary>Las variantes de la familia de 'species' (la base incluida), para elegir una con un personaje.</summary>
        public static List<SpeciesDef> VariantsOf(GameData data, SpeciesDef species)
        {
            var list = new List<SpeciesDef>();
            if (species == null) return list;
            var root = species.FormOf ?? species.Id;
            if (data.TryGetSpecies(root, out var baseSpecies)) list.Add(baseSpecies);
            foreach (var s in data.Species.All)
                if (s.FormOf.HasValue && s.FormOf.Value == root) list.Add(s);
            return list;
        }

        /// <summary>
        /// Cambia al miembro a otra VARIANTE de su familia (Rotom → Rotom Lavado al hablar con un personaje). Falla si la
        /// especie destino no es de su familia.
        /// </summary>
        public static FieldResult ChangeVariant(GameData data, PlayerSave save, int partyIndex, string speciesId)
        {
            if (!TryMember(save, partyIndex, out var mon, out var fail)) return fail;
            var species = data.SpeciesOf(mon);
            SpeciesDef target = null;
            foreach (var v in VariantsOf(data, species))
                if (string.Equals(v.Id.Value, speciesId, StringComparison.OrdinalIgnoreCase)) target = v;
            string name = data.NameOf(mon);
            if (target == null) return FieldResult.Fail($"{name} no puede cambiar a '{speciesId}'.");
            if (target.Id == species.Id) return FieldResult.Fail($"{name} ya es {target.DisplayName}.");
            var r = new FieldResult { Done = true };
            ChangeInto(data, mon, target, r, name);
            return r;
        }

        // Cambio de variante: misma genética, datos de la otra especie.
        private static void ChangeInto(GameData data, MonsterInstance mon, SpeciesDef target, FieldResult r, string oldName)
        {
            mon.Evolve(target.Id, target.BaseStats, data.Growth);
            r.Messages.Add($"¡{oldName} cambió a {target.DisplayName}!");
        }

        // Evoluciona YA (las piedras no se cancelan) y aprende lo de su nivel en la especie nueva.
        private static void Evolve(GameData data, MonsterInstance mon, SpeciesDef target, FieldResult r, string oldName)
        {
            var from = mon.SpeciesId;
            mon.Evolve(target.Id, target.BaseStats, data.Growth);
            r.Evolved = (from, target.Id);
            r.Messages.Add($"¡{oldName} evolucionó en {target.DisplayName}!");
            if (!data.Rules.LearnMovesOnLevelUp) return;
            foreach (var lm in target.Learnset)
            {
                if (lm.Level != mon.Level.Value || mon.KnowsMove(lm.Move) || !data.Moves.Contains(lm.Move)) continue;
                if (mon.Moves.Count < 4)
                {
                    mon.LearnMove(mon.Moves.Count, lm.Move, data.MaxPpOf(lm.Move));
                    r.Messages.Add($"¡{data.NameOf(mon)} aprendió {data.MoveName(lm.Move)}!");
                }
                else r.PendingMoves.Add(lm.Move);
            }
        }

        /// <summary>
        /// Aprende un movimiento pendiente: 'forgetSlot' (0-3) es el que olvida; -1 = no aprenderlo.
        /// Con hueco libre se añade sin olvidar nada.
        /// </summary>
        public static FieldResult LearnMove(GameData data, PlayerSave save, int partyIndex, Id<Move> move, int forgetSlot)
        {
            if (!TryMember(save, partyIndex, out var mon, out var fail)) return fail;
            string name = data.NameOf(mon);
            if (mon.KnowsMove(move)) return FieldResult.Fail($"{name} ya sabe {data.MoveName(move)}.");
            if (forgetSlot < 0) return new FieldResult { Done = true, Messages = { $"{name} no aprendió {data.MoveName(move)}." } };

            int slot = mon.Moves.Count < 4 ? mon.Moves.Count : forgetSlot;
            if (slot >= 4 || slot > mon.Moves.Count) return FieldResult.Fail("Elige uno de sus 4 movimientos para olvidarlo.");
            var old = slot < mon.Moves.Count ? mon.Moves[slot] : (Id<Move>?)null;
            if (!mon.LearnMove(slot, move, data.MaxPpOf(move))) return FieldResult.Fail("No pudo aprenderlo.");
            var r = new FieldResult { Done = true };
            if (old.HasValue) r.Messages.Add($"1, 2 y... ¡puf! {name} olvidó {data.MoveName(old.Value)}.");
            r.Messages.Add($"¡{name} aprendió {data.MoveName(move)}!");
            return r;
        }

        // ---------------- Objetos equipados ----------------

        /// <summary>Da un objeto de la mochila al miembro. Si ya llevaba uno, se cambian (el viejo vuelve a la mochila).</summary>
        public static FieldResult GiveItem(GameData data, PlayerSave save, int partyIndex, string itemId)
        {
            if (!TryMember(save, partyIndex, out var mon, out var fail)) return fail;
            if (!data.Ruleset.Generation.HeldItems) return FieldResult.Fail("En este juego los monstruos no pueden llevar objetos.");
            if (!data.TryGetItem(itemId, out var item)) return FieldResult.Fail($"El objeto '{itemId}' no existe.");
            if (!CanBeHeld(item)) return FieldResult.Fail($"{item.DisplayName} es un objeto clave: no se puede llevar.");
            if (!save.Bag.Has(item.Id)) return FieldResult.Fail($"No te quedan {item.DisplayName}.");
            string name = data.NameOf(mon);
            if (mon.HeldItem == item.Id) return FieldResult.Fail($"{name} ya lleva {item.DisplayName}.");

            save.Bag.Remove(item.Id);
            var previous = mon.SetHeldItem(item.Id);
            var r = new FieldResult { Done = true };
            if (!string.IsNullOrEmpty(previous))
            {
                save.Bag.Add(previous);
                r.Messages.Add($"{name} llevaba {data.ItemName(previous)}: vuelve a la mochila.");
            }
            r.Messages.Add($"{name} ahora lleva {item.DisplayName}.");
            if (!item.HasHeldEffect) r.Messages.Add("(Este objeto no hace nada equipado; solo lo guarda.)");
            return r;
        }

        /// <summary>Le quita el objeto equipado y lo guarda en la mochila.</summary>
        public static FieldResult TakeItem(GameData data, PlayerSave save, int partyIndex)
        {
            if (!TryMember(save, partyIndex, out var mon, out var fail)) return fail;
            string name = data.NameOf(mon);
            if (string.IsNullOrEmpty(mon.HeldItem)) return FieldResult.Fail($"{name} no lleva nada.");
            var held = mon.SetHeldItem(null);
            save.Bag.Add(held);
            return new FieldResult { Done = true, Messages = { $"Guardaste {data.ItemName(held)} de {name} en la mochila." } };
        }

        // ---------------- Orden del equipo ----------------

        /// <summary>Cambia de sitio dos miembros (el primero del equipo es el que sale a combatir).</summary>
        public static FieldResult Swap(GameData data, PlayerSave save, int a, int b)
        {
            if (!TryMember(save, a, out var ma, out var fail) || !TryMember(save, b, out var mb, out fail)) return fail;
            var res = save.Party.Swap(a, b);
            if (!res.IsSuccess) return FieldResult.Fail(res.Error);
            return new FieldResult { Done = true, Messages = { $"{data.NameOf(ma)} y {data.NameOf(mb)} cambiaron de sitio." } };
        }

        // ---------------- Resumen ----------------

        /// <summary>La ficha completa de un monstruo, lista para enseñar.</summary>
        public static MonsterSummary Summary(GameData data, MonsterInstance mon) => MonsterSummary.Of(data, mon);

        private static bool TryMember(PlayerSave save, int index, out MonsterInstance mon, out FieldResult fail)
        {
            mon = null; fail = null;
            if (save == null || index < 0 || index >= save.Party.Count) { fail = FieldResult.Fail("No hay nadie en esa posición del equipo."); return false; }
            mon = save.Party.Members[index];
            return true;
        }
    }

    /// <summary>
    /// RESUMEN de un monstruo (la pantalla de "Datos" de los juegos): identidad, PS, experiencia,
    /// estadísticas con sus IV/EV y el efecto de la naturaleza, movimientos con sus PP y el objeto.
    /// </summary>
    public sealed class MonsterSummary
    {
        public sealed class StatLine
        {
            public StatId Stat { get; internal set; }
            public string Name { get; internal set; }
            public int Value { get; internal set; }
            public int Iv { get; internal set; }
            public int Ev { get; internal set; }
            /// <summary>+1 = la naturaleza la sube, −1 = la baja, 0 = neutra.</summary>
            public int NatureEffect { get; internal set; }
        }

        public sealed class MoveLine
        {
            public Id<Move> Move { get; internal set; }
            public string Name { get; internal set; }
            public string TypeName { get; internal set; }
            public string Category { get; internal set; }
            public int Power { get; internal set; }
            public int Pp { get; internal set; }
            public int MaxPp { get; internal set; }
        }

        public string Name { get; private set; }
        public string SpeciesName { get; private set; }
        /// <summary>«Nº 001 · Pokémon Semilla · 0,7 m · 6,9 kg» (vacío si la especie no tiene datos de Pokédex).</summary>
        public string Dex { get; private set; } = "";
        public List<string> Types { get; } = new List<string>();
        public int Level { get; private set; }
        public int Hp { get; private set; }
        public int MaxHp { get; private set; }
        public string StatusName { get; private set; }      // vacío = sano
        public int Experience { get; private set; }
        public int ExperienceToNext { get; private set; }   // 0 = nivel máximo
        public string NatureName { get; private set; }
        public string AbilityName { get; private set; }
        public string HeldItemName { get; private set; }    // vacío = nada
        public int Friendship { get; private set; }
        public List<StatLine> Stats { get; } = new List<StatLine>();
        public List<MoveLine> Moves { get; } = new List<MoveLine>();
        public int EvTotal { get; private set; }
        /// <summary>Género del individuo.</summary>
        public CTEditor.GameDefinition.Domain.Species.Gender Gender { get; private set; }

        private static readonly (StatId id, string name)[] StatNames =
        {
            (StatId.Hp, "PS"), (StatId.Attack, "Ataque"), (StatId.Defense, "Defensa"),
            (StatId.SpAttack, "At. Esp."), (StatId.SpDefense, "Def. Esp."), (StatId.Speed, "Velocidad"),
        };

        internal static MonsterSummary Of(GameData data, MonsterInstance mon)
        {
            if (mon == null) throw new ArgumentNullException(nameof(mon));
            var species = data.SpeciesOf(mon);
            var s = new MonsterSummary
            {
                Name = data.NameOf(mon),
                Gender = mon.Gender,
                SpeciesName = species != null ? species.DisplayName : mon.SpeciesId.Value,
                Level = mon.Level.Value,
                Hp = mon.CurrentHp,
                MaxHp = mon.MaxHp,
                Experience = mon.Experience.Value,
                Friendship = mon.Friendship,
                EvTotal = mon.EvTotal,
                NatureName = mon.Nature != null ? mon.Nature.DisplayName : "—",
                HeldItemName = string.IsNullOrEmpty(mon.HeldItem) ? "" : data.ItemName(mon.HeldItem),
                StatusName = "",
                AbilityName = "—",
            };
            if (species != null)
            {
                foreach (var t in species.Types) s.Types.Add(data.TypeName(t));
                if (species.Dex.Number > 0 || species.Dex.HeightM > 0) s.Dex = species.Dex.Summary();
                var abId = species.AbilityFor(mon.AbilitySlot);
                if (abId.HasValue)
                    s.AbilityName = (data.Abilities.TryGet(new Id<AbilityDefinition>(abId.Value.Value), out var ab) ? ab.DisplayName : abId.Value.Value)
                                    + (mon.AbilitySlot == 2 && species.HiddenAbility.HasValue ? " (oculta)" : "");
                var curve = data.CurveFor(species);
                int max = Math.Min(data.Ruleset.LevelCap, curve.MaxLevel);
                s.ExperienceToNext = mon.Level.Value >= max ? 0 : Math.Max(0, curve.XpRemainingToLevel(mon.Experience.Value, mon.Level.Value + 1));
            }
            if (mon.Status.HasValue)
                s.StatusName = data.Statuses.TryGet(new Id<StatusConditionDefinition>(mon.Status.Value.Value), out var st) ? st.DisplayName : mon.Status.Value.Value;

            foreach (var (id, name) in StatNames)
            {
                int pct = mon.Nature != null ? mon.Nature.PercentFor(id) : 100;
                s.Stats.Add(new StatLine
                {
                    Stat = id, Name = name, Value = mon.Stats.Of(id), Iv = mon.IvOf(id), Ev = mon.EvOf(id),
                    NatureEffect = pct > 100 ? 1 : pct < 100 ? -1 : 0,
                });
            }

            for (int i = 0; i < mon.Moves.Count; i++)
            {
                var id = mon.Moves[i];
                int max = data.MaxPpOf(id);
                int cur = mon.CurrentPp != null && i < mon.CurrentPp.Count ? Math.Min(mon.CurrentPp[i], max) : max;
                var line = new MoveLine { Move = id, Name = data.MoveName(id), Pp = cur, MaxPp = max, TypeName = "", Category = "" };
                if (data.Moves.TryGet(id, out var mv))
                {
                    line.TypeName = data.TypeName(mv.Type);
                    line.Power = mv.Power;
                    line.Category = mv.Category == MoveCategory.Physical ? "Físico" : mv.Category == MoveCategory.Special ? "Especial" : "Estado";
                }
                s.Moves.Add(line);
            }
            return s;
        }

        /// <summary>El resumen en frases (para una interfaz sencilla o para depurar).</summary>
        public List<string> Lines()
        {
            var l = new List<string>
            {
                $"{Name}{(Name != SpeciesName ? $" ({SpeciesName})" : "")} {CTEditor.GameDefinition.Domain.Species.GenderText.Symbol(Gender)} · Nv. {Level} · {string.Join("/", Types)}",
                $"PS {Hp}/{MaxHp}{(StatusName.Length > 0 ? $" · {StatusName}" : "")}",
                ExperienceToNext > 0 ? $"Experiencia {Experience} · le faltan {ExperienceToNext} para subir" : $"Experiencia {Experience} · nivel máximo",
                $"Naturaleza {NatureName} · Habilidad {AbilityName} · Amistad {Friendship}",
                $"Objeto: {(HeldItemName.Length > 0 ? HeldItemName : "nada")}",
            };
            foreach (var st in Stats)
                l.Add($"{st.Name}: {st.Value}{(st.NatureEffect > 0 ? " ▲" : st.NatureEffect < 0 ? " ▼" : "")}  (IV {st.Iv} · EV {st.Ev})");
            l.Add($"EV totales: {EvTotal}");
            foreach (var m in Moves)
                l.Add($"• {m.Name} [{m.TypeName} · {m.Category}{(m.Power > 0 ? $" · {m.Power}" : "")}] PP {m.Pp}/{m.MaxPp}");
            return l;
        }
    }
}
