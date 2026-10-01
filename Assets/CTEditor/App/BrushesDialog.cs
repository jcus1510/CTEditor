using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using CTEditor.Editing;
using CTEditor.World.Domain;

namespace CTEditor.App
{
    /// <summary>
    /// PINCELES ALEATORIOS: grass with flowers here and there, rocks of several shapes... Each brush is a list of tiles with
    /// a weight (how often each comes out). Made from the tiles chosen in Tiles; used by the pencil, rectangle and fill.
    /// </summary>
    public static class BrushesDialog
    {
        public static void Show(AppShell shell)
        {
            var S = shell.Maps;
            if (S?.Map == null) { shell.Warn("Abre un mapa para usar pinceles."); return; }
            var atlases = new AtlasCache(shell.ProjectRoot, S.TilesetFor);
            var d = shell.ShowDialog("Pinceles aleatorios", 46, 70);
            d.OnClose = atlases.Dispose;

            var list = Ui.Column(8);
            var scroll = Ui.Scroll().Grow();
            scroll.Add(list);
            d.Body.Add(Ui.Hint("Cada casilla que pintes sale con uno de los tiles del pincel al azar; los de más peso salen más a menudo. " +
                               "Funciona con el lápiz, el rectángulo y el relleno. Elegir un tile en Tiles vuelve al sello normal."));
            d.Body.Add(scroll);

            void Rebuild()
            {
                list.Clear();
                if (S.Brushes.Count == 0)
                    list.Add(Ui.EmptyState("dado", "Sin pinceles", "Elige en Tiles varios tiles (un bloque) y pulsa «Nuevo pincel con los tiles elegidos».",
                        "Nuevo pincel con los tiles elegidos", NewBrush));
                foreach (var b in S.Brushes)
                {
                    var brush = b;
                    var card = Ui.Card();
                    bool active = S.ActiveBrush == brush;
                    if (active) card.Border(2, "acento", 6);
                    var head = Ui.Row(6);
                    head.style.alignItems = Align.Center;
                    var name = Ui.TextBox("", brush.Name, v => { if (!string.IsNullOrWhiteSpace(v)) { brush.Name = v.Trim(); S.SaveBrushes(); } }, delayed: true).Grow();
                    head.With(name,
                        Ui.Button(active ? "En uso" : "Usar", () => { S.UseBrush(active ? null : brush); Rebuild(); },
                            active ? Ui.ButtonKind.Primary : Ui.ButtonKind.Normal, active ? "Dejar de usarlo (vuelve al sello)" : "Pintar con este pincel"),
                        Ui.IconButton("mas", () => AddTiles(brush), "Añadir los tiles elegidos en Tiles a este pincel"),
                        Ui.IconButton("papelera", () => { S.DeleteBrush(brush); Rebuild(); }, "Quitar el pincel"));
                    card.Add(head);
                    if (!brush.UsableIn(S.Map)) card.Add(Ui.Hint("Este mapa no usa los tilesets de este pincel.").Colored("aviso"));

                    var tiles = Ui.Row(6).Wrap();
                    foreach (var t in brush.Tiles.ToList())
                    {
                        var tile = t;
                        var cell = Ui.Column(2);
                        cell.style.alignItems = Align.Center;
                        cell.Add(Preview(atlases, tile));
                        var pct = Ui.Text(Mathf.RoundToInt((float)brush.Chance(tile) * 100) + " %", 0.8f, dim: true);
                        cell.Add(Ui.MiniNumber(tile.Weight, 0, 99, v => { tile.Weight = v; S.SaveBrushes(); Rebuild(); }, "Peso: cuántas veces más sale que uno de peso 1", 44));
                        cell.Add(pct);
                        var remove = Ui.IconButton("cerrar", () => { brush.Tiles.Remove(tile); S.SaveBrushes(); Rebuild(); }, "Quitar este tile del pincel", iconSize: Ui.IconSize * 0.7f);
                        cell.Add(remove);
                        tiles.Add(cell.Margin(0, 4, 4, 0));
                    }
                    card.Add(tiles);
                    list.Add(card);
                }
            }

            void NewBrush()
            {
                var b = S.NewBrushFromStamp("Pincel " + (S.Brushes.Count + 1));
                if (b != null) { S.UseBrush(b); Rebuild(); }
            }

            void AddTiles(RandomBrush brush)
            {
                var stamp = S.Stamp;
                int added = 0;
                for (int y = 0; y < stamp.Height; y++)
                for (int x = 0; x < stamp.Width; x++)
                {
                    int c = stamp[x, y];
                    if (c < 0 || MapTile.Slot(c) >= S.Map.TilesetIds.Count) continue;
                    brush.Add(S.Map.TilesetIds[MapTile.Slot(c)], MapTile.Index(c));
                    added++;
                }
                if (added == 0) { shell.Warn("Elige antes en Tiles los tiles que quieres añadir."); return; }
                S.SaveBrushes();
                Rebuild();
            }

            Rebuild();
            d.Buttons.With(Ui.Button("Nuevo pincel con los tiles elegidos", NewBrush), Ui.Spacer(),
                Ui.Button("Cerrar", () => shell.CloseDialog(d), Ui.ButtonKind.Primary));
        }

        private static VisualElement Preview(AtlasCache atlases, BrushTile t)
        {
            float size = Ui.ControlHeight + 10;
            var box = new VisualElement().Border(1, "borde", 3);
            box.style.width = size;
            box.style.height = size;
            var uv = atlases.Get(t.TilesetId)?.UvFor(t.Index);
            if (uv.HasValue)
            {
                var img = new Image { image = uv.Value.texture, uv = uv.Value.uv, scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
                img.Fill();
                box.Add(img);
            }
            else box.Add(Ui.Text("?", 1f, dim: true));
            box.tooltip = $"Tile n.º {t.Index} de «{t.TilesetId}»";
            return box;
        }
    }
}
