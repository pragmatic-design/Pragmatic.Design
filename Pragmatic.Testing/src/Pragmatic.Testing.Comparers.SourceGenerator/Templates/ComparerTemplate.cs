using Pragmatic.SourceGen;
using Pragmatic.Testing.Comparers.SourceGenerator.Models;

namespace Pragmatic.Testing.Comparers.SourceGenerator.Templates;

/// <summary>
///     Emits the comparer, and the <c>BeEquivalentTo</c> extension that reaches it.
/// </summary>
/// <remarks>
///     The extension hangs off <c>ObjectAssertions&lt;object?&gt;</c> — where a value of the compared
///     type lands, since <c>Should()</c> falls back to <c>object</c> for anything without a family of
///     its own. It takes the expected value typed, so the compiler still picks the right overload per
///     compared type. <c>ObjectAssertions</c> deliberately has no <c>BeEquivalentTo</c> member: an
///     instance method always beats an extension, and this one would never be reached.
/// </remarks>
internal sealed class ComparerTemplate : CSharpTemplate
{
    private const string AssertionsNs = "global::Pragmatic.Testing.Assertions";

    private readonly ComparerModel _model;

    public ComparerTemplate(ComparerModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.Testing.Comparers.SourceGenerator";

    public override Artifact RenderOutput() => new($"Comparer.{_model.ClassName}.g.cs", ToSourceText());

    protected override bool Validate() => _model.Members.Count > 0;

    public override void RenderFile()
    {
        AppendLine("namespace Pragmatic.Tests.Generated;");
        AppendLine();
        AppendLine($"/// <summary>Compares two <c>{_model.TypeShortName}</c> member by member.</summary>");
        AppendLine($"public static class {_model.ClassName}");
        AppendLine("{");
        IncreaseIndent();

        RenderDifference();
        AppendLine();
        RenderElements();
        AppendLine();
        RenderExtension();

        DecreaseIndent();
        AppendLine("}");
    }

    /// <summary>
    ///     The comparison: how the first differing member differs, values included, or null.
    /// </summary>
    /// <remarks>
    ///     Naming the member is half the point; showing the two values is the other half. Without
    ///     them the reader gets "Total differs" and has to go and print both by hand.
    /// </remarks>
    private void RenderDifference()
    {
        AppendLine("/// <summary>How the first differing member differs, or null when none does.</summary>");
        AppendLine($"public static string? FirstDifference({_model.TypeFullName}? actual, {_model.TypeFullName}? expected)");
        AppendLine("{");
        IncreaseIndent();

        AppendLine("if (actual is null || expected is null)");
        IncreaseIndent();
        AppendLine("return ReferenceEquals(actual, expected) ? null : \"the value itself is null\";");
        DecreaseIndent();
        AppendLine();

        foreach (var member in _model.Members)
        {
            // A sequence member goes through Elements, which handles null and the fact that the
            // member's declared type is not necessarily IEnumerable<object?> — a dictionary is a
            // sequence of pairs, and `?? Array.Empty<object?>()` would not even compile against it.
            var comparison = member.IsSequence
                ? "!global::System.Linq.Enumerable.SequenceEqual("
                  + $"Elements(actual.{member.Name}), Elements(expected.{member.Name}))"
                : "!global::System.Collections.Generic.EqualityComparer<object?>.Default"
                  + $".Equals(actual.{member.Name}, expected.{member.Name})";

            AppendLine($"if ({comparison})");
            AppendLine("{");
            IncreaseIndent();
            // Pulled into locals: `global::` is not valid inside an interpolation (CS0103).
            AppendLine($"var mine = {AssertionsNs}.AssertionFailure.Format(actual.{member.Name});");
            AppendLine($"var theirs = {AssertionsNs}.AssertionFailure.Format(expected.{member.Name});");
            AppendLine($"return $\"{member.Name} differs: found {{mine}}, expected {{theirs}}\";");
            DecreaseIndent();
            AppendLine("}");
            AppendLine();
        }

        AppendLine("return null;");
        DecreaseIndent();
        AppendLine("}");
    }

    /// <summary>
    ///     Flattens any sequence member to <c>IEnumerable&lt;object?&gt;</c>, treating null as empty.
    /// </summary>
    private void RenderElements()
    {
        AppendLine("/// <summary>The member's elements as objects; null reads as empty.</summary>");
        AppendLine("private static global::System.Collections.Generic.IEnumerable<object?> Elements(");
        IncreaseIndent();
        AppendLine("global::System.Collections.IEnumerable? source) =>");
        AppendLine("source is null");
        IncreaseIndent();
        AppendLine("? global::System.Linq.Enumerable.Empty<object?>()");
        AppendLine(": global::System.Linq.Enumerable.Cast<object?>(source);");
        DecreaseIndent();
        DecreaseIndent();
    }

    /// <summary>The assertion the test calls.</summary>
    private void RenderExtension()
    {
        AppendLine("/// <summary>Fails unless the subject matches <paramref name=\"expected\"/> member by member.</summary>");
        AppendLine($"public static {AssertionsNs}.AndConstraint<{AssertionsNs}.ObjectAssertions<object?>> BeEquivalentTo(");
        IncreaseIndent();
        AppendLine($"this {AssertionsNs}.ObjectAssertions<object?> assertions,");
        AppendLine($"{_model.TypeFullName} expected,");
        AppendLine("string? because = null,");
        AppendLine("params object[] becauseArgs)");
        DecreaseIndent();
        AppendLine("{");
        IncreaseIndent();

        AppendLine($"var actual = assertions.Subject as {_model.TypeFullName};");
        AppendLine();

        AppendLine("if (actual is null)");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("var wrong = assertions.Subject is null ? \"<null>\" : assertions.Subject.GetType().Name;");
        AppendLine($"{AssertionsNs}.AssertionFailure.Throw(assertions.SubjectExpression,");
        IncreaseIndent();
        AppendLine($"\"to be a {_model.TypeShortName}\", $\"found {{wrong}}\", because, becauseArgs);");
        DecreaseIndent();
        DecreaseIndent();
        AppendLine("}");
        AppendLine();

        AppendLine("if (FirstDifference(actual, expected) is { } difference)");
        IncreaseIndent();
        AppendLine($"{AssertionsNs}.AssertionFailure.Throw(assertions.SubjectExpression,");
        IncreaseIndent();
        AppendLine($"\"to match the expected {_model.TypeShortName}\", difference, because, becauseArgs);");
        DecreaseIndent();
        DecreaseIndent();
        AppendLine();

        AppendLine($"return new {AssertionsNs}.AndConstraint<{AssertionsNs}.ObjectAssertions<object?>>(assertions);");
        DecreaseIndent();
        AppendLine("}");
    }
}
