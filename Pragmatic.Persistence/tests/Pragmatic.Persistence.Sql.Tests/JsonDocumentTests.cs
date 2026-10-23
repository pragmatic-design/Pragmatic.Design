using System.Text.Json;
using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests.RawJson;

/// <summary>The workload's Documents table with its JSON column read as it is: a <see cref="JsonDocument" />, no type of its own.</summary>
[Entity]
[Table("Documents")]
public sealed class RawDocument : ISqlEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";

    [Convert<SqlJsonDocument>]
    [Column("Spec", Cast = "jsonb")]
    public JsonDocument Spec { get; set; } = JsonDocument.Parse("{}");
}

/// <summary>
///     <c>[Convert&lt;SqlJsonDocument&gt;]</c>: a JSON column written from a <see cref="JsonDocument" />'s raw text and read back parsed, the same
///     column a typed <c>SqlJson</c> property reads (the shapes agree both ways). In a transaction, rolled back.
/// </summary>
[Collection("databases")]
public class JsonDocumentTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task A_json_column_reads_and_writes_as_a_json_document(SqlDialect dialect)
    {
        if (dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null)
            return;
        using var session = databases.Session(dialect);
        using var transaction = session.BeginTransaction();

        // Written raw, read typed: the document the typed property sees.
        using var spec = JsonDocument.Parse("""{"Pages":42,"Tags":["a","b"],"Author":{"full_name":"Ada"}}""");
        var raw = new RawDocument { Id = Guid.NewGuid(), Name = "raw", Spec = spec };
        await session.InsertAsync(raw);
        var typed = await session.Query<Document>().FirstOrDefaultAsync((d, id) => d.Id == id, raw.Id);
        Assert.Equal((42, 2), (typed!.Spec.Pages, typed.Spec.Tags.Count));

        // Written typed, read raw.
        var document = new Document { Id = Guid.NewGuid(), Name = "typed", Spec = new DocumentSpec { Pages = 7, Tags = ["x"] } };
        await session.InsertAsync(document);
        var read = await session.Query<RawDocument>().FirstOrDefaultAsync((d, id) => d.Id == id, document.Id);
        using (read!.Spec)
        {
            Assert.Equal(7, read.Spec.RootElement.GetProperty("Pages").GetInt32());
            Assert.Equal("x", read.Spec.RootElement.GetProperty("Tags")[0].GetString());
        }

        transaction.Rollback();
    }
}
