using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Pragmatic.Serialization;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Abstractions.Tests;

/// <summary>
///     A modifier registered on the shared options reaches the built resolver.
/// </summary>
/// <remarks>
///     <para>
///         This is the seam a contributor needs. Before it, the only way to add a
///         <see cref="JsonTypeInfo" /> modifier was to wrap whatever resolver happened to be on the
///         options at that moment — and the host's own wiring then assigned a freshly built resolver
///         over the top, discarding the wrap. Nothing reported it: the payload stayed well formed and
///         only the values were wrong.
///     </para>
///     <para>
///         ⚠️ Two modifiers, not one, and deliberately: one proves the mechanism, two prove it
///         <em>composes</em> — which is the property that was actually missing, since the failure was
///         never "no modifier ran" but "the second one displaced the first".
///     </para>
/// </remarks>
public class ASecondModifierSurvivesTheBuildTests
{
    private sealed class Payload
    {
        public string Kept { get; set; } = "";

        public string Dropped { get; set; } = "";

        public string AlsoDropped { get; set; } = "";
    }

    /// <summary>One registered modifier reaches the resolver.</summary>
    [Fact]
    public void AModifier_RegisteredOnTheOptions_ReachesTheBuiltResolver()
    {
        var options = new PragmaticJsonOptions()
            .AddModifier(Drop(nameof(Payload.Dropped)));

        var json = JsonSerializer.Serialize(new Payload(), options.Build());

        json.Should().Contain("kept");
        json.Should().NotContain("\"dropped\"", "the modifier removed it");
    }

    /// <summary>
    ///     Two modifiers both apply: the second does not displace the first.
    /// </summary>
    /// <remarks>
    ///     This is the case the framework got wrong. Each contributor wrapped the resolver it found, so
    ///     the last assignment won and every earlier wrap was lost — silently, because a discarded
    ///     modifier does not fail, it simply stops happening.
    /// </remarks>
    [Fact]
    public void TwoModifiers_BothApply_AndNeitherDisplacesTheOther()
    {
        var options = new PragmaticJsonOptions()
            .AddModifier(Drop(nameof(Payload.Dropped)))
            .AddModifier(Drop(nameof(Payload.AlsoDropped)));

        var json = JsonSerializer.Serialize(new Payload(), options.Build());

        json.Should().NotContain("\"dropped\"", "the first modifier still applies");
        json.Should().NotContain("\"alsoDropped\"", "and so does the second");
        json.Should().Contain("kept", "and neither touched what it was not asked to");
    }

    /// <summary>
    ///     The control: with no modifier registered, every property is published.
    /// </summary>
    /// <remarks>
    ///     Without it, "the modifier dropped the property" is satisfied by options that publish nothing
    ///     at all, which both assertions above would accept.
    /// </remarks>
    [Fact]
    public void WithNoModifier_EveryPropertyIsPublished()
    {
        var json = JsonSerializer.Serialize(new Payload(), new PragmaticJsonOptions().Build());

        json.Should().Contain("kept");
        json.Should().Contain("dropped");
        json.Should().Contain("alsoDropped");
    }

    private static Action<JsonTypeInfo> Drop(string propertyName)
        => typeInfo =>
        {
            if (typeInfo.Type != typeof(Payload))
                return;

            var property = typeInfo.Properties.FirstOrDefault(p => string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase));
            if (property is not null)
                typeInfo.Properties.Remove(property);
        };
}
