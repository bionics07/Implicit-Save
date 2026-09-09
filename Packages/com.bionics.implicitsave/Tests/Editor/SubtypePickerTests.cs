using System;
using System.Linq;
using ImplicitSave.Editor;
using NUnit.Framework;

namespace ImplicitSave.Tests.Editor
{
    /// <summary>
    /// Covers the editor side of storing a subtype: what the picker offers, and what survives
    /// switching a value from one type to another.
    /// </summary>
    /// <remarks>
    /// These fixtures deliberately sit on their own branch rather than on the <c>Weapon</c> one used
    /// by the serializer tests. One of them has no <c>[SaveType]</c> on purpose, and the editor
    /// validator reports exactly that whenever such a type can be reached from a save - so hanging it
    /// off a real save's field would mean a console error on every recompile.
    /// </remarks>
    [TestFixture]
    public class SubtypePickerTests
    {
        [SetUp]
        public void SetUp()
        {
            SaveTypeRegistry.RegisterSubtype("picker_alpha", typeof(PickerAlpha), () => new PickerAlpha());
            SaveTypeRegistry.RegisterSubtype("picker_beta", typeof(PickerBeta), () => new PickerBeta());
        }

        [Test]
        public void Picker_OffersTheSubtypesThatFitTheField()
        {
            var options = SubtypePicker.AssignableTypes(typeof(PickerProbe));

            Assert.That(options, Contains.Item(typeof(PickerAlpha)));
            Assert.That(options, Contains.Item(typeof(PickerBeta)));
        }

        [Test]
        public void Picker_OffersNothingThatIsNotInTheRegistry()
        {
            // Unity's own picker would list this, because it only asks what is assignable. A value of
            // a type with no [SaveType] cannot be written to a file at all, so offering it would just
            // move the failure to the next save.
            Assert.That(SubtypePicker.AssignableTypes(typeof(PickerProbe)),
                Has.None.EqualTo(typeof(PickerUnregistered)));
        }

        [Test]
        public void Picker_OffersNothingFromAnotherBranch()
        {
            Assert.That(SubtypePicker.AssignableTypes(typeof(PickerProbe)),
                Has.None.Matches<Type>(t => !typeof(PickerProbe).IsAssignableFrom(t)));
        }

        [Test]
        public void Picker_OffersNoSaveRoots()
        {
            var options = SubtypePicker.AssignableTypes(typeof(SaveData));

            Assert.That(options.Any(t => typeof(SaveData).IsAssignableFrom(t)), Is.False,
                "a save root is a file of its own, never a value stored inside another save");
        }

        [Test]
        public void Picker_OffersNothingForAFieldTypeItCannotResolve()
        {
            Assert.That(SubtypePicker.AssignableTypes(null), Is.Empty);
        }

        [Test]
        public void Discovery_ReadsThePreviousIdsThatReachTheGeneratedRegistry()
        {
            // This is the path a BUILD takes: the generator asks discovery, not the attribute.
            Assert.That(SaveTypeDiscovery.ResolvePreviousIds(typeof(RenamedBow)), Contains.Item("very_old_bow"));
        }

        [Test]
        public void Discovery_IgnoresAPreviousIdThatRepeatsTheCurrentOne()
        {
            Assert.That(SaveTypeDiscovery.ResolvePreviousIds(typeof(PickerAlpha)), Is.Empty);
        }

        [Test]
        public void SwitchingType_KeepsTheFieldsBothTypesShare()
        {
            var to = new PickerBeta();

            SubtypePicker.CopyCommonFields(new PickerAlpha { Name = "Longbow", Damage = 9, ArrowCount = 30 }, to);

            // Name and Damage live on the shared base and mean the same thing in both, so wiping
            // them on a type change would be gratuitous data loss.
            Assert.That(to.Name, Is.EqualTo("Longbow"));
            Assert.That(to.Damage, Is.EqualTo(9));
        }

        [Test]
        public void SwitchingType_LeavesTheNewTypesOwnFieldsAtTheirDefaults()
        {
            var to = new PickerBeta();
            var defaultMana = to.Mana;
            var defaultElement = to.Element;

            SubtypePicker.CopyCommonFields(new PickerAlpha { Name = "Longbow", ArrowCount = 30 }, to);

            // ArrowCount has nowhere to go, and inventing a value for Mana would be worse than the
            // default the author wrote.
            Assert.That(to.Mana, Is.EqualTo(defaultMana));
            Assert.That(to.Element, Is.EqualTo(defaultElement));
        }

        [Test]
        public void SwitchingType_DoesNotCopyAFieldThatSharesANameButNotAType()
        {
            var to = new PickerMismatched();

            SubtypePicker.CopyCommonFields(new PickerAlpha { Name = "Longbow", Damage = 9, ArrowCount = 30 }, to);

            Assert.That(to.Name, Is.EqualTo("Longbow"));
            Assert.That(to.ArrowCount, Is.EqualTo(0f),
                "an int must not be poured into a float field on the strength of a shared name");
        }
    }

    /// <summary>The base the picker is asked about.</summary>
    [Serializable]
    public abstract class PickerProbe
    {
        public string Name = "";
        public int Damage;
    }

    [SaveType("picker_alpha")]
    [Serializable]
    public class PickerAlpha : PickerProbe
    {
        public int ArrowCount = 12;
    }

    [SaveType("picker_beta")]
    [Serializable]
    public class PickerBeta : PickerProbe
    {
        public int Mana = 50;
        public string Element = "fire";
    }

    /// <summary>Assignable to the field but absent from the registry, so the picker must skip it.</summary>
    [Serializable]
    public class PickerUnregistered : PickerProbe
    {
    }

    /// <summary>Shares a field name with <see cref="PickerAlpha"/> at a different type.</summary>
    [Serializable]
    public class PickerMismatched : PickerProbe
    {
        public float ArrowCount;
    }
}
