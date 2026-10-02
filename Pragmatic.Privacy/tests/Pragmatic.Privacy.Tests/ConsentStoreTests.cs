using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Privacy.EFCore;

namespace Pragmatic.Privacy.Tests;

/// <summary>
///     Consent, and the two facts a boolean flag destroys: what the subject was told, and when they
///     stopped agreeing.
/// </summary>
public sealed class ConsentStoreTests : IDisposable
{
    private const string Subject = "subject-ref-1";
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly PrivacyDbContext _db;
    private readonly EfCoreConsentStore _store;

    public ConsentStoreTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        _db = new PrivacyDbContext(
            new DbContextOptionsBuilder<PrivacyDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();

        _store = new EfCoreConsentStore(_db, TimeProvider.System);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task Granted_ConsentCoversThatPurposeUnderThatNotice()
    {
        await _store.GrantAsync(Subject, "marketing", "v3");

        (await _store.IsGrantedAsync(Subject, "marketing", "v3")).Should().BeTrue();
    }

    [Fact]
    public async Task ConsentUnderAnOlderNotice_DoesNotCoverANewerOne()
    {
        // The whole reason the version is stored: "they consented" is not defensible on its own —
        // what matters is what they were told at the time.
        await _store.GrantAsync(Subject, "marketing", "v3");

        (await _store.IsGrantedAsync(Subject, "marketing", "v4")).Should().BeFalse();
    }

    [Fact]
    public async Task ConsentToOnePurpose_DoesNotCoverAnother()
    {
        // Bundled consent is not consent.
        await _store.GrantAsync(Subject, "marketing", "v3");

        (await _store.IsGrantedAsync(Subject, "profiling", "v3")).Should().BeFalse();
    }

    [Fact]
    public async Task NeverGranted_IsNotGranted()
        => (await _store.IsGrantedAsync(Subject, "marketing", "v3")).Should().BeFalse();

    // =========================================================================
    // Withdrawal
    // =========================================================================

    [Fact]
    public async Task Withdrawn_ConsentNoLongerCovers()
    {
        await _store.GrantAsync(Subject, "marketing", "v3");

        (await _store.WithdrawAsync(Subject, "marketing", Now)).Should().BeTrue();

        (await _store.IsGrantedAsync(Subject, "marketing", "v3")).Should().BeFalse();
    }

    [Fact]
    public async Task Withdrawal_KeepsTheRecordAsEvidence()
    {
        // A deleted consent record and a consent that was never given look identical, and one of them
        // is a breach.
        await _store.GrantAsync(Subject, "marketing", "v3", source: "signup form");
        await _store.WithdrawAsync(Subject, "marketing", Now);

        var history = await _store.GetHistoryAsync(Subject);

        history.Should().ContainSingle();
        history[0].WithdrawnAt.Should().Be(Now);
        history[0].GrantedAt.Should().NotBe(default);
        history[0].Source.Should().Be("signup form");
    }

    [Fact]
    public async Task Withdrawal_CoversEveryNoticeVersionOfThatPurpose()
    {
        // A subject withdrawing means "stop", not "stop under the version I happen to be looking at".
        await _store.GrantAsync(Subject, "marketing", "v2");
        await _store.GrantAsync(Subject, "marketing", "v3");

        await _store.WithdrawAsync(Subject, "marketing", Now);

        (await _store.IsGrantedAsync(Subject, "marketing", "v2")).Should().BeFalse();
        (await _store.IsGrantedAsync(Subject, "marketing", "v3")).Should().BeFalse();
    }

    [Fact]
    public async Task Withdrawal_LeavesOtherPurposesAlone()
    {
        await _store.GrantAsync(Subject, "marketing", "v3");
        await _store.GrantAsync(Subject, "analytics", "v3");

        await _store.WithdrawAsync(Subject, "marketing", Now);

        (await _store.IsGrantedAsync(Subject, "analytics", "v3")).Should().BeTrue();
    }

    [Fact]
    public async Task WithdrawingWhatWasNeverGranted_ReportsThatThereWasNothingToDo()
        => (await _store.WithdrawAsync(Subject, "marketing", Now)).Should().BeFalse();

    [Fact]
    public async Task WithdrawingTwice_ReportsThatThereWasNothingLeft()
    {
        await _store.GrantAsync(Subject, "marketing", "v3");
        await _store.WithdrawAsync(Subject, "marketing", Now);

        (await _store.WithdrawAsync(Subject, "marketing", Now)).Should().BeFalse();
    }

    [Fact]
    public async Task ConsentGivenAgainAfterWithdrawal_StandsOnceMore()
    {
        await _store.GrantAsync(Subject, "marketing", "v3");
        await _store.WithdrawAsync(Subject, "marketing", Now);

        await _store.GrantAsync(Subject, "marketing", "v3");

        (await _store.IsGrantedAsync(Subject, "marketing", "v3")).Should().BeTrue();
        (await _store.GetHistoryAsync(Subject)).Should().ContainSingle("it is the same fact, reactivated");
    }

    [Fact]
    public async Task History_IncludesWithdrawnConsent_BecauseAnAccessRequestNeedsIt()
    {
        await _store.GrantAsync(Subject, "marketing", "v3");
        await _store.WithdrawAsync(Subject, "marketing", Now);
        await _store.GrantAsync(Subject, "analytics", "v3");

        var history = await _store.GetHistoryAsync(Subject);

        history.Should().HaveCount(2);
        history.Should().Contain(c => !c.IsActive);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GrantingWithoutAPurpose_IsRefused(string purpose)
    {
        var act = async () => await _store.GrantAsync(Subject, purpose, "v3");

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
