using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using CTEditor.World.Domain;

namespace CTEditor.App
{
    /// <summary>
    /// Editor de PERFILES DE PRUEBA: a la izquierda la lista (nuevo, duplicar, borrar); a la derecha el elegido: nombre,
    /// hora, medallas, dinero, equipo (con el selector de especies), objetos e interruptores encendidos.
    /// </summary>
    public static class ProfilesDialog
    {
        private static readonly (TimeOfDay? time, string icon, string label)[] Hours =
        {
            (null, "reloj", "La del reloj"), (TimeOfDay.Morning, "manana", "Mañana"), (TimeOfDay.Day, "dia", "Día"),
            (TimeOfDay.Evening, "tarde", "Tarde"), (TimeOfDay.Night, "noche", "Noche"),
        };

        public static void Show(AppShell shell)
        {
            var d = shell.ShowDialog("Perfiles de prueba", 64, 80);
            var work = shell.Profiles.Select(p => p.Clone()).ToList();
            string selected = shell.ActiveProfile.Id;
            var left = Ui.Column(4);
            left.style.width = 220;
            left.style.flexShrink = 0;
            var right = Ui.Scroll().Grow();
            var columns = Ui.Row(16);
            columns.style.alignItems = Align.Stretch;
            columns.style.flexGrow = 1;
            columns.With(left, Ui.Separator(vertical: true), right);
            d.Body.Add(columns);

            TestProfile Current() => work.FirstOrDefault(p => p.Id == selected) ?? work.FirstOrDefault();

            void Fill()
            {
                left.Clear();
                left.Add(Ui.SectionTitle("Perfiles", Ui.IconButton("mas", () =>
                {
                    var p = new TestProfile(NewId(work, "perfil"), "Perfil " + (work.Count + 1));
                    work.Add(p); selected = p.Id; Fill();
                }, "Nuevo perfil")));
                foreach (var p in work)
                {
                    var id = p.Id;
                    var row = Ui.Row(6).Pad(8, 5).Round(4);
                    if (id == selected) row.style.backgroundColor = Ui.C("seleccion");
                    var n = Ui.Text(p.Name, bold: id == selected).Grow(); n.pickingMode = PickingMode.Ignore;
                    row.Add(n);
                    row.RegisterCallback<PointerUpEvent>(_ => { selected = id; Fill(); });
                    left.Add(row);
                }
                Editor();
            }

            void Editor()
            {
                right.Clear();
                var p = Current();
                if (p == null) return;
                var body = Ui.Column(10).Pad(4, 0);
                right.Add(body);

                var head = Ui.Row(6);
                head.With(Ui.TextBox("Nombre", p.Name, v => { p.Name = string.IsNullOrWhiteSpace(v) ? p.Name : v.Trim(); Fill(); }, delayed: true).Grow(),
                    Ui.IconButton("copiar", () => { var c = p.Clone(NewId(work, p.Id), p.Name + " (copia)"); work.Add(c); selected = c.Id; Fill(); }, "Duplicar"),
                    Ui.IconButton("papelera", () =>
                    {
                        if (work.Count == 1) { shell.Warn("Tiene que quedar al menos un perfil."); return; }
                        work.Remove(p); selected = work[0].Id; Fill();
                    }, "Borrar este perfil"));
                body.Add(head);

                body.Add(Ui.SectionTitle("Hora del día"));
                var hours = Ui.Row(2);
                foreach (var (time, icon, label) in Hours)
                {
                    var t = time;
                    hours.Add(Ui.IconButton(icon, () => { p.Time = t; Editor(); }, label, p.Time == t));
                }
                hours.Add(Ui.Text(Hours.First(h => h.time == p.Time).label, 0.85f, dim: true).Margin(6, 0, 0, 0));
                body.Add(hours);

                body.Add(Ui.SectionTitle("Progreso"));
                var progress = Ui.Row(22).Wrap();
                progress.With(Ui.NumberBox("Medallas", p.Badges, 0, TestProfile.MaxBadges, v => p.Badges = v),
                    Ui.NumberBox("Dinero", p.Money, 0, 9999999, v => p.Money = v));
                body.Add(progress);

                body.Add(Ui.SectionTitle($"Equipo ({p.Party.Count}/{TestProfile.MaxParty})", p.Party.Count < TestProfile.MaxParty
                    ? Ui.IconButton("mas", () => SpeciesPicker.Show(shell, id => { p.Party.Add(new TestMember(id, 5)); Editor(); }), "Añadir al equipo") : null));
                if (p.Party.Count == 0) body.Add(Ui.Hint("Sin equipo: se empieza como al principio del juego."));
                for (int i = 0; i < p.Party.Count; i++)
                {
                    var m = p.Party[i];
                    int index = i;
                    var row = Ui.Row(8).Pad(6, 4);
                    var name = Ui.Button(SpeciesPicker.Name(shell, m.SpeciesId), () => SpeciesPicker.Show(shell, id => { m.SpeciesId = id; Editor(); }), Ui.ButtonKind.Flat, "Cambiar la especie");
                    name.style.unityFontStyleAndWeight = FontStyle.Bold;
                    row.With(SpeciesPicker.Picture(shell, m.SpeciesId, 26), name, Ui.Spacer(), Ui.Text("Nv.", 0.85f, dim: true),
                        Ui.MiniNumber(m.Level, 1, 100, v => m.Level = v, "Nivel"),
                        Ui.IconButton("papelera", () => { p.Party.RemoveAt(index); Editor(); }, "Quitar del equipo"));
                    body.Add(row);
                }

                body.Add(Ui.SectionTitle("Objetos en la mochila", Ui.IconButton("mas", () =>
                    shell.Prompt("Añadir objeto", "Id del objeto (de datos/objetos.csv)", "", "Añadir", v =>
                    {
                        var id = (v ?? "").Trim();
                        if (id.Length == 0) return;
                        p.Items[id] = p.Items.TryGetValue(id, out var n) ? n + 1 : 1;
                        Editor();
                    }), "Añadir un objeto")));
                if (p.Items.Count == 0) body.Add(Ui.Hint("Mochila vacía."));
                foreach (var kv in p.Items.ToList())
                {
                    var id = kv.Key;
                    var row = Ui.Row(8).Pad(6, 2);
                    row.With(Ui.Text(id).Grow(), Ui.Text("×", dim: true), Ui.MiniNumber(kv.Value, 1, 999, v => p.Items[id] = v, "Cantidad"),
                        Ui.IconButton("papelera", () => { p.Items.Remove(id); Editor(); }, "Quitar"));
                    body.Add(row);
                }

                body.Add(Ui.SectionTitle("Interruptores encendidos", Ui.IconButton("mas", () =>
                    shell.Prompt("Encender un interruptor", "Interruptor (p. ej. liga_vencida)", "", "Añadir", v =>
                    {
                        var f = (v ?? "").Trim();
                        if (f.Length > 0) { p.Flags.Add(f); Editor(); }
                    }), "Encender un interruptor")));
                if (p.Flags.Count == 0) body.Add(Ui.Hint("Ninguno: lo que dependa de un interruptor (especies que salen tras la Liga...) no aparece."));
                var flags = Ui.Row(0).Wrap();
                foreach (var f in p.Flags.OrderBy(x => x).ToList())
                {
                    var flag = f;
                    var chip = Ui.Chip(f + "  ×", true, () => { p.Flags.Remove(flag); Editor(); }, "Clic: apagar");
                    flags.Add(chip.Margin(0, 2, 4, 2));
                }
                body.Add(flags);

                var known = new HashSet<string>((shell.Maps?.Species?.All() ?? new (string id, string name)[0]).Select(x => x.id), StringComparer.OrdinalIgnoreCase);
                var problems = p.Problems(known.Count == 0 ? null : (Func<string, bool>)known.Contains);
                foreach (var pr in problems) body.Add(Ui.Text(pr, 0.88f, wrap: true).Colored("aviso"));
            }

            Fill();
            d.Buttons.With(Ui.Button("Cancelar", () => shell.CloseDialog(d)),
                Ui.Button("Usar este perfil", () => Save(true), Ui.ButtonKind.Normal),
                Ui.Button("Guardar", () => Save(false), Ui.ButtonKind.Primary));

            void Save(bool activate)
            {
                shell.Profiles.Clear();
                shell.Profiles.AddRange(work);
                shell.SaveProfiles();
                if (activate) shell.SetActiveProfile(Current().Id);
                shell.CloseDialog(d);
            }
        }

        private static string NewId(List<TestProfile> list, string baseId)
        {
            string id = baseId;
            for (int n = 2; list.Any(p => p.Id == id); n++) id = baseId + "_" + n;
            return id;
        }
    }
}
