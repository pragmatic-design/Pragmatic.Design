using Pragmatic.Testing.Assertions;

namespace Pragmatic.Privacy.Tests;

/// <summary>
///     The orchestrator exists for one reason: erasure spans entities, files and keys, and the
///     <b>order</b> decides whether an interruption leaves something recoverable or something lost.
/// </summary>
public sealed class ErasureOrchestratorTests
{
    private const string Subject = "subject-ref-1";

    [Fact]
    public async Task Erase_RunsEveryStepAndSumsWhatWasErased()
    {
        var orchestrator = new ErasureOrchestrator(
            [new FakeStep("orders", 10, erased: 3), new FakeStep("notes", 20, erased: 2)],
            new FakeRegistry());

        var outcome = await orchestrator.EraseAsync(Subject);

        outcome.ErasedCount.Should().Be(5);
        outcome.IsTotal.Should().BeTrue();
    }

    [Fact]
    public async Task Erase_RunsStepsInAscendingOrder()
    {
        // Stored files must be reclaimed before the rows that point at them: delete the row first and
        // crash, and the file is orphaned with nobody able to find it again.
        var order = new List<string>();
        var orchestrator = new ErasureOrchestrator(
            [
                new FakeStep("rows", 20, onErase: () => order.Add("rows")),
                new FakeStep("files", 10, onErase: () => order.Add("files"))
            ],
            new FakeRegistry());

        await orchestrator.EraseAsync(Subject);

        order.Should().Equal("files", "rows");
    }

    [Fact]
    public async Task Erase_WithNothingRetained_DestroysTheKeyAndBreaksTheLink()
    {
        var registry = new FakeRegistry();
        var destroyed = false;
        var orchestrator = new ErasureOrchestrator([new FakeStep("orders", 10, erased: 1)], registry);

        var outcome = await orchestrator.EraseAsync(Subject, (_, _) =>
        {
            destroyed = true;
            return ValueTask.FromResult(true);
        });

        destroyed.Should().BeTrue();
        outcome.KeyDestroyed.Should().BeTrue();
        outcome.IdentityForgotten.Should().BeTrue();
        registry.Forgotten.Should().Contain(Subject);
    }

    [Fact]
    public async Task Erase_UnderALegalHold_KeepsTheKeyAndTheLink()
    {
        // Destroying the key while a hold is in force would make unreadable the very data the hold
        // protects — an erasure defeating the obligation that was supposed to override it.
        var registry = new FakeRegistry();
        var destroyed = false;
        var holds = new FakeHolds([new RetainedItem("billing records", "litigation hold #4471")]);
        var orchestrator = new ErasureOrchestrator([new FakeStep("orders", 10, erased: 1)], registry, holds);

        var outcome = await orchestrator.EraseAsync(Subject, (_, _) =>
        {
            destroyed = true;
            return ValueTask.FromResult(true);
        });

        destroyed.Should().BeFalse();
        outcome.KeyDestroyed.Should().BeFalse();
        outcome.IdentityForgotten.Should().BeFalse();
        registry.Forgotten.Should().BeEmpty("a held subject must stay identifiable");
    }

    [Fact]
    public async Task Erase_UnderAHold_ReportsWhatIsKeptAndWhy()
    {
        var holds = new FakeHolds([new RetainedItem("billing records", "litigation hold #4471")]);
        var orchestrator = new ErasureOrchestrator([new FakeStep("orders", 10, erased: 1)], new FakeRegistry(), holds);

        var outcome = await orchestrator.EraseAsync(Subject);

        outcome.IsTotal.Should().BeFalse();
        outcome.Retained.Should().ContainSingle()
            .Which.Reason.Should().Be("litigation hold #4471");
    }

    [Fact]
    public async Task Erase_StillErasesWhatItCan_WhileAHoldCoversSomethingElse()
    {
        // A partial erasure is a complete outcome: everything not under an obligation still goes.
        var holds = new FakeHolds([new RetainedItem("invoices", "fiscal obligation")]);
        var orchestrator = new ErasureOrchestrator([new FakeStep("orders", 10, erased: 7)], new FakeRegistry(), holds);

        var outcome = await orchestrator.EraseAsync(Subject);

        outcome.ErasedCount.Should().Be(7);
        outcome.Retained.Should().ContainSingle();
    }

