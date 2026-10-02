namespace Pragmatic.SourceGenerator.Features.Endpoints;

/// <summary>
///     One shape an <c>[Endpoint]</c> class may take.
/// </summary>
/// <param name="MetadataPrefix">
///     What the base type's (or attribute's) original definition starts with, as
///     <c>ToDisplayString()</c> renders it. A prefix rather than a full name because each base exists
///     in several arities and the arity is not what is being recognised.
/// </param>
/// <param name="Spelling">How the shape is named to the author in PRAG0501.</param>
internal sealed record EndpointShape(string MetadataPrefix, string Spelling);
