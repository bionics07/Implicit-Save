using System;
using System.Text;
using ImplicitSave.Serialization;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

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

        [Test]
        [TestCase(typeof(UnityEngine.Vector2))]
        [TestCase(typeof(UnityEngine.Vector3))]
        [TestCase(typeof(UnityEngine.Vector4))]
        [TestCase(typeof(UnityEngine.Vector2Int))]
        [TestCase(typeof(UnityEngine.Vector3Int))]
        [TestCase(typeof(UnityEngine.Quaternion))]
        [TestCase(typeof(UnityEngine.Color))]
        [TestCase(typeof(UnityEngine.Color32))]
        [TestCase(typeof(UnityEngine.Rect))]
        [TestCase(typeof(UnityEngine.RectInt))]
        [TestCase(typeof(UnityEngine.Bounds))]
        [TestCase(typeof(UnityEngine.BoundsInt))]
        [TestCase(typeof(UnityEngine.LayerMask))]
        [TestCase(typeof(UnityEngine.Matrix4x4))]
        [TestCase(typeof(UnityEngine.Hash128))]
        public void UnityBuiltInStruct_IsStorable(Type type)
        {
            // None of these carry [Serializable] - Unity handles them in native code - so a rule
            // that asks for the attribute rejects the most common types a save can hold.
            Assert.That(UnitySerializationRules.IsSerializableType(type, out var reason), Is.True, reason);
        }

        [Test]
        public void UnityBuiltInStructs_SurviveARoundTripWithTheirValues()
        {
            var data = new BuiltInStructsSaveData
            {
                Vector2 = new Vector2(1.5f, -2.5f),
                Vector3 = new Vector3(1f, 2f, 3f),
                Vector4 = new Vector4(1f, 2f, 3f, 4f),
                Vector2Int = new Vector2Int(7, -8),
                Vector3Int = new Vector3Int(1, 2, 3),
                Quaternion = new Quaternion(0f, 0.7071f, 0f, 0.7071f),
                Color = new Color(0.5f, 0.25f, 0.125f, 1f),
                Color32 = new Color32(10, 20, 30, 40),
                Rect = new Rect(1f, 2f, 3f, 4f),
                RectInt = new RectInt(1, 2, 3, 4),
                Bounds = new Bounds(new Vector3(1f, 2f, 3f), new Vector3(4f, 6f, 8f)),
                BoundsInt = new BoundsInt(1, 2, 3, 4, 5, 6),
                LayerMask = 5
            };

            var bytes = _serializer.Serialize(data, "built_in_structs");
            var back = (BuiltInStructsSaveData)_serializer.Deserialize(bytes, typeof(BuiltInStructsSaveData));

            Assert.That(back.Vector2, Is.EqualTo(data.Vector2));
            Assert.That(back.Vector3, Is.EqualTo(data.Vector3));
            Assert.That(back.Vector4, Is.EqualTo(data.Vector4));
            Assert.That(back.Vector2Int, Is.EqualTo(data.Vector2Int));
            Assert.That(back.Vector3Int, Is.EqualTo(data.Vector3Int));
            Assert.That(back.Quaternion, Is.EqualTo(data.Quaternion));
            Assert.That(back.Color, Is.EqualTo(data.Color));
            Assert.That(back.Color32.r, Is.EqualTo(data.Color32.r));
            Assert.That(back.Color32.a, Is.EqualTo(data.Color32.a));
            Assert.That(back.Rect, Is.EqualTo(data.Rect));
            Assert.That(back.RectInt.width, Is.EqualTo(data.RectInt.width));
            Assert.That(back.Bounds, Is.EqualTo(data.Bounds));
            Assert.That(back.BoundsInt, Is.EqualTo(data.BoundsInt));
            Assert.That(back.LayerMask.value, Is.EqualTo(data.LayerMask.value));
        }

        [Test]
        public void UnityBuiltInStructs_AreWrittenWithTheNamesTheCSharpApiUses()
        {
            // A save file is read and migrated by hand. Unity's own m_Center / m_XMin spellings
            // would leak an internal name nobody writes in code.
            var data = new BuiltInStructsSaveData
            {
                Rect = new Rect(1f, 2f, 3f, 4f),
                Bounds = new Bounds(Vector3.zero, Vector3.one),
                LayerMask = 5
            };

            var json = Encoding.UTF8.GetString(_serializer.Serialize(data, "built_in_structs"));

            Assert.That(json, Does.Contain("\"width\""));
            Assert.That(json, Does.Contain("\"height\""));
            Assert.That(json, Does.Contain("\"center\""));
            Assert.That(json, Does.Contain("\"extents\""));
            Assert.That(json, Does.Not.Contain("m_XMin"));
            Assert.That(json, Does.Not.Contain("m_Center"));

            // A mask is one number; wrapping it in an object would only hide that. Parsed rather
            // than matched as text, so the assertion does not depend on pretty-printing.
            var mask = JObject.Parse(json)["data"]["LayerMask"];

            Assert.That(mask.Type, Is.EqualTo(JTokenType.Integer));
            Assert.That(mask.Value<int>(), Is.EqualTo(5));
        }

        [Test]
        public void UnityBuiltInStructs_NeverSerializeAsAnEmptyObject()
        {
            // The failure mode this whole group exists to stop: the field is accepted, the window
            // shows a value, and the file holds {}.
            var data = new BuiltInStructsSaveData
            {
                Vector3Int = new Vector3Int(1, 2, 3),
                Rect = new Rect(1f, 2f, 3f, 4f),
                Bounds = new Bounds(Vector3.one, Vector3.one),
                BoundsInt = new BoundsInt(1, 2, 3, 4, 5, 6),
                Vector2Int = new Vector2Int(1, 2),
                RectInt = new RectInt(1, 2, 3, 4)
            };

            var json = Encoding.UTF8.GetString(_serializer.Serialize(data, "built_in_structs"));

            Assert.That(json, Does.Not.Contain("{}"), json);
        }

        [Test]
        public void AnimationCurve_IsRefusedWithSomethingToDoAboutIt()
        {
            // Unity does serialize it, so "no [Serializable]" would read as Unity's mistake rather
            // than our decision.
            Assert.That(UnitySerializationRules.IsSerializableType(typeof(AnimationCurve), out var reason), Is.False);
            Assert.That(reason, Does.Contain("ScriptableObject"));
        }

        [Test]
        public void Gradient_IsRefusedWithSomethingToDoAboutIt()
        {
            Assert.That(UnitySerializationRules.IsSerializableType(typeof(Gradient), out var reason), Is.False);
            Assert.That(reason, Does.Contain("ScriptableObject"));
        }
    }
}
