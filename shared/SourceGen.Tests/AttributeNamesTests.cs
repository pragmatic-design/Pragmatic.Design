using System;
using System.Linq;
using System.Reflection;
using Pragmatic.SourceGen;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGen.Tests;

/// <summary>
///     The one list of timezone attributes, and the guard that keeps it the only one.
/// </summary>
/// <remarks>
///     ⚠️ Two features read it: the Temporal feature builds a pipeline per entry, and the Endpoints
///     transform asks it what a trigger property declares so the behaviour reaches the generated
///     <c>{Trigger}Body</c> record — the type an endpoint actually deserializes. Without that list a
///     temporal attribute on a mutation would be inert: the record would carry the property's
///     documentation describing the conversion and not the conversion.
/// </remarks>
public class AttributeNamesTests
{
    [Fact]
    public void EveryTimezoneAttribute_IsInTheBehaviourMap()
    {
        var declared = typeof(AttributeNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Where(f => f.Name.StartsWith("Temporal", StringComparison.Ordinal))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        declared.Should().NotBeEmpty("the constants are there to be found");

        var mapped = AttributeNames.TemporalBehaviors.Select(b => b.AttributeName).ToList();

        mapped.Should().BeEquivalentTo(declared,
            "a seventh timezone attribute added to the constants and not to the map compiles, documents "
            + "itself on the generated body record, and converts nothing");
    }

    /// <summary>
    ///     The control: the map says which <c>TemporalJsonBehavior</c> member each attribute means, and
    ///     two attributes cannot mean the same one.
    /// </summary>
    /// <remarks>
    ///     Without it, the completeness check above is satisfied by a map whose behaviours are all the
    ///     same string — every attribute present, every conversion wrong.
    /// </remarks>
    [Fact]
    public void EachAttribute_MeansItsOwnBehaviour()
    {
        var behaviors = AttributeNames.TemporalBehaviors.Select(b => b.Behavior).ToList();

        behaviors.Should().OnlyHaveUniqueItems();
        behaviors.Should().AllSatisfy(b => b.Should().NotBeNullOrWhiteSpace());
    }

    /// <summary>
    ///     And the behaviour name is the attribute's own name without its suffix, which is what makes
    ///     the map readable rather than a lookup nobody can check by eye.
    /// </summary>
    [Fact]
    public void TheBehaviourName_IsTheAttributesName()
    {
        foreach (var (attributeName, behavior) in AttributeNames.TemporalBehaviors)
        {
            var simpleName = attributeName[(attributeName.LastIndexOf('.') + 1)..];

            simpleName.Should().Be($"{behavior}Attribute");
        }
    }
}
