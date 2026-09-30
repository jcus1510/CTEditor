using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Types;

namespace CTEditor.GameDefinition.Domain.Hazards
{
    /// <summary>
    /// Una TRAMPA DE CAMPO (Púas, Trampa Rocas, Púas Tóxicas, Red Viscosa...): se coloca en el lado del
    /// rival y afecta a cada monstruo que ENTRE en ese lado (al cambiar o al salir tras un debilitado).
    /// Es contenido: el autor inventa las suyas combinando estas piezas.
    ///
    ///   • Capas: se puede poner varias veces (Púas: 3). Cada capa puede hacer más daño o cambiar el estado.
    ///   • Daño: % de los PS máximos según las capas; opcionalmente × la eficacia de un tipo (Trampa
    ///     Rocas: roca contra los tipos del que entra → un Charizard recibe ×4).
    ///   • Estado según las capas (Púas Tóxicas: 1 capa = envenenar; 2 = envenenar gravemente).
    ///   • Etapa de estadística al entrar (Red Viscosa: −1 Velocidad).
    ///   • Inmunes: tipos que no la notan (Volador) y quien sea inmune a un tipo por su habilidad
    ///     (Levitación = inmune a Tierra). Absorbida por tipos: al entrar, la RETIRA (un Veneno retira las
    ///     Púas Tóxicas).
    /// </summary>
    public sealed class HazardDefinition
    {
        public string Id { get; }
        public string DisplayName { get; }
        public int MaxLayers { get; }

        /// <summary>% de PS máximos por capa (posición 0 = 1 capa). Vacío = no hace daño.</summary>
        public IReadOnlyList<float> DamagePercentByLayer { get; }

        /// <summary>Si tiene valor, el daño se multiplica por la eficacia de ESTE tipo contra el que entra.</summary>
        public Id<ElementType>? DamageScalesWithType { get; }

        /// <summary>Estado por capa (posición 0 = 1 capa). Vacío = no inflige estados.</summary>
        public IReadOnlyList<string> StatusByLayer { get; }

        /// <summary>Etapa que cambia al entrar (Red Viscosa: speed −1). Null = ninguna.</summary>
        public StatId? Stat { get; }
        public int Stages { get; }

        /// <summary>Tipos que no la notan (Volador para las que están "en el suelo").</summary>
        public IReadOnlyList<Id<ElementType>> ImmuneTypes { get; }

        /// <summary>Si el que entra es inmune a ESTE tipo por su habilidad (Levitación → Tierra), no la nota.</summary>
        public Id<ElementType>? ImmuneIfAbilityBlocksType { get; }

        /// <summary>Tipos que, al entrar, la RETIRAN de su lado (Veneno retira Púas Tóxicas).</summary>
        public IReadOnlyList<Id<ElementType>> AbsorbedByTypes { get; }

        public HazardDefinition(string id, string displayName, int maxLayers = 1,
            IReadOnlyList<float> damagePercentByLayer = null, Id<ElementType>? damageScalesWithType = null,
            IReadOnlyList<string> statusByLayer = null, StatId? stat = null, int stages = 0,
            IReadOnlyList<Id<ElementType>> immuneTypes = null, Id<ElementType>? immuneIfAbilityBlocksType = null,
            IReadOnlyList<Id<ElementType>> absorbedByTypes = null)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("La trampa necesita un id.", nameof(id));
            Id = id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName;
            MaxLayers = Math.Max(1, maxLayers);
            DamagePercentByLayer = damagePercentByLayer == null ? Array.Empty<float>() : new List<float>(damagePercentByLayer);
            DamageScalesWithType = damageScalesWithType;
            StatusByLayer = statusByLayer == null ? Array.Empty<string>() : new List<string>(statusByLayer);
            Stat = stat;
            Stages = stages;
            ImmuneTypes = immuneTypes == null ? Array.Empty<Id<ElementType>>() : new List<Id<ElementType>>(immuneTypes);
            ImmuneIfAbilityBlocksType = immuneIfAbilityBlocksType;
            AbsorbedByTypes = absorbedByTypes == null ? Array.Empty<Id<ElementType>>() : new List<Id<ElementType>>(absorbedByTypes);
        }

        /// <summary>Daño (%) con estas capas (usa el último valor si hay más capas que valores).</summary>
        public float DamagePercentFor(int layers)
            => layers <= 0 || DamagePercentByLayer.Count == 0 ? 0f : DamagePercentByLayer[Math.Min(layers, DamagePercentByLayer.Count) - 1];

        /// <summary>Estado con estas capas (null = ninguno).</summary>
        public string StatusFor(int layers)
        {
            if (layers <= 0 || StatusByLayer.Count == 0) return null;
            var s = StatusByLayer[Math.Min(layers, StatusByLayer.Count) - 1];
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }
    }
}
