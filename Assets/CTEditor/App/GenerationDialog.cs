using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using CTEditor.Content;
using CTEditor.Project;

namespace CTEditor.App
{
    /// <summary>
    /// CAMBIAR DE GENERACIÓN (or bring in any pack): 1) choose the pack; 2) PREVIEW, category by category, of what is
    /// added, what changes (and which columns) and what the pack does not have (and whether it is used), choosing the
    /// categories and what to do with what is missing; 3) a backup is made, then it is applied as one undo step.
    /// </summary>
    public static class GenerationDialog
    {
        public static void Show(AppShell shell)
        {
            if (shell.Content == null) return;
            var packs = PackInstaller.Find(AppShell.PacksRoot);
            var d = shell.ShowDialog("Cambiar de generación / traer un pack", 56, 80);
            d.Body.Add(Ui.Hint("Elige el pack. Antes de cambiar nada verás qué se añade, qué cambia y qué no trae el pack; se hace una copia de seguridad y todo se deshace con Ctrl+Z."));
            var row = Ui.Row(6).Wrap();
            foreach (var p in packs)
            {
                var folder = p;
                row.Add(Ui.Button(Path.GetFileName(p), () => Preview(shell, d, folder), Ui.ButtonKind.Primary).Margin(0, 0, 6, 6));
            }
            if (packs.Count == 0) row.Add(Ui.Hint("No se encuentran los packs (Assets/GameContent/Packs).").Colored("aviso"));
            d.Body.Add(row);
            var body = Ui.Column(6).Grow();
            body.name = "preview";
            var scroll = Ui.Scroll().Grow();
            scroll.Add(body);
            d.Body.Add(scroll);
            d.Buttons.With(Ui.Spacer(), Ui.Button("Cerrar", () => shell.CloseDialog(d)));
        }

        private static void Preview(AppShell shell, AppShell.Dialog d, string folder)
        {
            var body = d.Body.Q("preview");
            body.Clear();
            ContentDatabase pack;
            try { pack = ContentDatabase.Load(folder); }
            catch (Exception e) { shell.Error("No se pudo leer el pack: " + e.Message); return; }
            var diffs = PackChange.Compare(shell.Content.Db, pack, shell.Content.Index);
            var chosen = new HashSet<string>(diffs.Where(x => x.HasChanges).Select(x => x.Schema.Key));
            bool trash = false;
            body.Add(Ui.SectionTitle($"Pasar a {Path.GetFileName(folder)}"));
            foreach (var diff in diffs.OrderBy(x => x.Schema.Title))
            {
                var df = diff;
                var card = Ui.Card();
                var head = Ui.Row(8);
                head.style.alignItems = Align.Center;
                head.Add(Ui.Check("", chosen.Contains(df.Schema.Key), on => { if (on) chosen.Add(df.Schema.Key); else chosen.Remove(df.Schema.Key); }));
                head.Add(Ui.Text(df.Schema.Title, bold: true));
                int used = df.Missing.Count(m => m.uses > 0);
                head.Add(Ui.Text($"+{df.Added.Count} nuevas · {df.Changed.Count} cambian · {df.Same} iguales · {df.Missing.Count} que el pack no trae"
                                 + (used > 0 ? $" ({used} en uso)" : ""), 0.88f, dim: !df.HasChanges).Grow());
                card.Add(head);
                if (df.Added.Count > 0) card.Add(Ui.Hint("Nuevas: " + Sample(df.Added)));
                if (df.Changed.Count > 0)
                    card.Add(Ui.Hint("Cambian: " + string.Join(", ", df.Changed.Take(12).Select(c => $"{c.id} ({string.Join(", ", c.columns.Take(3))}{(c.columns.Count > 3 ? "…" : "")})")) + (df.Changed.Count > 12 ? $" y {df.Changed.Count - 12} más" : "")));
                if (df.Missing.Count > 0)
                    card.Add(Ui.Hint("No las trae el pack: " + string.Join(", ", df.Missing.Take(15).Select(m => m.uses > 0 ? $"{m.id} (usada {m.uses})" : m.id)) + (df.Missing.Count > 15 ? $" y {df.Missing.Count - 15} más" : ""))
                        .Colored(used > 0 ? "aviso" : "texto_suave"));
                body.Add(card);
            }
            body.Add(Ui.SectionTitle("Lo que el pack no trae"));
            var opts = Ui.Row(6);
            Button keep = null, toTrash = null;
            void Opts() { keep.SetEnabledLook(true); }
            keep = Ui.Chip("Dejarlo como está", true, null);
            toTrash = Ui.Chip("Mandarlo a la papelera", false, null);
            keep.clicked += () => { trash = false; Mark(keep, true); Mark(toTrash, false); };
            toTrash.clicked += () => { trash = true; Mark(keep, false); Mark(toTrash, true); };
            opts.With(keep, toTrash);
            body.Add(opts);
            body.Add(Ui.Hint("Si mandas a la papelera algo que se usa (en mapas, equipos...), la ventana Problemas lo dirá; se recupera desde Datos → Papelera."));
            body.Add(Ui.Button("Cambiar (con copia de seguridad antes)", () =>
            {
                if (chosen.Count == 0) { shell.Warn("No hay nada elegido."); return; }
                try
                {
                    shell.SaveEverything(quiet: true);
                    var b = ProjectBackups.Create(shell.ProjectRoot, "antes de " + Path.GetFileName(folder));
                    int n = shell.Content.ApplyPack(pack, trash, chosen);
                    shell.Content.Save();
                    shell.CloseDialog(d);
                    shell.Success($"Proyecto pasado a {Path.GetFileName(folder)}: {n} fichas cambiadas. Copia de seguridad de las {b.Time:HH:mm}; Ctrl+Z en una ventana de datos lo deshace.");
                }
                catch (Exception e) { shell.Error("No se pudo cambiar: " + e.Message); }
            }, Ui.ButtonKind.Primary));
            Opts();
        }

        private static void Mark(Button chip, bool on) => Ui.StyleButton(chip, on ? Ui.ButtonKind.Primary : Ui.ButtonKind.Normal);

        private static string Sample(List<string> ids) => string.Join(", ", ids.Take(20)) + (ids.Count > 20 ? $" y {ids.Count - 20} más" : "");
    }
}
