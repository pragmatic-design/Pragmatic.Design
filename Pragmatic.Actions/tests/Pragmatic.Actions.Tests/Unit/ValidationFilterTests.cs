using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Actions.Pipeline;
using Pragmatic.Actions.Pipeline.Filters;
using Pragmatic.Result;
using Pragmatic.Validation;
using Pragmatic.Validation.Types;
using Xunit;

namespace Pragmatic.Actions.Tests.Unit;

/// <summary>
///     Tests for the ValidationFilter behavior.
///     Covers: sync validation, async validation, [NoValidation], [Validate] modes, error combining.
/// </summary>
public class ValidationFilterTests
{
    // =========================================================================
    // Test doubles — Actions
    // =========================================================================

    /// <summary>
    ///     Action implementing ISyncValidator that fails validation.
    /// </summary>
    private sealed class InvalidSyncAction : DomainAction<string>, ISyncValidator
    {
        public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
            => Task.FromResult(Result<string, IError>.Success("ok"));

        public ValidationError Validate()
            => ValidationError.For("Email", "validation.required");
    }

    /// <summary>
    ///     Action implementing ISyncValidator that passes validation.
    /// </summary>
    private sealed class ValidSyncAction : DomainAction<string>, ISyncValidator
    {
        public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
            => Task.FromResult(Result<string, IError>.Success("ok"));

        public ValidationError Validate() => ValidationError.Valid;
    }

    /// <summary>
    ///     Action with [NoValidation] — should skip all validation.
    ///     Implements IActionValidationMetadata as the SG would generate.
    /// </summary>
    [NoValidation]
    private sealed class NoValidationAction : DomainAction<string>, ISyncValidator, IActionValidationMetadata
    {
        public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
            => Task.FromResult(Result<string, IError>.Success("ok"));

        public ValidationError Validate()
            => ValidationError.For("Field", "validation.should_not_run");

        bool IActionValidationMetadata.HasNoValidation => true;
        bool IActionValidationMetadata.RunSyncValidation => true;
        bool IActionValidationMetadata.RunAsyncValidation => false;
        ValidationError? IActionValidationMetadata.ValidateNestedSync() => null;
        Task<ValidationError?> IActionValidationMetadata.ValidateNestedAsync(IServiceProvider sp, CancellationToken ct)
            => Task.FromResult<ValidationError?>(null);
    }

    /// <summary>
    ///     Action with [Validate] — enables both sync + async.
    ///     Implements IActionValidationMetadata as the SG would generate.
    /// </summary>
    [Validate]
    private sealed class SyncAndAsyncAction : DomainAction<string>, ISyncValidator, IActionValidationMetadata
    {
        public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
            => Task.FromResult(Result<string, IError>.Success("ok"));

        public ValidationError Validate()
            => ValidationError.For("Name", "validation.sync_error");

        bool IActionValidationMetadata.HasNoValidation => false;
        bool IActionValidationMetadata.RunSyncValidation => true;
        bool IActionValidationMetadata.RunAsyncValidation => true;
        ValidationError? IActionValidationMetadata.ValidateNestedSync() => null;
        Task<ValidationError?> IActionValidationMetadata.ValidateNestedAsync(IServiceProvider sp, CancellationToken ct)
            => Task.FromResult<ValidationError?>(null);
    }

    /// <summary>
    ///     Action with [Validate(AsyncOnly = true)] — skips sync, runs async.
    ///     Implements IActionValidationMetadata as the SG would generate.
    /// </summary>
    [Validate(AsyncOnly = true)]
    private sealed class AsyncOnlyAction : DomainAction<string>, ISyncValidator, IActionValidationMetadata
    {
        public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
            => Task.FromResult(Result<string, IError>.Success("ok"));

        // This sync validator should NOT run because AsyncOnly = true
        public ValidationError Validate()
            => ValidationError.For("SyncField", "validation.sync_should_not_run");

