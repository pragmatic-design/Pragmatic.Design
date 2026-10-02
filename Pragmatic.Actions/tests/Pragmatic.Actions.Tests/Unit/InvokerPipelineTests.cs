using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Invoker;
using Pragmatic.Actions.Pipeline;
using Pragmatic.Result;
using Xunit;

namespace Pragmatic.Actions.Tests.Unit;

/// <summary>
///     Tests for the DomainAction invoker pipeline execution.
///     Covers: basic execution, void actions, filter ordering, short-circuit, and exception propagation.
/// </summary>
public class InvokerPipelineTests
{
    // =========================================================================
    // Test doubles
    // =========================================================================

    private sealed class GetUserAction : DomainAction<string>
    {
        public required string UserId { get; init; }

        public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
        {
            return Task.FromResult(Result<string, IError>.Success($"User-{UserId}"));
        }
    }

    private sealed class GetUserInvoker(IServiceProvider serviceProvider)
        : DomainActionInvoker<GetUserAction, string>(serviceProvider)
    {
        protected override void InjectDependencies(GetUserAction action) { }
    }

    private sealed class FailingAction : DomainAction<string>
    {
        public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
        {
            return Task.FromResult(Result<string, IError>.Failure(new TestError("ACTION_FAILED")));
        }
    }

    private sealed class FailingActionInvoker(IServiceProvider serviceProvider)
        : DomainActionInvoker<FailingAction, string>(serviceProvider)
    {
        protected override void InjectDependencies(FailingAction action) { }
    }

    private sealed class ThrowingAction : DomainAction<string>
    {
        public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
        {
            throw new InvalidOperationException("Boom!");
        }
    }

    private sealed class ThrowingActionInvoker(IServiceProvider serviceProvider)
        : DomainActionInvoker<ThrowingAction, string>(serviceProvider)
    {
        protected override void InjectDependencies(ThrowingAction action) { }
    }

    private sealed class SendEmailAction : VoidDomainAction
    {
        public required string To { get; init; }

        public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
        {
            return Task.FromResult(VoidResult<IError>.Success());
        }
    }

    private sealed class SendEmailInvoker(IServiceProvider serviceProvider)
        : VoidDomainActionInvoker<SendEmailAction>(serviceProvider)
    {
        protected override void InjectDependencies(SendEmailAction action) { }
    }

    private sealed class FailingVoidAction : VoidDomainAction
    {
        public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
        {
            return Task.FromResult(VoidResult<IError>.Failure(new TestError("VOID_FAILED")));
        }
    }

    private sealed class FailingVoidActionInvoker(IServiceProvider serviceProvider)
        : VoidDomainActionInvoker<FailingVoidAction>(serviceProvider)
    {
        protected override void InjectDependencies(FailingVoidAction action) { }
    }

    private sealed class CancellableAction : DomainAction<string>
    {
        public override async Task<Result<string, IError>> Execute(CancellationToken ct = default)
        {
            await Task.Delay(5000, ct).ConfigureAwait(true);
            return Result<string, IError>.Success("completed");
        }
    }

    private sealed class CancellableActionInvoker(IServiceProvider serviceProvider)
        : DomainActionInvoker<CancellableAction, string>(serviceProvider)
    {
        protected override void InjectDependencies(CancellableAction action) { }
    }

    /// <summary>
    ///     Action whose invoker injects a dependency.
    /// </summary>
    private sealed class ActionWithDeps : DomainAction<string>
    {
        public string? InjectedValue { get; set; }

        public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
        {
            return Task.FromResult(Result<string, IError>.Success(InjectedValue ?? "none"));
        }
    }

    private sealed class ActionWithDepsInvoker(IServiceProvider serviceProvider, string depValue)
        : DomainActionInvoker<ActionWithDeps, string>(serviceProvider)
    {
        protected override void InjectDependencies(ActionWithDeps action)
        {
            action.InjectedValue = depValue;
        }
    }

    private sealed class TestError(string code) : IError
    {
        public string Code { get; } = code;
        public int StatusCode => 500;
        public string Title => Code;
    }

    /// <summary>
    ///     Records before/after calls for ordering verification.
    /// </summary>
    private sealed class TrackingFilter(int order, List<string> log) : IActionFilter
    {
        public int Order => order;

