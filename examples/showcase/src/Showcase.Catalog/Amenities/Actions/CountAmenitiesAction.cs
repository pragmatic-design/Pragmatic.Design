using Microsoft.EntityFrameworkCore;

namespace Showcase.Catalog.Amenities.Actions;

/// <summary>
/// Declares the two services a boundary registers keyed by its own type — the <c>DbContext</c> and the
/// <c>IUnitOfWork</c> — and uses both.
/// </summary>
/// <remarks>
/// <para>
/// Demonstrates: an operation that needs the raw entity set, and one that decides when it saves.
/// Neither was declarable before: both are registered with <c>AddKeyedScoped(typeof(CatalogBoundary))</c>
/// and the generated invoker asked for them without the key, so resolution failed at startup naming a
/// generated type. The workaround an application finds on its own is a <c>private IServiceProvider</c>
/// field and <c>GetRequiredKeyedService&lt;T&gt;(typeof(CatalogBoundary))</c> repeated at every site.
/// </para>
/// <para>
/// ⚠️ Saving here is the point, not a convenience: with the unit of work injectable an operation can
/// write more than once, and <c>CommitMode</c> stops being the only answer to "I must save twice".
/// That widening was decided deliberately.
/// </para>
/// </remarks>
[DomainAction]
[Endpoint(HttpVerb.Post, "api/amenities/{id}/touch")]
public partial class CountAmenitiesAction : DomainAction<int>
{
    private DbContext _db = null!;
    private IUnitOfWork _unitOfWork = null!;

    public required Guid Id { get; init; }

    /// <summary>The suffix appended to the amenity's name, so the write is visible on the next read.</summary>
    public required string Marker { get; init; }

    public override async Task<Result<int, IError>> Execute(CancellationToken ct = default)
    {
        var amenity = await _db.Set<Amenity>()
            .FirstOrDefaultAsync(a => a.PersistenceId == Id, ct)
            .ConfigureAwait(false);

        if (amenity is null)
            return NotFoundError.For<Guid>(nameof(Amenity), Id);

        amenity.SetName($"{amenity.Name} {Marker}");
        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return await _db.Set<Amenity>().CountAsync(ct).ConfigureAwait(false);
    }
}
