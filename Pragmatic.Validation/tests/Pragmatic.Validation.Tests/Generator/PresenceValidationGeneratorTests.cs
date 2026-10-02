using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Validation.Tests.Generator;

/// <summary>
///     The presence rules — <c>[NotEmpty]</c> next to a presence guard.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ On a <c>required</c> string, <c>[NotEmpty]</c> rendered inside the <c>else</c> of a
///         <c>string.IsNullOrEmpty</c> presence guard would run only on a value the guard had already
///         proven non-empty, so it could never fire, and an empty string would come out as
///         <c>validation.required</c>. A case that asserts the property name and not the key cannot
///         see the difference.
///     </para>
///     <para>
///         A presence rule speaks about absence; a guard that says "only when present" silences it.
///         So it lives <b>outside</b> the guard, like <c>[RequiredIf]</c>, and the guard
///         narrows to <c>is null</c> so that each input has one rule speaking for it: null is
///         <c>validation.required</c>, empty is <c>validation.notempty</c>.
///     </para>
/// </remarks>
public class PresenceValidationGeneratorTests : ValidationGeneratorTestBase
{
    [Fact]
    public void NotEmpty_OnARequiredString_IsRenderedOutsideThePresenceGuard()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record CreateTagRequest
                              {
                                  [NotEmpty]
                                  public required string Name { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
        var generated = GetGeneratedSource(result, "CreateTagRequest.Validator")!;

        generated.Should().Contain("Name is null",
            "with [NotEmpty] alongside, the presence guard asks for null only: empty belongs to the rule");
        generated.Should().NotContain("string.IsNullOrEmpty(Name)",
            "an IsNullOrEmpty guard would claim the empty string before the rule could");
        generated.Should().Contain("Name?.Length == 0");

        var rule = generated.IndexOf("\"validation.notempty\"", StringComparison.Ordinal);
        var guard = generated.IndexOf("\"validation.required\"", StringComparison.Ordinal);
        rule.Should().BeGreaterThan(-1);
        guard.Should().BeGreaterThan(-1);
        rule.Should().BeLessThan(guard,
            "the rule is rendered before the guard, outside it — not in the else branch");
    }

    [Fact]
    public void NotEmpty_OnARequiredCollection_CountsElements()
    {
        const string source = """
                              using System.Collections.Generic;
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record CreateTagRequest
                              {
                                  [NotEmpty]
                                  public required List<string> Labels { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        // ⚠️ `is { Count: 0 } or { Length: 0 }` on a List<T> is a CS0117 inside a generated file:
        // a List has no Length, and a pattern names members the type must have.
        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
        var generated = GetGeneratedSource(result, "CreateTagRequest.Validator")!;

        generated.Should().Contain("Labels?.Count == 0");
        generated.Should().Contain("\"validation.notempty\"");
        generated.Should().Contain("Labels is null");
    }

    [Fact]
    public void NotEmpty_OnAnArray_CountsElements()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record CreateTagRequest
                              {
                                  [NotEmpty]
                                  public string[] Labels { get; init; } = [];
                              }
                              """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
        GetGeneratedSource(result, "CreateTagRequest.Validator")!
            .Should().Contain("Labels?.Length == 0");
    }

    /// <summary>The control: where nothing requires the value, an absent one is not refused.</summary>
    [Fact]
    public void NotEmpty_OnAnOptionalString_LeavesAbsenceAlone()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record CreateTagRequest
                              {
                                  [NotEmpty]
                                  public string? Nickname { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
        var generated = GetGeneratedSource(result, "CreateTagRequest.Validator")!;

        generated.Should().Contain("Nickname?.Length == 0");
        generated.Should().NotContain("\"validation.required\"",
            "nothing declared the value required: null passes, only the empty string is refused");
    }
}
