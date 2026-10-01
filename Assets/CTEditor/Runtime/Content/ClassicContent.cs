using System;
using System.Collections.Generic;
using System.Linq;

namespace CTEditor.Content
{
    /// <summary>
    /// The classic content the game engine already has (statuses, weathers, hazards, side effects): the packs use them
    /// without a sheet. They count as existing, appear in the lists to be chosen and are shown with their Spanish name.
    /// A row with the same id in the project's sheet replaces it.
    /// </summary>
    public static class ClassicContent
    {
        public static readonly Dictionary<string, (string id, string name)[]> Rows = new Dictionary<string, (string, string)[]>
        {
            [ContentSchemas.Statuses] = new[]
            {
                ("burn", "Quemadura"), ("poison", "Envenenamiento"), ("toxic", "Envenenamiento grave"), ("paralysis", "Parálisis"), ("sleep", "Sueño"),
                ("freeze", "Congelación"), ("confusion", "Confusión"), ("infatuation", "Enamoramiento"), ("drowsy", "Somnolencia (Bostezo)"),
                ("trapped", "Atrapado"), ("cant_escape", "No puede huir"), ("leech_seed", "Drenadoras"), ("curse", "Maldición"), ("nightmare", "Pesadilla"),
                ("taunt", "Mofa"), ("torment", "Tormento"), ("heal_block", "Anticura"), ("embargo", "Embargo"), ("perish_song", "Canto Mortal"),
                ("protect", "Protección"), ("kings_shield", "Escudo Real"), ("spiky_shield", "Barrera Espinosa"), ("baneful_bunker", "Búnker"),
                ("endure", "Aguante"), ("destiny_bond", "Mismo Destino"), ("grudge", "Rabia"), ("ingrain", "Arraigo"), ("aqua_ring", "Acua Aro"),
                ("charge", "Carga"), ("magnet_rise", "Levitón"), ("roost", "Respiro"), ("smacked_down", "Derribado"), ("identified", "Identificado"),
                ("miracle_eye", "Gran Ojo"), ("lock_on", "Fijado"), ("imprison", "Cerca"), ("magic_coat", "Capa Mágica"), ("snatch", "Robo"),
                ("gastro_acid", "Bilis"), ("fairy_lock", "Cerrojo Feérico"),
            },
            [ContentSchemas.Weathers] = new[] { ("rain", "Lluvia"), ("sun", "Sol"), ("sandstorm", "Tormenta de arena"), ("hail", "Granizo") },
            [ContentSchemas.Hazards] = new[] { ("spikes", "Púas"), ("stealth_rock", "Trampa Rocas"), ("toxic_spikes", "Púas Tóxicas"), ("sticky_web", "Red Viscosa") },
            [ContentSchemas.SideEffects] = new[]
            {
                ("reflect", "Reflejo"), ("light_screen", "Pantalla de Luz"), ("aurora_veil", "Velo Aurora"), ("mist", "Neblina"), ("safeguard", "Velo Sagrado"),
                ("tailwind", "Viento Afín"), ("lucky_chant", "Conjuro"), ("gravity", "Gravedad"), ("trick_room", "Espacio Raro"), ("magic_room", "Zona Mágica"),
                ("wonder_room", "Zona Extraña"), ("mud_sport", "Chapoteo Lodo"), ("water_sport", "Hidrochorro"), ("electric_terrain", "Campo Eléctrico"),
                ("grassy_terrain", "Campo de Hierba"), ("misty_terrain", "Campo de Niebla"), ("psychic_terrain", "Campo Psíquico"),
            },
        };

        public static bool Has(string category, string id) =>
            id != null && Rows.TryGetValue(category, out var rows) && rows.Any(r => string.Equals(r.id, id, StringComparison.OrdinalIgnoreCase));

        public static string NameOf(string category, string id) =>
            id != null && Rows.TryGetValue(category, out var rows) ? rows.FirstOrDefault(r => string.Equals(r.id, id, StringComparison.OrdinalIgnoreCase)).name : null;

        /// <summary>The classic rows as records (for lists and templates), except the ones the table already has.</summary>
        public static IEnumerable<ContentRecord> Records(string category, ContentTable table)
        {
            if (!Rows.TryGetValue(category, out var rows)) yield break;
            foreach (var (id, name) in rows)
            {
                if (table != null && table.Contains(id)) continue;
                var r = new ContentRecord();
                r["id"] = id;
                r["nombre"] = name;
                yield return r;
            }
        }
    }
}
