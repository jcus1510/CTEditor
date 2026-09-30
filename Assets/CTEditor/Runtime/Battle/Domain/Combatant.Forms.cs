using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.GameDefinition.Domain.Abilities;
using CTEditor.GameDefinition.Domain.Species;

namespace CTEditor.Battle.Domain
{
    /// <summary>
    /// FORMAS DE COMBATE: el combatiente puede cambiar de forma (tipos, estadísticas, habilidad) sin dejar de ser el mismo
    /// individuo. La forma queda por DEBAJO de Transformación/Conversión (que siguen funcionando encima) y, al acabar el
    /// combate, no se guarda nada: el monstruo vuelve a su forma normal.
    /// </summary>
    public sealed partial class Combatant
    {
        private IReadOnlyList<BattleForm> _forms = Array.Empty<BattleForm>();
        private IReadOnlyList<FormChange> _formChanges = Array.Empty<FormChange>();
        private StatBlock _normalStats;
        private IReadOnlyList<Id<ElementType>> _normalTypes;
        private AbilityId? _normalAbility;

        /// <summary>Forma actual ("" = la normal).</summary>
        public string FormId { get; private set; } = "";

        /// <summary>Formas de combate que tiene.</summary>
        public IReadOnlyList<BattleForm> Forms => _forms;

        /// <summary>Reglas de cambio de forma de su especie.</summary>
        public IReadOnlyList<FormChange> FormChanges => _formChanges;

        /// <summary>La forma actual (null = la normal).</summary>
        public BattleForm CurrentForm => FindForm(FormId);

        public BattleForm FindForm(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var f in _forms) if (string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase)) return f;
            return null;
        }

        private void InitForms(BattleParticipant snapshot)
        {
            _forms = snapshot.Forms;
            _formChanges = snapshot.FormChanges;
            _normalStats = snapshot.Stats;
            _normalTypes = snapshot.Types;
            _normalAbility = snapshot.Ability;
        }

        /// <summary>
        /// Cambia a la forma 'formId' ("" = la normal). Devuelve false si no existe o ya estaba en ella. Los PS máximos no
        /// cambian; una habilidad cambiada por un movimiento (Imitación...) se pierde: manda la de la forma.
        /// </summary>
        internal bool ChangeForm(string formId)
        {
            formId = formId ?? "";
            if (string.Equals(formId, FormId, StringComparison.OrdinalIgnoreCase)) return false;
            if (formId.Length == 0)
            {
                _stats = _normalStats;
                _types = _normalTypes;
                _baseAbility = _normalAbility;
                FormId = "";
            }
            else
            {
                var form = FindForm(formId);
                if (form == null) return false;
                _stats = WithHp(form.Stats ?? _normalStats, _normalStats.Of(StatId.Hp));
                _types = form.Types.Count > 0 ? form.Types : _normalTypes;
                _baseAbility = form.Ability ?? _normalAbility;
                FormId = form.Id;
            }
            _abilityChanged = false;
            _abilityOverride = null;
            return true;
        }

        // Al retirarse: las formas que lo piden vuelven a la normal.
        private void RevertFormOnSwitch()
        {
            var f = CurrentForm;
            if (f != null && f.RevertsOnSwitch) ChangeForm("");
        }

        // Subida de nivel en mitad del combate: la forma normal toma las nuevas estadísticas y, si está en otra forma, sus
        // estadísticas crecen en la misma proporción (aproximación: la foto no guarda IVs ni EVs).
        private void GrowForm(StatBlock newNormal)
        {
            var old = _normalStats;
            _normalStats = newNormal;
            if (FormId.Length == 0) { _stats = newNormal; return; }
            var b = new StatBlock.Builder();
            foreach (var s in _stats.Stats)
            {
                int before = old.Of(s), after = newNormal.Of(s);
                b.Set(s, before > 0 ? (int)Math.Round(_stats.Of(s) * (double)after / before) : _stats.Of(s));
            }
            _stats = WithHp(b.Build(), newNormal.Of(StatId.Hp));
        }

        private static StatBlock WithHp(StatBlock stats, int hp)
        {
            var b = new StatBlock.Builder();
            foreach (var s in stats.Stats) b.Set(s, stats.Of(s));
            b.Set(StatId.Hp, hp);
            return b.Build();
        }
    }
}
