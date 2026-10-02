// Tests for CompositeValidator<T> change-aware validation pipeline.
// Verifies sync + async composition with IAsyncValidatorBindings filtering.

using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Options;
using Pragmatic.Validation.Types;
using Xunit;

namespace Pragmatic.Validation.Tests.Unit;

public class CompositeValidatorTests
{
    // =========================================================================
    // Test types
    // =========================================================================

    /// <summary>
    ///     Entity with sync validation (ISyncValidator) for testing.
    /// </summary>
    private sealed class SyncEntity : ISyncValidator
    {
        public string Name { get; set; } = "Valid";
        public IReadOnlySet<string>? LastModifiedProperties { get; private set; }
        public int ValidateCallCount { get; private set; }

        public ValidationError Validate() => Validate(null);

        public ValidationError Validate(IReadOnlySet<string>? modifiedProperties)
        {
            ValidateCallCount++;
            LastModifiedProperties = modifiedProperties;

            var error = ValidationError.Valid;
            if (modifiedProperties is null || modifiedProperties.Contains(nameof(Name)))
                if (string.IsNullOrEmpty(Name))
                    error = error.WithFor(nameof(Name), "Name is required");

            return error;
        }
    }

    /// <summary>
    ///     Entity without ISyncValidator (async only).
    /// </summary>
    private sealed class AsyncOnlyEntity
    {
        public string Email { get; set; } = "test@example.com";
    }

    // =========================================================================
    // Async validator test doubles
    // =========================================================================

    private sealed class EmailValidator : IAsyncValidator<SyncEntity>
    {
        public bool WasCalled { get; private set; }
        public bool ShouldFail { get; set; }

        public Task<ValidationError> ValidateAsync(SyncEntity instance, CancellationToken ct = default)
        {
            WasCalled = true;
            return Task.FromResult(ShouldFail
                ? ValidationError.For("Email", "Email already exists")
                : ValidationError.Valid);
        }
    }

    private sealed class AvailabilityValidator : IAsyncValidator<SyncEntity>
    {
        public bool WasCalled { get; private set; }
        public bool ShouldFail { get; set; }

        public Task<ValidationError> ValidateAsync(SyncEntity instance, CancellationToken ct = default)
        {
            WasCalled = true;
            return Task.FromResult(ShouldFail
                ? ValidationError.For("Availability", "Not available")
                : ValidationError.Valid);
        }
    }

    private sealed class AsyncOnlyEmailValidator : IAsyncValidator<AsyncOnlyEntity>
    {
        public bool WasCalled { get; private set; }
        public bool ShouldFail { get; set; }

        public Task<ValidationError> ValidateAsync(AsyncOnlyEntity instance, CancellationToken ct = default)
        {
            WasCalled = true;
            return Task.FromResult(ShouldFail
                ? ValidationError.For("Email", "Email already exists")
                : ValidationError.Valid);
        }
    }

    // =========================================================================
    // Bindings test double
    // =========================================================================

    /// <summary>
    ///     Simulates generated bindings:
    ///     - AvailabilityValidator: entity-level (always fires)
    ///     - EmailValidator: property-level (fires when "Email" changes)
    /// </summary>
    private sealed class TestBindings : IAsyncValidatorBindings<SyncEntity>
    {
        public bool ShouldInvoke(Type validatorType, IReadOnlySet<string>? modifiedProperties)
        {
            if (modifiedProperties is null)
                return true; // create mode

            // Entity-level binding
            if (validatorType == typeof(AvailabilityValidator))
                return true;

            // Property-level binding
            if (validatorType == typeof(EmailValidator))
                return modifiedProperties.Contains("Email");

            return false; // unknown validator
        }
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static IOptions<ValidationOptions> DefaultOptions(bool failFast = false) =>
        Options.Create(new ValidationOptions { FailFast = failFast });

    // =========================================================================
    // Tests — Create mode (null modifiedProperties)
    // =========================================================================

    [Fact]
    public async Task CreateMode_SyncValidatesAll_AllBoundAsyncInvoked()
    {
        var emailValidator = new EmailValidator();
        var availabilityValidator = new AvailabilityValidator();
        var bindings = new TestBindings();

        var validator = new CompositeValidator<SyncEntity>(
            DefaultOptions(),
            [emailValidator, availabilityValidator],
            bindings);

        var entity = new SyncEntity { Name = "Valid" };
        var result = await validator.ValidateAsync(entity, modifiedProperties: null);

        result.IsSuccess.Should().BeTrue();
        entity.LastModifiedProperties.Should().BeNull(); // validates ALL
        emailValidator.WasCalled.Should().BeTrue();
        availabilityValidator.WasCalled.Should().BeTrue();
    }

    // =========================================================================
    // Tests — Update mode (specific modified properties)
    // =========================================================================

