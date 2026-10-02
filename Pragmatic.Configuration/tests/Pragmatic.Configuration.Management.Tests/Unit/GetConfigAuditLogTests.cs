using Pragmatic.Testing.Assertions;
using Pragmatic.Audit;
using Pragmatic.Configuration.Management.Actions;
using Pragmatic.Identity;
using Pragmatic.Result.Http;

namespace Pragmatic.Configuration.Management.Tests.Unit;

/// <summary>
///     Reading configuration changes back, now that they live on the framework audit trail.
/// </summary>
public class GetConfigAuditLogTests
{
    private sealed class RecordingReader(params AuditEntry[] entries) : IAuditTrailReader
    {
        public AuditQuery? LastQuery { get; private set; }

        public Task<AuditPage> QueryAsync(AuditQuery query, CancellationToken ct = default)
        {
            LastQuery = query;
            return Task.FromResult(new AuditPage(entries, entries.Length));
        }

        public Task<IntegrityReport> VerifyAsync(DateTimeOffset from, DateTimeOffset until, CancellationToken ct = default)
            => throw new NotSupportedException("Reading the log must not require verifying the chain.");
    }

    private static AuditEntry Entry(string key, byte[]? previousValueHash = null) => new()
    {
        SegmentId = "2026-07-31T15",
        OccurredAt = DateTimeOffset.UnixEpoch,
        Category = AuditCategory.Configuration,
        Operation = "Configuration.KeyUpdated",
        TargetType = "config",
        TargetId = key,
        ActorRef = "admin",
        Outcome = AuditOutcome.Success,
        ValueHash = previousValueHash,
    };

    private static GetConfigAuditLog Build(
        IAuditTrailReader? reader, string keyPrefix = "", int limit = 50,
        string? tenantId = null, ICurrentUser? caller = null)
    {
        var action = new GetConfigAuditLog { KeyPrefix = keyPrefix, Limit = limit, TenantId = tenantId };
        return ActionFieldInjector.Inject(
            ActionFieldInjector.Inject(action, "_trail", reader), "_currentUser", caller ?? TestCaller.Operator);
    }

    /// <summary>
    ///     No tenant asks the trail for every tenant's changes: an operator may, an administrator of one
    ///     tenant may not — the log names keys and who changed them, which is another tenant's business.
    /// </summary>
    [Fact]
    public async Task ACallerOfOneTenant_CannotReadEveryTenantsLog()
    {
        var reader = new RecordingReader(Entry("App:Name"));

        var result = await Build(reader, caller: TestCaller.Of("acme")).Execute();

        result.Error.Should().BeOfType<ForbiddenError>();
        reader.LastQuery.Should().BeNull();
    }

    [Fact]
    public async Task ACallerOfOneTenant_ReadsItsOwnLog()
    {
        var reader = new RecordingReader(Entry("App:Name"));

        var result = await Build(reader, tenantId: "acme", caller: TestCaller.Of("acme")).Execute();

        result.IsSuccess.Should().BeTrue();
        reader.LastQuery!.TenantId.Should().Be("acme");
    }

    [Fact]
    public async Task WithATrail_ItMapsTheEntries()
    {
        var action = Build(new RecordingReader(Entry("App:Name"), Entry("App:Timeout")));

        var result = await action.Execute();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value[0].EntityKey.Should().Be("App:Name");
        result.Value[0].Operation.Should().Be("Configuration.KeyUpdated");
        result.Value[0].ChangedBy.Should().Be("admin");
    }

    [Fact]
    public async Task ItAsksTheTrailOnlyForConfigurationEntries()
    {
        // Without the category filter the action would report messages, security events and everything
        // else the trail holds as though they were configuration changes.
        var reader = new RecordingReader(Entry("App:Name"));

        await Build(reader, limit: 7).Execute();

        reader.LastQuery!.Category.Should().Be(AuditCategory.Configuration);
        reader.LastQuery.Limit.Should().Be(7);
    }

    [Fact]
    public async Task TheKeyPrefix_FiltersTheResult()
    {
        var action = Build(new RecordingReader(Entry("App:Name"), Entry("Db:Timeout")), keyPrefix: "App:");

        var result = await action.Execute();

        result.Value.Should().ContainSingle().Which.EntityKey.Should().Be("App:Name");
    }

    [Fact]
    public async Task ThePreviousValueIsAHash_AndTheValueItselfIsGone()
    {
        // The visible consequence of the move, pinned so nobody quietly puts a plaintext column back.
        byte[] hash = [1, 2, 3, 4];
        var action = Build(new RecordingReader(Entry("App:Name", hash)));

        var result = await action.Execute();

        result.Value[0].PreviousValueHash.Should().Equal(hash);
        typeof(ConfigAuditEntry).GetProperty("OldValue").Should().BeNull();
        typeof(ConfigAuditEntry).GetProperty("NewValue").Should().BeNull();
    }

    [Fact]
    public async Task AnEmptyTrail_ReturnsAnEmptyList()
    {
        var result = await Build(new RecordingReader()).Execute();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task WithoutATrail_ItSaysSoRatherThanReturningNothing()
    {
        // An empty list would read as "no configuration has ever changed", which is a different and
        // much more reassuring statement than "nothing is recording it".
        var result = await Build(null).Execute();

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<BadRequestError>();
    }
}
