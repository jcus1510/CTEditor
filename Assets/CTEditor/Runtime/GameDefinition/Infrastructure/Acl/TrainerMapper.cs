using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Encounters;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Trainers;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;
using SpeciesDef = CTEditor.GameDefinition.Domain.Species.Species;

namespace CTEditor.GameDefinition.Infrastructure.Acl
{
    /// <summary>ACL de entrenadores, equipos prearmados y zonas: fichas de Unity → dominio.</summary>
    public static class TrainerMapper
    {
        /// <summary>
        /// Un miembro de equipo. Null si no tiene especie (fila vacía). Usa los IDS: el de la referencia si está
        /// viva y, si se rompió (la especie se borró y se volvió a crear), el id guardado de respaldo.
        /// </summary>
        public static TeamMemberSpec ToDomain(TeamMemberData d)
        {
            if (d == null) return null;
            string species = d.SpeciesKey;
            if (string.IsNullOrWhiteSpace(species)) return null;
            var moves = new List<Id<Move>>();
            foreach (var m in d.MoveKeys()) moves.Add(new Id<Move>(m));
            string nature = d.NatureKey;
            return new TeamMemberSpec(new Id<SpeciesDef>(species), d.level, moves,
                (d.heldItem ?? "").Trim(),
                nature.Length > 0 ? new Id<Nature>(nature) : (Id<Nature>?)null,
                d.fixedIvs >= 0 ? d.fixedIvs : (int?)null,
                d.nickname,
                d.gender == MemberGender.Male ? CTEditor.GameDefinition.Domain.Species.Gender.Male
                : d.gender == MemberGender.Female ? CTEditor.GameDefinition.Domain.Species.Gender.Female
                : (CTEditor.GameDefinition.Domain.Species.Gender?)null);
        }

        public static List<TeamMemberSpec> ToDomain(TeamMemberData[] team)
        {
            var list = new List<TeamMemberSpec>();
            if (team == null) return list;
            foreach (var m in team) { var spec = ToDomain(m); if (spec != null) list.Add(spec); }
            return list;
        }

        public static TrainerDefinition ToDomain(TrainerData d)
        {
            if (d == null) throw new ArgumentNullException(nameof(d));
            var bag = new List<(string, int)>();
            if (d.Items != null)
                foreach (var it in d.Items)
                    if (it != null && !string.IsNullOrWhiteSpace(it.itemId)) bag.Add((it.itemId.Trim(), Math.Max(1, it.quantity)));
            var ai = new TrainerAiSettings(d.UseItems, bag, d.HealBelowPercent, d.CanSwitch, d.MovesetStyle);
            return new TrainerDefinition(d.Id, d.DisplayName, ToDomain(d.Team), d.TrainerClass, d.Ai, d.BaseMoney,
                d.IntroLine, d.DefeatLine, d.VictoryLine, ai, d.AiLevel);
        }

        public static TeamPreset ToDomain(TeamPresetData d)
        {
            if (d == null) throw new ArgumentNullException(nameof(d));
            var items = new List<(string, int)>();
            if (d.Items != null)
                foreach (var it in d.Items)
                    if (it != null && !string.IsNullOrWhiteSpace(it.itemId)) items.Add((it.itemId.Trim(), Math.Max(1, it.quantity)));
            return new TeamPreset(d.Id, d.DisplayName, ToDomain(d.Members), d.Description, d.Money, items);
        }

        public static EncounterZone ToDomain(EncounterZoneData d)
        {
            if (d == null) throw new ArgumentNullException(nameof(d));
            var entries = new List<EncounterEntry>();
            if (d.Entries != null)
                foreach (var e in d.Entries)
                    if (e != null && !string.IsNullOrWhiteSpace(e.SpeciesKey))
                        entries.Add(new EncounterEntry(new Id<SpeciesDef>(e.SpeciesKey), e.minLevel, e.maxLevel, e.weight));
            return new EncounterZone(d.Id, d.DisplayName, entries);
        }
    }
}
