using Pragmatic.Email.Model;

namespace Pragmatic.Email.Templates;

/// <summary>Email section template.</summary>
public sealed record EmailSectionTemplate
{
    public Pragmatic.Documents.Templating.TemplateDirectives? Directives { get; init; }
    public string? BackgroundColor { get; init; }
    public EmailPadding Padding { get; init; } = EmailPadding.Default;
    public IReadOnlyList<EmailColumnTemplate> Columns { get; init; } = [];
}
