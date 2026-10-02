namespace Pragmatic.Documents.Markup;

/// <summary>Thrown when PDX markup parsing fails.</summary>
public sealed class MarkupParseException(string message) : Exception(message);