        bool IActionValidationMetadata.HasNoValidation => false;
        bool IActionValidationMetadata.RunSyncValidation => false;
        bool IActionValidationMetadata.RunAsyncValidation => true;
        ValidationError? IActionValidationMetadata.ValidateNestedSync() => null;
        Task<ValidationError?> IActionValidationMetadata.ValidateNestedAsync(IServiceProvider sp, CancellationToken ct)
            => Task.FromResult<ValidationError?>(null);
    }

    /// <summary>
    ///     Action with [Validate] and both sync + async errors to test combination.
    ///     Implements IActionValidationMetadata as the SG would generate.
    /// </summary>
    [Validate]
    private sealed class CombinedErrorAction : DomainAction<string>, ISyncValidator, IActionValidationMetadata
    {
        public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
            => Task.FromResult(Result<string, IError>.Success("ok"));

        public ValidationError Validate()
            => ValidationError.For("SyncField", "validation.sync_error");

        bool IActionValidationMetadata.HasNoValidation => false;
        bool IActionValidationMetadata.RunSyncValidation => true;
        bool IActionValidationMetadata.RunAsyncValidation => true;
        ValidationError? IActionValidationMetadata.ValidateNestedSync() => null;
        Task<ValidationError?> IActionValidationMetadata.ValidateNestedAsync(IServiceProvider sp, CancellationToken ct)
            => Task.FromResult<ValidationError?>(null);
    }

    /// <summary>
    ///     Plain action without ISyncValidator — no validator registered.
    /// </summary>
    private sealed class PlainAction : DomainAction<string>
    {
        public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
            => Task.FromResult(Result<string, IError>.Success("ok"));
    }

    // =========================================================================
    // Test doubles — Async Validators
    // =========================================================================

    private sealed class FailingAsyncValidator<TAction> : IAsyncValidator<TAction>
    {
        public Task<ValidationError> ValidateAsync(TAction instance, CancellationToken ct = default)
            => Task.FromResult(ValidationError.For("AsyncField", "validation.async_error"));
    }

    private sealed class PassingAsyncValidator<TAction> : IAsyncValidator<TAction>
    {
        public Task<ValidationError> ValidateAsync(TAction instance, CancellationToken ct = default)
            => Task.FromResult(ValidationError.Valid);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static (ValidationFilter Filter, ServiceProvider Provider) CreateFilter(
        Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        configure?.Invoke(services);
        var provider = services.BuildServiceProvider();
        var filter = new ValidationFilter(provider, provider.GetRequiredService<ILogger<ValidationFilter>>());
        return (filter, provider);
    }

    // =========================================================================
    // Tests — Filter order
    // =========================================================================

    [Fact]
    public void Order_Returns100()
    {
        var (filter, provider) = CreateFilter();
        using var _ = provider;

        filter.Order.Should().Be(FilterOrder.Validation);
        filter.Order.Should().Be(100);
    }

    // =========================================================================
    // Tests — Sync Validation
    // =========================================================================

