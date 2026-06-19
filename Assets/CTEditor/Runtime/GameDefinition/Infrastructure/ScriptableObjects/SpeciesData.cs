using System;
using UnityEngine;

namespace CTEditor.GameDefinition.Infrastructure.ScriptableObjects
{
    /// <summary>
    /// La ficha del autor para una criatura. Es la más rica del motor, así que reúne varias técnicas
    /// de autoría a la vez. La clave conceptual: aquí el autor disfruta de COMODIDAD (campos con
    /// nombre, listas arrastrables), y el mapper la colapsa luego al dominio UNIFORME y puro.
    /// </summary>
    [CreateAssetMenu(menuName = "CTEditor/Species", fileName = "NewSpecies")]
    public sealed class SpeciesData : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;

        // Lista de tipos (arrastrables). El '[]' es un arreglo serializable: Unity lo muestra como
        // una lista editable. Soporta mono, doble o más tipos: la libertad de la que hablamos.
        [SerializeField] private ElementTypeData[] types;

        // --- STATS BASE ---
        // Aquí está la idea importante: en el dominio los stats son UNIFORMES por clave (StatBlock).
        // Pero pedirle al autor que escriba "hp", "attack"... a mano sería horrible. Así que en la
        // FICHA los 6 clásicos son campos con nombre (cómodos), y los inventados van en una lista.
        // El mapper junta ambos en el StatBlock único. Misma idea que los accesores .Attack del
        // dominio: una comodidad encima del almacenamiento uniforme, sin dos caminos de verdad.
        [Header("Stats base — clásicos")]
        [SerializeField, Min(0)] private int hp = 1;
        [SerializeField, Min(0)] private int attack = 1;
        [SerializeField, Min(0)] private int defense = 1;
        [SerializeField, Min(0)] private int spAttack = 1;
        [SerializeField, Min(0)] private int spDefense = 1;
        [SerializeField, Min(0)] private int speed = 1;

        [Header("Stats base — inventados (opcional)")]
        [SerializeField] private CustomStatValue[] customStats;

        [Header("Aprendizaje y evolución")]
        [SerializeField] private LearnableMoveEntry[] learnset;
        [SerializeField] private EvolutionEntry[] evolutions;

        // --- Getters de solo lectura ---
        public string Id => id;
        public string DisplayName => displayName;
        public ElementTypeData[] Types => types;
        public int Hp => hp;
        public int Attack => attack;
        public int Defense => defense;
        public int SpAttack => spAttack;
        public int SpDefense => spDefense;
        public int Speed => speed;
        public CustomStatValue[] CustomStats => customStats;
        public LearnableMoveEntry[] Learnset => learnset;
        public EvolutionEntry[] Evolutions => evolutions;

        // --- Structs anidados y serializables ---
        // '[Serializable]' le dice a Unity "sabes dibujar esto en el Inspector". Sin él, estas
        // estructuras no aparecerían como campos editables. Usamos campos públicos porque es la
        // convención de Unity para estos contenedores de datos planos.

        /// <summary>Un stat inventado: su id de texto y su valor base.</summary>
        [Serializable]
        public struct CustomStatValue
        {
            public string statId;
            public int value;
        }

        /// <summary>Una entrada del learnset: el movimiento (arrastrado) y a qué nivel se aprende.</summary>
        [Serializable]
        public struct LearnableMoveEntry
        {
            public MoveData move;
            public int level;
        }

        /// <summary>Una evolución: la especie destino (arrastrada) y el nivel requerido.</summary>
        [Serializable]
        public struct EvolutionEntry
        {
            public SpeciesData target;
            public int requiredLevel;
        }
    }
}
