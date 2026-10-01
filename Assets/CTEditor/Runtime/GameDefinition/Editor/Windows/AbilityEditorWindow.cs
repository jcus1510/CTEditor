using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CTEditor.GameDefinition.Domain.Effects;
using CTEditor.GameDefinition.Infrastructure.Catalog;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;
using CTEditor.GameDefinition.Text;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// Editor de HABILIDADES (menú CTEditor → Criaturas → Habilidades). Cada habilidad es una lista de EFECTOS «cuándo / si /
    /// qué», las mismas piezas que los objetos (más las propias de las habilidades: inmune a estados, poner un clima, atrapar,
    /// y los comportamientos especiales como Rastro o Ausente). Las plantillas son las habilidades del pack elegido.
    /// </summary>
    public sealed class AbilityEditorWindow : ContentEditorWindow<AbilityData>
    {
        [MenuItem(EditorMenus.Creatures + "Habilidades", false, EditorMenus.CreaturesOrder + 3)]
        public static void Open() => OpenWindow<AbilityEditorWindow>("Habilidades");

        protected override string Category => ContentFolders.Abilities;
        protected override string Noun => "habilidad";
        protected override string Title => "Habilidades";
        protected override string Intro =>
            "Cada habilidad es una lista de EFECTOS: «cuándo» (siempre, al entrar, al final del turno, al recibir un golpe...), " +
            "«si» (condiciones) y «qué» (multiplicar, inmunidad, poner un estado, cambiar etapas...). Son las mismas piezas que los objetos.";

        public override string[] GuideSteps => new[]
        {
            "Pulsa «Crear las habilidades del pack» o elige una plantilla (las habilidades del pack elegido) para la seleccionada.",
            "Abajo están sus EFECTOS en tarjetas: «+ Añadir efecto» elige primero CUÁNDO y luego QUÉ.",
            "Combina piezas: «Siempre [si llueve] → Velocidad ×2», «Al entrar → al rival: Ataque −1», «Contacto → poner Parálisis (30 %)».",
            "Lo único de cada habilidad (Rastro, Ausente, Rompemoldes...) está en «Especial».",
            "Asígnala a una especie en su ficha (campo «Habilidad»).",
        };

        // ---------------- Plantillas = las habilidades de la FUENTE (pack elegido o tu Excel) ----------------

        public sealed class Preset { public string Id, Name, EnglishName, EffectsText; }

        /// <summary>The habilidades.csv the templates come from: the chosen source, or the newest pack that has abilities
        /// (the 1st and 2nd generation packs have none).</summary>
        public static string LibraryPath
        {
            get
            {
                string own = Path.Combine(PackTools.Folder, PackTools.AbilitiesFile);
                if (File.Exists(own)) return own;
                var pack = ContentHubWindow.AvailablePacks().LastOrDefault(p => File.Exists(Path.Combine(p, PackTools.AbilitiesFile)));
                return pack != null ? Path.Combine(pack, PackTools.AbilitiesFile) : own;
            }
        }

        private static Preset[] _library;
        private static string _libraryPath, _librarySource;
        private static DateTime _libraryStamp;
        private static double _libraryCheckedAt = -10;
        private static string[] _labels;
        private static List<(string id, string name, string group)> _templates;

        /// <summary>The abilities of the source (read again if the source or the file changes).</summary>
        public static Preset[] Library
        {
            get
            {
                double now = EditorApplication.timeSinceStartup;
                if (_library != null && now - _libraryCheckedAt < 2) return _library;   // the file is checked at most every 2 s
                _libraryCheckedAt = now;
                string full = Path.GetFullPath(LibraryPath);
                var stamp = File.Exists(full) ? File.GetLastWriteTimeUtc(full) : DateTime.MinValue;
                if (_library != null && stamp == _libraryStamp && full == _libraryPath) return _library;
                _libraryStamp = stamp; _libraryPath = full;
                _librarySource = Path.GetFileName(Path.GetDirectoryName(full));
                _library = Load(full);
                _labels = null; _templates = null;
                return _library;
            }
        }

        private static Preset[] Load(string path)
        {
            var list = new List<Preset>();
            if (!File.Exists(path)) return list.ToArray();
            string Cell(Dictionary<string, string> r, string k) => r.TryGetValue(k, out var v) ? (v ?? "").Trim() : "";
            foreach (var r in Csv.CsvTable.Load(path).Rows)
            {
                string id = Cell(r, "id");
                if (id.Length > 0) list.Add(new Preset { Id = id, Name = Cell(r, "nombre"), EnglishName = Cell(r, "nombre_en"), EffectsText = Cell(r, "efectos") });
            }
            return list.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }

        // Group of an ability in the menus: its first moment («Siempre», «Al entrar»...).
        private static string GroupOf(Preset p)
        {
            try
            {
                var b = EffectText.Parse(p.EffectsText).FirstOrDefault();
                return b == null ? "Sin efecto" : EffectText.Label(b.Trigger, true);
            }
            catch (FormatException) { return "Con errores"; }
        }

        /// <summary>Writes an ability of the source into the asset (the id is kept).</summary>
        public static void Fill(SerializedObject so, Preset p)
        {
            if (p.EnglishName.Length > 0) so.FindProperty("englishName").stringValue = p.EnglishName;
            List<EffectBlock> blocks;
            try { blocks = EffectText.Parse(p.EffectsText); }
            catch (FormatException e) { Debug.LogError($"Plantilla de habilidad '{p.Id}': {e.Message}"); blocks = new List<EffectBlock>(); }
            ItemEffectsEditing.SetBlocks(so, blocks);
        }

        /// <summary>Creates the abilities of the source that are missing. Returns how many it created.</summary>
        public static int CreateClassicSet()
        {
            int created = 0;
            foreach (var p in Library)
            {
                var preset = p;
                if (ContentAssets.CreateIfMissing<AbilityData>(ContentFolders.Abilities, p.Id, p.Name, so => Fill(so, preset))) created++;
            }
            AssetDatabase.SaveAssets();
            return created;
        }

        protected override IReadOnlyList<(string id, string name, string group)> Templates
        {
            get
            {
                var lib = Library;
                return _templates ??= lib.Select(p => (p.Id, p.Name, GroupOf(p))).ToList();
            }
        }

        protected override void ApplyTemplate(SerializedObject so, string id)
        {
            var p = Library.FirstOrDefault(x => x.Id == id);
            if (p == null) return;
            Fill(so, p);
            so.FindProperty("displayName").stringValue = p.Name;
        }

        protected override void DrawBulkPresets()
        {
            if (Library.Length == 0) { EditorGUILayout.HelpBox("Ningún pack trae habilidades (llegaron en la 3.ª generación).", MessageType.Info); return; }
            if (GUILayout.Button($"Crear las {Library.Length} habilidades del pack ({_librarySource})")) FinishBulk(CreateClassicSet(), "habilidades");
        }

        private int _preset;

        protected override void DrawPresets(AbilityData d)
        {
            var lib = Library;
            if (lib.Length == 0) return;
            _labels ??= lib.Select(p => $"{GroupOf(p)}/{p.Name}").ToArray();
            _preset = Mathf.Clamp(_preset, 0, lib.Length - 1);
            EditorGUILayout.LabelField($"Plantilla del pack {_librarySource} (reemplaza los efectos; el id se mantiene)", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            _preset = EditorGUILayout.Popup(_preset, _labels);
            if (GUILayout.Button("Aplicar", GUILayout.Width(70)))
            {
                var p = lib[_preset];
                EditSelected(so => { Fill(so, p); if (string.IsNullOrWhiteSpace(d.DisplayName) || d.DisplayName == d.Id) so.FindProperty("displayName").stringValue = p.Name; });
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(lib[_preset].EffectsText, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space();
        }

        protected override void DrawPreview(AbilityData d)
        {
            var lines = Describe(d);
            EditorGUILayout.LabelField("Qué hace", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(lines.Count == 0 ? "Todavía no hace nada: añade efectos abajo." : string.Join("\n", lines),
                lines.Count == 0 ? MessageType.Warning : MessageType.Info);
            foreach (var w in EffectChecks.Warnings(ItemEffectsEditing.Blocks(d), true)) EditorGUILayout.HelpBox(w, MessageType.Warning);
        }

        /// <summary>The ability in sentences, GROUPED by moment («Siempre», «Al entrar», «Al recibir un golpe»...).</summary>
        public static List<string> Describe(AbilityData d)
        {
            var l = new List<string>();
            foreach (var group in ItemEffectsEditing.Blocks(d).GroupBy(b => b.Trigger).OrderBy(g => (int)g.Key))
            {
                l.Add(EffectText.Label(group.Key, true).ToUpperInvariant() + ":");
                foreach (var b in group) l.Add("  • " + EffectText.Describe(b, EffectBlocksGui.Name, true));
            }
            return l;
        }
    }
}
