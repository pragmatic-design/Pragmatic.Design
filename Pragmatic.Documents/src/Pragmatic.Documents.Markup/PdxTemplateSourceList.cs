namespace Pragmatic.Documents.Markup;

/// <summary>The sources every <c>AddPdxTemplates</c> call has added, in order.</summary>
internal sealed class PdxTemplateSourceList
{
    public List<IPdxTemplateSource> Sources { get; } = [];
}
