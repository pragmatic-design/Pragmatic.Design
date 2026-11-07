namespace Pragmatic.Persistence.Documents.Filters;

/// <summary>
///     The caller's filters for the documents a to-one navigation reaches: the owner and scopes (<see cref="DocFilterFamily.Permission" />) and the
///     temporal window lifted, the tenancy, the soft delete and the declared rules as the caller has them. On EF Core Pragmatic's runtime filters
///     do not visit a reference (<c>FilterScope</c>), while its model filters (tenant, soft delete, rules) do: a caller who reads a reservation
///     reads its guest's name, whoever owns the guest (#580).
/// </summary>
internal sealed class DocNavigationFilters(IDocFilters? caller) : IDocFilters
{
    public DocFilterState StateOf(DocFilterSlot slot)
        => slot.Family is DocFilterFamily.Permission or DocFilterFamily.Temporal
            ? DocFilterState.Lifted
            : caller?.StateOf(slot) ?? DocFilterState.Active;

    public string? TenantId => caller?.TenantId;

    public string? UserId => caller?.UserId;

    public IReadOnlyCollection<string> Scopes => caller?.Scopes ?? [];

    public DateTimeOffset Now => caller?.Now ?? default; // the temporal family is lifted here: nothing compares against it
}
