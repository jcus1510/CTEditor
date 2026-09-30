using System;
using System.Collections.Generic;
using System.Linq;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Rules.Mechanics;
using CTEditor.GameDefinition.Domain.Species;
using CTEditor.GameDefinition.Domain.Stats;
using SpeciesDef = CTEditor.GameDefinition.Domain.Species.Species;

namespace CTEditor.Adventure.Domain
{
    /// <summary>Qué no encaja de un miembro de equipo con el contenido y las reglas actuales.</summary>
    public enum ConflictKind
    {
        MissingSpecies,       // la especie no existe → quitar el miembro
        MissingMove,          // un movimiento no existe → quitarlo (sin ninguno = automáticos)
        MissingItem,          // el objeto no existe → quitarlo
        HeldItemsOff,         // las reglas no tienen objetos equipados → quitarlo
        MegaStoneWithoutMega, // lleva megapiedra y no hay Megaevolución → quitarla
        MissingNature,        // la naturaleza no existe → al azar
        NaturesOff,           // las reglas no tienen naturalezas → quitarla
        GendersOff,           // las reglas no tienen géneros → al azar (sin género)
        ForeignAbility,       // la habilidad no es de su especie → la que le toque
        AbilitiesOff,         // las reglas no tienen habilidades → quitarla
        EvsOff,               // las reglas no tienen EVs → quitarlos
    }

    /// <summary>Un conflicto encontrado y cómo se arregla (el editor lo aplica si el autor lo marca).</summary>
    public sealed class RulesConflict
    {
        public ConflictKind Kind;
        /// <summary>Dónde (id de la ficha: entrenador, equipo o zona).</summary>
        public string Owner = "";
        /// <summary>Posición del miembro (o de la entrada de la zona).</summary>
        public int Index;
        /// <summary>Lo que falla (id del movimiento, del objeto...).</summary>
        public string Value = "";
        public string Description = "";
        public string Fix = "";
    }

    /// <summary>Un miembro tal como está escrito en una ficha (ids; vacío = automático).</summary>
    public sealed class MemberToReview
    {
        public string Owner = "", SpeciesId = "", ItemId = "", NatureId = "", AbilityId = "", Evs = "";
        public int Index;
        public bool GenderFixed;
        public List<string> Moves = new List<string>();
    }

    /// <summary>
    /// ADAPTAR A LAS REGLAS: tras cambiar de generación (o de contenido), revisa los equipos escritos a mano y dice qué ya
    /// no encaja. El juego NUNCA falla por esto (el motor ignora lo que no existe o lo que las reglas apagan), pero así el
    /// autor ve y arregla lo que quedaría «muerto». Puro: se prueba sin Unity.
    /// </summary>
    public static class RulesReview
    {
        public static List<RulesConflict> Review(MemberToReview m, GameData data)
        {
            var list = new List<RulesConflict>();
            void Add(ConflictKind k, string value, string what, string fix)
                => list.Add(new RulesConflict { Kind = k, Owner = m.Owner, Index = m.Index, Value = value ?? "", Description = what, Fix = fix });

            if (!data.TryGetSpecies(new Id<SpeciesDef>(m.SpeciesId ?? ""), out var species))
            {
                Add(ConflictKind.MissingSpecies, m.SpeciesId, $"La especie «{m.SpeciesId}» ya no existe.", "Quitar este miembro del equipo.");
                return list;   // lo demás da igual si el miembro se va
            }
            string who = species.DisplayName;
            foreach (var mv in m.Moves.Where(x => !string.IsNullOrWhiteSpace(x)))
                if (!data.Exists("move", mv))
                    Add(ConflictKind.MissingMove, mv, $"{who}: el movimiento «{mv}» ya no existe.", "Quitarlo (si no le queda ninguno, los elige su IA).");

            var gen = data.Ruleset.Generation;
            if (!string.IsNullOrWhiteSpace(m.ItemId))
            {
                if (!gen.HeldItems)
                    Add(ConflictKind.HeldItemsOff, m.ItemId, $"{who} lleva «{m.ItemId}», pero en estas reglas no hay objetos equipados.", "Quitar el objeto.");
                else if (!data.Exists("item", m.ItemId))
                    Add(ConflictKind.MissingItem, m.ItemId, $"{who}: el objeto «{m.ItemId}» ya no existe.", "Quitar el objeto (su IA le pondrá otro si lo hace).");
                else if (!data.Ruleset.Has(MechanicKind.MegaEvolution) && IsMegaStone(m.ItemId, data))
                    Add(ConflictKind.MegaStoneWithoutMega, m.ItemId, $"{who} lleva la megapiedra «{m.ItemId}», pero la Megaevolución no está activa.", "Quitar la megapiedra.");
            }
            if (!string.IsNullOrWhiteSpace(m.NatureId))
            {
                if (!gen.Natures) Add(ConflictKind.NaturesOff, m.NatureId, $"{who} tiene naturaleza, pero en estas reglas no hay naturalezas.", "Quitarla.");
                else if (!data.Exists("nature", m.NatureId)) Add(ConflictKind.MissingNature, m.NatureId, $"{who}: la naturaleza «{m.NatureId}» ya no existe.", "Quitarla (al azar).");
            }
            if (m.GenderFixed && !gen.Genders)
                Add(ConflictKind.GendersOff, "", $"{who} tiene el género fijado, pero en estas reglas no hay géneros.", "Dejarlo al azar.");
            if (!string.IsNullOrWhiteSpace(m.AbilityId))
            {
                if (!gen.Abilities) Add(ConflictKind.AbilitiesOff, m.AbilityId, $"{who} tiene habilidad elegida, pero en estas reglas no hay habilidades.", "Quitarla.");
                else if (TeamBuilder.AbilitySlotOf(species, new CTEditor.GameDefinition.Domain.Abilities.AbilityId(m.AbilityId.Trim())) < 0)
                    Add(ConflictKind.ForeignAbility, m.AbilityId, $"{who} ya no puede tener la habilidad «{m.AbilityId}».", "Quitarla (la que le toque).");
            }
            if (!string.IsNullOrWhiteSpace(m.Evs) && data.Ruleset.MaxEvPerStat <= 0)
                Add(ConflictKind.EvsOff, m.Evs, $"{who} tiene EVs, pero en estas reglas no hay EVs.", "Quitarlos.");
            return list;
        }

        /// <summary>¿Es megapiedra? (alguna especie megaevoluciona con ella).</summary>
        public static bool IsMegaStone(string itemId, GameData data)
            => data.Species.All.Any(s => s.FormChanges.Any(c => c.Trigger == FormTrigger.MegaEvolution
                && string.Equals(c.Item, itemId, StringComparison.OrdinalIgnoreCase)));
    }
}