    [Fact]
    public async Task Erase_ChecksHoldsBeforeTouchingAnything()
    {
        // Discovering a live dispute halfway through is discovering it too late: the earlier steps have
        // already committed.
        var sequence = new List<string>();
        var holds = new FakeHolds([], onQuery: () => sequence.Add("holds"));
        var orchestrator = new ErasureOrchestrator(
            [new FakeStep("orders", 10, onErase: () => sequence.Add("step"))], new FakeRegistry(), holds);

        await orchestrator.EraseAsync(Subject);

        sequence.Should().Equal("holds", "step");
    }

    [Fact]
    public async Task Erase_WithoutAKeyDestroyer_StillBreaksTheLink()
    {
        // Not every deployment encrypts per subject; the erasure must still complete.
        var registry = new FakeRegistry();
        var orchestrator = new ErasureOrchestrator([new FakeStep("orders", 10, erased: 1)], registry);

        var outcome = await orchestrator.EraseAsync(Subject);

        outcome.KeyDestroyed.Should().BeFalse();
        outcome.IdentityForgotten.Should().BeTrue();
    }

    /// <summary>
    ///     A step that says its data is erased by destroying a key, with nobody to destroy it, refuses.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ The generated plan for a <c>DestroyKey</c> field <b>skips</b> it — the field is not
    ///         cleared, because clearing is not how it is erased. So without a destroyer the data is
    ///         neither cleared nor made unreadable, and an erasure answering <c>KeyDestroyed:
    ///         false</c> while calling itself complete would be wrong. A subject-rights answer that is
    ///         wrong is worse than one that fails.
    ///     </para>
    ///     <para>
    ///         Compare <see cref="Erase_WithoutAKeyDestroyer_StillBreaksTheLink" />, which stays green:
    ///         a deployment that encrypts nothing per subject declares nothing, and completes.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task Erase_WhenAStepNeedsAKeyDestroyed_AndThereIsNone_Refuses()
    {
        var orchestrator = new ErasureOrchestrator(
            [new FakeStep("notes", 10, erased: 1, requiresKeyDestruction: true)], new FakeRegistry());

        var act = async () => await orchestrator.EraseAsync(Subject);

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Message.Should().Contain("notes", "the message names the step that cannot be honoured");
    }

    /// <summary>
    ///     And it refuses before anything is touched.
    /// </summary>
    /// <remarks>
    ///     The same reason holds as are checked first: a refusal halfway through has already committed
    ///     the earlier steps, and the subject is left half-erased with no way to tell.
    /// </remarks>
    [Fact]
    public async Task Erase_WhenAStepNeedsAKeyDestroyed_RefusesBeforeTouchingAnything()
    {
        var touched = new List<string>();
        var orchestrator = new ErasureOrchestrator(
            [
                new FakeStep("files", 10, onErase: () => touched.Add("files")),
                new FakeStep("notes", 20, requiresKeyDestruction: true, onErase: () => touched.Add("notes"))
            ],
            new FakeRegistry());

        var act = async () => await orchestrator.EraseAsync(Subject);

        await act.Should().ThrowAsync<InvalidOperationException>();
        touched.Should().BeEmpty("a refusal after the first step has already committed it");
    }

    /// <summary>The control on the refusal: with a destroyer, the same erasure completes.</summary>
    [Fact]
    public async Task Erase_WhenAStepNeedsAKeyDestroyed_AndOneIsSupplied_Completes()
    {
        var destroyed = false;
        var orchestrator = new ErasureOrchestrator(
            [new FakeStep("notes", 10, erased: 1, requiresKeyDestruction: true)], new FakeRegistry());

        var outcome = await orchestrator.EraseAsync(Subject, (_, _) =>
        {
            destroyed = true;
            return ValueTask.FromResult(true);
        });

        destroyed.Should().BeTrue();
        outcome.KeyDestroyed.Should().BeTrue();
        outcome.ErasedCount.Should().Be(1);
    }