    [Fact]
    public async Task BeforeExecute_SyncValidatorFails_ReturnsValidationError()
    {
        var (filter, provider) = CreateFilter();
        using var _ = provider;
        var action = new InvalidSyncAction();

        var result = await filter.BeforeExecuteAsync<InvalidSyncAction, string>(action, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ValidationError>();
        var validationError = (ValidationError)result.Error;
        validationError.Count.Should().Be(1);
        validationError.Issues[0].PropertyPath.Should().Be("Email");
    }

    [Fact]
    public async Task BeforeExecute_SyncValidatorPasses_ReturnsSuccess()
    {
        var (filter, provider) = CreateFilter();
        using var _ = provider;
        var action = new ValidSyncAction();

        var result = await filter.BeforeExecuteAsync<ValidSyncAction, string>(action, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    // =========================================================================
    // Tests — [NoValidation]
    // =========================================================================

    [Fact]
    public async Task BeforeExecute_NoValidationAttribute_SkipsValidation()
    {
        var (filter, provider) = CreateFilter();
        using var _ = provider;
        var action = new NoValidationAction();

        var result = await filter.BeforeExecuteAsync<NoValidationAction, string>(action, CancellationToken.None);

        // Even though ISyncValidator.Validate() would return errors, [NoValidation] skips it
        result.IsSuccess.Should().BeTrue();
    }

    // =========================================================================
    // Tests — [Validate(AsyncOnly = true)]
    // =========================================================================

    [Fact]
    public async Task BeforeExecute_AsyncOnlyAction_SkipsSyncValidation()
    {
        var (filter, provider) = CreateFilter(services =>
        {
            services.AddSingleton<IAsyncValidator<AsyncOnlyAction>>(
                new PassingAsyncValidator<AsyncOnlyAction>());
        });
        using var _ = provider;
        var action = new AsyncOnlyAction();

        var result = await filter.BeforeExecuteAsync<AsyncOnlyAction, string>(action, CancellationToken.None);

        // Sync validator returns error, but should be skipped because AsyncOnly = true
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task BeforeExecute_AsyncOnlyAction_RunsAsyncValidation()
    {
        var (filter, provider) = CreateFilter(services =>
        {
            services.AddSingleton<IAsyncValidator<AsyncOnlyAction>>(
                new FailingAsyncValidator<AsyncOnlyAction>());
        });
        using var _ = provider;
        var action = new AsyncOnlyAction();

        var result = await filter.BeforeExecuteAsync<AsyncOnlyAction, string>(action, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        var validationError = (ValidationError)result.Error;
        validationError.Issues[0].PropertyPath.Should().Be("AsyncField");
    }

    // =========================================================================
    // Tests — [Validate] (sync + async)
    // =========================================================================

    [Fact]
    public async Task BeforeExecute_ValidateAttribute_CombinesSyncAndAsyncErrors()
    {
        var (filter, provider) = CreateFilter(services =>
        {
            services.AddSingleton<IAsyncValidator<CombinedErrorAction>>(
                new FailingAsyncValidator<CombinedErrorAction>());
        });
        using var _ = provider;
        var action = new CombinedErrorAction();

        var result = await filter.BeforeExecuteAsync<CombinedErrorAction, string>(action, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        var validationError = (ValidationError)result.Error;
        // Should have both sync and async errors combined
        validationError.Count.Should().Be(2);
        validationError.Issues.Should().Contain(i => i.PropertyPath == "SyncField");
        validationError.Issues.Should().Contain(i => i.PropertyPath == "AsyncField");
    }

    // =========================================================================
    // Tests — No validator registered
    // =========================================================================

    [Fact]
    public async Task BeforeExecute_NoValidatorRegistered_SkipsGracefully()
    {
        var (filter, provider) = CreateFilter();
        using var _ = provider;
        var action = new PlainAction();

        var result = await filter.BeforeExecuteAsync<PlainAction, string>(action, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    // =========================================================================
    // Tests — Default behavior (no [Validate] attr, just ISyncValidator)
    // =========================================================================

    [Fact]
    public async Task BeforeExecute_DefaultBehavior_RunsSyncOnly()
    {
        // By default (no [Validate] attribute), only sync runs and async is skipped
        var (filter, provider) = CreateFilter(services =>
        {
            // Register async validator, but it should NOT run without [Validate]
            services.AddSingleton<IAsyncValidator<InvalidSyncAction>>(
                new FailingAsyncValidator<InvalidSyncAction>());
        });
        using var _ = provider;
        var action = new InvalidSyncAction();

        var result = await filter.BeforeExecuteAsync<InvalidSyncAction, string>(action, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        var validationError = (ValidationError)result.Error;
        // Only sync error, async should not have run
        validationError.Count.Should().Be(1);
        validationError.Issues[0].PropertyPath.Should().Be("Email");
    }

    // =========================================================================
    // Tests — AfterExecute is no-op
    // =========================================================================

    [Fact]
    public async Task AfterExecute_DoesNothing()
    {
        var (filter, provider) = CreateFilter();
        using var _ = provider;
        var action = new PlainAction();
        var result = Result<string, IError>.Success("ok");

        // Should not throw
        await filter.AfterExecuteAsync<PlainAction, string>(action, result, CancellationToken.None);
    }
}
