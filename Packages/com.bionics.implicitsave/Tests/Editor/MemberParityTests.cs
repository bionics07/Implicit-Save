using System;
using System.Collections.Generic;
using ImplicitSave.Serialization;
using ImplicitSave.Tests;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ImplicitSave.Tests.EditorTests
{
    /// <summary>
    /// The test the whole serialization contract rests on: for each fixture, the members Unity
    /// serializes are exactly the members Newtonsoft serializes.
    /// </summary>
    /// <remarks>
    /// This one is not negotiable. If it ever fails, the editor window and the save file have
    /// started telling different stories about the same data, which is the worst bug a save system
    /// can ship. Do not remove it, do not relax it, do not add an exception list to make it pass.
    /// <para>
    /// Unity's side is read from a real <c>SerializedObject</c> rather than from the documentation,
    /// so the test tracks whatever Unity actually does, including across version changes.
    /// </para>
    /// </remarks>
    public class MemberParityTests
    {
        private ParityHost _host;

        [SetUp]
        public void SetUp()
        {
            _host = ScriptableObject.CreateInstance<ParityHost>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_host);
        }

        private static IEnumerable<Type> Fixtures()
        {
            yield return typeof(ContractSaveData);
            yield return typeof(ItemsSaveData);
            yield return typeof(InventorySaveData);
            yield return typeof(HookSaveData);
            yield return typeof(VersionedSaveData);

            // Every Unity built-in struct at once. These carry no [Serializable], so the rules used
            // to reject all of them and a save silently lost its positions and colours - the exact
            // divergence this test exists to catch.
            yield return typeof(BuiltInStructsSaveData);
        }

        [Test]
        [TestCaseSource(nameof(Fixtures))]
        public void UnityAndNewtonsoftSerializeTheSameMembers(Type saveType)
        {
            var fromUnity = GetUnityMembers(saveType);
            var fromRules = GetContractMembers(saveType);

            Assert.That(fromRules, Is.EqualTo(fromUnity),
                $"Unity and ImplicitSave disagree about what '{saveType.Name}' contains. " +
                "The editor would show one thing and the file would hold another.");
        }

        [Test]
        public void TheFixturesActuallyExerciseTheRules()
        {
            // A parity test over empty lists would pass and prove nothing.
            var members = GetUnityMembers(typeof(ContractSaveData));

            Assert.That(members, Does.Contain("PublicField"));
            Assert.That(members, Does.Contain("_privateWithAttribute"),
                "If Unity stopped serializing this, the fixture would no longer cover the rule.");
            Assert.That(members, Does.Not.Contain("WritableProperty"));
            Assert.That(members, Does.Not.Contain("_privateWithoutAttribute"));
        }

        /// <summary>Asks Unity itself which members it keeps, through a managed reference.</summary>
        private List<string> GetUnityMembers(Type saveType)
        {
            _host.Payload = (SaveData)Activator.CreateInstance(saveType);

            var serializedObject = new SerializedObject(_host);
            var payload = serializedObject.FindProperty(nameof(ParityHost.Payload));
            var names = new List<string>();

            var iterator = payload.Copy();
            var end = payload.GetEndProperty();
            var depth = payload.depth;

            while (iterator.NextVisible(enterChildren: true) && !SerializedProperty.EqualContents(iterator, end))
            {
                // Only the direct children: nested members belong to the nested type's own contract.
                if (iterator.depth != depth + 1)
                {
                    continue;
                }

                names.Add(iterator.name);
            }

            return names;
        }

        /// <summary>Asks the package's rules the same question.</summary>
        private static List<string> GetContractMembers(Type saveType)
        {
            var names = new List<string>();

            foreach (var field in UnitySerializationRules.GetSerializedFields(saveType))
            {
                names.Add(field.Name);
            }

            return names;
        }
    }

    /// <summary>
    /// Holds a save instance so a <c>SerializedObject</c> can be built over it. This mirrors what
    /// the editor window's proxy will do in a later phase.
    /// </summary>
    public class ParityHost : ScriptableObject
    {
        [SerializeReference] public SaveData Payload;
    }
}
