using System.Text;
using ImplicitSave.Serialization;
using NUnit.Framework;

namespace ImplicitSave.Tests
{
    /// <summary>
    /// The Newtonsoft half of the serialization contract: exactly the members Unity keeps, and
    /// nothing else. The other half - that this really matches Unity - is the parity test in the
    /// editor suite.
    /// </summary>
    public class UnityContractResolverTests
    {
        private NewtonsoftSaveSerializer _serializer;

        [SetUp]
        public void SetUp()
        {
            _serializer = new NewtonsoftSaveSerializer();
        }

        [Test]
        public void PublicField_IsSaved()
        {
            var json = ToJson(new ContractSaveData { PublicField = 7 });

            Assert.That(json, Does.Contain("\"PublicField\": 7"));
        }

        [Test]
        public void PrivateFieldWithSerializeField_IsSaved()
        {
            var data = new ContractSaveData();
            data.SetPrivateValues(11, "texto");

            var restored = RoundTrip(data);
            restored.GetPrivateValues(out var number, out var text);

            Assert.That(number, Is.EqualTo(11),
                "Unity keeps a private field with [SerializeField], so the file has to keep it too.");
            Assert.That(text, Is.EqualTo("texto"));
        }

        [Test]
        public void PrivateFieldWithoutAttribute_IsNotSaved()
        {
            var data = new ContractSaveData();
            data.SetPrivateWithoutAttribute(99);

            var json = ToJson(data);
            var restored = RoundTrip(data);

            Assert.That(json, Does.Not.Contain("_privateWithoutAttribute"));
            Assert.That(restored.ReadPrivateWithoutAttribute(), Is.Zero);
        }

        [Test]
        public void Properties_AreNeverSaved()
        {
            var json = ToJson(new ContractSaveData { WritableProperty = 5, PublicField = 3 });

            Assert.That(json, Does.Not.Contain("WritableProperty"),
                "Unity ignores properties, so persisting one would make the window and the file disagree.");
            Assert.That(json, Does.Not.Contain("ComputedProperty"));
        }

        [Test]
        public void NonSerializedField_IsNotSaved()
        {
            var json = ToJson(new ContractSaveData { NonSerializedField = 4 });

            Assert.That(json, Does.Not.Contain("NonSerializedField"));
        }

        [Test]
        public void ReadOnlyAndStaticFields_AreNotSaved()
        {
            var json = ToJson(new ContractSaveData());

            Assert.That(json, Does.Not.Contain("ReadOnlyField"));
            Assert.That(json, Does.Not.Contain("StaticField"));
        }

        [Test]
        public void FieldNames_ReachTheFileVerbatim()
        {
            var json = ToJson(new ContractSaveData { PublicField = 1 });

            Assert.That(json, Does.Contain("\"PublicField\""), "Not camelCased, not renamed.");
            Assert.That(json, Does.Contain("\"_privateWithAttribute\""),
                "The underscore stays: the file mirrors what the user declared.");
        }

        [Test]
        public void EnvelopeStillWorks_EvenThoughItsMembersAreProperties()
        {
            // The envelope is package plumbing and uses properties, which the Unity rules exclude.
            // It is written with a separate serializer for exactly this reason.
            var json = ToJson(new ContractSaveData());

            Assert.That(json, Does.Contain("\"$saveId\""));
            Assert.That(json, Does.Contain("\"$schemaVersion\""));
        }

        [Test]
        public void Rules_MatchWhatUnityDoes()
        {
            var fields = UnitySerializationRules.GetSerializedFields(typeof(ContractSaveData));
            var names = new System.Collections.Generic.List<string>();
            foreach (var field in fields)
            {
                names.Add(field.Name);
            }

            Assert.That(names, Is.EqualTo(new[]
            {
                "PublicField",
                "PublicString",
                "PublicList",
                "_privateWithAttribute",
                "_privateStringWithAttribute",
                "Nested"
            }), "This list was taken from Unity's own serializer, not from the documentation.");
        }

        [Test]
        public void PlainDictionary_IsRejectedByTheRules()
        {
            var field = typeof(PlainDictionarySaveData).GetField("Broken");

            Assert.That(UnitySerializationRules.IsSerializedByUnity(field, out var reason), Is.False);

            // A public Dictionary without [SerializeField] never reaches the file, on any version.
            // Before Unity 6.6 the fix is SerializableDictionary; from 6.6 on it is the attribute.
            Assert.That(reason,
                UnitySerializationRules.NativeDictionaries
                    ? Does.Contain("[SerializeField]")
                    : Does.Contain("SerializableDictionary"),
                "The message has to name the fix, not just the problem.");
        }

        [Test]
        public void SerializableDictionary_IsAcceptedByTheRules()
        {
            var field = typeof(InventorySaveData).GetField("Counts");

            Assert.That(UnitySerializationRules.IsSerializedByUnity(field), Is.True);
        }

        private ContractSaveData RoundTrip(ContractSaveData data)
        {
            var bytes = _serializer.Serialize(data, "contract");
            return (ContractSaveData)_serializer.Deserialize(bytes, typeof(ContractSaveData));
        }

        private string ToJson(SaveData data)
        {
            return Encoding.UTF8.GetString(_serializer.Serialize(data, "contract"));
        }
    }
}
