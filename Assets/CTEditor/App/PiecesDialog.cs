using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using CTEditor.World.Domain;

namespace CTEditor.App
{
    /// <summary>
    /// PIEZAS REUTILIZABLES: a house, a big tree, a fountain... saved from a selection (all its layers) and placed in any
    /// map with a click, like pasting. Placing makes a copy: changing or removing the piece never touches the maps.
    /// </summary>
    public static class PiecesDialog
    {
        public static void Show(AppShell shell)
        {
            var S = shell.Maps;
            if (S?.Map == null) { shell.Warn("Abre un mapa para usar piezas."); return; }
            var atlases = new AtlasCache(shell.ProjectRoot, S.TilesetFor);
            var d = shell.ShowDialog("Piezas reutilizables", 50, 72);
            d.OnClose = atlases.Dispose;
            d.Body.Add(Ui.Hint("Una pieza es un trozo de mapa con todas sus capas (una casa, un árbol grande...). «Colocar» y clic en el mapa pone una copia; " +
                               "cambiar o quitar la pieza después no toca los mapas donde ya está."));
            var grid = Ui.Row(10).Wrap();
            var scroll = Ui.Scroll().Grow();
            scroll.Add(grid);
            d.Body.Add(scroll);

            void Rebuild()
            {
                grid.Clear();
                if (S.Pieces.Count == 0)
                {
                    grid.Add(Ui.EmptyState("casa", "Sin piezas", "Selecciona en el mapa una casa o un árbol (herramienta Selección, M) y pulsa «Guardar la selección como pieza» (Ctrl+Mayús+C).",
                        S.Selection.HasValue ? "Guardar la selección como pieza" : null, S.Selection.HasValue ? (Action)SaveSelection : null));
                    return;
                }
                foreach (var p in S.Pieces)
                {
                    var piece = p;
                    var card = Ui.Card();
                    card.style.width = 190;
                    card.Add(Preview(atlases, piece, 160));
                    var name = Ui.TextBox("", piece.Name, v => S.RenamePiece(piece, v), delayed: true);
                    card.Add(name);
                    card.Add(Ui.Hint($"{piece.Width} × {piece.Height} tiles · {piece.Content.Layers.Count(l => l.Any(c => c >= 0))} capas"));
                    var row = Ui.Row(4);
                    row.With(Ui.Button("Colocar", () => { shell.CloseDialog(d); S.PlacePiece(piece); shell.ActiveEditor = "mapa"; }, Ui.ButtonKind.Primary,
                            "Clic en el mapa para poner una copia (puedes poner varias; Esc termina)").Grow(),
                        Ui.IconButton("papelera", () => { S.DeletePiece(piece); Rebuild(); }, "Quitar la pieza (los mapas no cambian)"));
                    card.Add(row);
                    grid.Add(card);
                }
            }

            void SaveSelection()
            {
                if (S.SavePieceFromSelection("Pieza " + (S.Pieces.Count + 1)) != null) Rebuild();
            }

            Rebuild();
            var save = Ui.Button("Guardar la selección como pieza", SaveSelection, Ui.ButtonKind.Normal, "Ctrl+Mayús+C con una zona seleccionada");
            save.SetEnabledLook(S.Selection.HasValue);
            d.Buttons.With(save, Ui.Spacer(), Ui.Button("Cerrar", () => shell.CloseDialog(d), Ui.ButtonKind.Primary));
        }

        /// <summary>The piece drawn with its tiles, every layer on top of the one below.</summary>
        public static VisualElement Preview(AtlasCache atlases, MapPiece piece, float maxSide)
        {
            var c = piece.Content;
            float cell = Mathf.Max(2f, Mathf.Floor(Mathf.Min(24f, maxSide / Mathf.Max(c.Width, c.Height))));
            var box = new VisualElement().Bg("fondo").Border(1, "borde", 3);
            box.style.width = c.Width * cell + 2;
            box.style.height = c.Height * cell + 2;
            box.style.alignSelf = Align.Center;
            foreach (var layer in c.Layers)
                for (int y = 0; y < c.Height; y++)
                for (int x = 0; x < c.Width; x++)
                {
                    int t = layer[y * c.Width + x];
                    if (t < 0) continue;
                    int slot = MapTile.Slot(t);
                    if (slot >= c.TilesetIds.Count) continue;
                    var uv = atlases.Get(c.TilesetIds[slot])?.UvFor(MapTile.Index(t));
                    if (!uv.HasValue) continue;
                    var img = new Image { image = uv.Value.texture, uv = uv.Value.uv, scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
                    img.Absolute(x * cell, y * cell, cell, cell);
                    if (MapTile.Flags(t) != 0)
                    {
                        var (deg, sx) = MapRenderer.UiTransformOf(MapTile.Flags(t));
                        img.style.rotate = new Rotate(new Angle(deg, AngleUnit.Degree));
                        img.style.scale = new Scale(new Vector3(sx, 1, 1));
                    }
                    box.Add(img);
                }
            return box;
        }
    }
}
