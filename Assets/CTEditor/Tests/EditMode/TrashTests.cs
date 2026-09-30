using NUnit.Framework;
using CTEditor.GameDefinition.Editor;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// PAPELERA: qué rutas cuentan como «en la papelera» y de qué categoría es cada ficha (para
    /// devolverla a su carpeta al recuperarla).
    /// </summary>
    public class TrashTests
    {
        [Test]
        public void Only_paths_inside_the_trash_folder_count_as_trashed()
        {
            Assert.IsTrue(ContentTrash.IsTrashedPath("Assets/GameContent/Papelera/Moves/confusion.asset"));
            Assert.IsTrue(ContentTrash.IsTrashedPath("Assets\\GameContent\\Papelera\\Moves\\confusion.asset"));
            Assert.IsFalse(ContentTrash.IsTrashedPath("Assets/GameContent/Resources/Moves/confusion.asset"));
            Assert.IsFalse(ContentTrash.IsTrashedPath("Assets/GameContent/PapeleraVieja/x.asset"));
            Assert.IsFalse(ContentTrash.IsTrashedPath(null));
        }

        [Test]
        public void The_category_comes_from_the_folder_in_resources_or_in_the_trash()
        {
            Assert.AreEqual("Moves", ContentTrash.CategoryOfPath("Assets/GameContent/Resources/Moves/confusion.asset"));
            Assert.AreEqual("Moves", ContentTrash.CategoryOfPath("Assets/GameContent/Papelera/Moves/confusion.asset"));
            Assert.AreEqual("Interface/Menus", ContentTrash.CategoryOfPath("Assets/GameContent/Resources/Interface/Menus/pausa.asset"));
            Assert.AreEqual("", ContentTrash.CategoryOfPath("Assets/Otra/cosa.asset"));
        }
    }
}
