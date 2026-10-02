using System.Reflection;
using Pragmatic.Telemetry.Conventions;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Abstractions.Tests;

/// <summary>
///     The naming rule the convention classes exist to hold: a tag is either an OpenTelemetry
///     semantic-convention attribute, spelled exactly as the spec spells it, or it carries the
///     <c>pragmatic.</c> prefix.
/// </summary>
/// <remarks>
///     <para>
///         The rule is not cosmetic. <c>messaging.*</c>, <c>job.*</c>, <c>db.*</c> and <c>event.*</c>
///         are namespaces the spec owns; a tag of ours sitting in one squats a name the spec may later
///         define differently, and a backend that understands semconv will read it as something it is
///         not.
///     </para>
///     <para>
///         <b>What this does not catch.</b> It sees the constants, not the call sites, so it cannot
///         tell you that someone wrote the string out by hand at a <c>SetTag</c>. Catching that needs
///         an analyzer. What it does hold is that a new constant cannot quietly take a bare name.
///     </para>
/// </remarks>
public class TelemetryConventionTests
{
    /// <summary>
    ///     Attributes defined by the OpenTelemetry semantic conventions, which must stay unprefixed so
    ///     that a semconv-aware backend recognises them. Add to this list only from the spec.
    /// </summary>
    private static readonly HashSet<string> SemanticConventionAttributes =
    [
        "cache.hit",
        "db.namespace",
        "db.operation.name",
        "db.collection.name",
        "db.response.rows_affected",
        "error.type",
        "exception.type",
        "exception.message",
        "exception.stacktrace",
    ];

    private static readonly Type[] ConventionClasses = typeof(ActionTags).Assembly
        .GetTypes()
        .Where(t => t.Namespace == "Pragmatic.Telemetry.Conventions" && t is { IsClass: true, IsAbstract: true, IsSealed: true })
        .OrderBy(t => t.Name)
        .ToArray();

    private static IEnumerable<(string Owner, string Name, string Value)> AllTags()
        => ConventionClasses.SelectMany(t => t
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (t.Name, f.Name, (string)f.GetRawConstantValue()!)));

    /// <summary>Guards the discovery itself: an empty sweep would make every test below vacuous.</summary>
    [Fact]
    public void TheConventionClasses_AreFound()
    {
        ConventionClasses.Should().NotBeEmpty();
        AllTags().Count().Should().BeGreaterThan(50);
    }

    [Fact]
    public void EveryTag_IsEitherSemconvOrPrefixed()
    {
        var offenders = AllTags()
            .Where(t => !t.Value.StartsWith("pragmatic.", StringComparison.Ordinal))
            .Where(t => !SemanticConventionAttributes.Contains(t.Value))
            .Select(t => $"{t.Owner}.{t.Name} = \"{t.Value}\"")
            .ToArray();

        offenders.Should().BeEmpty();
    }

    /// <summary>
    ///     Two constants with one value means two names for one thing, and the second is the one that
    ///     goes stale.
    /// </summary>
    [Fact]
    public void NoTwoTags_ShareAValue()
    {
        var duplicates = AllTags()
            .GroupBy(t => t.Value)
            .Where(g => g.Count() > 1)
            .Select(g => $"\"{g.Key}\" is {string.Join(" and ", g.Select(t => $"{t.Owner}.{t.Name}"))}")
            .ToArray();

        duplicates.Should().BeEmpty();
    }

    /// <summary>A tag whose value is empty or has whitespace in it is not a tag name.</summary>
    [Fact]
    public void EveryTag_IsAUsableAttributeName()
    {
        var malformed = AllTags()
            .Where(t => string.IsNullOrWhiteSpace(t.Value) || t.Value.Any(char.IsWhiteSpace))
            .Select(t => $"{t.Owner}.{t.Name}")
            .ToArray();

        malformed.Should().BeEmpty();
    }
}
