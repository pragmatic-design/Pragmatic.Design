using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Pragmatic.Persistence.Workload;
using Xunit;
using Xunit.Abstractions;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     Vectors: a <c>[Vector(16)]</c> float[] column (pgvector, SQL Server 2025 VECTOR, JSON text on SQLite), written and read back, and
///     the distances in predicates and orderings: a k-NN is an OrderBy on <c>Sql.CosineDistance</c> and a Take. On PostgreSQL over 100.000
///     rows with an HNSW index (the roadmap's setpoint), the approximate top ten against the exact one.
/// </summary>
[Collection("databases")]
public class VectorTests(TestDatabases databases, ITestOutputHelper output)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Vectors_are_written_read_back_and_compared_by_distance(SqlDialect dialect)
    {
        if (Skip(dialect) || dialect == SqlDialect.MySql)
            return; // MySQL has no vector type and no .NET functions on the connection: the distances would be the application's stored functions (not defined here)
        using var session = Session(dialect);
        await session.Query<Embedding>().ExecuteDeleteAsync();
        var random = new Random(42);
        var rows = Enumerable.Range(0, 200).Select(i => new Embedding { Id = Guid.NewGuid(), Label = "v" + i, Vector = RandomVector(random) }).ToList();
        await session.InsertManyAsync(rows);

        // Read back as written.
        var first = await session.Query<Embedding>().FirstOrDefaultAsync((e, id) => e.Id == id, rows[0].Id);
        Assert.NotNull(first);
        Assert.Equal(rows[0].Vector, first.Vector);

        // The exact k-NN by cosine distance against the same computed in memory; the distance itself in a projection; a radius in a predicate.
        var query = RandomVector(random);
        var nearest = await session.Query<Embedding>().OrderBy((e, q) => Sql.CosineDistance(e.Vector, q), query).Take(5).ToListAsync();
        var expected = rows.OrderBy(r => Cosine(r.Vector, query)).Take(5).Select(r => r.Id).ToList();
        Assert.Equal(expected, nearest.Select(e => e.Id));
        var within = await session.Query<Embedding>().CountAsync((e, q) => Sql.L2Distance(e.Vector, q) < 1.5, query);
        Assert.Equal(rows.Count(r => L2(r.Vector, query) < 1.5), within);
        var farthestByDot = await session.Query<Embedding>().OrderByDescending((e, q) => Sql.InnerProduct(e.Vector, q), query).Take(1).Select(e => e.Id).FirstOrDefaultAsync();
        Assert.Equal(rows.OrderByDescending(r => Dot(r.Vector, query)).First().Id, farthestByDot);
    }

    [Fact]
    public async Task A_knn_over_a_hundred_thousand_vectors_uses_the_hnsw_index_on_postgresql()
    {
        if (databases.DockerUnavailable is not null)
            return;
        using var session = Session(SqlDialect.PostgreSql);
        await session.Query<Embedding>().ExecuteDeleteAsync();
        var random = new Random(7);
        var all = new List<Embedding>(100_000);
        for (var i = 0; i < 100_000; i++)
            all.Add(new Embedding { Id = Guid.NewGuid(), Label = "e" + i, Vector = RandomVector(random) });
        var loading = Stopwatch.StartNew();
        await session.InsertManyAsync(all);
        output.WriteLine($"100.000 vectors inserted in {loading.Elapsed.TotalSeconds:0.0} s");

        var query = RandomVector(random);
        var exact = all.OrderBy(e => Cosine(e.Vector, query)).Take(10).Select(e => e.Id).ToHashSet();
        var timer = Stopwatch.StartNew();
        var nearest = await session.Query<Embedding>().OrderBy((e, q) => Sql.CosineDistance(e.Vector, q), query).Take(10).ToListAsync();
        timer.Stop();
        output.WriteLine($"k-NN top 10 over 100.000 rows (HNSW): {timer.Elapsed.TotalMilliseconds:0.0} ms");
        Assert.Equal(10, nearest.Count);
        Assert.True(nearest.Count(e => exact.Contains(e.Id)) >= 8, "the HNSW index is approximate: at least 8 of the exact top 10 are expected"); // the index is approximate: recall, not identity
        Assert.True(timer.ElapsedMilliseconds < 2000, $"a k-NN over 100.000 rows took {timer.ElapsedMilliseconds} ms: the index is not used");
        await session.Query<Embedding>().ExecuteDeleteAsync();
        await RunAsync(session, "VACUUM ANALYZE \"Embeddings\""); // the HNSW index keeps the deleted tuples until a vacuum: a scan after the delete would meet them first and answer fewer rows
    }

    private static async Task RunAsync(SqlSession session, string sql)
    {
        using var command = session.Connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private SqlSession Session(SqlDialect dialect)
    {
        if (dialect == SqlDialect.SqlServer)
            Pragmatic.Persistence.Sql.SqlClient.SqlClientVectors.Register(); // SqlClient answers a SqlVector<float>: the provider package's reader
        var connection = databases.Open(dialect);
        if (dialect == SqlDialect.PostgreSql)
        {
            // pgvector 0.8: an HNSW scan that meets dead or filtered-out tuples goes on until it has the rows asked for, instead of answering fewer.
            using var iterative = connection.CreateCommand();
            iterative.CommandText = "SET hnsw.iterative_scan = relaxed_order";
            iterative.ExecuteNonQuery();
        }

        if (connection is SqliteConnection sqlite)
        {
            // SQLite has no vector functions: the library's, registered on the connection (an emulation the generator reports, PRAG3012).
            sqlite.CreateFunction<string, string, double>("vec_cosine", SqlVectors.Cosine);
            sqlite.CreateFunction<string, string, double>("vec_l2", SqlVectors.L2);
            sqlite.CreateFunction<string, string, double>("vec_dot", SqlVectors.Dot);
        }

        return new SqlSession(connection, dialect);
    }

    private static float[] RandomVector(Random random)
    {
        var v = new float[Embedding.Dimensions];
        for (var i = 0; i < v.Length; i++)
            v[i] = (float)(random.NextDouble() * 2 - 1);
        return v;
    }

    private static double Cosine(float[] a, float[] b) => SqlVectors.Cosine(SqlVectors.Format(a), SqlVectors.Format(b));

    private static double L2(float[] a, float[] b) => SqlVectors.L2(SqlVectors.Format(a), SqlVectors.Format(b));

    private static double Dot(float[] a, float[] b) => SqlVectors.Dot(SqlVectors.Format(a), SqlVectors.Format(b));
}
