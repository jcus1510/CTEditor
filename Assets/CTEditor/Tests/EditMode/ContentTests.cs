using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using CTEditor.Content;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// Bloque C (C1-C4): los datos del proyecto en CSV con esquema, errores con fila y columna, quién usa qué, y
    /// operaciones seguras: borrar con referencias (sustituir, quitar o dejar), renombrar en todas partes, papelera y
    /// deshacer.
    /// </summary>
    public class ContentTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "cteditor_content_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            void W(string f, params string[] lines) => File.WriteAllText(Path.Combine(_root, f), "﻿" + string.Join("\r\n", lines) + "\r\n");
            W("tipos.csv", "id;nombre;color", "electric;Eléctrico;F7D02C", "normal;Normal;A8A77A");
            W("tabla_tipos.csv", "ataca\\defiende;normal;electric", "normal;;", "electric;;0,5");
            W("movimientos.csv", "id;nombre;tipo;categoria;potencia;precision;pp;efectos",
                "thunder_shock;Impactrueno;electric;especial;40;100;30;estado:paralysis@10",
                "tackle;Placaje;normal;fisico;40;100;35;");
            W("objetos.csv", "id;nombre;efectos", "thunder_stone;Piedra Trueno;", "light_ball;Bolaluz;");
            W("especies.csv", "id;nombre;tipos;ps;ataque;defensa;atq_esp;def_esp;velocidad;aprende;evoluciona;descripcion;columna_mia",
                "pichu;Pichu;electric;20;40;15;35;35;60;1:thunder_shock;pikachu@amistad;\"Bebé; muy pequeño\";x",
                "pikachu;Pikachu;electric;35;55;40;50;50;90;1:thunder_shock|5:tackle;raichu@objeto:thunder_stone;Ratón;y",
                "raichu;Raichu;electric;60;90;55;90;80;110;1:thunder_shock;;;z");
            W("entrenadores.csv", "id;nombre;equipo;mochila", "rojo;Rojo;pikachu@81[thunder_shock/tackle]{light_ball} | raichu@70;light_ball:1");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [Test]
        public void Sheets_load_and_save_keeping_unknown_columns_and_quoted_text()
        {
            var db = ContentDatabase.Load(_root);
            var species = db.Table(ContentSchemas.Species);
            Assert.AreEqual(3, species.Records.Count);
            Assert.AreEqual("Bebé; muy pequeño", species.Find("pichu")["descripcion"]);
            Assert.IsTrue(species.Columns.Contains("columna_mia"), "columns CTEditor does not know are kept");
            Assert.IsEmpty(ContentChecks.Run(db).Where(i => i.Level == ContentIssueLevel.Error), "clean data has no errors");

            db.MarkDirty(ContentSchemas.Species);
            db.Save();
            var again = ContentDatabase.Load(_root).Table(ContentSchemas.Species);
            Assert.AreEqual("Bebé; muy pequeño", again.Find("pichu")["descripcion"]);
            Assert.AreEqual("z", again.Find("raichu")["columna_mia"]);
        }

        [Test]
        public void Errors_say_the_row_and_the_column_and_nothing_breaks()
        {
            File.WriteAllText(Path.Combine(_root, "movimientos.csv"),
                "id;nombre;tipo;categoria;potencia;pp\r\nsurf;Surf;water;especial;noventa;15\r\nsurf;Surf otra;normal;rara;90;15\r\n;Sin id;normal;fisico;1;1\r\n");
            var db = ContentDatabase.Load(_root);
            var issues = ContentChecks.Run(db);
            Assert.IsTrue(issues.Any(i => i.Row == 2 && i.Column == "potencia" && i.Text.Contains("no es un número")));
            Assert.IsTrue(issues.Any(i => i.Row == 2 && i.Column == "tipo" && i.Text.Contains("«water» no existe")));
            Assert.IsTrue(issues.Any(i => i.Row == 3 && i.Text.Contains("ya está en la fila 2")));
            Assert.IsTrue(issues.Any(i => i.Row == 3 && i.Column == "categoria" && i.Level == ContentIssueLevel.Warning));
            Assert.IsTrue(issues.Any(i => i.Row == 4 && i.Text.Contains("sin id")));
            // The trainer now uses moves that do not exist: reported, not fatal.
            Assert.IsTrue(issues.Any(i => i.Category == ContentSchemas.Trainers && i.Text.Contains("thunder_shock")));
        }

        [Test]
        public void The_index_finds_every_use()
        {
            var s = new ContentSession(ContentDatabase.Load(_root));
            var pikachu = s.UsesOf(ContentSchemas.Species, "pikachu");
            CollectionAssert.AreEquivalent(new[] { "especies:pichu", "entrenadores:rojo" }, pikachu.Select(u => u.OwnerCategory + ":" + u.OwnerId));
            Assert.AreEqual(3, s.UsesOf(ContentSchemas.Moves, "thunder_shock").Count(u => u.OwnerCategory == ContentSchemas.Species));
            Assert.IsTrue(s.UsesOf(ContentSchemas.Moves, "thunder_shock").Any(u => u.OwnerCategory == ContentSchemas.Trainers));
            Assert.IsTrue(s.UsesOf(ContentSchemas.Items, "thunder_stone").Any(u => u.Column == "evoluciona"));
            Assert.AreEqual(2, s.UsesOf(ContentSchemas.Items, "light_ball").Count, "held item and bag");
            Assert.IsTrue(s.UsesOf(ContentSchemas.Types, "electric").Any(u => u.OwnerCategory == ContentSchemas.TypeChart));
        }

        [Test]
        public void Deleting_pikachu_used_on_a_route_and_a_trainer_and_replacing_it_with_raichu()
        {
            var s = new ContentSession(ContentDatabase.Load(_root));
            string mapsGot = null;
            s.ExternalUses = () => new[] { new ContentRef(ContentSchemas.Species, "pikachu", "@mapas", "ruta_1", "encuentros", true, "Ruta 1 · hierba") };
            s.ExternalRewrite = (cat, old, now) => mapsGot = $"{cat}:{old}->{now}";
            Assert.AreEqual(3, s.UsesOf(ContentSchemas.Species, "pikachu").Count, "two in the sheets, one in a map");

            Assert.IsTrue(s.Delete(ContentSchemas.Species, "pikachu", DeleteMode.Replace, "raichu"));
            var db = s.Db;
            Assert.IsNull(db.Table(ContentSchemas.Species).Find("pikachu"));
            Assert.AreEqual("raichu@amistad", db.Table(ContentSchemas.Species).Find("pichu")["evoluciona"]);
            Assert.AreEqual("raichu@81[thunder_shock/tackle]{light_ball} | raichu@70", db.Table(ContentSchemas.Trainers).Find("rojo")["equipo"]);
            Assert.AreEqual("especies:pikachu->raichu", mapsGot, "the maps were told");
            Assert.IsEmpty(ContentChecks.Run(db).Where(i => i.Level == ContentIssueLevel.Error), "everything still works");
            Assert.AreEqual(1, s.Trash.Count);

            s.Undo();
            Assert.IsNotNull(db.Table(ContentSchemas.Species).Find("pikachu"), "one undo brings it back");
            Assert.AreEqual("pikachu@amistad", db.Table(ContentSchemas.Species).Find("pichu")["evoluciona"]);
        }

        [Test]
        public void Deleting_can_take_the_uses_out_or_leave_them()
        {
            var s = new ContentSession(ContentDatabase.Load(_root));
            s.Delete(ContentSchemas.Moves, "tackle", DeleteMode.RemoveUses);
            Assert.AreEqual("1:thunder_shock", s.Get(ContentSchemas.Species, "pikachu")["aprende"]);
            Assert.AreEqual("pikachu@81[thunder_shock]{light_ball} | raichu@70", s.Get(ContentSchemas.Trainers, "rojo")["equipo"]);

            s.Delete(ContentSchemas.Species, "pikachu", DeleteMode.RemoveUses);
            Assert.AreEqual("raichu@70", s.Get(ContentSchemas.Trainers, "rojo")["equipo"].Trim(), "the member leaves the team");
            Assert.AreEqual("", s.Get(ContentSchemas.Species, "pichu")["evoluciona"]);

            s.Delete(ContentSchemas.Items, "light_ball", DeleteMode.KeepUses);
            Assert.AreEqual("light_ball:1", s.Get(ContentSchemas.Trainers, "rojo")["mochila"], "left as it was");
            Assert.IsTrue(ContentChecks.Run(s.Db).Any(i => i.Text.Contains("light_ball") && i.Level == ContentIssueLevel.Error), "and Problems says so");

            Assert.IsFalse(s.Delete(ContentSchemas.Species, "raichu", DeleteMode.Replace, "nadie"), "replacing needs one that exists");
        }

        [Test]
        public void Renaming_an_id_changes_it_everywhere_even_the_type_chart_column()
        {
            var s = new ContentSession(ContentDatabase.Load(_root));
            Assert.IsTrue(s.Rename(ContentSchemas.Types, "electric", "electrico"));
            var db = s.Db;
            Assert.AreEqual("electrico", s.Get(ContentSchemas.Moves, "thunder_shock")["tipo"]);
            Assert.AreEqual("electrico", s.Get(ContentSchemas.Species, "raichu")["tipos"]);
            var chart = db.Table(ContentSchemas.TypeChart);
            Assert.IsTrue(chart.Columns.Contains("electrico") && !chart.Columns.Contains("electric"));
            Assert.AreEqual("0,5", chart.Find("electrico")["electrico"]);
            Assert.IsEmpty(ContentChecks.Run(db).Where(i => i.Level == ContentIssueLevel.Error));

            Assert.IsTrue(s.Rename(ContentSchemas.Moves, "thunder_shock", "impactrueno"));
            Assert.AreEqual("pikachu@81[impactrueno/tackle]{light_ball} | raichu@70", s.Get(ContentSchemas.Trainers, "rojo")["equipo"]);
            Assert.AreEqual("1:impactrueno|5:tackle", s.Get(ContentSchemas.Species, "pikachu")["aprende"]);

            Assert.IsFalse(s.Rename(ContentSchemas.Moves, "tackle", "impactrueno"), "taken");
            Assert.IsFalse(s.Rename(ContentSchemas.Moves, "tackle", "Placaje Fuerte"), "not a valid id");
            s.Undo();
            Assert.AreEqual("1:thunder_shock|5:tackle", s.Get(ContentSchemas.Species, "pikachu")["aprende"]);
        }

        [Test]
        public void Create_duplicate_change_trash_and_restore()
        {
            var s = new ContentSession(ContentDatabase.Load(_root));
            var mew = s.Create(ContentSchemas.Species, "Mew Nuevo");
            Assert.AreEqual("mew_nuevo", mew["id"]);
            var copy = s.Duplicate(ContentSchemas.Species, "pikachu");
            Assert.AreEqual(("pikachu_copia", "Pikachu (copia)"), (copy["id"], copy["nombre"]));
            Assert.IsTrue(s.SetValue(ContentSchemas.Species, "pikachu_copia", "ps", "99"));
            s.Undo();
            Assert.AreEqual("35", s.Get(ContentSchemas.Species, "pikachu_copia")["ps"]);

            s.Delete(ContentSchemas.Species, "raichu", DeleteMode.KeepUses);
            s.Save();
            var reopened = new ContentSession(ContentDatabase.Load(_root));
            Assert.AreEqual(1, reopened.Trash.Count, "the trash is saved");
            var back = reopened.Restore(reopened.Trash[0]);
            Assert.AreEqual("Raichu", back["nombre"]);
            Assert.AreEqual("110", back["velocidad"]);
            Assert.IsEmpty(reopened.Trash);
        }

        [Test]
        public void Teams_keep_every_annotation_and_natures_and_abilities_are_uses()
        {
            const string member = "haxorus@59%h[dragon_dance/outrage]{lum_berry}~jolly!mold_breaker(252 Atq/252 Vel)#31\"Hacha\"";
            var m = TeamFormat.ParseMember(member);
            Assert.AreEqual(("haxorus", 59, "h", "lum_berry", "jolly", "mold_breaker", 31, "Hacha"), (m.Species, m.Level, m.Gender, m.Item, m.Nature, m.Ability, m.Iv, m.Nickname));
            CollectionAssert.AreEqual(new[] { "dragon_dance", "outrage" }, m.Moves);
            Assert.AreEqual(member, TeamFormat.FormatMember(m), "written back the same");
            Assert.AreEqual("¿esto?", TeamFormat.FormatMember(TeamFormat.ParseMember("¿esto?")), "unreadable members are kept");

            File.WriteAllText(Path.Combine(_root, "naturalezas.csv"), "id;nombre;sube;baja\r\njolly;Alegre;speed;sp_attack\r\n");
            File.WriteAllText(Path.Combine(_root, "entrenadores.csv"), "id;nombre;equipo\r\nvictor;Víctor;pikachu@50~jolly | raichu@52\r\n");
            var s = new ContentSession(ContentDatabase.Load(_root));
            Assert.AreEqual(1, s.UsesOf(ContentSchemas.Natures, "jolly").Count);
            s.Rename(ContentSchemas.Natures, "jolly", "alegre");
            Assert.AreEqual("pikachu@50~alegre | raichu@52", s.Get(ContentSchemas.Trainers, "victor")["equipo"]);
        }

        [Test]
        public void Ids_are_made_from_names()
        {
            Assert.AreEqual("bola_sombra", ContentIds.Normalize("Bola Sombra!"));
            Assert.AreEqual("pinguino", ContentIds.Normalize("Pingüino"));
            Assert.IsTrue(ContentIds.IsValid("mr_mime"));
            Assert.IsFalse(ContentIds.IsValid("Mr. Mime"));
        }
    }
}