        public Task<VoidResult<IError>> BeforeExecuteAsync<TAction, TReturn>(TAction action, CancellationToken ct)
            where TAction : DomainAction<TReturn>
        {
            log.Add($"Before-{order}");
            return Task.FromResult(VoidResult<IError>.Success());
        }

        public Task AfterExecuteAsync<TAction, TReturn>(TAction action, Result<TReturn, IError> result, CancellationToken ct)
            where TAction : DomainAction<TReturn>
        {
            log.Add($"After-{order}");
            return Task.CompletedTask;
        }
    }

    private sealed class ShortCircuitFilter(int order) : IActionFilter
    {
        public int Order => order;

        public Task<VoidResult<IError>> BeforeExecuteAsync<TAction, TReturn>(TAction action, CancellationToken ct)
            where TAction : DomainAction<TReturn>
        {
            return Task.FromResult(VoidResult<IError>.Failure(new TestError("SHORT_CIRCUITED")));
        }

        public Task AfterExecuteAsync<TAction, TReturn>(TAction action, Result<TReturn, IError> result, CancellationToken ct)
            where TAction : DomainAction<TReturn>
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>A filter whose post-commit AfterExecute hook throws.</summary>
    private sealed class ThrowingAfterExecuteFilter : IActionFilter
    {
        public int Order => 500;

        public Task<VoidResult<IError>> BeforeExecuteAsync<TAction, TReturn>(TAction action, CancellationToken ct)
            where TAction : DomainAction<TReturn>
            => Task.FromResult(VoidResult<IError>.Success());

        public Task AfterExecuteAsync<TAction, TReturn>(TAction action, Result<TReturn, IError> result, CancellationToken ct)
            where TAction : DomainAction<TReturn>
            => throw new InvalidOperationException("post-commit boom");

        public Task AfterExecuteVoidAsync<TAction>(TAction action, VoidResult<IError> result, CancellationToken ct)
            where TAction : VoidDomainAction
            => throw new InvalidOperationException("post-commit boom");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static ServiceProvider BuildProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    // =========================================================================
    // Tests — Basic Execution
    // =========================================================================

    [Fact]
    public async Task InvokeAsync_SuccessfulAction_ReturnsSuccessResult()
    {
        using var provider = BuildProvider();
        var invoker = new GetUserInvoker(provider);
        var action = new GetUserAction { UserId = "42" };

        var result = await invoker.InvokeAsync(action);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("User-42");
    }

    [Fact]
    public async Task InvokeAsync_FailingAction_ReturnsFailureResult()
    {
        using var provider = BuildProvider();
        var invoker = new FailingActionInvoker(provider);
        var action = new FailingAction();

        var result = await invoker.InvokeAsync(action);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ACTION_FAILED");
    }

    [Fact]
    public async Task InvokeAsync_ThrowingAction_PropagatesException()
    {
        using var provider = BuildProvider();
        var invoker = new ThrowingActionInvoker(provider);
        var action = new ThrowingAction();

        var act = () => invoker.InvokeAsync(action);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Boom!");
    }

