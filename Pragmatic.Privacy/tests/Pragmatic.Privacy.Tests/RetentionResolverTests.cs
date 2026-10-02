using Pragmatic.Testing.Assertions;

namespace Pragmatic.Privacy.Tests;

/// <summary>
///     Retention, and the observation the whole design rests on: the period belongs to the lawful basis,
///     not to the type.
/// </summary>
public sealed class RetentionResolverTests
{
    private const string Subject = "subject-ref-1";
    private static readonly DateTimeOffset Created = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static RetentionContext Context(string purpose = "marketing", TimeSpan? declared = null)
        => new(Subject, "Order", purpose, Created, declared);

    private static ConsentAwareRetentionResolver Resolver(bool granted)
        => new(new FakeConsents(granted), "v3", TimeProvider.System);

    [Fact]
    public async Task WhileConsentStands_TheRecordHasNoEndDate()
    {
        // The basis itself keeps the record alive; it ends when the subject says so, not on a schedule.
        var decision = await Resolver(granted: true).ResolveAsync(Context());

        decision.KeepUntil.Should().BeNull();
        decision.Basis.Should().Contain("consent").And.Contain("v3");
    }

    [Fact]
    public async Task WithoutConsent_TheDeclaredPeriodApplies()
    {
        var decision = await Resolver(granted: false)
            .ResolveAsync(Context(declared: TimeSpan.FromDays(3650)));

        decision.KeepUntil.Should().Be(Created.AddDays(3650));
    }

    [Fact]
    public async Task WithoutConsentAndWithoutADeclaredPeriod_NothingJustifiesKeepingIt()
    {
        // Inventing a default is how data outlives its purpose. Saying "nothing justifies this" is more
        // useful than a number nobody chose.
        var decision = await Resolver(granted: false).ResolveAsync(Context());

        decision.IsExpired(Created).Should().BeTrue();
        decision.Basis.Should().Contain("no active consent");
    }

    [Fact]
    public async Task WithdrawingConsent_MakesTheRecordEligible_ButOnlyIfNothingElseHoldsIt()
    {
        // The difference between a withdrawal and an erasure request: a record held on a second basis
        // survives the withdrawal.
        var withConsent = await Resolver(granted: true).ResolveAsync(Context());
        var withoutConsentButDeclared = await Resolver(granted: false)
            .ResolveAsync(Context(declared: TimeSpan.FromDays(3650)));

        withConsent.KeepUntil.Should().BeNull();
        withoutConsentButDeclared.IsExpired(Created.AddDays(1)).Should().BeFalse();
    }

    // =========================================================================
    // RetentionDecision
    // =========================================================================

    [Fact]
    public void IndefiniteRetention_RequiresABasis()
    {
        // Indefinite retention without a reason is indistinguishable from having forgotten to delete.
        var act = () => RetentionDecision.Indefinite("  ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AnIndefiniteDecision_NeverExpires()
        => RetentionDecision.Indefinite("fiscal obligation")
            .IsExpired(DateTimeOffset.MaxValue).Should().BeFalse();

    [Fact]
    public void APeriodRunsFromTheRecordsCreation_NotFromNow()
    {
        // Anchoring on "now" would silently extend every period each time it was recomputed.
        var decision = RetentionDecision.For(Context(), TimeSpan.FromDays(30), "declared");

        decision.KeepUntil.Should().Be(Created.AddDays(30));
    }

    [Fact]
    public void ADecisionIsNotExpiredBeforeItsDate()
    {
        var decision = RetentionDecision.For(Context(), TimeSpan.FromDays(30), "declared");

        decision.IsExpired(Created.AddDays(29)).Should().BeFalse();
        decision.IsExpired(Created.AddDays(30)).Should().BeTrue();
    }

    private sealed class FakeConsents(bool granted) : IConsentStore
    {
        public ValueTask GrantAsync(string s, string p, string v, string? src = null, CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public ValueTask<bool> WithdrawAsync(string s, string p, DateTimeOffset now, CancellationToken ct = default)
            => ValueTask.FromResult(true);

        public ValueTask<bool> IsGrantedAsync(string s, string p, string v, CancellationToken ct = default)
            => ValueTask.FromResult(granted);

        public ValueTask<IReadOnlyList<ConsentRecord>> GetHistoryAsync(string s, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<ConsentRecord>>([]);
    }
}
