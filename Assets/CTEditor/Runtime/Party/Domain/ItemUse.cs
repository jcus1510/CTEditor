using System;
using System.Collections.Generic;
using CTEditor.GameDefinition.Domain.Items;
using CTEditor.GameDefinition.Domain.Stats;

namespace CTEditor.Party.Domain
{
    /// <summary>Resultado de usar un objeto FUERA del combate: qué pasó, en frases para la interfaz.</summary>
    public sealed class ItemUseResult
    {
        public bool Used { get; internal set; }
        public List<string> Messages { get; } = new List<string>();
        /// <summary>Si el objeto provoca una evolución, la especie destino (la aplica quien llama).</summary>
        public CTEditor.GameDefinition.Domain.Species.Evolution Evolution { get; internal set; }
    }

    /// <summary>
    /// Usar un objeto sobre un monstruo FUERA del combate (desde la mochila): curar, revivir, curar
    /// estados, recuperar PP, cambiar la amistad y detectar evoluciones por objeto. Solo gasta el objeto
    /// si hizo algo (una Poción sobre alguien con la vida llena no se gasta).
    /// </summary>
    public static class ItemUse
    {
        /// <param name="maxPpOf">PP máximos del movimiento en esa posición (los sabe el catálogo de movimientos).</param>
        public static ItemUseResult UseOn(ItemDefinition item, MonsterInstance mon, Bag bag,
            CTEditor.GameDefinition.Domain.Species.Species species = null, Func<int, int> maxPpOf = null,
            EvolutionContext evolutionContext = null)
        {
            var r = new ItemUseResult();
            if (item == null || mon == null) return r;
            if (bag != null && !bag.Has(item.Id)) { r.Messages.Add("No te quedan."); return r; }
            if (!item.UsableOutsideBattle) { r.Messages.Add("Ahora no se puede usar."); return r; }

            bool did = false;

            // Revivir (solo a debilitados).
            if (item.Revives && mon.IsFainted)
            {
                mon.Revive(Math.Max(1, (int)(mon.MaxHp * item.ReviveHpPercent / 100f)));
                r.Messages.Add("¡Se ha recuperado!");
                did = true;
            }

            // Curar PS (no a debilitados).
            if (item.HealsHp && !mon.IsFainted && mon.CurrentHp < mon.MaxHp)
            {
                int before = mon.CurrentHp;
                mon.Heal(item.HealHp + (int)(mon.MaxHp * item.HealPercent / 100f));
                r.Messages.Add($"Recuperó {mon.CurrentHp - before} PS.");
                did = true;
            }

            // Curar estados.
            if (item.CuresStatus && mon.Status.HasValue && !mon.IsFainted &&
                item.CuresThis(mon.Status.Value.Value))
            {
                mon.ClearStatus();
                r.Messages.Add("Se curó su estado.");
                did = true;
            }

            // PP.
            if (item.RestorePp > 0 && maxPpOf != null && RestorePp(mon, item, maxPpOf)) { r.Messages.Add("Recuperó PP."); did = true; }

            // Amistad.
            if (item.FriendshipChange != 0)
            {
                int before = mon.Friendship;
                mon.ChangeFriendship(item.FriendshipChange);
                if (mon.Friendship != before) { r.Messages.Add(item.FriendshipChange > 0 ? "Parece más contento." : "Parece molesto."); did = true; }
            }

            // Evolución por objeto (la aplica quien llama con MonsterInstance.Evolve).
            if (species != null)
            {
                var evo = EvolutionRules.Find(mon, species, EvolutionTrigger.UseItem, item.Id, evolutionContext);
                if (evo != null) { r.Evolution = evo; did = true; }
            }

            if (!did) { r.Messages.Add("No tendría ningún efecto."); return r; }
            r.Used = true;
            if (item.Consumable && bag != null) bag.Remove(item.Id);
            return r;
        }

        private static bool RestorePp(MonsterInstance mon, ItemDefinition item, Func<int, int> maxPpOf)
        {
            int count = mon.Moves.Count;
            var pp = new List<int>();
            for (int i = 0; i < count; i++)
            {
                int max = maxPpOf(i);
                int cur = mon.CurrentPp != null && i < mon.CurrentPp.Count ? mon.CurrentPp[i] : max;
                pp.Add(Math.Min(cur, max));
            }
            bool changed = false;
            for (int i = 0; i < count; i++)
            {
                int max = maxPpOf(i);
                if (pp[i] >= max) continue;
                pp[i] = Math.Min(max, pp[i] + item.RestorePp);
                changed = true;
                if (!item.RestorePpAllMoves) break; // Éter: solo el primero que lo necesite
            }
            if (changed) mon.SetCurrentPp(pp);
            return changed;
        }
    }
}
