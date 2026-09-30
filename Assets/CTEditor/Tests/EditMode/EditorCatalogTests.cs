using System.Linq;
using NUnit.Framework;
using CTEditor.GameDefinition.Editor;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// CATÁLOGO DE EDITORES: cada editor está en una categoría, sin repetidos; las categorías tienen nombre
    /// e icono; los "próximamente" se pueden sustituir registrando el editor de verdad.
    /// </summary>
    public class EditorCatalogTests
    {
        [Test]
        public void Every_category_is_named_and_titles_are_unique()
        {
            foreach (var cat in EditorCatalog.Categories)
            {
                var info = EditorCatalog.Info(cat);
                Assert.IsFalse(string.IsNullOrWhiteSpace(info.name));
                Assert.IsFalse(string.IsNullOrWhiteSpace(info.icon));
            }
            var all = EditorCatalog.All;
            Assert.Greater(all.Count, 20);
            Assert.AreEqual(all.Count, all.Select(e => e.Category + "/" + e.Title).Distinct().Count(), "sin editores repetidos");
            Assert.IsTrue(all.Any(e => e.ComingSoon && e.Title == "Mapas"), "los mapas tienen su sitio reservado");
        }

        [Test]
        public void Registering_replaces_a_coming_soon_entry()
        {
            const string title = "Editor de prueba del catálogo";
            try
            {
                EditorCatalog.Register(new EditorEntry { Category = EditorCategory.Interface, Title = title }); // "próximamente"
                Assert.IsTrue(EditorCatalog.In(EditorCategory.Interface).Single(e => e.Title == title).ComingSoon);

                bool opened = false;
                EditorCatalog.Register(new EditorEntry { Category = EditorCategory.Interface, Title = title, Open = () => opened = true });
                var entry = EditorCatalog.In(EditorCategory.Interface).Single(e => e.Title == title);
                Assert.IsFalse(entry.ComingSoon, "el editor de verdad sustituye al hueco reservado");
                entry.Open();
                Assert.IsTrue(opened);
            }
            finally { EditorCatalog.Unregister(EditorCategory.Interface, title); }
        }

        [Test]
        public void Search_finds_editors_by_what_they_do()
        {
            Assert.IsTrue(EditorCatalog.All.Any(e => e.Title == "Cadenas evolutivas" && e.Matches("evolucion")));
            Assert.IsTrue(EditorCatalog.All.Any(e => e.Title == "Entrenadores" && e.Matches("ia")));
            Assert.IsFalse(EditorCatalog.All.Single(e => e.Title == "Climas").Matches("zzzz"));
        }
    }
}
