using System.Collections.Generic;
using Pragmatic.SourceGen;

namespace Pragmatic.Persistence.Sql.Generator.Templates;

/// <summary>
///     <c>Pragmatic.Persistence.Sql.Queries.g.cs</c>: the <c>[Query]</c> definitions as session extensions. Their generated methods repeat the
///     definitions' expressions, so the file carries the definitions' usings.
/// </summary>
internal sealed class SqlQueriesTemplate(IEnumerable<string> usings, string declared) : CSharpTemplate
{
    protected override string? GeneratorName => "Pragmatic.Persistence.Sql";

    protected override string? SourceInfo => "[Query] definitions as session extensions";

    public override Artifact RenderOutput() => new("Pragmatic.Persistence.Sql.Queries.g.cs", ToSourceText());

    public override void RenderFile()
    {
        foreach (var directive in usings)
            AppendLine(directive);
        // TODO: the declared queries become a model (the engines' port, step 4).
        foreach (var line in TextLines.Of(declared))
            AppendLine(line);
    }
}
