using System.Text;
using System.Text.Json;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Privacy.Tests;

/// <summary>
///     Access and portability: what the subject is entitled to see, and in a form they can take away.
/// </summary>
public sealed class SubjectAccessServiceTests
{
    private const string Subject = "subject-ref-1";
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    private static SubjectAccessService Service(
        IEnumerable<IPersonalDataSource> sources, IReadOnlyList<ConsentRecord>? consents = null)
        => new(sources, new FakeConsents(consents ?? []), new FixedClock(Now));

    [Fact]
    public async Task Collect_GathersEverySourceUnderItsOwnCategory()
    {
        var export = await Service([
            new FakeSource("Orders", [new Dictionary<string, object?> { ["Total"] = 42 }]),
            new FakeSource("Profile", [new Dictionary<string, object?> { ["Email"] = "ada@example.com" }])
        ]).CollectAsync(Subject);

        export.Categories.Should().ContainKeys("Orders", "Profile");
        export.RecordCount.Should().Be(2);
    }

    [Fact]
    public async Task Collect_SkipsSourcesThatHoldNothing()
    {
        // An empty category in the export tells the subject nothing and invites them to wonder what
        // was left out.
        var export = await Service([
            new FakeSource("Orders", [new Dictionary<string, object?> { ["Total"] = 42 }]),
            new FakeSource("Empty", [])
        ]).CollectAsync(Subject);

        export.Categories.Should().ContainKey("Orders").And.NotContainKey("Empty");
    }

    [Fact]
    public async Task Collect_RefusesTwoSourcesClaimingTheSameCategory()
    {
        // Silently overwriting would leave the export short by exactly the records nobody noticed.
        var act = async () => await Service([
            new FakeSource("Orders", [new Dictionary<string, object?> { ["A"] = 1 }]),
            new FakeSource("Orders", [new Dictionary<string, object?> { ["B"] = 2 }])
        ]).CollectAsync(Subject);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Orders*");
    }

    [Fact]
    public async Task Collect_IncludesConsentHistory_WithdrawnOnesToo()
    {
        // The record of a withdrawal is itself data about the subject.
        var consents = new List<ConsentRecord>
        {
            new() { SubjectRef = Subject, Purpose = "marketing", NoticeVersion = "v3", WithdrawnAt = Now }
        };

        var export = await Service([], consents).CollectAsync(Subject);

        export.Consents.Should().ContainSingle().Which.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Collect_ForAnUnknownSubject_ReportsThatNothingIsHeld()
    {
        var export = await Service([]).CollectAsync(Subject);

        export.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task Collect_StampsWhenTheSnapshotWasTaken()
    {
        // An export is a snapshot, not a live view: without the time, the subject cannot tell what it
        // was true of.
        (await Service([]).CollectAsync(Subject)).GeneratedAt.Should().Be(Now);
    }

    [Fact]
    public async Task Collect_RejectsABlankSubject()
    {
        var act = async () => await Service([]).CollectAsync("   ");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // =========================================================================
    // Portability format
    // =========================================================================

    [Fact]
    public async Task JsonFormat_IsParseableAndCarriesTheData()
    {
        var export = await Service([
            new FakeSource("Orders", [new Dictionary<string, object?> { ["Total"] = 42 }])
        ]).CollectAsync(Subject);

        var bytes = await new JsonPortabilityFormatter().FormatAsync(export);
        using var parsed = JsonDocument.Parse(bytes);

        parsed.RootElement.GetProperty("subject").GetString().Should().Be(Subject);
        parsed.RootElement.GetProperty("data").GetProperty("Orders").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task JsonFormat_LeavesNonLatinTextReadable()
    {
        // Escaping to \uXXXX produces a file that is technically correct and unreadable to the person
        // it is for — which is not portability.
        var export = await Service([
            new FakeSource("Profile", [new Dictionary<string, object?> { ["Name"] = "Ada Città 日本語" }])
        ]).CollectAsync(Subject);

        var json = Encoding.UTF8.GetString(await new JsonPortabilityFormatter().FormatAsync(export));

        json.Should().Contain("Ada Città 日本語");
    }

    [Fact]
    public async Task JsonFormat_DeclaresHowItShouldBeServed()
    {
        var formatter = new JsonPortabilityFormatter();

        formatter.ContentType.Should().Be("application/json");
        formatter.FileExtension.Should().Be("json");
        await Task.CompletedTask;
    }

    private sealed class FakeSource(
        string category, IReadOnlyList<IReadOnlyDictionary<string, object?>> records) : IPersonalDataSource
    {
        public string Category => category;

        public ValueTask<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CollectAsync(
            string subjectRef, CancellationToken ct = default) => ValueTask.FromResult(records);
    }

    private sealed class FakeConsents(IReadOnlyList<ConsentRecord> history) : IConsentStore
    {
        public ValueTask GrantAsync(string s, string p, string v, string? src = null, CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public ValueTask<bool> WithdrawAsync(string s, string p, DateTimeOffset now, CancellationToken ct = default)
            => ValueTask.FromResult(true);

        public ValueTask<bool> IsGrantedAsync(string s, string p, string v, CancellationToken ct = default)
            => ValueTask.FromResult(false);

        public ValueTask<IReadOnlyList<ConsentRecord>> GetHistoryAsync(string s, CancellationToken ct = default)
            => ValueTask.FromResult(history);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
