using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Documents.Pragmatic;
using Pragmatic.Persistence.Repository;
using Pragmatic.Specification;
using PragmaticDocumentSamples.Callers;
using PragmaticDocumentSamples.Entities;

namespace PragmaticDocumentSamples.Flows;

/// <summary>
///     Pragmatic's unit of work on both backends, on the rows <see cref="EntitySamples" /> wrote, as anna: load, change, save (the audit and the
///     soft delete as the phases write them), a new versioned document, and the second of two writers refused. These samples' MongoDB is a
///     standalone server: the save runs without a transaction, its writes one after the other, as on Cosmos DB.
/// </summary>
internal static class UnitOfWorkSamples
{
    public static async Task<SampleRun> RunAsync(string backend, DocSession anna)
    {
        var checks = new Checks(backend);
        if (anna.Cosmos is not null)
            await anna.EnsureContainerAsync<Amenity>();
        checks.Check("Pragmatic writes: a standalone server runs no transaction (Cosmos DB neither)", !await anna.SupportsTransactionsAsync());

        DocUnitOfWork UnitOfWork() => new(anna, currentUser: new SampleUser("anna"), tenant: new TenantScope());
        var unitOfWork = UnitOfWork();
        IRepository<Property> properties = new Property.DocRepository(unitOfWork);
        var riva = (await properties.FindAsync(Spec<Property>.True)).Single();
        riva.Name = "RIVA nuova";
        checks.Check("Pragmatic writes: a loaded entity saved", await unitOfWork.SaveChangesAsync() == 1 && await unitOfWork.SaveChangesAsync() == 0);
        checks.Check("Pragmatic writes: read back changed", (await new Property.DocReadRepository(anna).GetByIdAsync(riva.Id))?.Name == "RIVA nuova");

        IRepository<Reservation> reservations = new Reservation.DocRepository(unitOfWork);
        var stays = await reservations.FindAsync(Spec<Reservation>.True);
        reservations.Remove(stays[0]);
        await unitOfWork.SaveChangesAsync();
        checks.Check("Pragmatic writes: a removal soft deletes", stays[0] is { IsDeleted: true, DeletedBy: "anna" } && (await reservations.FindAsync(Spec<Reservation>.True)).Count == stays.Count - 1);

        var wifi = new Amenity { Name = "WiFi" };
        new Amenity.DocRepository(unitOfWork).Add(wifi);
        await unitOfWork.SaveChangesAsync();
        var (first, second) = (UnitOfWork(), UnitOfWork());
        var mine = (await new Amenity.DocRepository(first).GetByIdAsync(wifi.Id))!;
        var theirs = (await new Amenity.DocRepository(second).GetByIdAsync(wifi.Id))!;
        mine.Name = "WiFi 6";
        await first.SaveChangesAsync();
        theirs.Name = "Fibre";
        try
        {
            await second.SaveChangesAsync();
            checks.Check("Pragmatic writes: the second of two writers refused", false);
        }
        catch (PersistenceRuleViolationException stale)
        {
            checks.Check("Pragmatic writes: the second of two writers refused", stale.Error is ConcurrencyError { EntityTypeName: "Amenity" } && mine.RowVersion == 1);
        }

        return checks.Run;
    }
}
