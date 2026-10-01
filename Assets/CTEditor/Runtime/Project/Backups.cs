using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace CTEditor.Project
{
    /// <summary>Una copia de seguridad del proyecto (un .zip en «copias/»).</summary>
    public sealed class BackupInfo
    {
        public string Path { get; }
        public DateTime Time { get; }
        public long Bytes { get; }
        /// <summary>Por qué se hizo: «auto» (cada 10 minutos con cambios) o «manual».</summary>
        public string Reason { get; }

        public BackupInfo(string path, DateTime time, long bytes, string reason)
        {
            Path = path; Time = time; Bytes = bytes; Reason = reason;
        }
    }

    /// <summary>
    /// HISTORIAL DE VERSIONES del proyecto: copias de seguridad comprimidas en «&lt;proyecto&gt;/copias/», con fecha y
    /// motivo en el nombre («copia_20261001_153000_auto.zip»). Se guardan como mucho <see cref="MaxCopies"/>: al hacer
    /// una nueva se borran las más viejas. Entra todo el proyecto menos la propia carpeta de copias. Restaurar devuelve
    /// los archivos a como estaban (antes guarda una copia de cómo están ahora, por si acaso).
    /// </summary>
    public static class ProjectBackups
    {
        public const string Folder = "copias";
        public const int MaxCopies = 5;
        private const string Prefix = "copia_";
        private const string Stamp = "yyyyMMdd_HHmmss";

        public static string FolderOf(string projectRoot) => System.IO.Path.Combine(projectRoot, Folder);

        /// <summary>Hace una copia ahora. reason: «auto» o «manual». Devuelve la copia hecha.</summary>
        public static BackupInfo Create(string projectRoot, string reason, DateTime? now = null, int maxCopies = MaxCopies)
        {
            if (!File.Exists(System.IO.Path.Combine(projectRoot, ProjectLayout.ProjectFile)))
                throw new InvalidOperationException("Esa carpeta no es un proyecto.");
            var time = now ?? DateTime.Now;
            var folder = FolderOf(projectRoot);
            Directory.CreateDirectory(folder);
            string name = Prefix + time.ToString(Stamp, CultureInfo.InvariantCulture) + "_" + Clean(reason) + ".zip";
            var path = System.IO.Path.Combine(folder, name);
            for (int n = 2; File.Exists(path); n++) path = System.IO.Path.Combine(folder, System.IO.Path.GetFileNameWithoutExtension(name) + "_" + n + ".zip");
            var tmp = path + ".tmp";
            using (var zipStream = File.Create(tmp))
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                foreach (var file in Directory.EnumerateFiles(projectRoot, "*", SearchOption.AllDirectories))
                {
                    var rel = ProjectLayout.Normalize(System.IO.Path.GetRelativePath(projectRoot, file));
                    if (rel.StartsWith(Folder + "/", StringComparison.OrdinalIgnoreCase)) continue;
                    var entry = zip.CreateEntry(rel, CompressionLevel.Optimal);
                    entry.LastWriteTime = File.GetLastWriteTime(file);
                    using (var src = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var dst = entry.Open())
                        src.CopyTo(dst);
                }
            }
            File.Move(tmp, path);
            Prune(projectRoot, maxCopies);
            return Info(path);
        }

        /// <summary>Las copias, de la más nueva a la más vieja.</summary>
        public static IReadOnlyList<BackupInfo> List(string projectRoot)
        {
            var folder = FolderOf(projectRoot);
            if (!Directory.Exists(folder)) return Array.Empty<BackupInfo>();
            return Directory.GetFiles(folder, Prefix + "*.zip").Select(Info).Where(b => b != null)
                .OrderByDescending(b => b.Time).ThenByDescending(b => b.Path, StringComparer.Ordinal).ToList();
        }

        /// <summary>Deja solo las <paramref name="max"/> más nuevas.</summary>
        public static void Prune(string projectRoot, int max = MaxCopies)
        {
            foreach (var old in List(projectRoot).Skip(Math.Max(1, max)))
                try { File.Delete(old.Path); } catch (IOException) { }
        }

        /// <summary>
        /// Devuelve el proyecto a una copia: primero guarda una copia de cómo está ahora («antes_de_restaurar»), luego
        /// borra lo que no estaba en la copia (menos «copias/») y escribe sus archivos.
        /// </summary>
        public static void Restore(string projectRoot, string backupPath)
        {
            if (!File.Exists(backupPath)) throw new FileNotFoundException("No existe esa copia.", backupPath);
            Create(projectRoot, "antes_de_restaurar", maxCopies: MaxCopies + 1);
            using (var zipStream = File.OpenRead(backupPath))
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Read))
            {
                var inZip = new HashSet<string>(zip.Entries.Select(e => ProjectLayout.Normalize(e.FullName)), StringComparer.OrdinalIgnoreCase);
                foreach (var file in Directory.EnumerateFiles(projectRoot, "*", SearchOption.AllDirectories).ToList())
                {
                    var rel = ProjectLayout.Normalize(System.IO.Path.GetRelativePath(projectRoot, file));
                    if (rel.StartsWith(Folder + "/", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!inZip.Contains(rel)) File.Delete(file);
                }
                foreach (var entry in zip.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue; // a folder
                    var dest = System.IO.Path.GetFullPath(System.IO.Path.Combine(projectRoot, entry.FullName));
                    if (!dest.StartsWith(System.IO.Path.GetFullPath(projectRoot), StringComparison.OrdinalIgnoreCase)) continue; // never outside
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest));
                    using (var src = entry.Open())
                    using (var dst = File.Create(dest))
                        src.CopyTo(dst);
                }
            }
            Prune(projectRoot, MaxCopies);
        }

        private static BackupInfo Info(string path)
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (!name.StartsWith(Prefix) || name.Length < Prefix.Length + Stamp.Length) return null;
            if (!DateTime.TryParseExact(name.Substring(Prefix.Length, Stamp.Length), Stamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                return null;
            string reason = name.Length > Prefix.Length + Stamp.Length + 1 ? name.Substring(Prefix.Length + Stamp.Length + 1) : "";
            return new BackupInfo(path, time, new FileInfo(path).Length, reason);
        }

        private static string Clean(string reason) =>
            new string((string.IsNullOrWhiteSpace(reason) ? "manual" : reason.Trim().ToLowerInvariant())
                .Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
    }

    /// <summary>
    /// Cuándo toca una copia automática: cada <see cref="Interval"/> y SOLO si hubo cambios desde la anterior (si no se
    /// toca nada, no se guarda nada). Sin reloj propio: la aplicación le dice qué hora es.
    /// </summary>
    public sealed class BackupSchedule
    {
        public TimeSpan Interval { get; }
        private DateTime _last;
        private bool _changed;

        public BackupSchedule(TimeSpan interval, DateTime start) { Interval = interval; _last = start; }

        public bool HasChanges => _changed;

        /// <summary>Algo cambió en el proyecto.</summary>
        public void NoteChange() => _changed = true;

        /// <summary>¿Toca copia ahora? (si sí, se da por hecha: se reinicia el plazo).</summary>
        public bool Due(DateTime now)
        {
            if (!_changed || now - _last < Interval) return false;
            _last = now;
            _changed = false;
            return true;
        }

        /// <summary>Se hizo una copia a mano: cuenta como la última.</summary>
        public void Done(DateTime now) { _last = now; _changed = false; }
    }
}
