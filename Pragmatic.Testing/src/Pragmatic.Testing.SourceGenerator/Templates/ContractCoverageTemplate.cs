using System.Collections.Generic;
using Pragmatic.SourceGen;
using Pragmatic.Testing.SourceGenerator.Models;

namespace Pragmatic.Testing.SourceGenerator.Templates;

/// <summary>
///     Emits <c>ContractCoverage.Operations</c>: every published operation the generator saw, what it
///     emitted for it, and one sentence per contract it considered and declined.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>The thing a generated suite cannot say about itself.</b> Its classes report what was
///         written; nothing reports what was not. So an application reads eight covered operations and
///         has no way to learn that it publishes eleven, or that an operation it thought was contracted
///         was declined for a reason the generator had and kept. A hand comparison against a route
///         table cannot see reasons.
///     </para>
///     <para>
///         Emitted as data rather than as a comment so an application can pin it: a test that asserts
///         which operations are uncovered fails when a new one joins them, which is the only way an
///         absence gets noticed.
///     </para>
///     <para>
///         ⚠️ A create's absence is reported only for a <b>POST</b>. For any other verb "a create is a
///         POST" is true of every row and says nothing, and the verb is in the row already.
///     </para>
/// </remarks>
internal sealed class ContractCoverageTemplate : CSharpTemplate
{
    private const string Entry = "global::Pragmatic.Testing.ContractCoverageEntry";

    private readonly IReadOnlyList<ContractCoverageModel> _operations;

    public ContractCoverageTemplate(IReadOnlyList<ContractCoverageModel> operations) => _operations = operations;

    protected override string? GeneratorName => "Pragmatic.Testing.SourceGenerator";

    public override Artifact RenderOutput() => new("_ContractCoverage.g.cs", ToSourceText());

    protected override bool Validate() => _operations.Count > 0;

    public override void RenderFile()
    {
        AppendLine("namespace Pragmatic.Tests.Generated;");
        AppendLine();
        AppendLine("/// <summary>");
        AppendLine("///     Every operation this application publishes, and what the contract generator emitted for it.");
        AppendLine("/// </summary>");
        AppendLine("public static class ContractCoverage");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("/// <summary>One entry per published operation, in boundary then route order.</summary>");
        AppendLine($"public static readonly global::System.Collections.Generic.IReadOnlyList<{Entry}> Operations =");
        AppendLine("[");
        IncreaseIndent();

        foreach (var operation in _operations)
        {
            AppendLine($"new {Entry}(");
            IncreaseIndent();
            AppendLine($"{Literal(operation.Boundary)},");
            AppendLine($"{Literal(operation.Operation)},");
            AppendLine($"{Literal(operation.HttpMethod)},");
            AppendLine($"{Literal(operation.Route)},");
            AppendLine($"{Strings(operation.Contracts)},");
            AppendLine($"{Strings(operation.NotCovered)}),");
            DecreaseIndent();
        }

        DecreaseIndent();
        AppendLine("];");
        AppendLine();
        AppendLine("/// <summary>The operations no contract was emitted for at all.</summary>");
        AppendLine($"public static global::System.Collections.Generic.IEnumerable<{Entry}> Uncovered =>");
        IncreaseIndent();
        AppendLine("global::System.Linq.Enumerable.Where(Operations, operation => operation.Contracts.Count == 0);");
        DecreaseIndent();
        DecreaseIndent();
        AppendLine("}");
    }

    private static string Strings(EquatableArray<string> values)
    {
        if (values.Count == 0)
            return "[]";

        var items = new List<string>(values.Count);
        foreach (var value in values)
            items.Add(Literal(value));

        return "[" + string.Join(", ", items) + "]";
    }

    private static string Literal(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
