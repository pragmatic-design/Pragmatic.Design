using Pragmatic.Persistence.Documents.Mongo.Bson;

namespace Pragmatic.Persistence.Documents.Filters;

/// <summary>
///     One filter of an entity: the entity, the family, the filter class a caller lifts it by (<c>Reservation.OwnershipFilter</c>), the name a
///     named lift uses and the permission that lets a caller past it. The Sql engine's <c>SqlFilterSlot</c>, for documents.
/// </summary>
public sealed class DocFilterSlot(Type entity, DocFilterFamily family, Type? filter, string? name, string? bypass = null)
{
    public Type Entity { get; } = entity;

    public DocFilterFamily Family { get; } = family;

    public Type? Filter { get; } = filter;

    public string? Name { get; } = name;

    public string? Bypass { get; } = bypass;
}
