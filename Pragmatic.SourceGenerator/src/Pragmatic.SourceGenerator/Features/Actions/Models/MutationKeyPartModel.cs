namespace Pragmatic.SourceGenerator.Features.Actions.Models;

/// <summary>
///     One part of an entity's <c>[LogicKey]</c>, as a <c>ReturnType = LogicalKey</c> mutation returns it.
/// </summary>
/// <param name="Name">The entity property the part is, and the name of the record property that carries it.</param>
/// <param name="TypeFullName">
///     Its type, fully qualified; empty when the part is a key a relation declared on the other entity
///     puts here — only the relation graph knows that type (PRAG0403).
/// </param>
/// <param name="JsonName">
///     The <c>[JsonPropertyName]</c> the entity property declares, so the record carries the part under
///     the same wire name the entity does; <c>null</c> for the naming policy's default.
/// </param>
internal sealed record MutationKeyPartModel(string Name, string TypeFullName, string? JsonName = null);
