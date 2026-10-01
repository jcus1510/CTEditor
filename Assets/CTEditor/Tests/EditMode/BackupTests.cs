using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using CTEditor.Project;

namespace CTEditor.Tests.EditMode
{
    /// <summary>Version history: zipped backups, at most five, restore, and «only if something changed».</summary>
    public sealed class BackupTests
    {
        private static string NewProject()
        {
            var root = Path.Combine(Path.GetTempPath(), "ct_copias_" + Guid.NewGuid().ToString("N"));
            ProjectLayout.CreateFolders(root);
            File.WriteAllText(Path.Combine(root, ProjectLayout.ProjectFile), "{}");
            File.WriteAllText(Path.Combine(root, "mapas", "pueblo.json"), "v1");
            return root;
        }

        [Test]
        public void At_most_five_copies_are_kept_newest_first()
        {
            var root = NewProject();
            try
            {
                var t = new DateTime(2026, 10, 1, 12, 0, 0);
                for (int i = 0; i < 7; i++) ProjectBackups.Create(root, "auto", t.AddMinutes(10 * i));
                var list = ProjectBackups.List(root);
                Assert.AreEqual(ProjectBackups.MaxCopies, list.Count);
                Assert.AreEqual(t.AddMinutes(60), list[0].Time, "the newest first");
                Assert.AreEqual("auto", list[0].Reason);
                Assert.IsTrue(list.All(b => b.Bytes > 0));
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public void Restoring_brings_files_back_and_keeps_a_copy_of_the_current_state()
        {
            var root = NewProject();
            try
            {
                var copy = ProjectBackups.Create(root, "manual", new DateTime(2026, 10, 1, 12, 0, 0));
                File.WriteAllText(Path.Combine(root, "mapas", "pueblo.json"), "v2");
                File.WriteAllText(Path.Combine(root, "mapas", "nuevo.json"), "x");

                ProjectBackups.Restore(root, copy.Path);
                Assert.AreEqual("v1", File.ReadAllText(Path.Combine(root, "mapas", "pueblo.json")));
                Assert.IsFalse(File.Exists(Path.Combine(root, "mapas", "nuevo.json")), "files that were not in the copy go");
                Assert.IsTrue(ProjectBackups.List(root).Any(b => b.Reason == "antes_de_restaurar"), "a copy of how it was");
                Assert.IsFalse(Directory.GetFiles(ProjectBackups.FolderOf(root)).Any(f => f.EndsWith(".tmp")));
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public void Automatic_copies_only_when_something_changed()
        {
            var start = new DateTime(2026, 10, 1, 12, 0, 0);
            var s = new BackupSchedule(TimeSpan.FromMinutes(10), start);
            Assert.IsFalse(s.Due(start.AddMinutes(11)), "no changes: nothing");
            s.NoteChange();
            Assert.IsFalse(s.Due(start.AddMinutes(5)), "too soon");
            Assert.IsTrue(s.Due(start.AddMinutes(11)));
            Assert.IsFalse(s.Due(start.AddMinutes(30)), "nothing changed since");
            s.NoteChange();
            s.Done(start.AddMinutes(31));
            Assert.IsFalse(s.Due(start.AddMinutes(35)), "a manual copy counts");
        }
    }
}
