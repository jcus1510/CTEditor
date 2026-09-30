using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CTEditor.GameDefinition.Domain.Trainers;
using CTEditor.GameDefinition.Infrastructure.Catalog;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// Editor de SETS DE COMPETICIÓN (menú CTEditor → Entrenadores → Sets de competición): los sets de Smogon del pack (o
    /// los tuyos), con filtro por formato y especie. Los entrenadores cuya IA usa sets (Campeón, Maestro, Injusto) los eligen
    /// al azar dando más peso a los de más puntuación; en su editor, «🏆 Set de Smogon…» los pone a mano.
    /// </summary>
    public sealed class SetsEditorWindow : ContentEditorWindow<CompetitiveSetData>
    {
        [MenuItem(EditorMenus.Characters + "Sets de competición", false, EditorMenus.CharactersOrder + 5)]
        public static void Open() => OpenWindow<SetsEditorWindow>("Sets");

        protected override string Category => ContentFolders.Sets;
        protected override string Noun => "set";
        protected override string Title => "Sets de competición";
        protected override string Intro =>
            "Cómo juega cada especie en competición (Smogon): movimientos con alternativas, objeto, habilidad, naturaleza y EVs. " +
            "Los usan los entrenadores cuya IA tiene «Usa sets de competición» (Campeón, Maestro e Injusto por defecto).";

        public override string[] GuideSteps => new[]
        {
            "Importa el pack de tu generación: trae sets.csv con los sets de Smogon (OU, Ubers, UU, RU, NU, PU, LC).",
            "Filtra por formato o especie. La puntuación (0-100) es cuánto se usa: la IA elige con más peso los altos.",
            "Movimientos: 4 huecos separados por «/»; en cada hueco, alternativas separadas por coma.",
            "En el editor de entrenadores: «Formatos de sus sets» limita de dónde los coge; «Moveset cambiante» elige otro en cada combate.",
            "«🏆 Set de Smogon…» (en entrenadores y equipos) pone un set a mano en un miembro.",
        };

        private string _format = "";
        private string _species = "";

        protected override void DrawFilters()
        {
            var formats = new[] { "" }.Concat(ContentAssets.LoadAll<CompetitiveSetData>().Select(s => s.Format ?? "").Distinct().OrderBy(f => f)).ToList();
            int idx = Math.Max(0, formats.IndexOf(_format));
            idx = EditorGUILayout.Popup("Formato", idx, formats.Select(f => f.Length == 0 ? "Todos" : f.ToUpperInvariant()).ToArray());
            _format = formats[idx];
            _species = EditorGUILayout.TextField("Especie", _species);
        }

        protected override bool PassesFilter(CompetitiveSetData d)
            => (_format.Length == 0 || string.Equals(d.Format, _format, StringComparison.OrdinalIgnoreCase))
               && (_species.Length == 0 || (d.SpeciesId ?? "").IndexOf(_species.Trim(), StringComparison.OrdinalIgnoreCase) >= 0
                   || SpeciesName(d.SpeciesId).IndexOf(_species.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);

        protected override int CompareItems(CompetitiveSetData a, CompetitiveSetData b)
        {
            int c = string.Compare(a.SpeciesId, b.SpeciesId, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : b.Score.CompareTo(a.Score);
        }

        protected override string RowTooltip(CompetitiveSetData d) => $"{SpeciesName(d.SpeciesId)} · {d.Format?.ToUpperInvariant()} · {d.DisplayName} ({d.Score})";

        private static string SpeciesName(string id)
        {
            var s = string.IsNullOrWhiteSpace(id) ? null : ContentAssets.FindById<SpeciesData>(id);
            return s != null ? s.DisplayName : id ?? "";
        }

        private static string NameOf<T>(string id) where T : ScriptableObject, IContentAsset
        {
            var a = ContentAssets.FindById<T>(id);
            return a != null ? a.DisplayName : id + " (no existe)";
        }

        protected override void DrawPreview(CompetitiveSetData d)
        {
            EditorTheme.Section($"{SpeciesName(d.SpeciesId)} · {(d.Format ?? "").ToUpperInvariant()} · {d.DisplayName}", Accent);
            EditorTheme.Bar("Puntuación (uso)", d.Score, 100, Accent, d.Score.ToString());
            string Opts<T>(string text) where T : ScriptableObject, IContentAsset
            {
                var o = CompetitiveSet.ParseOptions(text);
                return o.Count == 0 ? "—" : string.Join(" o ", o.Select(NameOf<T>));
            }
            EditorTheme.Paragraph($"Objeto: {Opts<ItemData>(d.Items)}\nHabilidad: {Opts<AbilityData>(d.Abilities)}\nNaturaleza: {Opts<NatureData>(d.Natures)}\n" +
                                  $"EVs: {(string.IsNullOrWhiteSpace(d.Evs) ? "—" : d.Evs)}" + (string.IsNullOrWhiteSpace(d.Ivs) ? "" : $"   IVs: {d.Ivs}"), false);
            EditorGUILayout.LabelField("Movimientos", EditorStyles.boldLabel);
            foreach (var slot in CompetitiveSet.ParseSlots(d.Moves))
                EditorTheme.Paragraph("• " + string.Join(" / ", slot.Select(NameOf<MoveData>)), false);

            foreach (var p in Problems(d)) EditorGUILayout.HelpBox(p, MessageType.Warning);
            var sp = ContentAssets.FindById<SpeciesData>(d.SpeciesId);
            if (sp != null && GUILayout.Button("Abrir la especie", GUILayout.Width(140))) SpeciesEditorWindow.OpenAndSelect(sp);
        }

        /// <summary>Lo que el set nombra y no existe en el juego (el set lo salta al usarse).</summary>
        public static List<string> Problems(CompetitiveSetData d)
        {
            var list = new List<string>();
            var sp = ContentAssets.FindById<SpeciesData>(d.SpeciesId);
            if (sp == null) { list.Add($"La especie '{d.SpeciesId}' no existe: este set nunca se usa."); return list; }
            var slots = CompetitiveSet.ParseSlots(d.Moves);
            if (slots.Count == 0) list.Add("No tiene movimientos.");
            foreach (var m in slots.SelectMany(s => s).Where(m => ContentAssets.FindById<MoveData>(m) == null))
                list.Add($"El movimiento '{m}' no existe (se salta).");
            foreach (var a in CompetitiveSet.ParseOptions(d.Abilities))
                if (a != sp.AbilityId && a != sp.SecondAbilityId && a != sp.HiddenAbilityId)
                    list.Add($"La habilidad '{a}' no es de {sp.DisplayName} (se ignora).");
            foreach (var i in CompetitiveSet.ParseOptions(d.Items).Where(i => ContentAssets.FindById<ItemData>(i) == null))
                list.Add($"El objeto '{i}' no existe (se salta).");
            if (!CTEditor.GameDefinition.Domain.Stats.StatSpread.TryParse(d.Evs, out _, out var e1)) list.Add("EVs mal escritos: " + e1);
            if (!CTEditor.GameDefinition.Domain.Stats.StatSpread.TryParse(d.Ivs, out _, out var e2)) list.Add("IVs mal escritos: " + e2);
            return list;
        }
    }
}
