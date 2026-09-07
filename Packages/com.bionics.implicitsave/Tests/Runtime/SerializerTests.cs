using System.Text;
using ImplicitSave.Serialization;
using NUnit.Framework;

namespace ImplicitSave.Tests
{
    /// <summary>
    /// Covers phase 1's first acceptance criterion: a faithful round-trip of int, string, List and a
    /// nested object, plus the envelope those values are wrapped in.
    /// </summary>
    public class SerializerTests
    {
        private NewtonsoftSaveSerializer _serializer;

        [SetUp]
        public void SetUp()
        {
            _serializer = new NewtonsoftSaveSerializer();
        }

        [Test]
        public void RoundTrip_PreservesPrimitivesListsAndNestedObjects()
        {
            var original = new ItemsSaveData
            {
                SelectedSlot = 3,
                LastPickedUp = "rusty sword",
                Owned = { "potion", "rope", "map" },
                Stats =
                {
                    Strength = 12,
                    Multiplier = 1.75f,
                    History = { 1, 2, 3 }
                }
            };

            var bytes = _serializer.Serialize(original, "items");
            var restored = (ItemsSaveData)_serializer.Deserialize(bytes, typeof(ItemsSaveData));

            Assert.That(restored.SelectedSlot, Is.EqualTo(3));
            Assert.That(restored.LastPickedUp, Is.EqualTo("rusty sword"));
            Assert.That(restored.Owned, Is.EqualTo(new[] { "potion", "rope", "map" }));
            Assert.That(restored.Stats.Strength, Is.EqualTo(12));
            Assert.That(restored.Stats.Multiplier, Is.EqualTo(1.75f));
            Assert.That(restored.Stats.History, Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void RoundTrip_ReturnsANewInstance()
        {
            var original = new ItemsSaveData { SelectedSlot = 1 };

            var restored = _serializer.Deserialize(_serializer.Serialize(original, "items"), typeof(ItemsSaveData));

            Assert.That(restored, Is.Not.SameAs(original),
                "Deserialize must build a new instance, never hand back the one that was serialized.");
        }

        [Test]
        public void Serialize_WritesTheEnvelopeAroundThePayload()
        {
            var json = SerializeToJson(new ItemsSaveData { SelectedSlot = 2 }, "items");

            Assert.That(json, Does.Contain("\"$saveId\": \"items\""));
            Assert.That(json, Does.Contain("\"$schemaVersion\": 1"));
            Assert.That(json, Does.Contain("\"$savedAt\""));
            Assert.That(json, Does.Contain("\"data\""));
        }

        [Test]
        public void Serialize_RecordsTheTypesOwnSchemaVersion()
        {
            var envelope = _serializer.ReadEnvelope(_serializer.Serialize(new VersionedSaveData(), "versioned"));

            Assert.That(envelope.SchemaVersion, Is.EqualTo(7));
        }

        [Test]
        public void Serialize_KeepsRuntimeOnlyStateOutOfTheFile()
        {
            var data = new ItemsSaveData { ProfileId = 4, IsDirty = true };

            var json = SerializeToJson(data, "items");

            Assert.That(json, Does.Not.Contain("ProfileId"),
                "ProfileId is bookkeeping, not save data - it must never reach the file.");
            Assert.That(json, Does.Not.Contain("IsDirty"));
            Assert.That(json, Does.Not.Contain("SchemaVersion"),
                "SchemaVersion belongs to the envelope, not inside the payload.");
        }

        [Test]
        public void Serialize_NeverWritesDotNetTypeNames()
        {
            var json = SerializeToJson(new ItemsSaveData(), "items");

            Assert.That(json, Does.Not.Contain("$type"),
                "Type names in the file would make a class rename break a player's save (D10).");
            Assert.That(json, Does.Not.Contain("ImplicitSave.Tests"));
        }

        [Test]
        public void Deserialize_RejectsContentThatIsNotAnImplicitSaveFile()
        {
            var notOurs = Encoding.UTF8.GetBytes("{\"SelectedSlot\":3}");

            Assert.Throws<SaveSerializationException>(
                () => _serializer.Deserialize(notOurs, typeof(ItemsSaveData)));
        }

        [Test]
        public void Deserialize_RejectsGarbage()
        {
            var garbage = Encoding.UTF8.GetBytes("this is not json at all");

            Assert.Throws<SaveSerializationException>(
                () => _serializer.Deserialize(garbage, typeof(ItemsSaveData)));
        }

        [Test]
        public void Deserialize_RejectsAnEnvelopeWithNoPayload()
        {
            var noData = Encoding.UTF8.GetBytes("{\"$saveId\":\"items\",\"$schemaVersion\":1}");

            Assert.Throws<SaveSerializationException>(
                () => _serializer.Deserialize(noData, typeof(ItemsSaveData)));
        }

        [Test]
        public void Serialize_WithoutPrettyPrint_ProducesSmallerOutput()
        {
            var data = new ItemsSaveData { Owned = { "a", "b" } };

            var pretty = new NewtonsoftSaveSerializer(prettyPrint: true).Serialize(data, "items");
            var compact = new NewtonsoftSaveSerializer(prettyPrint: false).Serialize(data, "items");

            Assert.That(compact.Length, Is.LessThan(pretty.Length));
        }

        private string SerializeToJson(SaveData data, string saveId)
        {
            return Encoding.UTF8.GetString(_serializer.Serialize(data, saveId));
        }
    }
}