    /// <summary>
    ///     The canonical case: an identifier kept in the clear for the audit, and a note erased by
    ///     destroying the key. Keeping the identifier must not keep the key — the note would stay readable.
    /// </summary>
    [Fact]
    public async Task Erase_AFieldRetainedInTheClear_DoesNotKeepTheKey()
    {
        var registry = new FakeRegistry();
        var orchestrator = new ErasureOrchestrator(
            [
                new FakeStep("notes", 10, erased: 1, requiresKeyDestruction: true,
                    retained: [new RetainedItem("Customer.TaxCode", "fiscal records, 10 years", RequiresKey: false)]),
            ],
            registry);

        var outcome = await orchestrator.EraseAsync(Subject, (_, _) => ValueTask.FromResult(true));

        outcome.KeyDestroyed.Should().BeTrue("nothing kept needs the key to be read");
        outcome.Retained.Should().ContainSingle();
        outcome.IdentityForgotten.Should().BeFalse("what is kept still belongs to someone the register can name");
    }

    /// <summary>The control: a value kept under the subject's key keeps the key.</summary>
    [Fact]
    public async Task Erase_AFieldRetainedUnderTheKey_KeepsTheKey()
    {
        var destroyed = false;
        var orchestrator = new ErasureOrchestrator(
            [
                new FakeStep("notes", 10, erased: 1, requiresKeyDestruction: true,
                    retained: [new RetainedItem("Customer.Iban", "anti-money-laundering, 5 years", RequiresKey: true)]),
            ],
            new FakeRegistry());

        var outcome = await orchestrator.EraseAsync(Subject, (_, _) =>
        {
            destroyed = true;
            return ValueTask.FromResult(true);
        });

        destroyed.Should().BeFalse("destroying it would erase what the obligation keeps");
        outcome.KeyDestroyed.Should().BeFalse();
    }

    [Fact]
    public async Task Erase_WithNoSteps_IsStillAValidErasure()
    {
        var outcome = await new ErasureOrchestrator([], new FakeRegistry()).EraseAsync(Subject);

        outcome.ErasedCount.Should().Be(0);
        outcome.IsTotal.Should().BeTrue();
        outcome.IdentityForgotten.Should().BeTrue();
    }

    [Fact]
    public async Task Erase_RejectsABlankSubject()
    {
        var act = async () => await new ErasureOrchestrator([], new FakeRegistry()).EraseAsync("  ");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private sealed class FakeStep(
        string name,
        int order,
        int erased = 0,
        Action? onErase = null,
        bool requiresKeyDestruction = false,
        IReadOnlyList<RetainedItem>? retained = null) : IErasureStep
    {
        public string Name => name;
        public int Order => order;
        public bool RequiresKeyDestruction => requiresKeyDestruction;

        public ValueTask<ErasureStepResult> EraseAsync(string subjectRef, CancellationToken ct = default)
        {
            onErase?.Invoke();
            return ValueTask.FromResult(retained is null
                ? ErasureStepResult.Erased(erased)
                : new ErasureStepResult(erased, retained));
        }
    }

    private sealed class FakeRegistry : ISubjectRegistry
    {
        public List<string> Forgotten { get; } = [];

        public ValueTask<string> GetOrCreateReferenceAsync(string t, string i, CancellationToken ct = default)
            => ValueTask.FromResult(Subject);

        public ValueTask<string?> FindReferenceAsync(string t, string i, CancellationToken ct = default)
            => ValueTask.FromResult<string?>(Subject);

        public ValueTask<string?> ResolveIdentityAsync(string r, CancellationToken ct = default)
            => ValueTask.FromResult<string?>(null);

        public ValueTask<bool> ForgetAsync(string subjectRef, CancellationToken ct = default)
        {
            Forgotten.Add(subjectRef);
            return ValueTask.FromResult(true);
        }
    }

    private sealed class FakeHolds(IReadOnlyList<RetainedItem> holds, Action? onQuery = null) : ILegalHoldStore
    {
        public ValueTask<IReadOnlyList<RetainedItem>> GetActiveHoldsAsync(
            string subjectRef, CancellationToken ct = default)
        {
            onQuery?.Invoke();
            return ValueTask.FromResult(holds);
        }
    }
}
