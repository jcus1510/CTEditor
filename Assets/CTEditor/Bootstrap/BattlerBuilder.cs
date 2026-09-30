using System.Collections.Generic;
using CTEditor.SharedKernel.Abstractions;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Species;
using CTEditor.GameDefinition.Domain.Growth;
using CTEditor.GameDefinition.Domain.Rules;
using CTEditor.GameDefinition.Infrastructure.Acl;
using CTEditor.Party.Domain;
using CTEditor.Battle.Domain;

namespace CTEditor.Bootstrap
{
    /// <summary>
    /// UN solo lugar que convierte una ficha de autor (BattlerSpec) en un monstruo real, y un
    /// monstruo real en un snapshot de combate. Antes, PartyHolder y BattleScreen repetían esta
    /// lógica; con la genética (IVs, naturaleza, curva) la duplicación iba a crecer, así que la
    /// reunimos aquí (DRY). Vive en Bootstrap porque cruza contextos: lee contenido (Species,
    /// curvas, naturalezas), crea en Party y produce snapshots para Battle.
    /// </summary>
    public static class BattlerBuilder
    {
        /// <summary>
        /// Crea el MonsterInstance de una ficha: curva de su especie (XP inicial coherente), IVs
        /// (fijos si el autor los puso, si no al azar con 'rng'), naturaleza (fija o al azar del
        /// catálogo) y moveset (explícito o learnset). Devuelve null si la ficha está vacía.
        /// </summary>
        public static MonsterInstance Create(BattlerSpec spec, string id, Ruleset rules,
            IStatGrowthFormula growth, IRng rng, out Species species)
        {
            species = null;
            if (spec == null || spec.Species == null) return null;

            species = SpeciesMapper.ToDomain(spec.Species);

            // Curva de la especie (si la eligió y existe en GameContent/Curves).
            GrowthCurve curve = null;
            if (species.GrowthCurveId.HasValue)
                ContentLibrary.Curves.TryGet(species.GrowthCurveId.Value, out curve);

            var nature = spec.FixedNature() ?? RandomNature(rng);

            var mon = MonsterFactory.Create(
                new Id<MonsterInstance>(id), species, spec.Level, rules, growth,
                spec.MoveIds(), curve, rng, nature, spec.FixedIvs);
            mon?.SetHeldItem(spec.HeldItem);
            return mon;
        }

        /// <summary>El snapshot de combate de un monstruo con su estado ACTUAL (J.5).</summary>
        public static BattleParticipant ToSnapshot(MonsterInstance mon, Species species)
            => new BattleParticipant(
                new Id<BattleParticipant>(mon.Id.Value), mon.SpeciesId, mon.Level.Value,
                mon.Stats, mon.CurrentHp, species.Types, mon.Moves, mon.Status, species.AbilityFor(mon.AbilitySlot),
                species.BaseExpYield, species.EvYield, mon.CurrentPp, mon.Friendship, mon.HeldItem, species.CatchRate, species.Dex.WeightKg,
                mon.Gender, species.Evolutions.Count > 0);

        // Una naturaleza al azar entre las autoradas. Si no hay ninguna (o no hay azar), neutra (null).
        private static Nature RandomNature(IRng rng)
        {
            if (rng == null) return null;
            var all = ContentLibrary.Natures.All;
            if (all == null || all.Count == 0) return null;

            int pick = rng.Next(0, all.Count);
            int i = 0;
            foreach (var n in all)
                if (i++ == pick) return n;
            return null;
        }

        /// <summary>
        /// Texto de la genética de un monstruo para el debug del Inspector: naturaleza y, por stat,
        /// IV y EV. Solo lectura; sirve para verificar que todo cargó bien.
        /// </summary>
        public static string DescribeGenetics(MonsterInstance mon)
        {
            if (mon == null) return string.Empty;
            var sb = new System.Text.StringBuilder();
            sb.Append("Naturaleza: ").Append(mon.Nature != null ? Describe(mon.Nature) : "neutra");
            sb.Append("   EVs totales: ").Append(mon.EvTotal).AppendLine();
            foreach (var stat in mon.Stats.Stats)
                sb.Append("  ").Append(stat.Value).Append(": IV ").Append(mon.IvOf(stat))
                  .Append(" / EV ").Append(mon.EvOf(stat)).AppendLine();
            return sb.ToString();
        }

        private static string Describe(Nature n)
        {
            if (n.IsNeutral) return n.DisplayName + " (neutra)";
            string up = n.BoostedStat.HasValue ? "+" + n.BoostedStat.Value.Value : "";
            string down = n.HinderedStat.HasValue ? " -" + n.HinderedStat.Value.Value : "";
            return $"{n.DisplayName} ({up}{down})";
        }
    }
}
