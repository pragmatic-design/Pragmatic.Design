using System.Globalization;
using System.Linq;
using System.Reflection;
using Pragmatic.Composition.Metadata;
using Pragmatic.SourceGenerator.Features.Composition;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Core;

/// <summary>
///     Guards the one thing linking <see cref="MetadataCategory" /> into the generator does not fix
///     by itself: a category can still be added to the enum and forgotten here.
/// </summary>
/// <remarks>
///     A category emitted through the named enum member rather than a cast never appears in this list,
///     and its number reads as free while it is taken. The values themselves cannot drift, because
///     none of them is written down; what is left is completeness, and completeness is what a test can
///     hold.
/// </remarks>
public class MetadataCategoryIdsTests
{
    private static FieldInfo[] Constants =>
        typeof(MetadataCategoryIds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(string))
            .ToArray();

    /// <summary>
    ///     Every category the runtime declares has an entry. A new one added to the enum and not here
    ///     is the failure this test exists for: the generator would emit its ordinal and no reader
    ///     would have a name to compare against.
    /// </summary>
    [Fact]
    public void EveryCategory_HasAConstant()
    {
        var declared = typeof(MetadataCategoryIds).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => f.Name)
            .ToHashSet();

        var missing = System.Enum.GetNames(typeof(MetadataCategory))
            .Where(name => !declared.Contains(name))
            .ToArray();

        missing.Should().BeEmpty(
            "a MetadataCategory with no entry in MetadataCategoryIds is a category the generator can "
            + "emit and no reader can match — add it, named after the enum member");
    }

    /// <summary>
    ///     And every entry names a category that exists, so removing a member from the enum cannot
    ///     leave a constant behind pointing at nothing.
    /// </summary>
    [Fact]
    public void EveryConstant_NamesACategoryThatExists()
    {
        var names = System.Enum.GetNames(typeof(MetadataCategory)).ToHashSet();

        var orphans = Constants.Select(f => f.Name).Where(n => !names.Contains(n)).ToArray();

        orphans.Should().BeEmpty("a constant naming no category is a leftover of a removed member");
    }

    /// <summary>
    ///     Each constant carries the ordinal of the member it is named after. Linking the enum makes a
    ///     wrong <i>number</i> unwritable, but a member can still be initialised from the wrong enum
    ///     value — <c>Caching = Of(MetadataCategory.Actions)</c> compiles.
    /// </summary>
    [Fact]
    public void EveryConstant_CarriesTheOrdinalOfItsOwnCategory()
    {
        foreach (var field in Constants)
        {
            var expected = ((int)System.Enum.Parse(typeof(MetadataCategory), field.Name))
                .ToString(CultureInfo.InvariantCulture);

            field.GetValue(null).Should().Be(expected,
                $"MetadataCategoryIds.{field.Name} must carry the ordinal of MetadataCategory.{field.Name}");
        }
    }
}
