using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using ImplicitSave.Serialization;
using NUnit.Framework;

namespace ImplicitSave.Tests
{
    /// <summary>
    /// Phase 2's first criterion: a dictionary that round-trips and produces clean JSON, for string,
    /// int and enum keys.
    /// </summary>
    public class SerializableDictionaryTests
    {
        private NewtonsoftSaveSerializer _serializer;

        [SetUp]
        public void SetUp()
        {
            _serializer = new NewtonsoftSaveSerializer();
        }

        [Test]
        public void BehavesLikeADictionary()
        {
            var map = new SerializableDictionary<string, int> { { "potion", 5 } };
            map["rope"] = 2;

            Assert.That(map.Count, Is.EqualTo(2));
            Assert.That(map["potion"], Is.EqualTo(5));
            Assert.That(map.ContainsKey("rope"), Is.True);
            Assert.That(map.TryGetValue("rope", out var rope), Is.True);
            Assert.That(rope, Is.EqualTo(2));
            Assert.That(map.Remove("rope"), Is.True);
            Assert.That(map.Count, Is.EqualTo(1));
        }

        [Test]
        public void WritesAsAPlainJsonObject()
        {
            var data = new InventorySaveData();
            data.Counts["potion"] = 5;
            data.Counts["rope"] = 2;

            var json = ToJson(data);

            Assert.That(json, Does.Contain("\"potion\": 5"),
                "A readable save file is one of the four things this package is sold on.");
            Assert.That(json, Does.Not.Contain("_keys"),
                "The two backing lists are an implementation detail and must not reach the file.");
            Assert.That(json, Does.Not.Contain("_values"));
        }

        [Test]
        public void RoundTrips_StringKeys()
        {
            var data = new InventorySaveData();
            data.Counts["potion"] = 5;
            data.Counts["rope"] = 2;

            var restored = RoundTrip(data);

            Assert.That(restored.Counts.Count, Is.EqualTo(2));
            Assert.That(restored.Counts["potion"], Is.EqualTo(5));
            Assert.That(restored.Counts["rope"], Is.EqualTo(2));
        }

        [Test]
        public void RoundTrips_IntKeys()
        {
            var data = new InventorySaveData();
            data.ById[1] = "espada";
            data.ById[42] = "escudo";

            var restored = RoundTrip(data);

            Assert.That(restored.ById[1], Is.EqualTo("espada"));
            Assert.That(restored.ById[42], Is.EqualTo("escudo"));
        }

        [Test]
        public void RoundTrips_EnumKeys()
        {
            var data = new InventorySaveData();
            data.ByRarity[ItemRarity.Rare] = 3;
            data.ByRarity[ItemRarity.Legendary] = 1;

            var restored = RoundTrip(data);

            Assert.That(restored.ByRarity[ItemRarity.Rare], Is.EqualTo(3));
            Assert.That(restored.ByRarity[ItemRarity.Legendary], Is.EqualTo(1));
        }

        [Test]
        public void EnumKeys_AreWrittenByNameNotByNumber()
        {
            var data = new InventorySaveData();
            data.ByRarity[ItemRarity.Legendary] = 1;

            var json = ToJson(data);

            Assert.That(json, Does.Contain("\"Legendary\""),
                "Written by name, so renumbering the enum cannot remap what a player already owns.");
            Assert.That(json, Does.Not.Contain("\"9\": 1"));
        }

        [Test]
        public void RoundTrips_ObjectValues()
        {
            var data = new InventorySaveData();
            data.Nested["hero"] = new NestedStats { Strength = 7, Multiplier = 2.5f };

            var restored = RoundTrip(data);

            Assert.That(restored.Nested["hero"].Strength, Is.EqualTo(7));
            Assert.That(restored.Nested["hero"].Multiplier, Is.EqualTo(2.5f));
        }

        [Test]
        public void RoundTrips_EmptyDictionary()
        {
            var restored = RoundTrip(new InventorySaveData());

            Assert.That(restored.Counts, Is.Empty);
            Assert.That(restored.Counts, Is.Not.Null, "An empty dictionary must not come back null.");
        }

        [Test]
        public void UnitySerializationCallbacks_RebuildTheDictionary()
        {
            var map = new SerializableDictionary<string, int> { { "a", 1 }, { "b", 2 } };

            // What Unity does around a domain reload: flatten, then rebuild.
            map.OnBeforeSerialize();
            map.Clear();
            map.OnAfterDeserialize();

            Assert.That(map.Count, Is.EqualTo(2));
            Assert.That(map["a"], Is.EqualTo(1));
            Assert.That(map["b"], Is.EqualTo(2));
        }

        [Test]
        public void DuplicateKeys_KeepTheFirstAndDoNotThrow()
        {
            // The backing lists allow what a dictionary cannot. Unity calls OnAfterDeserialize while
            // loading, so throwing here would take the whole load down.
            var map = new SerializableDictionary<string, int>();
            var keys = new List<string> { "a", "a" };
            var values = new List<int> { 1, 2 };
            SetBackingLists(map, keys, values);

            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            Assert.DoesNotThrow(() => map.OnAfterDeserialize());
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;

            Assert.That(map.Count, Is.EqualTo(1));
            Assert.That(map["a"], Is.EqualTo(1), "The first occurrence wins.");
            Assert.That(map.HasDuplicateKeys, Is.True, "The drawer needs to know so it can point at it.");
        }

        [Test]
        public void KeysSurviveACultureThatFormatsNumbersDifferently()
        {
            // A save written in pt-BR has to open in en-US and back. This is the failure that only
            // ever shows up after release, on someone else's machine.
            var data = new InventorySaveData();
            data.ById[1] = "um";
            data.ById[1000] = "mil";
            data.Nested["hero"] = new NestedStats { Multiplier = 1.75f };

            var original = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("pt-BR");
                var bytes = _serializer.Serialize(data, "inventory");

                Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
                var restored = (InventorySaveData)_serializer.Deserialize(bytes, typeof(InventorySaveData));

                Assert.That(restored.ById[1], Is.EqualTo("um"));
                Assert.That(restored.ById[1000], Is.EqualTo("mil"));
                Assert.That(restored.Nested["hero"].Multiplier, Is.EqualTo(1.75f));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }

        private InventorySaveData RoundTrip(InventorySaveData data)
        {
            var bytes = _serializer.Serialize(data, "inventory");
            return (InventorySaveData)_serializer.Deserialize(bytes, typeof(InventorySaveData));
        }

        private string ToJson(SaveData data)
        {
            return Encoding.UTF8.GetString(_serializer.Serialize(data, "inventory"));
        }

        private static void SetBackingLists<TKey, TValue>(
            SerializableDictionary<TKey, TValue> map, List<TKey> keys, List<TValue> values)
        {
            var type = typeof(SerializableDictionary<TKey, TValue>);
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

            type.GetField("_keys", flags).SetValue(map, keys);
            type.GetField("_values", flags).SetValue(map, values);
        }
    }
}
