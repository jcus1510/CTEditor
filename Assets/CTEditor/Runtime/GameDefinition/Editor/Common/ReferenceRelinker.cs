using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// 🔗 REENLAZAR POR ID: vuelve a enlazar las referencias rotas de los equipos (entrenadores y equipos
    /// prearmados) y de las zonas salvajes usando el id que guardan de respaldo.
    ///
    /// Por qué pasa: una referencia de Unity apunta al ARCHIVO (GUID). Si una especie o un movimiento se borra y
    /// se vuelve a crear (o se reimporta un pack, o se vuelve a una versión anterior del proyecto), el archivo
    /// nuevo tiene otro GUID y la referencia queda «Missing». El id (bulbasaur, tackle...) sigue siendo el mismo.
    ///
    /// También rellena el id de respaldo de las referencias vivas (fichas antiguas que aún no lo tenían).
    /// </summary>
    public static class ReferenceRelinker
    {
        public sealed class Report
        {
            public int Relinked, IdsFilled, Assets;
            public readonly List<string> Unresolved = new List<string>();

            public override string ToString()
            {
                var s = $"Fichas revisadas: {Assets}\nReferencias reenlazadas: {Relinked}\nIds de respaldo rellenados: {IdsFilled}";
                if (Unresolved.Count > 0)
                    s += $"\n\nNo se pudieron reenlazar ({Unresolved.Count}): el id no existe en el proyecto. Crea o importa esas fichas " +
                         "(p. ej. el pack) y vuelve a pulsar:\n• " + string.Join("\n• ", Unresolved.Take(25)) + (Unresolved.Count > 25 ? "\n…" : "");
                return s;
            }
        }

        [MenuItem(EditorMenus.Tools + "🔗 Reenlazar referencias por id", false, EditorMenus.ToolsOrder + 4)]
        public static void RunFromMenu()
        {
            var r = RelinkAll();
            EditorUtility.DisplayDialog("Reenlazar por id", r.ToString(), "Vale");
        }

        /// <summary>Reenlaza entrenadores, equipos prearmados y zonas. Devuelve qué hizo.</summary>
        public static Report RelinkAll()
        {
            var r = new Report();
            foreach (var t in ContentAssets.LoadAll<TrainerData>()) RelinkTeam(t, "team", r);
            foreach (var t in ContentAssets.LoadAll<TeamPresetData>()) RelinkTeam(t, "members", r);
            foreach (var z in ContentAssets.LoadAll<EncounterZoneData>()) RelinkZone(z, r);
            AssetDatabase.SaveAssets();
            return r;
        }

        private static void RelinkTeam(ScriptableObject owner, string field, Report r)
        {
            r.Assets++;
            string who = ContentAssets.KeyOf(owner);
            ContentAssets.Edit(owner, so =>
            {
                var team = so.FindProperty(field);
                if (team == null) return;
                for (int i = 0; i < team.arraySize; i++)
                {
                    var el = team.GetArrayElementAtIndex(i);
                    Link<SpeciesData>(el.FindPropertyRelative("species"), el.FindPropertyRelative("speciesId"), $"{who}: especie", r);
                    Link<NatureData>(el.FindPropertyRelative("nature"), el.FindPropertyRelative("natureId"), $"{who}: naturaleza", r);

                    var moves = el.FindPropertyRelative("moves");
                    var ids = el.FindPropertyRelative("moveIds");
                    if (moves == null || ids == null) continue;
                    // Si solo quedan los ids (todas las referencias se perdieron), se recrea la lista de movimientos.
                    if (moves.arraySize < ids.arraySize) moves.arraySize = ids.arraySize;
                    if (ids.arraySize < moves.arraySize) ids.arraySize = moves.arraySize;
                    for (int k = 0; k < moves.arraySize; k++)
                        Link<MoveData>(moves.GetArrayElementAtIndex(k), ids.GetArrayElementAtIndex(k), $"{who}: movimiento", r);
                }
            });
        }

        private static void RelinkZone(EncounterZoneData zone, Report r)
        {
            r.Assets++;
            string who = ContentAssets.KeyOf(zone);
            ContentAssets.Edit(zone, so =>
            {
                var entries = so.FindProperty("entries");
                if (entries == null) return;
                for (int i = 0; i < entries.arraySize; i++)
                {
                    var el = entries.GetArrayElementAtIndex(i);
                    Link<SpeciesData>(el.FindPropertyRelative("species"), el.FindPropertyRelative("speciesId"), $"{who}: especie", r);
                }
            });
        }

        // Referencia viva → guarda su id. Referencia vacía o rota con id → la busca por id y la enlaza.
        private static void Link<T>(SerializedProperty reference, SerializedProperty id, string what, Report r)
            where T : ScriptableObject, IContentAsset
        {
            if (reference == null || id == null) return;
            if (reference.objectReferenceValue is T live && !string.IsNullOrWhiteSpace(live.Id))
            {
                if (id.stringValue != live.Id) { id.stringValue = live.Id; r.IdsFilled++; }
                return;
            }
            string key = (id.stringValue ?? "").Trim();
            if (key.Length == 0) return;
            var found = ContentAssets.FindById<T>(key);
            if (found != null) { reference.objectReferenceValue = found; r.Relinked++; }
            else r.Unresolved.Add($"{what} '{key}'");
        }
    }
}
