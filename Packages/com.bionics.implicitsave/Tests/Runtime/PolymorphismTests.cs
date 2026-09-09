using System;
using System.Collections.Generic;
using System.Text;
using ImplicitSave.Serialization;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ImplicitSave.Tests
{
    /// <summary>
    /// Covers storing a subtype in a save: the file records a <c>$t</c> id and never a .NET type
    /// name, which is what makes renaming the class safe.
    /// </summary>
    [TestFixture]
    public class PolymorphismTests
    {
        private NewtonsoftSaveSerializer _serializer;

        [SetUp]
        public void SetUp()
        {
            _serializer = new NewtonsoftSaveSerializer(prettyPrint: false);

            // The registry is emptied between play sessions, and these subtypes live in a test
            // assembly the generated registry deliberately skips.
            SaveTypeRegistry.RegisterSubtype("melee_weapon", typeof(MeleeWeapon), () => new MeleeWeapon());
            SaveTypeRegistry.RegisterSubtype("bow", typeof(Bow), () => new Bow());
            SaveTypeRegistry.RegisterSubtype("magic_staff", typeof(MagicStaff), () => new MagicStaff());
            SaveTypeRegistry.RegisterSubtype("renamed_bow", typeof(RenamedBow), () => new RenamedBow());
        }

        private string Json(SaveData data)
        {
            return Encoding.UTF8.GetString(_serializer.Serialize(data, "loadout"));
        }

        private LoadoutSaveData RoundTrip(LoadoutSaveData data)
        {
            var bytes = _serializer.Serialize(data, "loadout");
            return (LoadoutSaveData)_serializer.Deserialize(bytes, typeof(LoadoutSaveData));
        }

        [Test]
        public void Field_KeepsItsConcreteTypeAcrossASave()
        {
            var data = new LoadoutSaveData
            {
                Equipped = new MeleeWeapon { Name = "Sword", Damage = 12, Reach = 2.5f }
            };

            var back = RoundTrip(data);

            Assert.That(back.Equipped, Is.InstanceOf<MeleeWeapon>());
            Assert.That(((MeleeWeapon)back.Equipped).Reach, Is.EqualTo(2.5f));
            Assert.That(back.Equipped.Name, Is.EqualTo("Sword"));
        }

        [Test]
        public void List_KeepsADifferentTypePerEntry()
        {
            var data = new LoadoutSaveData();
            data.Backpack.Add(new Bow { Name = "Bow", ArrowCount = 30 });
            data.Backpack.Add(new MagicStaff { Name = "Staff", Mana = 200 });

            var back = RoundTrip(data);

            Assert.That(back.Backpack.Count, Is.EqualTo(2));
            Assert.That(back.Backpack[0], Is.InstanceOf<Bow>());
            Assert.That(((Bow)back.Backpack[0]).ArrowCount, Is.EqualTo(30));
            Assert.That(back.Backpack[1], Is.InstanceOf<MagicStaff>());
            Assert.That(((MagicStaff)back.Backpack[1]).Mana, Is.EqualTo(200));
        }

        [Test]
        public void Nesting_TagsASubtypeHeldInsideAnotherSubtype()
        {
            var data = new LoadoutSaveData();
            data.Backpack.Add(new Bow
            {
                Name = "Bow",
                Sidearm = new MagicStaff { Name = "Wand", Mana = 40 }
            });

            var back = RoundTrip(data);
            var bow = (Bow)back.Backpack[0];

            Assert.That(bow.Sidearm, Is.InstanceOf<MagicStaff>());
            Assert.That(((MagicStaff)bow.Sidearm).Mana, Is.EqualTo(40));
        }

        [Test]
        public void Null_SurvivesBothInAFieldAndInAList()
        {
            var data = new LoadoutSaveData { Equipped = null };
            data.Backpack.Add(null);
            data.Backpack.Add(new Bow { Name = "Bow" });

            var back = RoundTrip(data);

            Assert.That(back.Equipped, Is.Null);
            Assert.That(back.Backpack[0], Is.Null);
            Assert.That(back.Backpack[1], Is.InstanceOf<Bow>());
        }

        [Test]
        public void File_HoldsTheRegistryIdAndNoDotNetTypeName()
        {
            // Named so that the class name cannot show up as ordinary data and pass the check by
            // accident.
            var data = new LoadoutSaveData { Equipped = new Bow { Name = "Arco longo" } };

            var json = Json(data);

            Assert.That(json, Does.Contain("\"$t\":\"bow\""));

            // The whole reason for a registry id: a file naming the class would break the moment
            // anyone renamed it or moved it to another assembly.
            Assert.That(json, Does.Not.Contain(nameof(Bow)));
            Assert.That(json, Does.Not.Contain("ImplicitSave.Tests"));
            Assert.That(json, Does.Not.Contain("$type"));
        }

        [Test]
        public void RenamingTheClass_DoesNotBreakAnExistingSave()
        {
            // Renaming a class is exactly this from the file's side: the id it was written under is
            // unchanged, and the reader asks the registry which class carries that id today. Nothing
            // in the file has to be touched, because nothing in the file named the class.
            var onDisk = JObject.Parse("{\"Equipped\":{\"$t\":\"renamed_bow\",\"Name\":\"Old bow\",\"Damage\":5}}");

            var back = (LoadoutSaveData)_serializer.DeserializePayload(onDisk, typeof(LoadoutSaveData));

            Assert.That(back.Equipped, Is.InstanceOf<RenamedBow>());
            Assert.That(back.Equipped.Name, Is.EqualTo("Old bow"));
            Assert.That(back.Equipped.Damage, Is.EqualTo(5));
        }

        [Test]
        public void UnknownDiscriminator_DropsThatValueAndKeepsTheRestOfTheSave()
        {
            // Deleting a class is a developer's decision; the person who then cannot open the file
            // is a player. Losing one weapon beats losing the save.
            var payload = JObject.Parse(
                "{\"Equipped\":{\"$t\":\"crossbow\",\"Name\":\"?\"},\"OwnerName\":\"Ana\"}");

            LoadoutSaveData back = null;
            LogAssert.Expect(LogType.Error, new Regex("crossbow"));
            Assert.DoesNotThrow(() => back = (LoadoutSaveData)_serializer.DeserializePayload(payload, typeof(LoadoutSaveData)));

            Assert.That(back.Equipped, Is.Null);
            Assert.That(back.OwnerName, Is.EqualTo("Ana"), "everything else has to survive");
        }

        [Test]
        public void UnknownDiscriminator_SaysHowToFixItWhenTheTypeWasRenamed()
        {
            var payload = JObject.Parse("{\"Equipped\":{\"$t\":\"crossbow\"}}");

            LogAssert.Expect(LogType.Error, new Regex("PreviousIds"));
            _serializer.DeserializePayload(payload, typeof(LoadoutSaveData));
        }

        [Test]
        public void UnknownDiscriminator_CanBeMadeFatalForGamesThatPreferIt()
        {
            ImplicitSaveSettings.Instance.FailOnUnknownSubtype = true;

            try
            {
                var payload = JObject.Parse("{\"Equipped\":{\"$t\":\"crossbow\"}}");

                var e = Assert.Throws<SaveSerializationException>(
                    () => _serializer.DeserializePayload(payload, typeof(LoadoutSaveData)));

                Assert.That(e.Message, Does.Contain("crossbow"));
            }
            finally
            {
                ImplicitSaveSettings.Instance.FailOnUnknownSubtype = false;
            }
        }

        [Test]
        public void PreviousId_LetsARenamedIdKeepLoadingOldSaves()
        {
            // The id changed from "old_bow" to "renamed_bow". Saves written before that still name
            // the old one, and only the author can say the two meant the same type.
            SaveTypeRegistry.RegisterPreviousId("old_bow", typeof(RenamedBow));

            var onDisk = JObject.Parse("{\"Equipped\":{\"$t\":\"old_bow\",\"Name\":\"Velho\",\"Damage\":3}}");
            var back = (LoadoutSaveData)_serializer.DeserializePayload(onDisk, typeof(LoadoutSaveData));

            Assert.That(back.Equipped, Is.InstanceOf<RenamedBow>());
            Assert.That(back.Equipped.Name, Is.EqualTo("Velho"));
        }

        [Test]
        public void PreviousId_IsNeverWrittenBack()
        {
            // The point of reading an old id is to stop needing to. A save that passes through the
            // game comes back out under the current id and heals itself.
            SaveTypeRegistry.RegisterPreviousId("old_bow", typeof(RenamedBow));

            var onDisk = JObject.Parse("{\"Equipped\":{\"$t\":\"old_bow\",\"Name\":\"Velho\"}}");
            var back = (LoadoutSaveData)_serializer.DeserializePayload(onDisk, typeof(LoadoutSaveData));

            var rewritten = Json(back);

            Assert.That(rewritten, Does.Contain("renamed_bow"));
            Assert.That(rewritten, Does.Not.Contain("old_bow"));
        }

        [Test]
        public void PreviousId_NeverShadowsAnIdSomeTypeUsesToday()
        {
            // Honouring this would hand Bow's saves to RenamedBow, which is worse than the rename
            // it was meant to fix.
            LogAssert.Expect(LogType.Error, new Regex("bow"));
            SaveTypeRegistry.RegisterPreviousId("bow", typeof(RenamedBow));

            Assert.That(SaveTypeRegistry.TryGetType("bow", out var resolved), Is.True);
            Assert.That(resolved, Is.EqualTo(typeof(Bow)));
        }

        [Test]
        public void MissingDiscriminator_FailsInsteadOfGuessingAType()
        {
            var payload = JObject.Parse("{\"Equipped\":{\"Name\":\"?\",\"Damage\":1}}");

            var e = Assert.Throws<SaveSerializationException>(
                () => _serializer.DeserializePayload(payload, typeof(LoadoutSaveData)));

            Assert.That(e.Message, Does.Contain("$t"));
        }

        [Test]
        public void SubtypeWithoutAnId_FailsAtSaveTimeWithTheFixInTheMessage()
        {
            // Deliberately not reached through a SaveData field: a type like this one IS a mistake,
            // and the editor validator reports it at compile time. Putting it in a save fixture
            // would mean an error in the console on every recompile.
            var writer = new JsonTextWriter(new System.IO.StringWriter());

            var e = Assert.Throws<SaveSerializationException>(() => PolymorphicConverter.Shared.WriteJson(
                writer, new UnregisteredGadget(), JsonSerializer.CreateDefault()));

            Assert.That(e.Message, Does.Contain("SaveType"));
        }

        [Test]
        public void SharedReference_IsReportedBecauseAFileCannotHoldOne()
        {
            var shared = new MeleeWeapon { Name = "Sword" };
            var data = new LoadoutSaveData { Equipped = shared };
            data.Backpack.Add(shared);

            var found = PolymorphicConverter.FindSharedReferences(data);

            Assert.That(found, Has.Count.EqualTo(1));
            Assert.That(found[0], Is.SameAs(shared));
        }

        [Test]
        public void SharedReference_IsNotReportedForTwoSeparateButEqualValues()
        {
            var data = new LoadoutSaveData { Equipped = new MeleeWeapon { Name = "Sword" } };
            data.Backpack.Add(new MeleeWeapon { Name = "Sword" });

            Assert.That(PolymorphicConverter.FindSharedReferences(data), Is.Empty);
        }

        [Test]
        public void SharedReference_BecomesTwoObjectsAfterASave()
        {
            // This is the behaviour the warning exists for, pinned so it cannot change silently.
            var shared = new MeleeWeapon { Name = "Sword" };
            var data = new LoadoutSaveData { Equipped = shared };
            data.Backpack.Add(shared);

            var back = RoundTrip(data);

            Assert.That(back.Equipped, Is.Not.SameAs(back.Backpack[0]));
            Assert.That(back.Equipped.Name, Is.EqualTo(back.Backpack[0].Name));
        }

        [Test]
        public void SerializeReference_MakesAnAbstractFieldSerializable()
        {
            // Without the attribute Unity would skip the field entirely, and so would the file.
            var field = typeof(LoadoutSaveData).GetField(nameof(LoadoutSaveData.Equipped));

            Assert.That(UnitySerializationRules.IsSerializedByUnity(field), Is.True);
            Assert.That(UnitySerializationRules.IsSerializableType(typeof(Weapon)), Is.False,
                "an abstract type is only storable because the field is by reference");
        }

        [Test]
        public void SerializeReference_StillRefusesAValueType()
        {
            Assert.That(UnitySerializationRules.IsSerializableByReference(typeof(int), out var reason), Is.False);
            Assert.That(reason, Does.Contain("[SerializeReference]"));
        }

    }

    /// <summary>Stands in for a class that was renamed: the id is what a file refers to.</summary>
    [SaveType("renamed_bow", PreviousIds = new[] { "very_old_bow" })]
    [Serializable]
    public class RenamedBow : Weapon
    {
        public int ArrowCount = 12;
        public float DrawTime = 0.8f;
    }

    /// <summary>
    /// A type the author forgot to give a <c>[SaveType]</c>. Kept off the <c>Weapon</c> branch on
    /// purpose - no save type can reach it, so the editor validator has nothing to complain about
    /// while the serializer's own error can still be tested.
    /// </summary>
    [Serializable]
    public class UnregisteredGadget
    {
        public string Name = "";
    }
}
