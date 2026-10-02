namespace Pragmatic.Email.Templates;

/// <summary>Reusable email partial — a named fragment of sections.</summary>
public sealed record EmailPartialDefinition
{
    public required string Name { get; init; }
    public IReadOnlyList<EmailSectionTemplate> Sections { get; init; } = [];
}
