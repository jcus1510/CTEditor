using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CTEditor.GameDefinition.Editor.Csv;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// EL PACK DE 1ª GENERACIÓN (o TU carpeta de Excel, eligiéndola en «Fuente de datos») desde cualquier editor, sin borrar nada:
    ///   • Importar lo que falta (no toca lo que ya tienes).
    ///   • Actualizar lo que ya existe desde el pack (antes ves los cambios en la ventana de Excel).
    ///   • Restaurar UNA ficha (una especie, un movimiento) a los datos del pack.
    ///   • Reparar especies con movimientos vacíos (pasa si se BORRA un movimiento y se vuelve a crear:
    ///     las especies que lo aprendían se quedan con el hueco vacío).
    /// Todo pasa por el importador de Excel de siempre: mismas validaciones y el id se mantiene.
    /// </summary>
    public static class PackTools
    {
        public const string SpeciesFile = "especies.csv";
        public const string MovesFile = "movimientos.csv";
        public const string AbilitiesFile = "habilidades.csv";
        public const string TrainersFile = "entrenadores.csv";

        // ---------------- De dónde se leen los datos (la FUENTE) ----------------
        // Por defecto, el pack de 1ª generación. Puedes elegir TU carpeta de Excel (la que exportas desde
        // Herramientas → Excel): así «Restaurar» y «Actualizar» usan tus datos (tu región, tus cambios).
        // Se guarda por proyecto en EditorPrefs.
        private static string PrefKey => "CTEditor.PackSource." + Application.dataPath.GetHashCode();

        /// <summary>Carpeta de Excel propia elegida (null = usar el pack 1ª gen.). Si ya no existe, se ignora.</summary>
        public static string CustomFolder
        {
            get { string f = EditorPrefs.GetString(PrefKey, ""); return !string.IsNullOrEmpty(f) && Directory.Exists(f) ? f : null; }
            set { if (string.IsNullOrEmpty(value)) EditorPrefs.DeleteKey(PrefKey); else EditorPrefs.SetString(PrefKey, value); IdCache.Clear(); }
        }

        public static bool UsingCustom => CustomFolder != null;
        public static string Folder => CustomFolder ?? ContentHubWindow.PackFolder;
        public static bool Available => Directory.Exists(Folder);

        /// <summary>Nombre corto de la fuente para botones y mensajes: «pack 1ª gen.» o «tu Excel (Kanto2)».</summary>
        public static string SourceName => UsingCustom ? $"tu Excel ({Path.GetFileName(CustomFolder.TrimEnd('/', '\\'))})" : (ContentHubWindow.PackFolder == ContentHubWindow.Gen14PackFolder ? "pack 1ª-4ª gen."
                : ContentHubWindow.PackFolder == ContentHubWindow.Gen12PackFolder ? "pack 1ª-2ª gen." : "pack 1ª gen.");

        /// <summary>Pide al autor su carpeta de Excel (con los .csv exportados). Devuelve true si eligió una válida.</summary>
        public static bool ChooseCustomFolder()
        {
            string start = CustomFolder ?? Application.dataPath;
            string f = EditorUtility.OpenFolderPanel("Carpeta con tus archivos de Excel (.csv)", start, "");
            if (string.IsNullOrEmpty(f)) return false;
            if (!Directory.GetFiles(f, "*.csv").Any())
            {
                EditorUtility.DisplayDialog("Mi Excel", "Esa carpeta no tiene archivos .csv. Exporta primero desde Herramientas → Excel.", "Vale");
                return false;
            }
            CustomFolder = f;
            return true;
        }

        // ---------------- Consultas ----------------

        private static readonly Dictionary<string, (DateTime stamp, HashSet<string> ids)> IdCache = new Dictionary<string, (DateTime, HashSet<string>)>();

        /// <summary>¿El pack trae una fila con ese id en ese archivo?</summary>
        public static bool Has(string file, string id)
        {
            if (string.IsNullOrEmpty(id) || !Available) return false;
            string path = Path.Combine(Folder, file);
            if (!File.Exists(path)) return false;
            var stamp = File.GetLastWriteTimeUtc(path);
            if (!IdCache.TryGetValue(path, out var c) || c.stamp != stamp)
            {
                var ids = new HashSet<string>();
                try { foreach (var r in CsvTable.Load(path).Rows) if (r.TryGetValue("id", out var v)) ids.Add(v.Trim()); }
                catch (Exception) { /* un pack ilegible se trata como vacío */ }
                IdCache[path] = c = (stamp, ids);
            }
            return c.ids.Contains(id);
        }

        /// <summary>Copia de la tabla con solo las filas de esos ids (null = todas). Puro: se prueba sin Unity.</summary>
        public static CsvTable Filter(CsvTable table, ICollection<string> ids)
        {
            var result = new CsvTable(table.Headers);
            foreach (var row in table.Rows)
                if (ids == null || row.TryGetValue("id", out var id) && ids.Contains(id.Trim())) result.AddRow(row);
            return result;
        }

        /// <summary>Especies con alguna entrada de learnset sin movimiento (referencia rota).</summary>
        public static List<SpeciesData> SpeciesWithEmptyMoves()
            => ContentAssets.LoadAll<SpeciesData>().Where(s => s.Learnset != null && s.Learnset.Any(l => l.move == null)).ToList();

        // ---------------- Importar en la ventana de Excel (con revisión) ----------------

        /// <summary>Abre la ventana de Excel con esos archivos del pack ya analizados, en el modo pedido.</summary>
        public static void OpenImport(string[] files, ImportMode mode, string intro)
        {
            if (!CheckAvailable()) return;
            CsvWindow.OpenFolder(Stage(files, null), mode, intro);
        }

        /// <summary>Importar lo que falta de especies (con sus movimientos y habilidades).</summary>
        public static void ImportMissingSpecies()
            => OpenImport(new[] { SpeciesFile, MovesFile, AbilitiesFile }, ImportMode.CreateOnly,
                Up() + " — solo se crean las especies, movimientos y habilidades que FALTAN. Lo que ya tienes no se toca. Revisa y pulsa Aplicar.");

        /// <summary>Actualizar todas las especies desde el pack (antes crea en silencio los movimientos y habilidades que falten).</summary>
        public static void UpdateAllSpecies()
        {
            if (!CheckAvailable()) return;
            CreateMissingSilently(MovesFile, AbilitiesFile);
            OpenImport(new[] { SpeciesFile }, ImportMode.CreateAndUpdate,
                Up() + " — se ACTUALIZAN las especies con estos datos (y se crean las que falten). Revisa los cambios fila a fila antes de pulsar Aplicar. " +
                "El id y las referencias (entrenadores, zonas...) se mantienen.");
        }

        public static void ImportMissingMoves()
            => OpenImport(new[] { MovesFile }, ImportMode.CreateOnly,
                Up() + " — solo se crean los movimientos que FALTAN. Lo que ya tienes no se toca.");

        /// <summary>Importar los entrenadores que FALTAN (líderes, Alto Mando, rivales y de ruta). Necesitan las especies y movimientos.</summary>
        public static void ImportMissingTrainers()
        {
            if (!CheckAvailable()) return;
            CreateMissingSilently(MovesFile, AbilitiesFile, SpeciesFile);
            OpenImport(new[] { TrainersFile }, ImportMode.CreateOnly,
                Up() + " — solo se crean los entrenadores que FALTAN (y antes, en silencio, las especies y movimientos que usan). Los tuyos no se tocan.");
        }

        /// <summary>Actualizar todos los entrenadores desde el pack (equipos, movimientos, frases...).</summary>
        public static void UpdateAllTrainers()
        {
            if (!CheckAvailable()) return;
            CreateMissingSilently(MovesFile, AbilitiesFile, SpeciesFile);
            OpenImport(new[] { TrainersFile }, ImportMode.CreateAndUpdate,
                Up() + " — se ACTUALIZAN los entrenadores con estos datos (y se crean los que falten). Revisa los cambios antes de pulsar Aplicar.");
        }

        public static void UpdateAllMoves()
            => OpenImport(new[] { MovesFile }, ImportMode.CreateAndUpdate,
                Up() + " — se ACTUALIZAN los movimientos con estos datos (y se crean los que falten). Revisa los cambios antes de pulsar Aplicar.");

        // ---------------- Una sola ficha ----------------

        /// <summary>Restaura UNA ficha (fila 'id' de 'file') a los datos del pack, enseñando antes los cambios.</summary>
        public static bool RestoreOne(string file, string id)
        {
            if (!CheckAvailable()) return false;
            if (file == SpeciesFile) CreateMissingSilently(MovesFile, AbilitiesFile); // su learnset puede nombrar movimientos que borraste
            var analyses = AnalyzeStaged(new[] { file }, new[] { id }, ImportMode.CreateAndUpdate, out var ctx);
            var row = analyses.SelectMany(a => a.Rows).FirstOrDefault();
            if (row == null) { EditorUtility.DisplayDialog("Restaurar", $"El {SourceName} no trae '{id}'.", "Vale"); return false; }
            if (row.Errors.Count > 0)
            {
                EditorUtility.DisplayDialog("Restaurar desde " + SourceName, $"No se puede restaurar '{id}':\n\n• " + string.Join("\n• ", row.Errors.Take(8)), "Vale");
                return false;
            }
            if (!row.HasChanges) { EditorUtility.DisplayDialog("Restaurar desde " + SourceName, $"'{id}' ya tiene exactamente esos datos.", "Vale"); return false; }

            var lines = row.IsNew ? new List<string> { "(no existe: se creará)" }
                : row.Changes.Take(10).Select(c => $"• {c.column}: {Short(c.before)} → {Short(c.after)}").ToList();
            if (row.Changes.Count > 10) lines.Add($"… y {row.Changes.Count - 10} cambio(s) más");
            if (!EditorUtility.DisplayDialog("Restaurar desde " + SourceName,
                    $"Cambios en '{id}':\n\n{string.Join("\n", lines)}\n\nEl id y las referencias se mantienen.", "Restaurar", "Cancelar"))
                return false;
            CsvImporter.Apply(analyses, ctx, null);
            return true;
        }

        /// <summary>
        /// Repara las especies con movimientos vacíos: crea los movimientos que falten y vuelve a leer del pack
        /// el learnset (y el resto de datos) de esas especies. Las que no están en el pack se listan para revisarlas a mano.
        /// </summary>
        public static void RepairEmptyMoves()
        {
            var broken = SpeciesWithEmptyMoves();
            if (broken.Count == 0) { EditorUtility.DisplayDialog("Reparar especies", "Ninguna especie tiene movimientos vacíos. ✔", "Vale"); return; }
            if (!CheckAvailable()) return;
            var inPack = broken.Where(s => Has(SpeciesFile, s.Id)).ToList();
            var manual = broken.Except(inPack).ToList();
            string msg = $"{broken.Count} especie(s) tienen movimientos vacíos (suele pasar al borrar un movimiento y volver a crearlo).\n\n" +
                         (inPack.Count > 0 ? $"Se repararán desde {SourceName} ({inPack.Count}): {string.Join(", ", inPack.Take(15).Select(s => s.DisplayName))}{(inPack.Count > 15 ? "…" : "")}\n" : "") +
                         (manual.Count > 0 ? $"\nNo están en {SourceName} (revísalas a mano): {string.Join(", ", manual.Select(s => s.DisplayName))}\n" : "") +
                         "\nSe vuelven a poner sus datos; el id y las referencias se mantienen.";
            if (inPack.Count == 0) { EditorUtility.DisplayDialog("Reparar especies", msg, "Vale"); return; }
            if (!EditorUtility.DisplayDialog("Reparar especies", msg, "Reparar", "Cancelar")) return;

            CreateMissingSilently(MovesFile, AbilitiesFile);
            var analyses = AnalyzeStaged(new[] { SpeciesFile }, inPack.Select(s => s.Id).ToList(), ImportMode.CreateAndUpdate, out var ctx);
            string report = CsvImporter.Apply(analyses, ctx, null);
            int still = SpeciesWithEmptyMoves().Count;
            EditorUtility.DisplayDialog("Reparar especies", report + (still > 0 ? $"\n\nQuedan {still} especie(s) por revisar a mano." : "\n\n¡Todo reparado!"), "Vale");
        }

        // ---------------- Botones listos para los editores ----------------

        private static DateTime _brokenStamp;
        private static int _brokenCount;

        /// <summary>Nº de especies con movimientos vacíos (se recalcula como mucho cada 2 s: no carga assets en cada repintado).</summary>
        public static int BrokenSpeciesCount
        {
            get
            {
                if ((DateTime.UtcNow - _brokenStamp).TotalSeconds > 2) { _brokenCount = SpeciesWithEmptyMoves().Count; _brokenStamp = DateTime.UtcNow; }
                return _brokenCount;
            }
        }

        /// <summary>Botón «📦 Pack 1ª gen. ▾» (o «📦 Tu Excel ▾»): importar lo que falta, actualizar todo y elegir la fuente.</summary>
        public static void DrawPackMenu(string noun, Action importMissing, Action updateAll)
        {
            if (!Available && !UsingCustom && !Directory.Exists(ContentHubWindow.PackFolder)) return;
            var old = GUI.backgroundColor;
            GUI.backgroundColor = UsingCustom ? new Color(0.8f, 1f, 0.8f) : new Color(0.75f, 0.9f, 1f);
            string label = "📦 " + char.ToUpperInvariant(SourceName[0]) + SourceName.Substring(1) + " ▾";
            if (GUILayout.Button(new GUIContent(label, $"Importa {noun} de la fuente elegida sin borrar nada: solo lo que falta, o actualizando lo que ya tienes (verás los cambios antes).")))
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent($"Importar {noun} que FALTAN (lo tuyo no se toca)"), false, () => importMissing());
                menu.AddItem(new GUIContent($"Actualizar TODOS los {noun} desde {SourceName} (revisar cambios)…"), false, () => updateAll());
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Fuente de datos/Pack clásico (" + ContentHubWindow.PackName + ")"), !UsingCustom, () => CustomFolder = null);
                menu.AddItem(new GUIContent("Fuente de datos/Mi carpeta de Excel…"), UsingCustom, () => ChooseCustomFolder());
                menu.ShowAsContext();
            }
            GUI.backgroundColor = old;
        }

        /// <summary>Aviso + botón «🔧 Reparar» si hay especies con movimientos vacíos.</summary>
        public static void DrawRepairWarning(Action afterRepair)
        {
            int n = BrokenSpeciesCount;
            if (n == 0) return;
            EditorGUILayout.HelpBox($"⚠ {n} especie(s) tienen movimientos VACÍOS en su learnset. Pasa al borrar un movimiento y volver a crearlo: " +
                                    "las especies que lo aprendían se quedan con el hueco.", MessageType.Warning);
            if (GUILayout.Button($"🔧 Reparar {n} especie(s) desde {SourceName}")) { RepairEmptyMoves(); _brokenStamp = default; afterRepair?.Invoke(); }
        }

        /// <summary>Botón «↺ Restaurar desde el pack» para la ficha seleccionada (solo si el pack la trae).</summary>
        public static void DrawRestoreButton(string file, string id, Action afterRestore)
        {
            if (!Has(file, id)) return;
            if (GUILayout.Button(new GUIContent("↺ Restaurar desde " + SourceName, "Vuelve a poner los datos de la fuente en ESTA ficha. Antes te enseña qué cambia. El id y las referencias se mantienen.")))
                if (RestoreOne(file, id)) { _brokenStamp = default; afterRestore?.Invoke(); }
        }

        // ---------------- Por dentro ----------------

        // Crea (sin preguntar) las filas que FALTEN de esos archivos: nunca toca lo que ya existe.
        private static void CreateMissingSilently(params string[] files)
        {
            var analyses = AnalyzeStaged(files, null, ImportMode.CreateOnly, out var ctx);
            if (analyses.Sum(a => a.New) > 0) CsvImporter.Apply(analyses, ctx, null);
        }

        private static List<FileAnalysis> AnalyzeStaged(string[] files, ICollection<string> ids, ImportMode mode, out ImportContext ctx)
        {
            string folder = Stage(files, ids);
            var schemas = CsvSchemas.All();
            var sources = new List<(CsvSchema, string)>();
            foreach (var f in files)
            {
                var schema = schemas.FirstOrDefault(s => string.Equals(s.FileName, f, StringComparison.OrdinalIgnoreCase));
                if (schema != null) sources.Add((schema, Path.Combine(folder, f)));
            }
            return CsvImporter.AnalyzeFiles(sources, mode, out ctx);
        }

        // Copia los archivos pedidos del pack (filtrados por ids si se dan) a una carpeta temporal del proyecto.
        private static string Stage(IEnumerable<string> files, ICollection<string> ids)
        {
            string temp = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Temp", "CTEditorPack");
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
            Directory.CreateDirectory(temp);
            foreach (var f in files)
            {
                string src = Path.Combine(Folder, f);
                if (!File.Exists(src)) continue;
                if (ids == null) File.Copy(src, Path.Combine(temp, f));
                else Filter(CsvTable.Load(src), ids).Save(Path.Combine(temp, f));
            }
            return temp;
        }

        private static bool CheckAvailable()
        {
            if (Available) return true;
            EditorUtility.DisplayDialog("Fuente de datos", "No encuentro la carpeta:\n" + Folder +
                (UsingCustom ? "" : "\n\nCopia la carpeta GameContent del zip dentro de Assets."), "Vale");
            return false;
        }

        private static string Up() => SourceName.ToUpperInvariant();

        private static string Short(string s)
        {
            s = string.IsNullOrEmpty(s) ? "(vacío)" : s.Replace("\n", " ");
            return s.Length > 60 ? s.Substring(0, 57) + "…" : s;
        }
    }
}
