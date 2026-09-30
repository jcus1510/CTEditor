using System;
using System.Collections.Generic;

namespace CTEditor.SharedKernel.Editing
{
    /// <summary>
    /// Un cambio que se puede deshacer. Cada editor (mapas, píxeles, eventos...) define los suyos; el historial es común.
    /// </summary>
    public interface IEditCommand
    {
        /// <summary>Texto para el menú («Deshacer: pintar 12 tiles»).</summary>
        string Label { get; }
        void Do();
        void Undo();
    }

    /// <summary>Varios cambios que se deshacen juntos.</summary>
    public sealed class CompositeCommand : IEditCommand
    {
        private readonly List<IEditCommand> _parts;
        public string Label { get; }
        public IReadOnlyList<IEditCommand> Parts => _parts;

        public CompositeCommand(string label, IEnumerable<IEditCommand> parts)
        {
            Label = label;
            _parts = new List<IEditCommand>(parts);
        }

        public void Do() { foreach (var p in _parts) p.Do(); }
        public void Undo() { for (int i = _parts.Count - 1; i >= 0; i--) _parts[i].Undo(); }
    }

    /// <summary>
    /// Deshacer / rehacer. «Execute» hace el cambio y lo apunta; «Record» apunta uno que ya se hizo (un trazo de pincel se
    /// ve mientras se arrastra y se apunta entero al soltar). Un cambio nuevo borra lo que se podía rehacer.
    /// </summary>
    public sealed class CommandHistory
    {
        private readonly List<IEditCommand> _undo = new List<IEditCommand>();
        private readonly List<IEditCommand> _redo = new List<IEditCommand>();

        public int Limit { get; }
        /// <summary>Cambios hechos desde el último guardado (0 = guardado). Negativo si se deshizo más allá.</summary>
        public int ChangesSinceSave { get; private set; }
        public bool IsDirty => ChangesSinceSave != 0;

        /// <summary>Se llama tras cada hacer / deshacer / rehacer.</summary>
        public event Action Changed;

        public CommandHistory(int limit = 200) { Limit = Math.Max(1, limit); }

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;
        public string UndoLabel => CanUndo ? _undo[_undo.Count - 1].Label : null;
        public string RedoLabel => CanRedo ? _redo[_redo.Count - 1].Label : null;

        public void Execute(IEditCommand command)
        {
            if (command == null) return;
            command.Do();
            Record(command);
        }

        public void Record(IEditCommand command)
        {
            if (command == null) return;
            _undo.Add(command);
            if (_undo.Count > Limit) _undo.RemoveAt(0);
            _redo.Clear();
            ChangesSinceSave++;
            Changed?.Invoke();
        }

        public bool Undo()
        {
            if (!CanUndo) return false;
            var c = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            c.Undo();
            _redo.Add(c);
            ChangesSinceSave--;
            Changed?.Invoke();
            return true;
        }

        public bool Redo()
        {
            if (!CanRedo) return false;
            var c = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            c.Do();
            _undo.Add(c);
            ChangesSinceSave++;
            Changed?.Invoke();
            return true;
        }

        public void MarkSaved()
        {
            ChangesSinceSave = 0;
            Changed?.Invoke();
        }

        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
            ChangesSinceSave = 0;
            Changed?.Invoke();
        }
    }
}
