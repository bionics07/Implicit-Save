using NUnit.Framework;
using UnityEngine.TestTools;

namespace ImplicitSave.Tests
{
    /// <summary>
    /// The save id decides the file name, so getting it wrong orphans a player's save. These pin the
    /// rules down.
    /// </summary>
    public class SaveIdResolverTests
    {
        [Test]
        public void Resolve_UsesTheDeclaredId()
        {
            Assert.That(SaveIdResolver.Resolve(typeof(ItemsSaveData)), Is.EqualTo("items"));
        }

        [Test]
        public void Resolve_WithoutTheAttribute_FallsBackToSnakeCaseAndWarns()
        {
            LogAssert.ignoreFailingMessages = true;

            Assert.That(SaveIdResolver.Resolve(typeof(UnnamedSaveData)), Is.EqualTo("unnamed_save_data"));

            LogAssert.ignoreFailingMessages = false;
        }

        [TestCase("ItemsSaveData", ExpectedResult = "items_save_data")]
        [TestCase("UIStateSaveData", ExpectedResult = "ui_state_save_data")]
        [TestCase("Progress", ExpectedResult = "progress")]
        [TestCase("HTTPCache", ExpectedResult = "http_cache")]
        [TestCase("Slot2Data", ExpectedResult = "slot2_data")]
        public string ToSnakeCase_SplitsWordsAndAcronyms(string typeName)
        {
            return SaveIdResolver.ToSnakeCase(typeName);
        }

        [TestCase("items")]
        [TestCase("player_progress")]
        [TestCase("world-1")]
        [TestCase("a1")]
        public void IsValidId_AcceptsFileSafeIds(string id)
        {
            Assert.That(SaveIdResolver.IsValidId(id, out _), Is.True);
        }

        [TestCase("", TestName = "IsValidId_Rejects_Empty")]
        [TestCase("Items", TestName = "IsValidId_Rejects_Uppercase")]
        [TestCase("my items", TestName = "IsValidId_Rejects_Space")]
        [TestCase("../escape", TestName = "IsValidId_Rejects_PathTraversal")]
        [TestCase("sub/dir", TestName = "IsValidId_Rejects_Separator")]
        [TestCase("items.json", TestName = "IsValidId_Rejects_Dot")]
        public void IsValidId_RejectsAnythingUnsafeAsAFileName(string id)
        {
            Assert.That(SaveIdResolver.IsValidId(id, out var reason), Is.False);
            Assert.That(reason, Is.Not.Null.And.Not.Empty, "A rejection must say why.");
        }
    }
}
