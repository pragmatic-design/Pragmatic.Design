namespace Pragmatic.SourceGenerator.Features.Glossary.Models;

/// <summary>
///     One property of an event's payload in the generated AsyncAPI document: the JSON Schema property
///     name and its JSON type. Makes the event contract concrete (and snapshot-testable).
/// </summary>
internal sealed record AsyncApiPropertyModel
{
    /// <summary>
    ///     The name the property serializes to: the <c>[JsonPropertyName]</c> value, else the CLR name
    ///     under the camelCase policy — see <see cref="Glossary.JsonSchemaNaming" />.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>JSON Schema type: string, integer, number, boolean or object.</summary>
    public required string JsonType { get; init; }
}
