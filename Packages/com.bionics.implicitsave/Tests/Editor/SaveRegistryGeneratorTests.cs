using System;
using System.Collections.Generic;
using System.IO;
using ImplicitSave.Editor;
using ImplicitSave.Tests;
using NUnit.Framework;

namespace ImplicitSave.Tests.EditorTests
{
    /// <summary>
    /// The generated registry is what makes the package survive a build. These cover the validation
    /// that stops a broken one from shipping, and the properties the generated file has to have.
    /// </summary>
    public class SaveRegistryGeneratorTests
    {
        [Test]
        public void DuplicateIds_AreRefused()
        {
            // Two types on one id means one silently loads the other's file - the kind of bug a
            // player reports as "my save got replaced".
            var problems = SaveRegistryGenerator.Validate(
                new[] { typeof(ItemsSaveData), typeof(DuplicateIdFixture) },
                Array.Empty<Type>());

            Assert.That(Join(problems), Does.Contain("items"));
            Assert.That(Join(problems), Does.Contain("different id"));
        }

        [Test]
        public void AnIdThatIsNotAValidFileName_IsRefused()
        {
            var problems = SaveRegistryGenerator.Validate(
                new[] { typeof(BadIdFixture) }, Array.Empty<Type>());

            Assert.That(Join(problems), Does.Contain("not usable"));
        }

        [Test]
        public void ATypeWithNoParameterlessConstructor_IsRefused()
        {
            var problems = SaveRegistryGenerator.Validate(
                new[] { typeof(NoDefaultConstructorFixture) }, Array.Empty<Type>());

            Assert.That(Join(problems), Does.Contain("parameterless constructor"));
        }

        [Test]
        public void AHealthyProjectProducesNoProblems()
        {
            var problems = SaveRegistryGenerator.Validate(
                new[] { typeof(ItemsSaveData), typeof(InventorySaveData) }, Array.Empty<Type>());

            Assert.That(problems, Is.Empty);
        }

        [Test]
        public void TheGeneratedFileExists()
        {
            Assert.That(File.Exists(SaveRegistryGenerator.RegistryPath), Is.True,
                $"'{SaveRegistryGenerator.RegistryPath}' should be written whenever scripts recompile.");
        }

        [Test]
        public void TheGeneratedFileExplainsItself()
        {
            var source = File.ReadAllText(SaveRegistryGenerator.RegistryPath);

            // "Implicit" must not mean "you cannot see what happened" - the header is a product
            // requirement, not a nicety.
            Assert.That(source, Does.Contain("GENERATED FILE"));
            Assert.That(source, Does.Contain("stripping"));
            Assert.That(source, Does.Contain("Regenerate Registry"));
        }

        [Test]
        public void TheGeneratedFileNamesEveryBuildableType()
        {
            var source = File.ReadAllText(SaveRegistryGenerator.RegistryPath);

            foreach (var type in SaveTypeDiscovery.FindBuildableSaveTypes())
            {
                Assert.That(source, Does.Contain(type.FullName),
                    $"'{type.FullName}' would be stripped from a build without a reference here.");
            }
        }

        [Test]
        public void TheGeneratedFileLeavesOutTypesThatCannotBeInABuild()
        {
            var source = File.ReadAllText(SaveRegistryGenerator.RegistryPath);

            // Naming a test assembly's type here produces a file that will not compile. Found the
            // direct way: the first generated registry did exactly that.
            Assert.That(source, Does.Not.Contain("ImplicitSave.Tests."),
                "Test assemblies are not compiled into a player, so their types cannot be named here.");
        }

        [Test]
        public void TestFixturesAreReportedAsAbsentFromABuild()
        {
            var excluded = SaveTypeDiscovery.FindExcludedTypes();
            var names = new List<string>();

            foreach (var entry in excluded)
            {
                names.Add(entry.Key.FullName);
            }

            Assert.That(names, Has.Member(typeof(ItemsSaveData).FullName),
                "A save type the editor sees but a build will not is worth saying out loud.");
        }

        [Test]
        public void GeneratingTwiceInARowChangesNothing()
        {
            // Rewriting an identical file would trigger a recompile, which would trigger the
            // generator, which would rewrite the file.
            SaveRegistryGenerator.Generate();

            Assert.That(SaveRegistryGenerator.Generate(), Is.False);
        }

        [Test]
        public void TheLinkerFileKeepsTheSameTypes()
        {
            var xml = File.ReadAllText(SaveRegistryGenerator.LinkXmlPath);

            foreach (var type in SaveTypeDiscovery.FindBuildableSaveTypes())
            {
                Assert.That(xml, Does.Contain(type.FullName),
                    "Belt and braces: the registry keeps the type, link.xml keeps its fields too.");
            }
        }

        private static string Join(IReadOnlyList<string> problems)
        {
            return string.Join("\n", problems);
        }
    }

    // These fixtures deliberately break the rules, and they do NOT derive from SaveData on purpose.
    // The validator reads attributes and constructors, so it does not need them to; deriving would
    // put them in the project-wide scan, where a fixture claiming "items" would collide with the
    // real save type and break unrelated tests. It did, the first time.

    /// <summary>Collides with ItemsSaveData's id on purpose.</summary>
    [SaveId("items")]
    public class DuplicateIdFixture
    {
    }

    /// <summary>Has an id that cannot be a file name.</summary>
    [SaveId("../escape")]
    public class BadIdFixture
    {
    }

    /// <summary>Cannot be created when no save file exists.</summary>
    [SaveId("no_ctor")]
    public class NoDefaultConstructorFixture
    {
        /// <param name="required">Makes the compiler drop the implicit parameterless constructor.</param>
        public NoDefaultConstructorFixture(int required)
        {
        }
    }
}