    [Fact]
    public async Task UpdateMode_SyncValidatesOnlyModified_AsyncFilteredByBindings()
    {
        var emailValidator = new EmailValidator();
        var availabilityValidator = new AvailabilityValidator();
        var bindings = new TestBindings();

        var validator = new CompositeValidator<SyncEntity>(
            DefaultOptions(),
            [emailValidator, availabilityValidator],
            bindings);

        var entity = new SyncEntity { Name = "Valid" };
        var modified = new HashSet<string> { "Name" };
        var result = await validator.ValidateAsync(entity, modified);

        result.IsSuccess.Should().BeTrue();
        entity.LastModifiedProperties.Should().BeEquivalentTo(["Name"]);
        // Email not modified → EmailValidator skipped
        emailValidator.WasCalled.Should().BeFalse();
        // Entity-level → always invoked
        availabilityValidator.WasCalled.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateMode_EntityLevelBinding_AlwaysInvoked()
    {
        var availabilityValidator = new AvailabilityValidator();
        var bindings = new TestBindings();

        var validator = new CompositeValidator<SyncEntity>(
            DefaultOptions(),
            [availabilityValidator],
            bindings);

        var entity = new SyncEntity { Name = "Valid" };
        // Even though we only modified "SomeOther", entity-level validators fire
        var modified = new HashSet<string> { "SomeOther" };
        var result = await validator.ValidateAsync(entity, modified);

        result.IsSuccess.Should().BeTrue();
        availabilityValidator.WasCalled.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateMode_PropertyLevelBinding_InvokedWhenTriggerModified()
    {
        var emailValidator = new EmailValidator();
        var bindings = new TestBindings();

        var validator = new CompositeValidator<SyncEntity>(
            DefaultOptions(),
            [emailValidator],
            bindings);

        var entity = new SyncEntity { Name = "Valid" };
        var modified = new HashSet<string> { "Email" };
        var result = await validator.ValidateAsync(entity, modified);

        result.IsSuccess.Should().BeTrue();
        emailValidator.WasCalled.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateMode_PropertyLevelBinding_SkippedWhenDifferentPropertyModified()
    {
        var emailValidator = new EmailValidator();
        var bindings = new TestBindings();

        var validator = new CompositeValidator<SyncEntity>(
            DefaultOptions(),
            [emailValidator],
            bindings);

        var entity = new SyncEntity { Name = "Valid" };
        var modified = new HashSet<string> { "Name" }; // Not "Email"
        var result = await validator.ValidateAsync(entity, modified);

        result.IsSuccess.Should().BeTrue();
        emailValidator.WasCalled.Should().BeFalse();
    }

    // =========================================================================
    // Tests — No bindings (backward compat for DTOs)
    // =========================================================================

    [Fact]
    public async Task NoBindings_AllAsyncInvoked()
    {
        var emailValidator = new EmailValidator();
        var availabilityValidator = new AvailabilityValidator();

        // No bindings → all async validators invoked unconditionally
        var validator = new CompositeValidator<SyncEntity>(
            DefaultOptions(),
            [emailValidator, availabilityValidator],
            bindings: null);

        var entity = new SyncEntity { Name = "Valid" };
        var modified = new HashSet<string> { "Name" };
        var result = await validator.ValidateAsync(entity, modified);

        result.IsSuccess.Should().BeTrue();
        emailValidator.WasCalled.Should().BeTrue();
        availabilityValidator.WasCalled.Should().BeTrue();
    }

    // =========================================================================
    // Tests — FailFast behavior
    // =========================================================================

    [Fact]
    public async Task FailFast_SyncFails_AsyncSkipped()
    {
        var emailValidator = new EmailValidator();

        var validator = new CompositeValidator<SyncEntity>(
            DefaultOptions(failFast: true),
            [emailValidator],
            bindings: null);

        var entity = new SyncEntity { Name = "" }; // Sync will fail
        var result = await validator.ValidateAsync(entity, modifiedProperties: null);

        result.IsFailure.Should().BeTrue();
        emailValidator.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task FailFast_FirstAsyncFails_RemainingSkipped()
    {
        var emailValidator = new EmailValidator { ShouldFail = true };
        var availabilityValidator = new AvailabilityValidator();

        var validator = new CompositeValidator<SyncEntity>(
            DefaultOptions(failFast: true),
            [emailValidator, availabilityValidator],
            bindings: null);

        var entity = new SyncEntity { Name = "Valid" };
        var result = await validator.ValidateAsync(entity, modifiedProperties: null);

        result.IsFailure.Should().BeTrue();
        emailValidator.WasCalled.Should().BeTrue();
        availabilityValidator.WasCalled.Should().BeFalse();
    }

    // =========================================================================
    // Tests — Multiple async validators
    // =========================================================================

    [Fact]
    public async Task MultipleAsyncValidators_AllBoundInvoked_ErrorsCombined()
    {
        var emailValidator = new EmailValidator { ShouldFail = true };
        var availabilityValidator = new AvailabilityValidator { ShouldFail = true };

        var validator = new CompositeValidator<SyncEntity>(
            DefaultOptions(failFast: false),
            [emailValidator, availabilityValidator],
            bindings: null);

        var entity = new SyncEntity { Name = "Valid" };
        var result = await validator.ValidateAsync(entity, modifiedProperties: null);

        result.IsFailure.Should().BeTrue();
        result.Issues.Should().HaveCount(2);
        emailValidator.WasCalled.Should().BeTrue();
        availabilityValidator.WasCalled.Should().BeTrue();
    }

    // =========================================================================
    // Tests — No sync + async only
    // =========================================================================

    [Fact]
    public async Task NoSync_AsyncOnly_ValidatorsStillWork()
    {
        var emailValidator = new AsyncOnlyEmailValidator();

        var validator = new CompositeValidator<AsyncOnlyEntity>(
            DefaultOptions(),
            [emailValidator],
            bindings: null);

        var entity = new AsyncOnlyEntity();
        var result = await validator.ValidateAsync(entity, modifiedProperties: null);

        result.IsSuccess.Should().BeTrue();
        emailValidator.WasCalled.Should().BeTrue();
    }
}
