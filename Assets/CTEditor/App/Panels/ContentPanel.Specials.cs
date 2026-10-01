using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using CTEditor.Content;

namespace CTEditor.App
{
    public sealed partial class ContentPanel
    {
        // ── Specials of each Unity editor ─────────────────────────────────────────────────────────

        private static readonly string[] NatureStats = { "attack", "defense", "speed", "sp_attack", "sp_defense" };
        private static readonly string[] NatureStatNames = { "Ataque", "Defensa", "Velocidad", "At. esp.", "Def. esp." };

        /// <summary>The 5 × 5 table of the Unity editor: rows = what goes up, columns = what goes down.</summary>
        private void NatureGrid(VisualElement box, ContentRecord chosen)
        {
            var t = Table;
            var grid = Ui.Column(2);
            var head = Ui.Row(2);
            head.Add(Ui.Text("sube \\ baja", 0.8f, dim: true).NoShrink());
            head[0].style.width = 80;
            foreach (var n in NatureStatNames) { var h = Ui.Text(n, 0.8f, dim: true); h.style.width = 92; head.Add(h); }
            grid.Add(head);
            for (int i = 0; i < 5; i++)
            {
                var row = Ui.Row(2);
                var label = Ui.Text(NatureStatNames[i], 0.8f, dim: true).NoShrink(); label.style.width = 80;
                row.Add(label);
                for (int j = 0; j < 5; j++)
                {
                    string up = NatureStats[i], down = NatureStats[j];
                    var r = t.Records.FirstOrDefault(x => x["sube"].Trim() == up && x["baja"].Trim() == down);
                    var cell = Ui.Text(r == null ? "—" : t.NameOf(r), 0.85f).Pad(4, 2).Round(3);
                    cell.style.width = 92;
                    cell.style.backgroundColor = r != null && r == chosen ? Ui.WithAlpha(Ui.C("acento"), 0.35f) : i == j ? Ui.C("panel_alt") : new Color(0, 0, 0, 0);
                    if (r != null) { var id = t.IdOf(r); cell.RegisterCallback<ClickEvent>(_ => Select(id)); cell.tooltip = id; }
                    row.Add(cell);
                }
                grid.Add(row);
            }
            box.Add(grid);
        }

        /// <summary>Total XP for a level with the classic formulas (the same as the game).</summary>
        public static double ClassicXp(string shape, int n)
        {
            double n3 = Math.Pow(n, 3);
            switch ((shape ?? "").Trim().ToLowerInvariant())
            {
                case "fast": case "rapida": return 4 * n3 / 5;
                case "medium_slow": case "parabolica": return Math.Max(0, 1.2 * n3 - 15 * n * n + 100 * n - 140);
                case "slow": case "lenta": return 5 * n3 / 4;
                case "erratic": case "erratica":
                    return n <= 50 ? n3 * (100 - n) / 50 : n <= 68 ? n3 * (150 - n) / 100 : n <= 98 ? n3 * Math.Floor((1911 - 10 * n) / 3.0) / 500 : n3 * (160 - n) / 100;
                case "fluctuating": case "fluctuante":
                    return n <= 15 ? n3 * (Math.Floor((n + 1) / 3.0) + 24) / 50 : n <= 36 ? n3 * (n + 14) / 50 : n3 * (Math.Floor(n / 2.0) + 32) / 50;
                default: return n3; // medium_fast / media
            }
        }

