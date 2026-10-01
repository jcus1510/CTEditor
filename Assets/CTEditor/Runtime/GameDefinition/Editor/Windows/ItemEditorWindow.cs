using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CTEditor.GameDefinition.Domain.Conditions;
using CTEditor.GameDefinition.Domain.Effects;
using CTEditor.GameDefinition.Domain.Items;
using CTEditor.GameDefinition.Infrastructure.Catalog;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;
using CTEditor.GameDefinition.Text;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// Editor de OBJETOS (menú CTEditor → Objetos → Todos los objetos). Trae una biblioteca de objetos clásicos (medicinas,
    /// revivir, curas de estado, éteres, bolas, piedras evolutivas, objetos X, objetos equipables y
    /// bayas) y deja inventar los tuyos combinando sus efectos.
    /// </summary>
    public sealed class ItemEditorWindow : ContentEditorWindow<ItemData>
    {
        [MenuItem(EditorMenus.Items + "Todos los objetos", false, EditorMenus.ItemsOrder + 1)]
        public static void Open() => OpenWindow<ItemEditorWindow>("Objetos");

        protected override string Category => ContentFolders.Items;
        protected override string Noun => "objeto";
        protected override string Intro =>
            "Cada objeto es una lista de EFECTOS: «cuándo» (al usarlo, mientras lo lleva, al recibir un golpe, con poca vida...), " +
            "«si» (condiciones) y «qué» (curar, curar estados, multiplicar, resistir un tipo...). Combínalos para crear cualquier objeto.";

        public override string[] GuideSteps => new[]
        {
            "Pulsa «Crear los objetos clásicos» para tener una base (Poción, Poké Ball, Piedra Fuego, Restos, bayas…).",
            "Elige una ficha: arriba lo común (nombre, bolsillo, precio, dónde se usa) y abajo sus EFECTOS en tarjetas.",
            "«+ Añadir efecto» elige primero CUÁNDO y luego QUÉ; los tipos, estados y estadísticas se eligen de un desplegable.",
            "«✨ Plantillas por piezas» añade efectos hechos: «Baya que resiste un ataque de tipo ▸ Fuego», «Cura el estado ▸ Parálisis»...",
            "Cada efecto puede tener condiciones, probabilidad, veces por combate y gastar el objeto.",
            "Las piedras evolutivas se conectan desde la especie: en su evolución elige «Objeto» y este objeto.",
        };

        // Color de cada categoría (chips del editor y de la lista).
        // En la lista: una marca con el color de su categoría; al pasar el ratón, su descripción.
        protected override Color? RowMark(ItemData d) => ColorOf(d.Category);
        protected override string RowTooltip(ItemData d)
            => string.IsNullOrEmpty(d.Description) ? ContentAssets.Label(d) : $"{ContentAssets.Label(d)} — {d.Description}";

        public static Color ColorOf(ItemCategory c)
        {
            switch (c)
            {
                case ItemCategory.Medicine: return new Color(0.35f, 0.8f, 0.45f);
                case ItemCategory.Revive: return new Color(0.95f, 0.8f, 0.3f);
                case ItemCategory.StatusCure: return new Color(0.55f, 0.75f, 0.95f);
                case ItemCategory.PpRestore: return new Color(0.7f, 0.55f, 0.95f);
                case ItemCategory.Ball: return new Color(0.95f, 0.4f, 0.35f);
                case ItemCategory.Evolution: return new Color(0.95f, 0.6f, 0.2f);
                case ItemCategory.BattleBoost: return new Color(0.9f, 0.45f, 0.7f);
                case ItemCategory.Held: return new Color(0.45f, 0.75f, 0.7f);
                case ItemCategory.Key: return new Color(0.6f, 0.6f, 0.6f);
                default: return new Color(0.7f, 0.7f, 0.7f);
            }
        }

        // «Other» is also a target label («Rival») in Etiquetas: items need their own word.
        public static string NameOf(ItemCategory c) => c == ItemCategory.Other ? "Otros" : Etiquetas.Enum(c.ToString());

        // ---------------- Plantillas = los objetos de la FUENTE (el pack elegido o tu Excel) ----------------

        /// <summary>Un objeto de la fuente: una fila de su objetos.csv (con todos sus efectos).</summary>
        public sealed class Preset
        {
            public string Id, Name, EnglishName, Summary, EffectsText;
            public ItemCategory Cat;
            public int Price;
            public bool InBattle, Outside, Consumable, IsBerry;
        }

        /// <summary>The objetos.csv the templates come from: the one of the chosen pack or of the author's own Excel
        /// folder (PackTools.Folder). There is no separate templates sheet: the pack IS the catalogue.</summary>
        public static string LibraryPath => System.IO.Path.Combine(PackTools.Folder, "objetos.csv");

        private static Preset[] _library;
        private static string _libraryPath;
        private static DateTime _libraryStamp;
        private static double _libraryCheckedAt = -10;
        private static string[] _libraryLabels;

        /// <summary>Los objetos de la fuente (se vuelven a leer si cambia la fuente o el archivo).</summary>
        public static Preset[] Library
        {
            get
            {
                double now = EditorApplication.timeSinceStartup;
                if (_library != null && now - _libraryCheckedAt < 2) return _library;   // the file is checked at most every 2 s
                _libraryCheckedAt = now;
                string full = System.IO.Path.GetFullPath(LibraryPath);
                var stamp = System.IO.File.Exists(full) ? System.IO.File.GetLastWriteTimeUtc(full) : DateTime.MinValue;
                if (_library != null && stamp == _libraryStamp && full == _libraryPath) return _library;
                _libraryStamp = stamp;
                _libraryPath = full;
                _library = LoadLibrary(full);
                _libraryLabels = null;
                return _library;
            }
        }

        private static string[] LibraryLabels
            => _libraryLabels ??= Library.Select(p => $"{NameOf(p.Cat)}/{p.Name}").ToArray();

        private static Preset[] LoadLibrary(string path)
        {
            var list = new List<Preset>();
            if (!System.IO.File.Exists(path)) { Debug.LogWarning("La fuente no tiene objetos.csv: " + path); return list.ToArray(); }
            string Cell(Dictionary<string, string> r, string k) => r.TryGetValue(k, out var v) ? (v ?? "").Trim() : "";
            bool Yes(string v) => v == "si" || v == "sí" || v == "true" || v == "1";
            foreach (var r in Csv.CsvTable.Load(path).Rows)
            {
                string id = Cell(r, "id");
                if (id.Length == 0) continue;
                Enum.TryParse(Cell(r, "categoria"), true, out ItemCategory cat);
                int.TryParse(Cell(r, "precio"), out int price);
                list.Add(new Preset
                {
                    Id = id, Name = Cell(r, "nombre"), EnglishName = Cell(r, "nombre_en"), Summary = Cell(r, "descripcion"), Cat = cat,
                    Price = price, InBattle = Yes(Cell(r, "en_combate")), Outside = Yes(Cell(r, "fuera_combate")),
                    Consumable = Yes(Cell(r, "se_gasta")), IsBerry = Yes(Cell(r, "es_baya")), EffectsText = Cell(r, "efectos"),
                });
            }
            return list.ToArray();
        }

        /// <summary>Pone en la ficha los datos y los efectos de un objeto clásico (el id no se toca).</summary>
        public static void Fill(SerializedObject so, Preset p)
        {
            so.FindProperty("category").intValue = (int)p.Cat;
            so.FindProperty("price").intValue = p.Price;
            so.FindProperty("description").stringValue = p.Summary;
            if (p.EnglishName.Length > 0) so.FindProperty("englishName").stringValue = p.EnglishName;
            so.FindProperty("usableInBattle").boolValue = p.InBattle;
            so.FindProperty("usableOutsideBattle").boolValue = p.Outside;
            so.FindProperty("consumable").boolValue = p.Consumable;
            so.FindProperty("isBerry").boolValue = p.IsBerry;
            List<EffectBlock> blocks;
            try { blocks = EffectText.Parse(p.EffectsText); }
            catch (FormatException e) { Debug.LogError($"Plantilla de objeto '{p.Id}': {e.Message}"); blocks = new List<EffectBlock>(); }
            ItemEffectsEditing.SetBlocks(so, blocks);
        }

        /// <summary>Crea los objetos de la fuente que falten. Público: lo usa el Centro de Contenido.</summary>
        public static int CreateClassicSet()
        {
            int created = 0;
            foreach (var p in Library)
            {
                var preset = p;
                if (ContentAssets.CreateIfMissing<ItemData>(ContentFolders.Items, p.Id, p.Name, so => Fill(so, preset)))
                    created++;
            }
            AssetDatabase.SaveAssets();
            return created;
        }

        private int _preset;

        private static Preset[] _templatesOf;
        private static List<(string id, string name, string group)> _templates;

        // Cached per library (the base window asks for it on every GUI event).
        protected override IReadOnlyList<(string id, string name, string group)> Templates
        {
            get
            {
                var lib = Library;
                if (_templates == null || _templatesOf != lib) { _templatesOf = lib; _templates = lib.Select(p => (p.Id, p.Name, NameOf(p.Cat))).ToList(); }
                return _templates;
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
            if (GUILayout.Button($"Crear los {Library.Length} objetos de {PackTools.SourceName}")) FinishBulk(CreateClassicSet(), "objetos");
        }

        protected override void DrawPresets(ItemData d)
        {
            if (Library.Length == 0) return;
            _preset = Mathf.Clamp(_preset, 0, Library.Length - 1);
            EditorGUILayout.LabelField($"Plantilla de {PackTools.SourceName} (reemplaza los efectos; el id se mantiene)", EditorStyles.boldLabel);
            var labels = LibraryLabels;
            EditorGUILayout.BeginHorizontal();
            _preset = EditorGUILayout.Popup(_preset, labels);
            if (GUILayout.Button("Aplicar", GUILayout.Width(70)))
            {
                var p = Library[_preset];
                EditSelected(so => { Fill(so, p); if (string.IsNullOrWhiteSpace(d.DisplayName) || d.DisplayName == d.Id) so.FindProperty("displayName").stringValue = p.Name; });
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(Library[_preset].Summary, EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space();
        }

        protected override void DrawPreview(ItemData d)
        {
            EditorTheme.Chip($"{NameOf(d.Category).ToUpperInvariant()} · {d.Price} ₽", ColorOf(d.Category));
            var lines = Describe(d);
            EditorGUILayout.LabelField("Qué hace", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(lines.Count <= 1 ? "Todavía no hace nada: añade efectos abajo (o una plantilla por piezas)." : string.Join("\n", lines),
                lines.Count <= 1 ? MessageType.Warning : MessageType.Info);

            foreach (var w in Check(d)) EditorGUILayout.HelpBox(w, MessageType.Warning);

            if (d.Category == ItemCategory.Evolution)
            {
                var users = ContentAssets.LoadAll<SpeciesData>().Where(s => s.Evolutions != null && s.Evolutions.Any(e =>
                    e.method == CTEditor.GameDefinition.Domain.Species.EvolutionMethod.Item && e.itemId == d.Id)).ToList();
                EditorGUILayout.HelpBox(users.Count == 0
                    ? "Ninguna especie evoluciona con este objeto todavía. En la especie, añade una evolución con método «Objeto» y elige este."
                    : "Hace evolucionar a: " + string.Join(", ", users.Select(u => u.DisplayName)), MessageType.None);
            }
        }

        /// <summary>
        /// The item in sentences, GROUPED by moment («Al usarlo», «Mientras lo lleva», «Con poca vida»...): the heading
        /// says when, so the sentences do not repeat «Equipado:».
        /// </summary>
        public static List<string> Describe(ItemData d)
        {
            var l = new List<string>();
            var blocks = ItemEffectsEditing.Blocks(d);
            foreach (var group in blocks.GroupBy(b => b.Trigger).OrderBy(g => (int)g.Key))
            {
                l.Add(EffectText.Label(group.Key).ToUpperInvariant() + ":");
                foreach (var b in group) l.Add("  • " + EffectText.Describe(b, EffectBlocksGui.Name));
            }
            if (d.Category == ItemCategory.Evolution) l.Add("Hace evolucionar a las especies que lo indiquen en su evolución.");
            l.Add($"Se usa {(d.UsableInBattle && d.UsableOutsideBattle ? "dentro y fuera del combate" : d.UsableInBattle ? "solo en combate" : d.UsableOutsideBattle ? "solo fuera del combate" : "solo equipado o como objeto clave")}" +
                  (d.Consumable ? " y se gasta." : " y no se gasta.") + (d.IsBerry ? " Es una baya." : ""));
            return l;
        }

        private static HashSet<string> _evoItems;
        private static int _evoVersion = -1;

        /// <summary>Ids de objetos que usa alguna evolución (con objeto, por intercambio o «lleva: objeto»).</summary>
        public static HashSet<string> EvolutionItems()
        {
            if (_evoItems != null && _evoVersion == ContentAssets.Version) return _evoItems;
            var set = new HashSet<string>();
            foreach (var sp in ContentAssets.LoadAll<SpeciesData>())
                foreach (var e in sp.Evolutions ?? new SpeciesData.EvolutionEntry[0])
                {
                    if (!string.IsNullOrWhiteSpace(e.itemId)) set.Add(e.itemId.Trim());
                    foreach (var c in e.conditions ?? new SpeciesData.EvolutionConditionData[0])
                        if (c != null && !string.IsNullOrWhiteSpace(c.itemId)) set.Add(c.itemId.Trim());
                }
            _evoItems = set;
            _evoVersion = ContentAssets.Version;
            return set;
        }

        public static List<string> Check(ItemData d)
        {
            var w = new List<string>();
            var blocks = ItemEffectsEditing.Blocks(d);
            bool isBall = blocks.Any(b => b.Trigger == EffectTrigger.OnUse && b.Action == EffectAction.Catch && b.Amount > 0);
            if (isBall && !d.UsableInBattle) w.Add("Es una bola pero no se puede usar en combate: nunca servirá para capturar.");
            bool held = blocks.Any(b => b.Trigger != EffectTrigger.OnUse && b.Trigger != EffectTrigger.OnWalk);
            // Los objetos que hacen EVOLUCIONAR (al llevarlos o al intercambiar) no necesitan efecto en combate: su efecto es la evolución.
            if ((d.Category == ItemCategory.Held || d.Category == ItemCategory.Berry) && !held && !EvolutionItems().Contains(d.Id ?? ""))
                w.Add("Es para llevar equipado pero no tiene efectos al llevarlo (ni hace evolucionar a ninguna especie).");
            if (blocks.Any(b => b.Trigger == EffectTrigger.OnUse) && !d.UsableInBattle && !d.UsableOutsideBattle)
                w.Add("Tiene efectos «Al usarlo», pero no se puede usar ni en combate ni fuera.");
            w.AddRange(EffectChecks.Warnings(blocks, false));
            return w;
        }
    }
}
