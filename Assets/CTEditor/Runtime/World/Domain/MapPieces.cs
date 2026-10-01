using System;
using System.Collections.Generic;

namespace CTEditor.World.Domain
{
    /// <summary>
    /// A REUSABLE PIECE: a house, a big tree, a fountain... saved from a selection of a map with all its layers and placed
    /// in any other map like a paste (the tilesets it needs are added to that map). Placing makes a copy: changing or
    /// removing the piece later never touches the maps where it was placed.
    /// </summary>
    public sealed class MapPiece
    {
        public string Id { get; }
        public string Name { get; set; }
        public MapClipboard Content { get; }

        public MapPiece(string id, string name, MapClipboard content)
        {
            Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("La pieza necesita un id.", nameof(id)) : id;
            Name = name ?? id;
            Content = content ?? throw new ArgumentNullException(nameof(content));
        }

        public int Width => Content.Width;
        public int Height => Content.Height;
    }

    /// <summary>Where the project's pieces are kept («datos/piezas.json»).</summary>
    public interface IMapPieceRepository
    {
        IReadOnlyList<MapPiece> Load();
        void Save(IReadOnlyList<MapPiece> pieces);
    }
}
