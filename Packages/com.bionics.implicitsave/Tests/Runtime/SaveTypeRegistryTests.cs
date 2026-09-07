using System;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace ImplicitSave.Tests
{
    /// <summary>
    /// The registry is the map between a stable id and a .NET type, in both directions, and the
    /// thing that keeps those types from being stripped out of a build.
    /// </summary>
    public class SaveTypeRegistryTests
    {
        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
        }

        [Test]
        public void TheEditorScanFindsTypesWithNoRegistrationAtAll()
        {
            // The premise of the package: declaring the class is the whole job.
            Assert.That(SaveTypeRegistry.TryGetType("items", out var type), Is.True);
            Assert.That(type, Is.EqualTo(typeof(ItemsSaveData)));
        }

        [Test]
        public void MapsBothWays()
        {
            Assert.That(SaveTypeRegistry.TryGetId(typeof(ItemsSaveData), out var id), Is.True);
            Assert.That(id, Is.EqualTo("items"));
        }

        [Test]
        public void UsesTheDeclaredIdNotTheTypeName()
        {
            // This is what lets a class be renamed without orphaning a player's save.
            Assert.That(SaveTypeRegistry.TryGetType("inventory", out var type), Is.True);
            Assert.That(type, Is.EqualTo(typeof(InventorySaveData)));
        }

        [Test]
        public void Create_BuildsAnInstanceFromAnIdAlone()
        {
            // Loading a save file only has the id in it, never a type.
            var instance = SaveTypeRegistry.Create("items");

            Assert.That(instance, Is.TypeOf<ItemsSaveData>());
        }

        [Test]
        public void Create_OnAnUnknownId_ReturnsNull()
        {
            Assert.That(SaveTypeRegistry.Create("nao_existe"), Is.Null);
        }

        [Test]
        public void TryGetType_OnAnUnknownId_ReturnsFalse()
        {
            Assert.That(SaveTypeRegistry.TryGetType("nao_existe", out _), Is.False);
        }

        [Test]
        public void GetSaveTypes_ListsRootsAndNotTheAbstractBase()
        {
            var types = SaveTypeRegistry.GetSaveTypes();

            Assert.That(types, Has.Member(typeof(ItemsSaveData)));
            Assert.That(types, Has.No.Member(typeof(SaveData)));
        }

        [Test]
        public void Entries_AreOrderedById()
        {
            var entries = SaveTypeRegistry.GetAll();
            var previous = string.Empty;

            foreach (var entry in entries)
            {
                Assert.That(string.CompareOrdinal(entry.Id, previous), Is.GreaterThanOrEqualTo(0),
                    "A stable order is what keeps the generated file's diff readable.");
                previous = entry.Id;
            }
        }

        [Test]
        public void RegisteringTheSameTypeTwice_IsHarmless()
        {
            var before = SaveTypeRegistry.Count;

            SaveTypeRegistry.Register("items", typeof(ItemsSaveData), () => new ItemsSaveData());

            Assert.That(SaveTypeRegistry.Count, Is.EqualTo(before),
                "The editor scan and the generated registry both run in play mode.");
        }

        [Test]
        public void TwoTypesClaimingOneId_IsRefusedAndReported()
        {
            // Left alone, one type would silently load the other's file. The build-time validation
            // refuses this; the runtime guard is the second line.
            LogAssert.ignoreFailingMessages = true;
            var before = SaveTypeRegistry.Count;

            SaveTypeRegistry.Register("items", typeof(HookSaveData), () => new HookSaveData());

            Assert.That(SaveTypeRegistry.Count, Is.EqualTo(before));
            Assert.That(SaveTypeRegistry.TryGetType("items", out var type), Is.True);
            Assert.That(type, Is.EqualTo(typeof(ItemsSaveData)), "The first registration wins.");
        }

        [Test]
        public void SubtypesAreKeptApartFromSaveRoots()
        {
            var roots = SaveTypeRegistry.GetSaveTypes();

            foreach (var entry in SaveTypeRegistry.GetAll())
            {
                if (entry.IsSubtype)
                {
                    Assert.That(roots, Has.No.Member(entry.Type),
                        "A subtype has no save file of its own - it lives inside one.");
                }
            }
        }

        [Test]
        public void EveryEntryKnowsWhereItCameFrom()
        {
            foreach (var entry in SaveTypeRegistry.GetAll())
            {
                Assert.That(Enum.IsDefined(typeof(SaveTypeOrigin), entry.Origin), Is.True,
                    "Being able to say where a type came from is what makes the discovery inspectable.");
            }
        }
    }
}
