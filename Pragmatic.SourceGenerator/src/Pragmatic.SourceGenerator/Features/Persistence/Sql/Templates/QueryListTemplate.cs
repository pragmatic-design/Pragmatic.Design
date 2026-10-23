using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Templates;

/// <summary>
///     A list of the translated queries as JSON, in a generated source that never compiles: the lines sit in a disabled <c>#if</c> block, and a
///     build target copies them out (<c>_Infra.Sql.Census.g.cs</c> to the committed <c>queries.census.json</c>, <c>_Infra.Sql.Explain.g.cs</c> to the
///     EXPLAIN tool).
/// </summary>
/// <remarks>
///     The directive names the file a target reads, and the target copies the lines between the two directives: nothing else of the file is
///     read, so the header is the template's own.
/// </remarks>
internal sealed class QueryListTemplate(string hintName, string symbol, string purpose, string json) : CSharpTemplate
{
    protected override string? GeneratorName => "Pragmatic.Persistence.Sql";

    protected override string? SourceInfo => purpose;

    public override Artifact RenderOutput() => new(hintName, ToSourceText());

    public override void RenderFile()
    {
        AppendLine($"#if {symbol}");
        foreach (var line in TextLines.Of(json))
            AppendLine(line);
        AppendLine("#endif");
    }
}
