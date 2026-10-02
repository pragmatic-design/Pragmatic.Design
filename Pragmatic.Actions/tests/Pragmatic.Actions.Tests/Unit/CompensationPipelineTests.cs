using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Compensation;
using Pragmatic.Actions.Invoker;
using Pragmatic.Result;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Unit;

/// <summary>
///     The compensation mechanism as it actually runs, not as it is generated.
///     <para>
///         The generator tests check that the right text comes out. These run the pipeline: an inner
///         action commits, the outer one fails, and the undo is expected to have happened — including
///         the two cases that decide whether the mechanism is worth anything, namely that it does
///         <b>not</b> fire on success, and that a failing undo reaches the caller instead of being
///         swallowed into the original error.
///     </para>
/// </summary>
public class CompensationPipelineTests
{
    // =========================================================================
    // Test doubles — an inner action that commits and knows how to undo itself
    // =========================================================================

    private sealed record Committed(int Rows);

    private sealed class TestError(string code) : IError
    {
        public string Code { get; } = code;
        public int StatusCode => 500;
        public string Title => Code;
    }

    private sealed class Ledger
    {
        public List<string> Events { get; } = [];
    }

    private sealed class InnerAction : DomainAction<Committed>
    {
        public override Task<Result<Committed, IError>> Execute(CancellationToken ct = default)
            => Task.FromResult(Result<Committed, IError>.Success(new Committed(5)));
    }

    private sealed class Compensator(Ledger ledger, bool fails) : ICompensates<Committed>
    {
        public Task<VoidResult<IError>> Undo(Committed committed, CancellationToken ct = default)
        {
            ledger.Events.Add($"undo:{committed.Rows}");
            return Task.FromResult(fails
                ? VoidResult<IError>.Failure(new TestError("UNDO_FAILED"))
                : VoidResult<IError>.Success());
        }
    }

    /// <summary>Stands in for the generated invoker: it commits, and it knows its compensator.</summary>
    private sealed class InnerInvoker(IServiceProvider serviceProvider, Ledger ledger)
        : DomainActionInvoker<InnerAction, Committed>(serviceProvider)
    {
        protected override void InjectDependencies(InnerAction action) { }

        protected override Task SaveChangesAsync(CancellationToken ct)
        {
            ledger.Events.Add("commit:inner");
            return Task.CompletedTask;
        }

        protected override bool IsCompensable => true;

        protected override async Task<VoidResult<IError>> CompensateAsync(Committed committed, CancellationToken ct)
        {
            var compensator = ServiceProvider.GetRequiredService<ICompensates<Committed>>();
            var undone = await compensator.Undo(committed, ct).ConfigureAwait(false);
            if (undone.IsFailure)
                return undone;

            ledger.Events.Add("commit:undo");
            return VoidResult<IError>.Success();
        }
    }

    /// <summary>The caller: writes in its own boundary, calls the inner one, then decides its fate.</summary>
    private sealed class OuterAction(IDomainActionInvoker<InnerAction, Committed> inner, bool succeeds)
        : DomainAction<string>
    {
        public override async Task<Result<string, IError>> Execute(CancellationToken ct = default)
        {
            var innerResult = await inner.InvokeAsync(new InnerAction(), ct).ConfigureAwait(false);
            if (innerResult.IsFailure)
                return Result<string, IError>.Failure(innerResult.Error);

            return succeeds
                ? Result<string, IError>.Success("done")
                : Result<string, IError>.Failure(new TestError("OUTER_FAILED"));
        }
    }

    private sealed class OuterInvoker(IServiceProvider serviceProvider, Ledger ledger)
        : DomainActionInvoker<OuterAction, string>(serviceProvider)
    {
        protected override void InjectDependencies(OuterAction action) { }

        protected override Task SaveChangesAsync(CancellationToken ct)
        {
            ledger.Events.Add("commit:outer");
            return Task.CompletedTask;
        }
    }

    private static (IServiceProvider Scope, Ledger Ledger) Build(bool undoFails = false)
    {
        var ledger = new Ledger();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(ledger);
        services.AddScoped<ICompensationScope, CompensationScope>();
        services.AddScoped<ICompensates<Committed>>(sp => new Compensator(ledger, undoFails));
        services.AddScoped<IDomainActionInvoker<InnerAction, Committed>, InnerInvoker>();
        services.AddScoped<OuterInvoker>();

        var scope = services.BuildServiceProvider().CreateScope();
        return (scope.ServiceProvider, ledger);
    }

    private static Task<Result<string, IError>> Run(IServiceProvider sp, bool outerSucceeds)
        => sp.GetRequiredService<OuterInvoker>()
            .InvokeAsync(new OuterAction(sp.GetRequiredService<IDomainActionInvoker<InnerAction, Committed>>(), outerSucceeds));

    // =========================================================================

    [Fact]
    public async Task OuterFails_TheInnerBoundarysCommittedWorkIsUndone()
    {
        var (sp, ledger) = Build();

        var result = await Run(sp, outerSucceeds: false);

        result.IsFailure.Should().BeTrue();
        ledger.Events.Should().Contain("undo:5", "the inner commit had to be undone");
        ledger.Events.Should().Contain("commit:undo", "an undo that is never saved is a no-op");
    }

    /// <summary>
    ///     The one that would let a broken mechanism look healthy: if compensation ran on success too,
    ///     every test above would still pass and every successful request would be silently reversed.
    /// </summary>
    [Fact]
    public async Task OuterSucceeds_NothingIsUndone()
    {
        var (sp, ledger) = Build();

        var result = await Run(sp, outerSucceeds: true);

        result.IsSuccess.Should().BeTrue();
        ledger.Events.Should().NotContain("undo:5");
        ledger.Events.Should().Contain("commit:outer");
    }

    /// <summary>Registration happens after the inner commit, so the undo can never precede it.</summary>
    [Fact]
    public async Task TheUndoRunsAfterTheCommitItUndoes()
    {
        var (sp, ledger) = Build();

        await Run(sp, outerSucceeds: false);

        ledger.Events.IndexOf("undo:5").Should().BeGreaterThan(
            ledger.Events.IndexOf("commit:inner"));
    }

    [Fact]
    public async Task UndoFails_TheCallerIsToldTheSystemIsInconsistent()
    {
        var (sp, _) = Build(undoFails: true);

        var result = await Run(sp, outerSucceeds: false);

        result.IsFailure.Should().BeTrue();
        var error = result.Error.Should().BeOfType<CompensationFailedError>().Subject;

        error.OriginalError.Code.Should().Be("OUTER_FAILED", "why the operation did not happen");
        error.CompensationError.Code.Should().Be("UNDO_FAILED", "why it could not be undone");
        error.UncompensatedAction.Should().Be(nameof(InnerAction));
    }

    /// <summary>
    ///     Without a registered scope the branches must be inert, not throw: an application that
    ///     declares no compensator registers nothing, and every invoker still runs.
    /// </summary>
    [Fact]
    public async Task WithoutACompensationScope_TheInvokerStillRuns()
    {
        var ledger = new Ledger();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(ledger);
        services.AddScoped<ICompensates<Committed>>(sp => new Compensator(ledger, fails: false));
        services.AddScoped<IDomainActionInvoker<InnerAction, Committed>, InnerInvoker>();
        services.AddScoped<OuterInvoker>();

        using var scope = services.BuildServiceProvider().CreateScope();

        var result = await Run(scope.ServiceProvider, outerSucceeds: false);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("OUTER_FAILED", "no scope means no compensation, not a wrapped error");
        ledger.Events.Should().NotContain("undo:5");
    }
}
