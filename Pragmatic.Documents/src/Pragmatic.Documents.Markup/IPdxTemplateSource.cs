namespace Pragmatic.Documents.Markup;

/// <summary>
///     Where the markup of a template is found, by name — <c>"invoice.pdxdoc"</c>,
///     <c>"mail/reminder.pdxemail"</c>.
/// </summary>
/// <remarks>
///     A source answers <c>null</c> for a name it does not have, so sources compose: an organisation's
///     own templates before the application's (<see cref="FirstFoundPdxTemplateSource" />). Its
///     <c>ToString()</c> is what an error names when no source had the template.
/// </remarks>
public interface IPdxTemplateSource
{
    /// <summary>The markup of the template, or <c>null</c> when this source does not have it.</summary>
    ValueTask<string?> FindAsync(string name, CancellationToken ct = default);
}
