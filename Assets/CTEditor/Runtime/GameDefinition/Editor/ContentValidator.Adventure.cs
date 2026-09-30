using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// Validación de lo que se usa para JUGAR combates: entrenadores, equipos prearmados y zonas
    /// salvajes. Avisa de equipos vacíos, niveles fuera de las reglas, más miembros de los que caben,
    /// movimientos que la especie no tiene y objetos que no existen.
    /// </summary>
    public static partial class ContentValidator
    {
        private static void ValidateAdventure(List<RulesetData> rulesets, List<ValidationIssue> issues)
        {
            var rules = rulesets.FirstOrDefault();
            int maxParty = rules != null ? rules.MaxPartySize : 6;
            int maxMoves = rules != null ? rules.MaxMovesPerMonster : 4;
            int levelCap = rules != null ? rules.LevelCap : 100;
            var itemIds = new HashSet<string>(LoadAll<ItemData>().Where(i => !string.IsNullOrEmpty(i.Id)).Select(i => i.Id));

            var trainers = LoadAll<TrainerData>();
            CollectIds(trainers, t => t.Id, "entrenador", issues);
            foreach (var t in trainers)
            {
                ValidateTeam(t.Team, t, $"El entrenador '{Name(t)}'", maxParty, maxMoves, levelCap, itemIds, issues);
                if (t.BaseMoney == 0) issues.Add(Warning($"El entrenador '{Name(t)}' no da dinero al perder (dinero base 0).", t));
                // Su mochila: objetos que existan y que sirvan en combate.
                foreach (var it in t.Items ?? new BagEntryData[0])
                {
                    if (it == null || string.IsNullOrWhiteSpace(it.itemId)) continue;
                    var item = LoadAll<ItemData>().FirstOrDefault(x => x.Id == it.itemId);
                    if (item == null) issues.Add(Error($"El entrenador '{Name(t)}' lleva en la mochila '{it.itemId}', que no existe.", t));
                    else if (!item.UsableInBattle) issues.Add(Warning($"El entrenador '{Name(t)}' lleva '{item.DisplayName}', que no se puede usar en combate: nunca lo usará.", t));
                }
            }

            var teams = LoadAll<TeamPresetData>();
            CollectIds(teams, t => t.Id, "equipo prearmado", issues);
            foreach (var team in teams)
            {
                ValidateTeam(team.Members, team, $"El equipo '{Name(team)}'", maxParty, maxMoves, levelCap, itemIds, issues);
                if (team.Items != null)
                    foreach (var it in team.Items)
                        if (it != null && !string.IsNullOrWhiteSpace(it.itemId) && !itemIds.Contains(it.itemId.Trim()))
                            issues.Add(Warning($"El equipo '{Name(team)}' lleva en la mochila '{it.itemId}', que no existe.", team));
            }

            var zones = LoadAll<EncounterZoneData>();
            CollectIds(zones, z => z.Id, "zona salvaje", issues);
            foreach (var z in zones)
            {
                if (z.Entries == null || z.Entries.Count(e => e != null && e.SpeciesKey.Length > 0) == 0)
                { issues.Add(Error($"La zona '{Name(z)}' no tiene ninguna especie.", z)); continue; }
                foreach (var e in z.Entries)
                {
                    if (e == null) continue;
                    if (e.species == null && e.SpeciesKey.Length > 0) issues.Add(Warning($"La zona '{Name(z)}': '{e.SpeciesKey}' tiene la referencia rota (se usa su id). Pulsa «🔗 Reenlazar por id».", z));
                    else if (e.species == null) issues.Add(Warning($"La zona '{Name(z)}' tiene una fila sin especie (se ignora).", z));
                    else if (e.minLevel > e.maxLevel) issues.Add(Warning($"La zona '{Name(z)}': {Name(e.species)} tiene el nivel mínimo ({e.minLevel}) mayor que el máximo ({e.maxLevel}); se intercambian.", z));
                    else if (e.maxLevel > levelCap) issues.Add(Warning($"La zona '{Name(z)}': {Name(e.species)} pasa del nivel máximo del juego ({levelCap}).", z));
                }
            }
        }

        private static string Name(TrainerData t) => string.IsNullOrWhiteSpace(t.DisplayName) ? (string.IsNullOrWhiteSpace(t.Id) ? t.name : t.Id) : t.DisplayName;
        private static string Name(TeamPresetData t) => string.IsNullOrWhiteSpace(t.DisplayName) ? (string.IsNullOrWhiteSpace(t.Id) ? t.name : t.Id) : t.DisplayName;
        private static string Name(EncounterZoneData z) => string.IsNullOrWhiteSpace(z.DisplayName) ? (string.IsNullOrWhiteSpace(z.Id) ? z.name : z.Id) : z.DisplayName;

        private static void ValidateTeam(TeamMemberData[] team, Object owner, string who, int maxParty, int maxMoves, int levelCap,
            HashSet<string> itemIds, List<ValidationIssue> issues)
        {
            // Referencias rotas: si hay id de respaldo el juego lo usa igual (aviso); si no, ese miembro se pierde (error).
            foreach (var m in team ?? new TeamMemberData[0])
            {
                if (m == null || m.species != null) continue;
                if (m.SpeciesKey.Length > 0)
                    issues.Add(Warning($"{who}: el miembro '{m.SpeciesKey}' tiene la referencia rota (se usa su id). Pulsa «🔗 Reenlazar por id».", owner));
                else
                    issues.Add(Error($"{who} tiene un miembro SIN especie ni id (se ignora). Reimporta su CSV o elige la especie.", owner));
            }
            var members = (team ?? new TeamMemberData[0]).Where(m => m != null && m.species != null).ToList();
            if (members.Count == 0 && (team ?? new TeamMemberData[0]).All(m => m == null || m.SpeciesKey.Length == 0))
            { issues.Add(Error($"{who} no tiene ningún miembro con especie.", owner)); return; }
            if (members.Count > maxParty) issues.Add(Warning($"{who} tiene {members.Count} miembros; las reglas permiten {maxParty} (los demás no combaten).", owner));

            foreach (var m in members)
            {
                string mon = Name(m.species);
                if (m.level > levelCap) issues.Add(Warning($"{who}: {mon} está a nivel {m.level}, por encima del máximo ({levelCap}).", owner));
                var moves = (m.moves ?? new MoveData[0]).Where(x => x != null).ToList();
                if (moves.Count > maxMoves) issues.Add(Warning($"{who}: {mon} tiene {moves.Count} movimientos; solo se usan los {maxMoves} primeros.", owner));
                if (moves.Count == 0 && (m.species.Learnset == null || !m.species.Learnset.Any(l => l.move != null && l.level <= m.level)))
                    issues.Add(Error($"{who}: {mon} no tiene movimientos elegidos ni aprende ninguno hasta el nivel {m.level}: no podría luchar.", owner));
                if (!string.IsNullOrWhiteSpace(m.heldItem) && !itemIds.Contains(m.heldItem.Trim()))
                    issues.Add(Warning($"{who}: {mon} lleva '{m.heldItem}', que no existe.", owner));
            }
        }
    }
}
