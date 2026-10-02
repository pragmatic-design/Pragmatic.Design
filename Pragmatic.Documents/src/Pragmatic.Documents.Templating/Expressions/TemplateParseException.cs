namespace Pragmatic.Documents.Templating.Expressions;

/// <summary>Thrown when a template expression fails to parse.</summary>
public sealed class TemplateParseException(string message) : Exception(message);
