using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Options;

namespace Pragmatic.Privacy.Tests;

/// <summary>
///     The Article 30 register: derived from the code where it can be, and honest about the rest.
/// </summary>
public sealed class ProcessingRegisterTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    private static ProcessingActivity Activity(
        string type, string[]? categories = null, bool isSubject = false, RetainedItem[]? retained = null)
        => new(type, categories ?? ["Contact"], isSubject,
            new Dictionary<string, string> { ["Email"] = "Null" }, retained ?? []);

    private static ProcessingRegisterBuilder Builder(
        IEnumerable<ProcessingActivity> activities, Action<ProcessingRegisterOptions>? configure = null)
    {
        var options = new ProcessingRegisterOptions
        {
            ControllerName = "Acme Srl",
            ControllerContact = "dpo@acme.example"
        };
        configure?.Invoke(options);

        return new ProcessingRegisterBuilder(
            [new FakeSource(activities.ToList())], Options.Create(options), new FixedClock(Now));
    }

    [Fact]
    public async Task TheRegisterCarriesTheControllerAndTheTime()
    {
        var register = await Builder([Activity("App.Customer")]).BuildAsync();

        register.ControllerName.Should().Be("Acme Srl");
        register.ControllerContact.Should().Be("dpo@acme.example");
        register.GeneratedAt.Should().Be(Now);
    }

    [Fact]
    public async Task AnActivityWithoutADeclaredPurpose_IsListedAsIncomplete()
    {
        // Reported rather than invented: a register that fills in its own purposes reads as
        // authoritative and is not.
        var register = await Builder([Activity("App.Customer")]).BuildAsync();

        register.IsComplete.Should().BeFalse();
        register.Incomplete.Should().ContainSingle().Which.EntityType.Should().Be("App.Customer");
    }

    [Fact]
    public async Task ADeclaredPurpose_CompletesTheEntry()
    {
        var register = await Builder([Activity("App.Customer")],
            o => o.Purposes["App.Customer"] = "managing customer accounts").BuildAsync();

        register.IsComplete.Should().BeTrue();
        register.Activities[0].Purpose.Should().Be("managing customer accounts");
    }

    [Fact]
    public async Task TheSameTypeReportedTwice_AppearsOnce()
    {
        // A shared type can be reported by more than one assembly's metadata. Counting it twice would
        // suggest processing that does not happen.
        var register = await Builder([Activity("App.Customer"), Activity("App.Customer")]).BuildAsync();

        register.Activities.Should().ContainSingle();
    }

    [Fact]
    public async Task ActivitiesAreSorted_SoTwoBuildsCanBeCompared()
    {
        // A register that reorders itself cannot be diffed, and one nobody can diff is one nobody reviews.
        var register = await Builder([Activity("App.Zeta"), Activity("App.Alpha")]).BuildAsync();

        register.Activities.Select(a => a.EntityType).Should().Equal("App.Alpha", "App.Zeta");
    }

    [Fact]
    public async Task TheRegisterListsEveryCategoryProcessed_Deduplicated()
    {
        var register = await Builder([
            Activity("App.Customer", ["Contact", "Identity"]),
            Activity("App.Order", ["Financial", "Contact"])
        ]).BuildAsync();

        register.AllCategories.Should().Equal("Contact", "Financial", "Identity");
    }

    [Fact]
    public async Task SpecialCategories_AreFlagged()
    {
        // They carry stricter obligations, so whether any exist is the first thing to know.
        var register = await Builder([Activity("App.Patient", ["Special"])]).BuildAsync();

        register.ProcessesSpecialCategories.Should().BeTrue();
    }

    [Fact]
    public async Task WithoutSpecialCategories_TheFlagIsClear()
        => (await Builder([Activity("App.Customer", ["Contact"])]).BuildAsync())
            .ProcessesSpecialCategories.Should().BeFalse();

    [Fact]
    public async Task RetainedFieldsAndTheirReasons_ReachTheRegister()
    {
        // What is kept despite an erasure request, and on what grounds, is exactly what an authority
        // asks about first.
        var register = await Builder([
            Activity("App.Invoice", retained: [new RetainedItem("Total", "Art. 2220 c.c.")])
        ]).BuildAsync();

        register.Activities[0].Retained.Should().ContainSingle()
            .Which.Reason.Should().Be("Art. 2220 c.c.");
    }

    [Fact]
    public async Task AnEmptySystem_ProducesAnEmptyButValidRegister()
    {
        var register = await Builder([]).BuildAsync();

        register.Activities.Should().BeEmpty();
        register.IsComplete.Should().BeTrue("nothing is processed, so nothing is undeclared");
    }

    // ------------------------------------------------------------------ operations

    private static ProcessingOperation Operation(
        string type, ProcessingAccess access = ProcessingAccess.Read, string entity = "App.Customer",
        bool recorded = false)
        => new(type, access, entity, ["Contact"], "/customers", recorded);

    private static ProcessingRegisterBuilder OperationBuilder(
        IEnumerable<ProcessingOperation> operations, Action<ProcessingRegisterOptions>? configure = null)
    {
        var options = new ProcessingRegisterOptions
        {
            ControllerName = "Acme Srl",
            ControllerContact = "dpo@acme.example"
        };
        configure?.Invoke(options);

        return new ProcessingRegisterBuilder(
            [new FakeSource([Activity("App.Customer")], operations.ToList())],
            Options.Create(options), new FixedClock(Now));
    }

    /// <summary>
    ///     The operations a source reports reach the register.
    /// </summary>
    /// <remarks>
    ///     The assertion that stops this from being a list nothing collects: the generated source builds
    ///     one per assembly, and unless the builder asks for it the whole path ends in an unread field.
    /// </remarks>
    [Fact]
    public async Task TheOperationsASourceReports_ReachTheRegister()
    {
        var register = await OperationBuilder([Operation("App.ListCustomersQuery")]).BuildAsync();

        register.ProcessingOperations.Should().ContainSingle()
            .Which.OperationType.Should().Be("App.ListCustomersQuery");
    }

    [Fact]
    public async Task AnOperationWithoutAPurpose_IsListedAsIncomplete()
    {
        var register = await OperationBuilder([Operation("App.ListCustomersQuery")]).BuildAsync();

        register.IncompleteOperations.Should().ContainSingle()
            .Which.OperationType.Should().Be("App.ListCustomersQuery");
    }

    /// <summary>
    ///     Two operations on the same entity take two purposes.
    /// </summary>
    /// <remarks>
    ///     The reason the operation half exists at all. Keyed by entity, "why do we hold Customer" has one
    ///     answer for a search and another for an export, and the register could only ever record one of
    ///     them.
    /// </remarks>
    [Fact]
    public async Task TwoOperationsOnTheSameEntity_CarryTheirOwnPurposes()
    {
        var register = await OperationBuilder(
            [Operation("App.ListCustomersQuery"), Operation("App.ExportCustomersQuery")],
            o =>
            {
                o.OperationPurposes["App.ListCustomersQuery"] = "servicing the account";
                o.OperationPurposes["App.ExportCustomersQuery"] = "a subject access request";
            }).BuildAsync();

        register.IncompleteOperations.Should().BeEmpty();
        register.ProcessingOperations.Select(o => o.Purpose)
            .Should().Equal("a subject access request", "servicing the account");
    }

    [Fact]
    public async Task TheReadsAreSeparable_BecauseNothingElseRecordsThem()
    {
        // Writes are recorded by the audit trail on every path; reads are recorded nowhere. Which reads
        // exist is the list that makes deciding about them possible.
        var register = await OperationBuilder([
            Operation("App.ListCustomersQuery"),
            Operation("App.UpdateCustomerMutation", ProcessingAccess.Write)
        ]).BuildAsync();

        register.Reads.Should().ContainSingle()
            .Which.OperationType.Should().Be("App.ListCustomersQuery");
    }

    /// <summary>
    ///     A read nothing records is listed, so leaving it unrecorded stays a visible decision.
    /// </summary>
    /// <remarks>
    ///     Not a defect list: recording every read would bury the ones that matter. What it buys is that
    ///     "who looked at this person's record" gets decided while reading this, rather than after
    ///     somebody asks — by then the evidence either was written or does not exist.
    /// </remarks>
    [Fact]
    public async Task AReadNothingRecords_IsListedSeparatelyFromOneThatIsRecorded()
    {
        var register = await OperationBuilder([
            Operation("App.ListCustomersQuery"),
            Operation("App.ExportCustomersQuery", recorded: true)
        ]).BuildAsync();

        register.Reads.Should().HaveCount(2);
        register.UnrecordedReads.Should().ContainSingle()
            .Which.OperationType.Should().Be("App.ListCustomersQuery");
    }

    [Fact]
    public async Task AWriteIsNeverAnUnrecordedRead()
        // The audit trail records writes on every path, so they are outside this question entirely.
        => (await OperationBuilder([Operation("App.UpdateCustomerMutation", ProcessingAccess.Write)])
            .BuildAsync()).UnrecordedReads.Should().BeEmpty();

    [Fact]
    public async Task TheSameOperationReportedTwice_AppearsOnce()
        => (await OperationBuilder([Operation("App.ListCustomersQuery"), Operation("App.ListCustomersQuery")])
            .BuildAsync()).ProcessingOperations.Should().ContainSingle();

    /// <summary>
    ///     An operation that touches two entities is listed against each: the generated source emits one row
    ///     per entity, and keying the deduplication on the operation alone kept the first and dropped the
    ///     rest — the understatement the generator's own comment warns against.
    /// </summary>
    [Fact]
    public async Task AnOperationOnTwoEntities_IsListedAgainstEach()
        => (await OperationBuilder([
                Operation("App.ImportAction", ProcessingAccess.Write, "App.Customer"),
                Operation("App.ImportAction", ProcessingAccess.Write, "App.Supplier")
            ]).BuildAsync()).ProcessingOperations.Select(o => o.EntityType)
            .Should().BeEquivalentTo(["App.Customer", "App.Supplier"]);

    [Fact]
    public async Task ASourceThatReportsNoOperations_LeavesTheListEmptyRatherThanNull()
        => (await Builder([Activity("App.Customer")]).BuildAsync())
            .ProcessingOperations.Should().BeEmpty();

    private sealed class FakeSource(
        IReadOnlyList<ProcessingActivity> activities,
        IReadOnlyList<ProcessingOperation>? operations = null) : IProcessingActivitySource
    {
        public ValueTask<IReadOnlyList<ProcessingActivity>> GetActivitiesAsync(CancellationToken ct = default)
            => ValueTask.FromResult(activities);

        public ValueTask<IReadOnlyList<ProcessingOperation>> GetOperationsAsync(CancellationToken ct = default)
            => ValueTask.FromResult(operations ?? []);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
