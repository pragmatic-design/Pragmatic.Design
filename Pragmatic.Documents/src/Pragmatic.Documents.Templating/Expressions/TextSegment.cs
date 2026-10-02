namespace Pragmatic.Documents.Templating.Expressions;

/// <summary>Literal text segment.</summary>
public sealed record TextSegment(string Text) : InterpolatedSegment;
