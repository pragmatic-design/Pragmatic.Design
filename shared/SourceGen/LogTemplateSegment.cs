// ReSharper disable once CheckNamespace
namespace Pragmatic.SourceGen;

/// <summary>One part of a log message template: literal text, or a placeholder.</summary>
/// <param name="Text">
///     The literal text with <c>{{</c> and <c>}}</c> already unescaped, or, for a placeholder, its name as
///     written — the structured property's key.
/// </param>
/// <param name="IsPlaceholder">Whether this is a placeholder.</param>
/// <param name="Format">The placeholder's format string (after <c>:</c>), or null.</param>
/// <param name="Alignment">The placeholder's alignment (after <c>,</c>), or null.</param>
internal sealed record LogTemplateSegment(string Text, bool IsPlaceholder, string? Format, string? Alignment)
{
    /// <summary>The name a placeholder matches a parameter by: without a leading <c>@</c>.</summary>
    public string MatchName => Text.Length > 1 && Text[0] == '@' ? Text.Substring(1) : Text;
}