    [Fact]
    public async Task InvokeAsync_NullAction_ThrowsArgumentNullException()
    {
        using var provider = BuildProvider();
        var invoker = new GetUserInvoker(provider);

        var act = () => invoker.InvokeAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // =========================================================================
    // Tests — Void Actions
    // =========================================================================

    [Fact]
    public async Task InvokeAsync_VoidAction_ReturnsSuccess()
    {
        using var provider = BuildProvider();
        var invoker = new SendEmailInvoker(provider);
        var action = new SendEmailAction { To = "test@example.com" };

        var result = await invoker.InvokeAsync(action);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_FailingVoidAction_ReturnsFailure()
    {
        using var provider = BuildProvider();
        var invoker = new FailingVoidActionInvoker(provider);
        var action = new FailingVoidAction();

        var result = await invoker.InvokeAsync(action);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("VOID_FAILED");
    }

    // =========================================================================
    // Tests — Dependency Injection
    // =========================================================================

    [Fact]
    public async Task InvokeAsync_WithDependencies_InjectsDependenciesBeforeExecute()
    {
        using var provider = BuildProvider();
        var invoker = new ActionWithDepsInvoker(provider, "injected-value");
        var action = new ActionWithDeps();

        var result = await invoker.InvokeAsync(action);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("injected-value");
    }

    // =========================================================================
    // Tests — Cancellation
    // =========================================================================

    [Fact]
    public async Task InvokeAsync_CancelledToken_PropagatesCancellation()
    {
        using var provider = BuildProvider();
        var invoker = new CancellableActionInvoker(provider);
        var action = new CancellableAction();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => invoker.InvokeAsync(action, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // =========================================================================
    // Tests — Filter Pipeline
    // =========================================================================

    [Fact]
    public async Task InvokeAsync_FiltersExecuteInOrder_BeforeAscendingAfterDescending()
    {
        var log = new List<string>();

        using var provider = BuildProvider(services =>
        {
            services.AddSingleton<IActionFilter>(new TrackingFilter(100, log));
            services.AddSingleton<IActionFilter>(new TrackingFilter(200, log));
            services.AddSingleton<IActionFilter>(new TrackingFilter(300, log));
        });

        var invoker = new GetUserInvoker(provider);
        var action = new GetUserAction { UserId = "1" };

        await invoker.InvokeAsync(action);

        // Before should be ascending order
        log[0].Should().Be("Before-100");
        log[1].Should().Be("Before-200");
        log[2].Should().Be("Before-300");

        // After should be descending (reverse) order
        log[3].Should().Be("After-300");
        log[4].Should().Be("After-200");
        log[5].Should().Be("After-100");
    }

    [Fact]
    public async Task InvokeAsync_FilterShortCircuits_DoesNotExecuteAction()
    {
        using var provider = BuildProvider(services =>
        {
            services.AddSingleton<IActionFilter>(new ShortCircuitFilter(100));
        });

        var invoker = new GetUserInvoker(provider);
        var action = new GetUserAction { UserId = "1" };

        var result = await invoker.InvokeAsync(action);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SHORT_CIRCUITED");
    }

    [Fact]
    public async Task InvokeAsync_FilterShortCircuits_LaterFiltersNotExecuted()
    {
        var log = new List<string>();

        using var provider = BuildProvider(services =>
        {
            services.AddSingleton<IActionFilter>(new TrackingFilter(50, log));
            services.AddSingleton<IActionFilter>(new ShortCircuitFilter(100));
            services.AddSingleton<IActionFilter>(new TrackingFilter(200, log));
        });

        var invoker = new GetUserInvoker(provider);
        var action = new GetUserAction { UserId = "1" };

        await invoker.InvokeAsync(action);

        // Only the filter before the short-circuit should have run
        log.Should().ContainSingle().Which.Should().Be("Before-50");
    }

    [Fact]
    public async Task InvokeAsync_NoFilters_ExecutesActionDirectly()
    {
        using var provider = BuildProvider();
        var invoker = new GetUserInvoker(provider);
        var action = new GetUserAction { UserId = "direct" };

        var result = await invoker.InvokeAsync(action);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("User-direct");
    }

    [Fact]
    public async Task InvokeAsync_TypedFilter_AppliesOnlyToMatchingAction()
    {
        var log = new List<string>();

        using var provider = BuildProvider(services =>
        {
            services.AddSingleton<IActionFilter<GetUserAction, string>>(
                new TypedTrackingFilter(50, log));
        });

        var invoker = new GetUserInvoker(provider);
        var action = new GetUserAction { UserId = "1" };

        await invoker.InvokeAsync(action);

        log.Should().Contain("TypedBefore-50");
        log.Should().Contain("TypedAfter-50");
    }

    private sealed class TypedTrackingFilter(int order, List<string> log)
        : IActionFilter<GetUserAction, string>
    {
        public int Order => order;

        public Task<VoidResult<IError>> BeforeExecuteAsync(GetUserAction action, CancellationToken ct)
        {
            log.Add($"TypedBefore-{order}");
            return Task.FromResult(VoidResult<IError>.Success());
        }

        public Task AfterExecuteAsync(GetUserAction action, Result<string, IError> result, CancellationToken ct)
        {
            log.Add($"TypedAfter-{order}");
            return Task.CompletedTask;
        }
    }

    // =========================================================================
    // Test doubles — SaveChangesAsync tracking
    // =========================================================================

    /// <summary>
    ///     Invoker that tracks whether SaveChangesAsync was called.
    ///     Simulates a generated invoker that has a boundary-keyed IUnitOfWork.
    /// </summary>
    private sealed class SaveTrackingInvoker(IServiceProvider sp, Action onSave)
        : DomainActionInvoker<GetUserAction, string>(sp)
    {
        protected override void InjectDependencies(GetUserAction action) { }

        protected override Task SaveChangesAsync(CancellationToken ct)
        {
            onSave();
            return Task.CompletedTask;
        }
    }

    private sealed class FailingSaveTrackingInvoker(IServiceProvider sp, Action onSave)
        : DomainActionInvoker<FailingAction, string>(sp)
    {
        protected override void InjectDependencies(FailingAction action) { }

        protected override Task SaveChangesAsync(CancellationToken ct)
        {
            onSave();
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingSaveTrackingInvoker(IServiceProvider sp, Action onSave)
        : DomainActionInvoker<ThrowingAction, string>(sp)
    {
        protected override void InjectDependencies(ThrowingAction action) { }

        protected override Task SaveChangesAsync(CancellationToken ct)
        {
            onSave();
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     Invoker that tracks whether PrepareActionAsync (the [LoadEntity] hook) ran.
    ///     Used to prove the entity load happens AFTER the authorization filters, so a denied
    ///     caller never triggers the load.
    /// </summary>
    private sealed class PrepareTrackingInvoker(IServiceProvider sp, Action onPrepare)
        : DomainActionInvoker<GetUserAction, string>(sp)
    {
        protected override void InjectDependencies(GetUserAction action) { }

        protected override Task<IError?> PrepareActionAsync(GetUserAction action, CancellationToken ct)
        {
            onPrepare();
            return Task.FromResult<IError?>(null);
        }
    }

    /// <summary>
    ///     Void invoker that logs the SaveChangesAsync call into a shared ordering log.
    /// </summary>
    private sealed class OrderTrackingVoidInvoker(IServiceProvider sp, List<string> log)
        : VoidDomainActionInvoker<SendEmailAction>(sp)
    {
        protected override void InjectDependencies(SendEmailAction action) { }

        protected override Task SaveChangesAsync(CancellationToken ct)
        {
            log.Add("Save");
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     Filter that logs the void AfterExecute hook into a shared ordering log.
    /// </summary>
    private sealed class VoidAfterTrackingFilter(List<string> log) : IActionFilter
    {
        public int Order => 0;

        public Task<VoidResult<IError>> BeforeExecuteAsync<TAction, TReturn>(TAction action, CancellationToken ct)
            where TAction : DomainAction<TReturn>
            => Task.FromResult(VoidResult<IError>.Success());

        public Task AfterExecuteAsync<TAction, TReturn>(TAction action, Result<TReturn, IError> result, CancellationToken ct)
            where TAction : DomainAction<TReturn>
            => Task.CompletedTask;

        public Task AfterExecuteVoidAsync<TAction>(TAction action, VoidResult<IError> result, CancellationToken ct)
            where TAction : VoidDomainAction
        {
            log.Add("After");
            return Task.CompletedTask;
        }
    }

    // =========================================================================
    // Tests — SaveChangesAsync pipeline behavior
    // =========================================================================

    [Fact]
    public async Task InvokeAsync_VoidAction_SavesBeforeAfterExecuteHooks()
    {
        // A void action must commit BEFORE its AfterExecute hooks run, like result
        // actions — otherwise a post-hook side effect could fire even though persistence
        // later fails. Assert the save is logged before the hook.
        var log = new List<string>();

        using var provider = BuildProvider(services =>
            services.AddSingleton<IActionFilter>(new VoidAfterTrackingFilter(log)));

        var invoker = new OrderTrackingVoidInvoker(provider, log);

        await invoker.InvokeAsync(new SendEmailAction { To = "user@example.com" });

        log.Should().Equal("Save", "After");
    }

    [Fact]
    public async Task InvokeAsync_SuccessfulAction_CallsSaveChangesAsync()
    {
        var saveChangesCalled = false;

        using var provider = BuildProvider();
        var invoker = new SaveTrackingInvoker(provider, () => saveChangesCalled = true);
        var action = new GetUserAction { UserId = "1" };

        await invoker.InvokeAsync(action);

        saveChangesCalled.Should().BeTrue(
            because: "persistence must be flushed after a successful action");
    }

    [Fact]
    public async Task InvokeAsync_FailingAction_DoesNotCallSaveChangesAsync()
    {
        var saveChangesCalled = false;

        using var provider = BuildProvider();
        var invoker = new FailingSaveTrackingInvoker(provider, () => saveChangesCalled = true);
        var action = new FailingAction();

        await invoker.InvokeAsync(action);

        saveChangesCalled.Should().BeFalse(
            because: "failed actions must not persist partial state");
    }

    [Fact]
    public async Task InvokeAsync_ThrowingAction_DoesNotCallSaveChangesAsync()
    {
        var saveChangesCalled = false;

        using var provider = BuildProvider();
        var invoker = new ThrowingSaveTrackingInvoker(provider, () => saveChangesCalled = true);
        var action = new ThrowingAction();

        try
        { await invoker.InvokeAsync(action); }
        catch { /* expected */ }

        saveChangesCalled.Should().BeFalse(
            because: "exceptions must not trigger persistence");
    }

    [Fact]
    public async Task InvokeAsync_ShortCircuitedByFilter_DoesNotCallSaveChangesAsync()
    {
        var saveChangesCalled = false;

        using var provider = BuildProvider(services =>
            services.AddSingleton<IActionFilter>(new ShortCircuitFilter(100)));

        var invoker = new SaveTrackingInvoker(provider, () => saveChangesCalled = true);
        var action = new GetUserAction { UserId = "1" };

        await invoker.InvokeAsync(action);

        saveChangesCalled.Should().BeFalse(
            because: "short-circuited actions (filter returns failure) must not persist");
    }

    // =========================================================================
    // Tests — entity load runs AFTER authorization filters
    // =========================================================================

    [Fact]
    public async Task InvokeAsync_FilterShortCircuits_DoesNotPrepareAction()
    {
        // A short-circuiting authorization filter must deny BEFORE PrepareActionAsync
        // (the [LoadEntity] hook) runs — otherwise an unauthorized caller triggers a DB load
        // and can probe entity existence via 404-vs-403.
        var prepared = false;

        using var provider = BuildProvider(services =>
            services.AddSingleton<IActionFilter>(new ShortCircuitFilter(100)));

        var invoker = new PrepareTrackingInvoker(provider, () => prepared = true);

        var result = await invoker.InvokeAsync(new GetUserAction { UserId = "1" });

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SHORT_CIRCUITED");
        prepared.Should().BeFalse(
            because: "the entity load must not run when authorization denies the caller");
    }

    [Fact]
    public async Task InvokeAsync_FiltersPass_PreparesActionBeforeExecute()
    {
        // Complement to the above: when authorization passes, PrepareActionAsync still runs.
        var prepared = false;

        using var provider = BuildProvider();
        var invoker = new PrepareTrackingInvoker(provider, () => prepared = true);

        var result = await invoker.InvokeAsync(new GetUserAction { UserId = "1" });

        result.IsSuccess.Should().BeTrue();
        prepared.Should().BeTrue(
            because: "with no denying filter, the entity load runs before execution");
    }

    // =========================================================================
    // Tests — post-commit side-effect failure does not flip the outcome
    // =========================================================================

    [Fact]
    public async Task InvokeAsync_AfterExecuteHookThrows_StillReturnsCommittedSuccess()
    {
        // The action executed and committed; an AfterExecute (post-commit) hook throwing must NOT
        // turn the committed success into a thrown failure — otherwise the caller retries and the
        // operation double-runs. The exception is logged and swallowed; the result stays success.
        using var provider = BuildProvider(services =>
            services.AddSingleton<IActionFilter>(new ThrowingAfterExecuteFilter()));
        var invoker = new GetUserInvoker(provider);

        var result = await invoker.InvokeAsync(new GetUserAction { UserId = "1" });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("User-1");
    }

    [Fact]
    public async Task InvokeAsync_VoidAfterExecuteHookThrows_StillReturnsCommittedSuccess()
    {
        // The same rule for the void pipeline.
        using var provider = BuildProvider(services =>
            services.AddSingleton<IActionFilter>(new ThrowingAfterExecuteFilter()));
        var invoker = new SendEmailInvoker(provider);

        var result = await invoker.InvokeAsync(new SendEmailAction { To = "user@example.com" });

        result.IsSuccess.Should().BeTrue();
    }
}
