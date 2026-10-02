using Pragmatic.SourceGen;

namespace Pragmatic.Client.SourceGenerator.Templates;

/// <summary>
///     Emits the client's <c>PagedResult&lt;T&gt;</c>, mirroring the server's wire shape
///     (items/totalCount/page/pageSize).
/// </summary>
/// <remarks>
///     Emitted once per client project, not per boundary: the type lives in the shared namespace, so a
///     per-boundary emission would collide (CS0101) on a multi-boundary client.
/// </remarks>
internal sealed class ClientPagedResultTemplate(string clientNamespace) : CSharpTemplate
{
    protected override string? GeneratorName => "Pragmatic.Client.SourceGenerator";

    public override Artifact RenderOutput() => new("PagedResult.g.cs", ToSourceText());

    protected override bool Validate() => clientNamespace.Length > 0;

    public override void RenderFile()
    {
        AppendNamespace(clientNamespace);
        AppendLine();

        XmlSummary("Paged response payload (mirrors the server's PagedResult wire shape).");
        AppendLine("/// <typeparam name=\"T\">The item type.</typeparam>");

        Class("PagedResult<T>", RenderBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Sealed = true });
    }

    private void RenderBody()
    {
        XmlSummary("The items in the current page.");
        AppendLine("public T[] Items { get; set; } = [];");
        AppendLine();
        XmlSummary("Total item count across all pages.");
        AppendLine("public int TotalCount { get; set; }");
        AppendLine();
        XmlSummary("Current page (1-based).");
        AppendLine("public int Page { get; set; }");
        AppendLine();
        XmlSummary("Page size.");
        AppendLine("public int PageSize { get; set; }");
    }
}
