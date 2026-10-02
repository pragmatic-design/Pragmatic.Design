namespace Pragmatic.SourceGenerator.Features.ValueObject.Models;

/// <summary>
///     A single parameter (fully-qualified type + name) of a value object's
///     <c>Validate</c> method or constructor.
/// </summary>
internal sealed record ValueObjectParameter(string Type, string Name);
