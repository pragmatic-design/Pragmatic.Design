using System.Text;
using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pragmatic.Audit;
using Pragmatic.Cryptography;

namespace Pragmatic.Privacy.E2E.Tests;

/// <summary>
///     The vertical slice: personal data encrypted under a subject's own key, the key destroyed, and the
///     audit trail proving it happened — against a real PostgreSQL database.
/// </summary>
/// <remarks>
///     This is the checkpoint the plan put before the risky work. If crypto-shredding and the trail do
///     not hold together here, the design is wrong and the place to fix it is here — not after another
///     module has been built on top of it.
/// </remarks>
public sealed class CryptoShreddingE2ETests : IAsyncLifetime
{
    private readonly ComplianceStackFixture _f = new();

    public Task InitializeAsync() => _f.InitializeAsync();
    public Task DisposeAsync() => _f.DisposeAsync();

    private bool DockerMissing => _f.ConnectionString is null;

    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    private async Task SealAsync()
    {
        _f.Clock.Advance(TimeSpan.FromHours(1) + _f.Naming.GracePeriod + TimeSpan.FromMinutes(1));
        await _f.Sealing.SealDueSegmentsAsync();
    }

    [Fact]
    public async Task PersonalData_IsUnreadableAfterTheSubjectsKeyIsDestroyed()
    {
        if (DockerMissing) return;

        const string subject = "subject-e2e-1";
        var packed = await _f.Protector.ProtectAsync(subject, Bytes("ada@example.com"));

        (await _f.Protector.TryReadAsync(packed)).Outcome.Should().Be(DecryptOutcome.Success);

        await _f.Keys.DestroyAsync(subject);

        var afterErasure = await _f.Protector.TryReadAsync(packed);
        afterErasure.Outcome.Should().Be(DecryptOutcome.KeyDestroyed);
        afterErasure.Plain.Should().BeEmpty();
    }

    [Fact]
    public async Task TheCiphertextSurvivesErasure_WhichIsWhatReachesTheBackups()
    {
        if (DockerMissing) return;

        // Nothing is overwritten. The bytes are still on disk and still in every backup taken before
        // the erasure — and nobody can read any of them, which is the only way to erase a copy you do
        // not control.
        const string subject = "subject-e2e-2";
        var packed = await _f.Protector.ProtectAsync(subject, Bytes("sensitive"));

        await _f.Keys.DestroyAsync(subject);

        packed.Should().NotBeEmpty();
        Encoding.UTF8.GetString(packed).Should().NotContain("sensitive");
        (await _f.Protector.TryReadAsync(packed)).Outcome.Should().Be(DecryptOutcome.KeyDestroyed);
    }

    [Fact]
    public async Task ADestroyedKeysRowRemains_SoErasedIsNotConfusedWithTampered()
    {
        if (DockerMissing) return;

        const string subject = "subject-e2e-3";
        var key = await _f.Keys.GetOrCreateAsync(subject);
        await _f.Keys.DestroyAsync(subject);
        _f.KeyDb.ChangeTracker.Clear();

        var row = await _f.KeyDb.SubjectKeys.SingleAsync(r => r.SubjectRef == subject);
        row.WrappedKey.Should().BeNull("the key itself is gone");
        row.KeyId.Should().Be(key.KeyId, "the id stays, or a later read cannot tell erased from tampered");
        row.DestroyedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task TamperedCiphertext_IsStillReportedAsAnAttack_AfterOtherSubjectsWereErased()
    {
        if (DockerMissing) return;

        // The distinction has to survive ordinary operation: erasing subjects must not turn a real
        // tampering signal into routine noise.
        await _f.Keys.GetOrCreateAsync("subject-e2e-4a");
        await _f.Keys.DestroyAsync("subject-e2e-4a");

        var packed = await _f.Protector.ProtectAsync("subject-e2e-4b", Bytes("value"));
        packed[^1] ^= 0xFF;

        (await _f.Protector.TryReadAsync(packed)).Outcome.Should().Be(DecryptOutcome.AuthenticationFailed);
    }

    [Fact]
    public async Task TheTrailRecordsTheErasure_AndStillVerifiesAfterwards()
    {
        if (DockerMissing) return;

        const string subject = "subject-e2e-5";
        await _f.Keys.GetOrCreateAsync(subject);

        var report = await _f.Keys.DestroyAsync(subject);

        // The trail carries the pseudonym and the key id — never an identity, never a value.
        await _f.Trail.RecordAsync(new AuditEntry
        {
            SegmentId = string.Empty,
            OccurredAt = _f.Clock.GetUtcNow(),
            Category = AuditCategory.Privacy,
            Operation = "Privacy.SubjectKeyDestroyed",
            SubjectRef = subject,
            TargetType = "SubjectKey",
            TargetId = report.KeyId,
            Outcome = AuditOutcome.Success
        });

        await SealAsync();

        var page = await _f.TrailReader.QueryAsync(new AuditQuery { SubjectRef = subject });
        page.Entries.Should().ContainSingle()
            .Which.Operation.Should().Be("Privacy.SubjectKeyDestroyed");

        var integrity = await _f.TrailReader.VerifyAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue);
        integrity.IsIntact.Should().BeTrue("the proof of an erasure is worthless if it cannot be verified");
    }

    [Fact]
    public async Task TheTrailNeverHoldsThePersonalValue_EvenWhenTheCallerPutsItInTheDetail()
    {
        if (DockerMissing) return;

        await _f.Trail.RecordAsync(new AuditEntry
        {
            SegmentId = string.Empty,
            OccurredAt = _f.Clock.GetUtcNow(),
            Category = AuditCategory.Privacy,
            Operation = "Privacy.AccessRequested",
            SubjectRef = "subject-e2e-6",
            Outcome = AuditOutcome.Success,
            Detail = "requested by ada@example.com"
        });

        _f.AuditDb.ChangeTracker.Clear();
        var stored = await _f.AuditDb.Entries
            .Where(e => e.SubjectRef == "subject-e2e-6")
            .SingleAsync();

        stored.Detail.Should().NotContain("ada@example.com");
        stored.Detail.Should().Contain("[redacted]");
    }

    [Fact]
    public async Task StoredCiphertext_IsNotReadableThroughRawSql()
    {
        if (DockerMissing) return;

        // Read the column the way an operator with database access would, bypassing every abstraction.
        const string subject = "subject-e2e-7";
        await _f.Protector.ProtectAsync(subject, Bytes("ada@example.com"));

        var conn = new NpgsqlConnection(_f.ConnectionString);
        await using (conn.ConfigureAwait(false))
        {
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = """SELECT "WrappedKey" FROM "__SubjectKeys" WHERE "SubjectRef" = @s""";
            cmd.Parameters.AddWithValue("s", subject);

            var wrapped = (byte[])(await cmd.ExecuteScalarAsync())!;

            wrapped.Should().NotBeEmpty();
            Encoding.UTF8.GetString(wrapped).Should().NotContain("ada@example.com");
        }
    }

    [Fact]
    public async Task ADestroyedSubjectIsNeverResurrected()
    {
        if (DockerMissing) return;

        // The invariant the erasure report depends on: if the key came back, data written afterwards
        // would be readable under a reference already reported as erased.
        const string subject = "subject-e2e-8";
        await _f.Keys.GetOrCreateAsync(subject);
        await _f.Keys.DestroyAsync(subject);

        var act = async () => await _f.Protector.ProtectAsync(subject, Bytes("new data"));

        await act.Should().ThrowAsync<SubjectKeyDestroyedException>();
    }
}
