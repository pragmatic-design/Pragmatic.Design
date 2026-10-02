using System.Security.Cryptography;
using System.Text;
using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Audit;
using Pragmatic.Configuration.Database.Dialects;

namespace Pragmatic.Configuration.Database.Tests.Unit;

/// <summary>
///     What reaches the audit trail when a configuration value changes.
/// </summary>
/// <remarks>
///     <para>
///         Rewritten when configuration moved onto the shared trail. The trail keeps a hash of the
///         previous value and never the value, so "was the plaintext masked?" became "was the hash
///         taken of a placeholder rather than the secret?" — the same question, one layer down.
///     </para>
///     <para>
///         Hashing the secret would have been the easy mistake here. An unsalted hash of a short or
///         guessable value is recoverable by trying candidates, and an append-only record kept for
///         years is the worst possible place to learn that.
///     </para>
/// </remarks>
public sealed class DatabaseConfigurationAuditMaskingTests : IDisposable
{
    private const string SensitiveKey = "Secret:ApiKey";
    private const string OrdinaryKey = "App:Timeout";
    private const string Placeholder = "(sensitive)";

    private readonly SqliteConnectionFactory _factory = new();
    private readonly ServiceProvider _sp;
    private readonly IConfigurationStore _store;

    public DatabaseConfigurationAuditMaskingTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDbConnectionFactory>(_factory);
        services.AddSingleton<ISensitiveKeyClassifier>(new StubClassifier(SensitiveKey));

        services.AddDatabaseConfigurationStore(opts =>
        {
            opts.Provider = DatabaseProvider.Sqlite;
            opts.AutoCreateSchema = true;
            opts.AuditUser = "test-user";
        });
        services.AddLogging();

        _sp = services.BuildServiceProvider();
        _store = _sp.GetRequiredService<IConfigurationStore>();
    }

    private static byte[] Sha256(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));

    /// <summary>Reads the trail directly: the point is what was persisted, not what an API reports.</summary>
    private async Task<List<(string Operation, string? TargetId, byte[]? ValueHash)>> TrailAsync()
    {
        var rows = new List<(string, string?, byte[]?)>();

        var connection = (SqliteConnection)_factory.CreateConnection();
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync();
            var cmd = connection.CreateCommand();
            cmd.CommandText = """SELECT "Operation","TargetId","ValueHash" FROM "__AuditEntries" ORDER BY "Seq" """;

            var reader = await cmd.ExecuteReaderAsync();
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync())
                    rows.Add((
                        reader.GetString(0),
                        reader.IsDBNull(1) ? null : reader.GetString(1),
                        reader.IsDBNull(2) ? null : (byte[])reader.GetValue(2)));
            }
        }

        return rows;
    }

    [Fact]
    public async Task CreatingASensitiveKey_RecordsNoValueAtAll()
    {
        await _store.SetAsync(SensitiveKey, "sk-live-1234567890");

        var trail = await TrailAsync();

        trail.Should().ContainSingle();
        trail[0].Operation.Should().Be("Configuration.KeyCreated");
        trail[0].TargetId.Should().Be(SensitiveKey);
        trail[0].ValueHash.Should().BeNull("there was no previous value to hash");
    }

    [Fact]
    public async Task UpdatingASensitiveKey_HashesThePlaceholder_NotTheSecret()
    {
        await _store.SetAsync(SensitiveKey, "old-secret");
        await _store.SetAsync(SensitiveKey, "new-secret");

        var trail = await TrailAsync();

        trail.Should().HaveCount(2);
        trail[1].Operation.Should().Be("Configuration.KeyUpdated");
        trail[1].ValueHash.Should().Equal(Sha256(Placeholder));
        trail[1].ValueHash.Should().NotEqual(Sha256("old-secret"),
            "an unsalted hash of a short secret is recoverable by trying candidates");
    }

    [Fact]
    public async Task UpdatingAnOrdinaryKey_HashesTheRealPreviousValue()
    {
        // The other half: for a value that is not secret, the hash is what makes the record useful --
        // anyone holding a candidate can be told whether it was the one in place.
        await _store.SetAsync(OrdinaryKey, "30");
        await _store.SetAsync(OrdinaryKey, "60");

        var trail = await TrailAsync();

        trail[1].ValueHash.Should().Equal(Sha256("30"));
    }

    [Fact]
    public async Task DeletingASensitiveKey_StillHashesThePlaceholder()
    {
        await _store.SetAsync(SensitiveKey, "sk-live-1234567890");
        await _store.DeleteAsync(SensitiveKey);

        var trail = await TrailAsync();

        trail.Should().HaveCount(2);
        trail[1].Operation.Should().Be("Configuration.KeyDeleted");
        trail[1].ValueHash.Should().Equal(Sha256(Placeholder));
    }

    [Fact]
    public async Task NoTrailEntryEverContainsThePlaintext()
    {
        // The blunt version of the promise, stated once against the bytes actually stored.
        await _store.SetAsync(SensitiveKey, "sk-live-1234567890");
        await _store.SetAsync(SensitiveKey, "sk-live-rotated");

        var secretBytes = Sha256("sk-live-1234567890");

        (await TrailAsync()).Should().OnlyContain(r => r.ValueHash == null || !r.ValueHash.SequenceEqual(secretBytes));
    }

    public void Dispose()
    {
        _sp.Dispose();
        _factory.Dispose();
    }

    private sealed class StubClassifier(string sensitiveKey) : ISensitiveKeyClassifier
    {
        public bool IsSensitive(string key) => key == sensitiveKey;
    }
}