        /// <summary>XP per level as bars (every 5 levels), with the classic curves' totals at 50 and 100 to compare.</summary>
        private void CurveChart(VisualElement box, ContentRecord r)
        {
            double mult = ContentChecks.TryNumber(r["multiplicador_xp"], out var m) && m > 0 ? m : 1;
            int max = int.TryParse(r["nivel_max"], out var mx) && mx > 0 ? Math.Min(mx, 100) : 100;
            string shape = r["forma"].Trim().Length > 0 ? r["forma"] : Table.IdOf(r);
            double top = ClassicXp("slow", 100) * Math.Max(1, mult);
            var chart = Ui.Row(2);
            chart.style.alignItems = Align.FlexEnd;
            chart.style.height = 120;
            for (int lvl = 5; lvl <= max; lvl += 5)
            {
                double xp = ClassicXp(shape, lvl) * mult;
                var bar = new VisualElement().Round(2);
                bar.style.width = 10;
                bar.style.height = Length.Percent((float)Math.Max(1, xp / top * 100));
                bar.style.backgroundColor = Ui.C("acento");
                bar.tooltip = $"Nivel {lvl}: {xp:N0} XP";
                chart.Add(bar);
            }
            box.Add(chart);
            box.Add(Ui.Text($"Nivel 50: {ClassicXp(shape, 50) * mult:N0} XP · nivel {max}: {ClassicXp(shape, max) * mult:N0} XP", 0.9f));
            var cmp = Ui.Row(10).Wrap();
            foreach (var (id, name) in new[] { ("fast", "Rápida"), ("medium_fast", "Media"), ("medium_slow", "Parabólica"), ("slow", "Lenta"), ("erratic", "Errática"), ("fluctuating", "Fluctuante") })
                cmp.Add(Ui.Text($"{name}: {ClassicXp(id, 100):N0}", 0.82f, dim: true));
            box.Add(Ui.Hint("Al nivel 100, las clásicas:"));
            box.Add(cmp);
        }

        /// <summary>The family: who evolves into this, this, and what it evolves into (with how), clickable.</summary>
        private void Matrix()
        {
            var types = C.Db.Table(ContentSchemas.Types);
            var chart = Table;
            var ids = types.Ids.ToList();
            var box = Ui.Column(2).Pad(12, 8);
            box.Add(Ui.Hint("Filas: el tipo que ATACA. Columnas: el que DEFIENDE. Clic en una casilla: ×1 → ×2 → ×½ → ×0. Verde = muy eficaz, rojo = poco, negro = no afecta."));
            float w = Mathf.Round(Ui.FontSize * 3.2f);
            var head = Ui.Row(1);
            var corner = Ui.Text("ataca \\ defiende", 0.7f, dim: true); corner.style.width = 96; head.Add(corner);
            foreach (var d in ids)
            {
                var h = ContentLook.Badge(Short(types, d), ContentLook.TypeColor(C.Db, d));
                h.style.width = w; h.style.unityTextAlign = TextAnchor.MiddleCenter;
                h.tooltip = d;
                head.Add(h);
            }
            box.Add(head);
            foreach (var a in ids)
            {
                var row = Ui.Row(1);
                var label = ContentLook.Badge(types.NameOf(types.Find(a)), ContentLook.TypeColor(C.Db, a));
                label.style.width = 96;
                row.Add(label);
                var rec = chart.Find(a);
                foreach (var d in ids)
                {
                    string v = rec?[d] ?? "";
                    double x = ContentChecks.TryNumber(v, out var n) ? n : 1;
                    var cell = Ui.Text(x == 1 ? "" : x == 0 ? "0" : x > 1 ? "×" + Num(x) : "½", 0.9f, bold: true).Round(2);
                    cell.style.width = w;
                    cell.style.height = Ui.ControlHeight - 4;
                    cell.style.unityTextAlign = TextAnchor.MiddleCenter;
                    cell.style.backgroundColor = x == 1 ? Ui.C("panel_alt") : x == 0 ? new Color(0.1f, 0.1f, 0.1f) : x > 1 ? new Color(0.3f, 0.65f, 0.3f) : new Color(0.75f, 0.3f, 0.28f);
                    cell.style.color = Color.white;
                    cell.tooltip = $"{a} contra {d}: ×{Num(x)}";
                    string att = a, def = d;
                    cell.RegisterCallback<ClickEvent>(_ =>
                    {
                        string next = x == 1 ? "2" : x > 1 ? "0,5" : x > 0 ? "0" : "";
                        if (chart.Find(att) == null) C.Create(ContentSchemas.TypeChart, att);
                        if (!chart.Columns.Contains(def)) chart.EnsureColumn(def);
                        C.SetValue(ContentSchemas.TypeChart, att, def, next);
                    });
                    row.Add(cell);
                }
                box.Add(row);
            }
            _right.Add(box);
        }

        private static string Short(ContentTable types, string id)
        {
            var n = types.NameOf(types.Find(id));
            return n.Length <= 4 ? n : n.Substring(0, 3) + ".";
        }

        private static string Num(double x) => x.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',');

        /// <summary>A team as cards (species, level, moves, item, nature, ability), like the Unity trainer editor; «texto» edits it raw.</summary>

    }
}
