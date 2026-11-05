using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Repository;
using Pragmatic.Specification;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     A Pragmatic entity hierarchy in one collection (#539): <see cref="Charge" />, <see cref="LateCharge" /> and <see cref="DamageCharge" /> written
///     through their repositories, read back through the root's as their own types with every member (the base's private setters among them), a
///     derived type's reads returning its own documents only. MongoDB and Cosmos DB.
/// </summary>
[Collection("document servers")]
public class InheritanceTests(DocumentServers servers)
{
    /// <summary>One charge of each type, their reason the run's batch: what keeps one run's charges apart from another's.</summary>
    private static async Task<(DocSession Session, string Batch, Guid Plain, Guid Late, Guid Damage)> ChargesAsync(DocumentServers servers, Store store)
    {
        var session = servers.Session(store);
        if (store == Store.Cosmos)
            await session.EnsureContainerAsync<Charge>();
        var batch = "C-" + Guid.NewGuid().ToString("N")[..10];
        var unitOfWork = UnitOfWorkTests.UnitOfWork(session);

        var plain = Charge.Create();
        plain.Charged(5m, batch);
        var late = LateCharge.Create();
        late.Charged(40.5m, batch);
        late.Late(3);
        var damage = DamageCharge.Create();
        damage.Charged(120m, batch);
        damage.Damaged("lamp");
        new Charge.DocRepository(unitOfWork).Add(plain);
        new LateCharge.DocRepository(unitOfWork).Add(late);
        new DamageCharge.DocRepository(unitOfWork).Add(damage);
        Assert.Equal(3, await unitOfWork.SaveChangesAsync());
        return (session, batch, plain.Id, late.Id, damage.Id);
    }

    [Fact]
    public void The_hierarchy_is_one_collection_with_its_discriminator_indexed()
    {
        var charges = Assert.Single(DefaultDocSchema.Collections, c => c.Collection == "Charges");
        Assert.Contains(charges.Indexes, i => i is { Name: "IX_Charges_ChargeType", Unique: false } && i.Fields.SequenceEqual(["ChargeType"]));
        Assert.DoesNotContain(DefaultDocSchema.Collections, c => c.Collection is "LateCharges" or "DamageCharges");
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task The_roots_reads_build_each_document_as_its_own_type(Store store)
    {
        var (session, batch, plain, late, damage) = await ChargesAsync(servers, store);
        IReadRepository<Charge> charges = new Charge.DocReadRepository(session);

        var all = (await charges.FindAsync(Spec<Charge>.Where(c => c.Reason == batch))).OrderBy(c => c.Amount).ToList();

        Assert.Equal([typeof(Charge), typeof(LateCharge), typeof(DamageCharge)], all.Select(c => c.GetType()));
        Assert.Equal([5m, 40.5m, 120m], all.Select(c => c.Amount));
        Assert.All(all, c => Assert.Equal(batch, c.Reason));
        Assert.Equal(3, Assert.IsType<LateCharge>(all[1]).HoursLate);
        Assert.Equal("lamp", Assert.IsType<DamageCharge>(all[2]).Item);
        Assert.Equal(late, all[1].Id);

        // By key too, and through a chain on the root.
        Assert.Equal("lamp", Assert.IsType<DamageCharge>(await charges.GetByIdAsync(damage)).Item);
        Assert.IsType<Charge>(await charges.GetByIdAsync(plain));
        Assert.Equal(3, await session.Query<Charge>().CountAsync((c, b) => c.Reason == b, batch));
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_derived_types_reads_return_its_own_documents_only(Store store)
    {
        var (session, batch, plain, late, damage) = await ChargesAsync(servers, store);
        IReadRepository<LateCharge> lates = new LateCharge.DocReadRepository(session);
        IReadRepository<DamageCharge> damages = new DamageCharge.DocReadRepository(session);

        var found = Assert.Single(await lates.FindAsync(Spec<LateCharge>.Where(c => c.Reason == batch)));
        Assert.Equal((late, 40.5m, 3), (found.Id, found.Amount, found.HoursLate));
        Assert.Equal("lamp", Assert.Single(await damages.FindAsync(Spec<DamageCharge>.Where(c => c.Reason == batch))).Item);
        Assert.Equal(1, await session.Query<LateCharge>().CountAsync((c, b) => c.Reason == b, batch));

        // Another type's key is not one of its documents.
        Assert.Null(await lates.GetByIdAsync(damage));
        Assert.Null(await lates.GetByIdAsync(plain));
        Assert.NotNull(await lates.GetByIdAsync(late));
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_derived_entity_written_as_the_root_keeps_its_type_and_members(Store store)
    {
        var (session, batch, _, _, _) = await ChargesAsync(servers, store);
        var late = LateCharge.Create();
        late.Charged(7m, batch);
        late.Late(9);

        await session.InsertAsync<Charge>(late); // the root's writer, handed an entity of a type below it

        var read = Assert.IsType<LateCharge>(await new Charge.DocReadRepository(session).GetByIdAsync(late.Id));
        Assert.Equal((7m, 9), (read.Amount, read.HoursLate));
        Assert.Equal(2, await session.Query<LateCharge>().CountAsync((c, b) => c.Reason == b, batch));
    }
}
