using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using CTEditor.GameDefinition.Editor;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// "¿Quién usa esto?": encuentra referencias por OBJETO y por ID, y no confunde familias que
    /// comparten id (el TIPO "poison" y el ESTADO "poison").
    /// </summary>
    public class ReferenceFinderTests
    {
        private static T Make<T>(string id) where T : ScriptableObject
        {
            var a = ScriptableObject.CreateInstance<T>();
            Set(a, "id", id);
            return a;
        }

        private static void Set(object target, string field, object value)
            => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        [Test]
        public void Finds_id_references_to_the_right_family_only()
        {
            var poisonStatus = Make<StatusConditionData>("poison");
            var poisonType = Make<ElementTypeData>("poison");
            var immunity = Make<AbilityData>("immunity");
            Set(immunity, "statusImmunities", new[] { "poison", "toxic" });

            var toStatus = ReferenceFinder.FindIn(immunity, poisonStatus);
            Assert.AreEqual(1, toStatus.Count);
            Assert.IsTrue(toStatus[0].ById);
            Assert.AreEqual("statusImmunities.Array.data[0]", toStatus[0].PropertyPath);
            Assert.AreEqual("statusImmunities[1]", toStatus[0].Field);

            // El texto "poison" de las inmunidades de ESTADO no es una referencia al TIPO "poison".
            Assert.AreEqual(0, ReferenceFinder.FindIn(immunity, poisonType).Count);
        }

        [Test]
        public void Finds_object_references_inside_arrays()
        {
            var ground = Make<ElementTypeData>("ground");
            var levitate = Make<AbilityData>("levitate");
            Set(levitate, "typeImmunities", new[] { ground });

            var refs = ReferenceFinder.FindIn(levitate, ground);
            Assert.AreEqual(1, refs.Count);
            Assert.IsFalse(refs[0].ById); // por objeto: sobrevive a renombrados sin tocarla
        }

        [Test]
        public void Finds_curve_and_ability_ids_in_species()
        {
            var curve = Make<GrowthCurveData>("slow");
            var species = Make<SpeciesData>("snorlax");
            Set(species, "growthCurveId", "slow");

            var refs = ReferenceFinder.FindIn(species, curve);
            Assert.AreEqual(1, refs.Count);
            Assert.AreEqual("growthCurveId", refs[0].PropertyPath);
        }
    }
}
