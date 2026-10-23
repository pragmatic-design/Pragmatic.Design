using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>An attachment in the repository's [PolymorphicAttachment] shape (OwnerType holds the owner's full type name, OwnerId its key), on the Assignments table.</summary>
[Entity]
[Table("Assignments")]
public sealed class Attachment
{
    public Guid Id { get; set; }

    [Column("ParentId")]
    public Guid OwnerId { get; set; }

    [Column("ParentType")]
    public string OwnerType { get; set; } = "";

    public string Note { get; set; } = "";

    public string Kind { get; set; } = "Attachment";
}

/// <summary>
///     <c>ForOwner&lt;TOwner&gt;()</c>: the attachments of an owner type, <c>OwnerType = 'Namespace.Owner'</c> written by the generator from the type,
///     as the repository's generated filter compares it. In a transaction, rolled back.
/// </summary>
[Collection("databases")]
public class AttachmentTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task For_owner_reads_the_attachments_of_one_owner_type(SqlDialect dialect)
    {
        if (dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null)
            return;
        using var session = databases.Session(dialect);
        using var transaction = session.BeginTransaction();
        var mark = "att-" + Guid.NewGuid().ToString("N")[..6];
        var order = databases.Seed.Orders[3];
        var customer = databases.Seed.BusyCustomer;
        await session.InsertManyAsync(new[]
        {
            new Attachment { Id = Guid.NewGuid(), OwnerId = order.Id, OwnerType = typeof(Order).FullName!, Note = mark + "-o1" },
            new Attachment { Id = Guid.NewGuid(), OwnerId = order.Id, OwnerType = typeof(Order).FullName!, Note = mark + "-o2" },
            new Attachment { Id = Guid.NewGuid(), OwnerId = customer.Id, OwnerType = typeof(Customer).FullName!, Note = mark + "-c1" },
        });

        var ofOrders = await session.Query<Attachment>().ForOwner<Order>().Where((a, m) => Sql.Like(a.Note, m), mark + "%").OrderBy(a => a.Note).ToListAsync();
        Assert.Equal([mark + "-o1", mark + "-o2"], ofOrders.Select(a => a.Note));
        var ofCustomers = await session.Query<Attachment>().ForOwner<Customer>().Where((a, m) => Sql.Like(a.Note, m), mark + "%").ToListAsync();
        Assert.Equal([customer.Id], ofCustomers.Select(a => a.OwnerId));
        transaction.Rollback();
    }
}
