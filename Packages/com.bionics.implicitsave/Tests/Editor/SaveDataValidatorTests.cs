using System;
using System.Collections.Generic;
using ImplicitSave.Editor;
using ImplicitSave.Tests;
using NUnit.Framework;

namespace ImplicitSave.Tests.EditorTests
{
    /// <summary>
    /// The validator is what turns Unity's silent data loss into something the author sees at
    /// compile time. These check it actually speaks up.
    /// </summary>
    public class SaveDataValidatorTests
    {
        [Test]
        public void Property_IsReported()
        {
            var issue = FindIssue(typeof(ContractSaveData), nameof(ContractSaveData.WritableProperty));

            Assert.That(issue, Is.Not.Null, "A property looks like saved state and is not saved.");
            Assert.That(issue.Value.Severity, Is.EqualTo(ValidationSeverity.Warning));
            Assert.That(issue.Value.Message, Does.Contain("field"), "The message has to say what to do instead.");
        }

        [Test]
        public void ComputedProperty_IsNotReported()
        {
            // A get-only property derived from fields is normal and correct. Warning about it would
            // train people to ignore the validator.
            Assert.That(FindIssue(typeof(ContractSaveData), nameof(ContractSaveData.ComputedProperty)), Is.Null);
        }

        [Test]
        public void PlainDictionary_IsReportedAsAnError()
        {
            var issue = FindIssue(typeof(PlainDictionarySaveData), nameof(PlainDictionarySaveData.Broken));

            Assert.That(issue, Is.Not.Null);
            Assert.That(issue.Value.Severity, Is.EqualTo(ValidationSeverity.Error));
            Assert.That(issue.Value.Message, Does.Contain("SerializableDictionary"));
        }

        [Test]
        public void SerializableDictionary_IsNotReported()
        {
            var issues = SaveDataValidator.Validate(typeof(InventorySaveData));

            Assert.That(issues, Is.Empty, "The supported way of storing a map must not warn.");
        }

        [Test]
        public void NestingDeeperThanUnityHandles_IsReported()
        {
            var issues = SaveDataValidator.Validate(typeof(DeepSaveData));

            Assert.That(issues, Is.Not.Empty, "Unity truncates past seven levels without a word.");
            Assert.That(Describe(issues), Does.Contain("nested deeper"));
        }

        [Test]
        public void ShallowNesting_IsNotReported()
        {
            var issues = SaveDataValidator.Validate(typeof(ItemsSaveData));

            Assert.That(issues, Is.Empty);
        }

        [Test]
        public void JsonIgnoreWithoutNonSerialized_IsReported()
        {
            var issue = FindIssue(typeof(JsonIgnoreSaveData), nameof(JsonIgnoreSaveData.Ignored));

            Assert.That(issue, Is.Not.Null,
                "[JsonIgnore] alone is the one attribute where the two serializers would disagree.");
            Assert.That(issue.Value.Message, Does.Contain("NonSerialized"));
        }

        [Test]
        public void MissingSaveId_IsReported()
        {
            var issues = SaveDataValidator.Validate(typeof(UnnamedSaveData));

            Assert.That(Describe(issues), Does.Contain("[SaveId]"));
        }

        [Test]
        public void DeclaredSaveId_IsNotReported()
        {
            var issues = SaveDataValidator.Validate(typeof(ItemsSaveData));

            Assert.That(Describe(issues), Does.Not.Contain("[SaveId]"));
        }

        [Test]
        public void FindSaveTypes_DiscoversTypesWithoutAnyRegistration()
        {
            // The premise of the whole package: declaring the class is enough.
            var types = SaveDataValidator.FindSaveTypes();

            Assert.That(types, Has.Member(typeof(ItemsSaveData)));
            Assert.That(types, Has.Member(typeof(InventorySaveData)));
            Assert.That(types, Has.No.Member(typeof(SaveData)), "The abstract base is not a save type.");
        }

        [Test]
        public void SubtypeWithoutAnId_IsReportedAgainstTheFieldThatCouldHoldIt()
        {
            var issue = FindIssue(typeof(ProbeLoadoutSaveData), nameof(ProbeLoadoutSaveData.Equipped));

            Assert.That(issue, Is.Not.Null, "a subtype with no [SaveType] cannot be written to a file");
            Assert.That(issue.Value.Severity, Is.EqualTo(ValidationSeverity.Error));
            Assert.That(issue.Value.Message, Does.Contain(nameof(ProbeUntagged)));

            // The message has to carry the fix, not just the diagnosis.
            Assert.That(issue.Value.Message, Does.Contain("[SaveType(\"probe_untagged\")]"));
        }

        [Test]
        public void SubtypeWithAnId_IsNotReported()
        {
            foreach (var issue in SaveDataValidator.Validate(typeof(ProbeLoadoutSaveData)))
            {
                Assert.That(issue.Message, Does.Not.Contain(nameof(ProbeTagged)));
            }
        }

        private static ValidationIssue? FindIssue(Type saveType, string memberName)
        {
            foreach (var issue in SaveDataValidator.Validate(saveType))
            {
                if (issue.MemberName == memberName)
                {
                    return issue;
                }
            }

            return null;
        }

        private static string Describe(IReadOnlyList<ValidationIssue> issues)
        {
            var text = new System.Text.StringBuilder();
            foreach (var issue in issues)
            {
                text.AppendLine(issue.ToString());
            }

            return text.ToString();
        }
    }
}
