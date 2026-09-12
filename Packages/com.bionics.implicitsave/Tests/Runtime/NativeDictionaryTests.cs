using System.Collections.Generic;
using ImplicitSave.Serialization;
using NUnit.Framework;
using UnityEngine;

namespace ImplicitSave.Tests
{
    /// <summary>
    /// Unity 6.6 serializes a plain <c>Dictionary</c> field; older versions do not. The package
    /// follows the editor it is running in, so every test here asks the rules what this editor does
    /// instead of hard-coding an answer - that way the same suite is meaningful on both sides of the
    /// change.
    /// </summary>
    public class NativeDictionaryTests
    {
        private NewtonsoftSaveSerializer _serializer;

        [SetUp]
        public void SetUp()
        {
            _serializer = new NewtonsoftSaveSerializer();
        }

        [Test]
        public void ADictionaryFieldFollowsTheEditorThisRunsIn()
        {
            var field = typeof(NativeDictionarySaveData).GetField(nameof(NativeDictionarySaveData.Loot));

            var serialized = UnitySerializationRules.IsSerializedByUnity(field, out var reason);

            Assert.That(serialized, Is.EqualTo(UnitySerializationRules.NativeDictionaries),
                "The rules and the editor disagree about Dictionary. Reason given: " + reason);
        }

        [Test]
        public void APublicDictionaryWithoutSerializeField_IsNeverSaved()
        {
            // Every other field type is saved when it is public. A Dictionary is the exception, and
            // it is a quiet one: the field looks like all the others and would never reach the file.
            var field = typeof(NativeDictionarySaveData).GetField(nameof(NativeDictionarySaveData.NotSaved));

            Assert.That(UnitySerializationRules.IsSerializedByUnity(field, out var reason), Is.False);
            Assert.That(reason, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void AKeyThatCannotBeWrittenAsText_IsRefusedWithTheReason()
        {
            var field = typeof(UnsaveableDictionaryProbe).GetField(nameof(UnsaveableDictionaryProbe.ByPosition));

            Assert.That(UnitySerializationRules.IsSerializedByUnity(field, out var reason), Is.False);

            if (UnitySerializationRules.NativeDictionaries)
            {
                Assert.That(reason, Does.Contain("key"),
                    "On an editor with native dictionaries the reason has to name the key, because " +
                    "the field is otherwise perfectly valid to Unity.");
            }
        }

        [Test]
        public void ADictionaryInsideAList_IsRefused()
        {
            var field = typeof(UnsaveableDictionaryProbe).GetField(nameof(UnsaveableDictionaryProbe.Nested));

            Assert.That(UnitySerializationRules.IsSerializedByUnity(field, out _), Is.False);
        }

        [Test]
        public void SerializableDictionary_KeepsWorkingWhateverTheEditor()
        {
            // The package's own type is the portable answer: same code on 2021.3 and on 6.6.
            var field = typeof(InventorySaveData).GetField(nameof(InventorySaveData.Counts));

            Assert.That(UnitySerializationRules.IsSerializedByUnity(field, out var reason), Is.True, reason);
        }

        [Test]
        public void ARoundTripKeepsEveryKeyKind()
        {
            RequireNativeDictionaries();

            var save = new NativeDictionarySaveData();
            save.Loot["potion"] = 3;
            save.Counts[LootRarity.Rare] = 2;
            save.Names[7] = "seven";
            save.NotSaved["ignored"] = 1;

            var restored = RoundTrip(save);

            Assert.That(restored.Loot["potion"], Is.EqualTo(3));
            Assert.That(restored.Counts[LootRarity.Rare], Is.EqualTo(2));
            Assert.That(restored.Names[7], Is.EqualTo("seven"));
            Assert.That(restored.NotSaved, Is.Empty, "The field Unity skips must not come back from the file.");
        }

        [Test]
        public void TheFileIsAPlainJsonObject_WithEnumKeysByName()
        {
            RequireNativeDictionaries();

            var save = new NativeDictionarySaveData();
            save.Loot["potion"] = 3;
            save.Counts[LootRarity.Rare] = 2;

            var json = System.Text.Encoding.UTF8.GetString(_serializer.Serialize(save, "native_dictionary_probe"));

            Assert.That(json, Does.Contain("\"potion\": 3").Or.Contain("\"potion\":3"),
                "A dictionary has to read as an object in the file: {\"potion\": 3}.");
            Assert.That(json, Does.Contain("Rare"),
                "An enum key goes in by name. Written as a number, renumbering the enum would move " +
                "a player's data to a different entry without a word.");
        }

        private static void RequireNativeDictionaries()
        {
            if (!UnitySerializationRules.NativeDictionaries)
            {
                Assert.Ignore("This editor does not serialize plain dictionaries; SerializableDictionary covers it.");
            }
        }

        private NativeDictionarySaveData RoundTrip(NativeDictionarySaveData save)
        {
            var bytes = _serializer.Serialize(save, "native_dictionary_probe");
            return (NativeDictionarySaveData)_serializer.Deserialize(bytes, typeof(NativeDictionarySaveData));
        }
    }
}
