// Tests for MutationInvoker Level 2: entity validation after ApplyAsync.
// Covers change-tracking-aware validation, create vs update modes,
// ISyncValidator on entity, and optional IAsyncValidator<TEntity>.

using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Actions.Invoker;
using Pragmatic.Actions.Mutation;
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Lifecycle;
using Pragmatic.Result;
using Pragmatic.Validation;
using Pragmatic.Validation.Extensions;
using Pragmatic.Validation.Types;
using Xunit;

namespace Pragmatic.Actions.Tests.Unit;

/// <summary>
///     Tests for MutationInvoker Level 2 entity validation pipeline.
///     Verifies that entity validation is change-tracking-aware after mutation apply.
/// </summary>
public class MutationInvokerL2ValidationTests
{
    // =========================================================================
    // Test Entity: implements ISyncValidator + IChangeTracking
    // =========================================================================

    /// <summary>
    ///     Entity that implements ISyncValidator with change-aware overload
    ///     and IChangeTracking for modified properties tracking.
    /// </summary>
    private sealed class ValidatableProduct : ISyncValidator, IChangeTracking
    {
        private readonly HashSet<string> _modifiedProperties = [];

        public string Name { get; private set; } = "";
        public decimal Price { get; private set; }
        public IReadOnlySet<string> ModifiedProperties => _modifiedProperties;
        public IReadOnlySet<string> CollectionsModified { get; } = new HashSet<string>();
        public bool IsNew { get; set; }

        /// <summary>
        ///     Tracks which properties were passed to Validate for test assertions.
        /// </summary>
        public IReadOnlySet<string>? LastValidatedProperties { get; private set; }

        public int ValidateCallCount { get; private set; }

        internal void SetName(string value)
        {
            if (Name == value)
                return;
            Name = value;
            _modifiedProperties.Add(nameof(Name));
        }

        internal void SetPrice(decimal value)
        {
            if (Price == value)
                return;
            Price = value;
            _modifiedProperties.Add(nameof(Price));
        }

        public void ResetModifiedProperties() => _modifiedProperties.Clear();

        // ISyncValidator: parameterless validates ALL
        public ValidationError Validate() => Validate(modifiedProperties: null);

        // ISyncValidator: change-aware validates only specified
        public ValidationError Validate(IReadOnlySet<string>? modifiedProperties)
        {
            ValidateCallCount++;
            LastValidatedProperties = modifiedProperties;

            var error = ValidationError.Valid;

            // Simulate change-aware: only validate if null (all) or contains the property
            if (modifiedProperties is null || modifiedProperties.Contains(nameof(Name)))
                if (string.IsNullOrEmpty(Name))
                    error = error.WithFor(nameof(Name), "Name is required");

            if (modifiedProperties is null || modifiedProperties.Contains(nameof(Price)))
                if (Price < 0)
                    error = error.WithFor(nameof(Price), "Price must be non-negative");

            return error;
        }
    }

    /// <summary>
    ///     Entity WITHOUT ISyncValidator — validation should be skipped.
    /// </summary>
    private sealed class NonValidatableProduct : IChangeTracking
    {
        private readonly HashSet<string> _modifiedProperties = [];

        public string Name { get; private set; } = "";
        public IReadOnlySet<string> ModifiedProperties => _modifiedProperties;
        public IReadOnlySet<string> CollectionsModified { get; } = new HashSet<string>();
        public bool IsNew { get; set; }

        internal void SetName(string value)
        {
            Name = value;
            _modifiedProperties.Add(nameof(Name));
        }

        public void ResetModifiedProperties() => _modifiedProperties.Clear();
    }

    /// <summary>
    ///     Entity without IChangeTracking — validates all properties (no tracking).
    /// </summary>
    private sealed class UntrackedProduct : ISyncValidator
    {
        public string Name { get; set; } = "";

        public int ValidateCallCount { get; private set; }
        public IReadOnlySet<string>? LastValidatedProperties { get; private set; }

        public ValidationError Validate()
        {
            ValidateCallCount++;
            LastValidatedProperties = null;
            return string.IsNullOrEmpty(Name)
                ? ValidationError.For(nameof(Name), "Name is required")
                : ValidationError.Valid;
        }

        // Default interface method kicks in — calls Validate()
    }

    // =========================================================================
    // Mutations
    // =========================================================================

    private sealed class UpdateProductNameMutation : Mutation<ValidatableProduct>
    {
        public required string NewName { get; init; }

        public override Task<Result<ValidatableProduct, IError>> ApplyAsync(
            ValidatableProduct entity, CancellationToken ct = default)
        {
            entity.SetName(NewName);
            return Task.FromResult<Result<ValidatableProduct, IError>>(entity);
        }
    }

    private sealed class CreateProductMutation : Mutation<ValidatableProduct>
    {
        public required string Name { get; init; }
        public required decimal Price { get; init; }

        public override Task<Result<ValidatableProduct, IError>> ApplyAsync(
            ValidatableProduct entity, CancellationToken ct = default)
        {
            entity.SetName(Name);
            entity.SetPrice(Price);
            return Task.FromResult<Result<ValidatableProduct, IError>>(entity);
        }
    }

    private sealed class UpdateNonValidatableMutation : Mutation<NonValidatableProduct>
    {
        public required string NewName { get; init; }

        public override Task<Result<NonValidatableProduct, IError>> ApplyAsync(
            NonValidatableProduct entity, CancellationToken ct = default)
        {
            entity.SetName(NewName);
            return Task.FromResult<Result<NonValidatableProduct, IError>>(entity);
        }
    }

    private sealed class UpdateUntrackedMutation : Mutation<UntrackedProduct>
    {
        public required string NewName { get; init; }

        public override Task<Result<UntrackedProduct, IError>> ApplyAsync(
            UntrackedProduct entity, CancellationToken ct = default)
        {
            entity.Name = NewName;
            return Task.FromResult<Result<UntrackedProduct, IError>>(entity);
        }
    }

    // =========================================================================
    // Invokers (test doubles for generated code)
    // =========================================================================

    private sealed class UpdateProductNameInvoker(IServiceProvider sp)
        : MutationInvoker<UpdateProductNameMutation, ValidatableProduct>(sp)
    {
        private ValidatableProduct? _entity;

        public void SetEntity(ValidatableProduct entity) => _entity = entity;

        protected override void InjectDependencies(UpdateProductNameMutation mutation) { }

        protected override Task<ValidatableProduct?> LoadEntityAsync(
            UpdateProductNameMutation mutation, CancellationToken ct)
            => Task.FromResult(_entity);

        protected override ValidatableProduct CreateEntity() => new();
        protected override MutationMode GetMode() => MutationMode.Update;
        protected override string? GetEntityIdString(UpdateProductNameMutation mutation) => "test-id";
        protected override void PersistNew(ValidatableProduct entity) { }
        protected override void DeleteEntity(ValidatableProduct entity) { }
        protected override Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class CreateProductInvoker(IServiceProvider sp)
        : MutationInvoker<CreateProductMutation, ValidatableProduct>(sp)
    {
        protected override void InjectDependencies(CreateProductMutation mutation) { }

        protected override Task<ValidatableProduct?> LoadEntityAsync(
            CreateProductMutation mutation, CancellationToken ct)
            => Task.FromResult<ValidatableProduct?>(null);

        protected override ValidatableProduct CreateEntity() => new();
        protected override MutationMode GetMode() => MutationMode.Create;
        protected override string? GetEntityIdString(CreateProductMutation mutation) => null;
        protected override void PersistNew(ValidatableProduct entity) { }
        protected override void DeleteEntity(ValidatableProduct entity) { }
        protected override Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class UpdateNonValidatableInvoker(IServiceProvider sp)
        : MutationInvoker<UpdateNonValidatableMutation, NonValidatableProduct>(sp)
    {
        private NonValidatableProduct? _entity;

        public void SetEntity(NonValidatableProduct entity) => _entity = entity;

        protected override void InjectDependencies(UpdateNonValidatableMutation mutation) { }

        protected override Task<NonValidatableProduct?> LoadEntityAsync(
            UpdateNonValidatableMutation mutation, CancellationToken ct)
            => Task.FromResult(_entity);

        protected override NonValidatableProduct CreateEntity() => new();
        protected override MutationMode GetMode() => MutationMode.Update;
        protected override string? GetEntityIdString(UpdateNonValidatableMutation mutation) => "test-id";
        protected override void PersistNew(NonValidatableProduct entity) { }
        protected override void DeleteEntity(NonValidatableProduct entity) { }
        protected override Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class UpdateUntrackedInvoker(IServiceProvider sp)
        : MutationInvoker<UpdateUntrackedMutation, UntrackedProduct>(sp)
    {
        private UntrackedProduct? _entity;

        public void SetEntity(UntrackedProduct entity) => _entity = entity;

        protected override void InjectDependencies(UpdateUntrackedMutation mutation) { }

        protected override Task<UntrackedProduct?> LoadEntityAsync(
            UpdateUntrackedMutation mutation, CancellationToken ct)
            => Task.FromResult(_entity);

        protected override UntrackedProduct CreateEntity() => new();
        protected override MutationMode GetMode() => MutationMode.Update;
        protected override string? GetEntityIdString(UpdateUntrackedMutation mutation) => "test-id";
        protected override void PersistNew(UntrackedProduct entity) { }
        protected override void DeleteEntity(UntrackedProduct entity) { }
        protected override Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    // =========================================================================
    // Async Validator test double
    // =========================================================================

    private sealed class FailingEntityAsyncValidator : IAsyncValidator<ValidatableProduct>
    {
        public bool WasCalled { get; private set; }

        public Task<ValidationError> ValidateAsync(
            ValidatableProduct instance, CancellationToken ct = default)
        {
            WasCalled = true;
            return Task.FromResult(ValidationError.For("Name", "Name already exists"));
        }
    }

    private sealed class PassingEntityAsyncValidator : IAsyncValidator<ValidatableProduct>
    {
        public bool WasCalled { get; private set; }

        public Task<ValidationError> ValidateAsync(
            ValidatableProduct instance, CancellationToken ct = default)
        {
            WasCalled = true;
            return Task.FromResult(ValidationError.Valid);
        }
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static ServiceProvider BuildProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        // Register default ValidationOptions for CompositeValidator<T>
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new ValidationOptions()));
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    // =========================================================================
    // Tests — IEntityLifecycle hooks are invoked by the pipeline
    // =========================================================================

    private sealed class RecordingLifecycle : IEntityLifecycle<ValidatableProduct>
    {
        public List<string> Calls { get; } = [];
        public void OnCreating(ValidatableProduct entity, LifecycleContext context) => Calls.Add("OnCreating");
        public void OnSaving(ValidatableProduct entity, LifecycleContext context) => Calls.Add("OnSaving");
    }

    [Fact]
    public async Task Create_InvokesEntityLifecycleHooks_OnCreatingThenOnSaving()
    {
        // IEntityLifecycle<T>.OnCreating/OnSaving are documented as framework-invoked; without a
        // pipeline call site, implementing the interface would do nothing. Prove both fire, in order.
        var lifecycle = new RecordingLifecycle();
        using var provider = BuildProvider(s => s.AddSingleton<IEntityLifecycle<ValidatableProduct>>(lifecycle));

        var invoker = new CreateProductInvoker(provider);
        var result = await invoker.InvokeAsync(new CreateProductMutation { Name = "Widget", Price = 5m });

        result.IsSuccess.Should().BeTrue();
        lifecycle.Calls.Should().Equal("OnCreating", "OnSaving");
    }

    [Fact]
    public async Task Create_NoLifecycleRegistered_StillSucceeds()
    {
        // The hook resolution is a no-op when nothing is registered (no per-entity generation needed).
        using var provider = BuildProvider();

        var invoker = new CreateProductInvoker(provider);
        var result = await invoker.InvokeAsync(new CreateProductMutation { Name = "Widget", Price = 5m });

        result.IsSuccess.Should().BeTrue();
    }

    // =========================================================================
    // Tests — temporal constraints enforced by the pipeline on CREATE, skipped on UPDATE
    // =========================================================================

    private sealed class TemporalError : IError
    {
        public string Code => "TEMPORAL_OVERLAP";
        public int StatusCode => 409;
        public string Title => "Temporal constraint violated";
    }

    private sealed class TemporalCreateInvoker(IServiceProvider sp, List<string> log, IError? error)
        : MutationInvoker<CreateProductMutation, ValidatableProduct>(sp)
    {
        protected override void InjectDependencies(CreateProductMutation mutation) { }
        protected override Task<ValidatableProduct?> LoadEntityAsync(CreateProductMutation m, CancellationToken ct)
            => Task.FromResult<ValidatableProduct?>(null);
        protected override ValidatableProduct CreateEntity() => new();
        protected override MutationMode GetMode() => MutationMode.Create;
        protected override string? GetEntityIdString(CreateProductMutation m) => null;
        protected override void PersistNew(ValidatableProduct entity) { }
        protected override void DeleteEntity(ValidatableProduct entity) { }
        protected override Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
        protected override IError? CheckTemporalConstraints(ValidatableProduct entity)
        {
            log.Add("temporal");
            return error;
        }
    }

    private sealed class TemporalUpdateInvoker(IServiceProvider sp, List<string> log)
        : MutationInvoker<UpdateProductNameMutation, ValidatableProduct>(sp)
    {
        private ValidatableProduct? _entity;
        public void SetEntity(ValidatableProduct entity) => _entity = entity;
        protected override void InjectDependencies(UpdateProductNameMutation mutation) { }
        protected override Task<ValidatableProduct?> LoadEntityAsync(UpdateProductNameMutation m, CancellationToken ct)
            => Task.FromResult(_entity);
        protected override ValidatableProduct CreateEntity() => new();
        protected override MutationMode GetMode() => MutationMode.Update;
        protected override string? GetEntityIdString(UpdateProductNameMutation m) => "test-id";
        protected override void PersistNew(ValidatableProduct entity) { }
        protected override void DeleteEntity(ValidatableProduct entity) { }
        protected override Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
        protected override IError? CheckTemporalConstraints(ValidatableProduct entity)
        {
            log.Add("temporal");
            return null;
        }
    }

    [Fact]
    public async Task Create_TemporalConstraintViolated_RejectsMutation()
    {
        var log = new List<string>();
        using var provider = BuildProvider();
        var invoker = new TemporalCreateInvoker(provider, log, new TemporalError());

        var result = await invoker.InvokeAsync(new CreateProductMutation { Name = "Widget", Price = 5m });

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("TEMPORAL_OVERLAP");
        log.Should().ContainSingle("the temporal check runs once on create");
    }

    [Fact]
    public async Task Create_TemporalConstraintSatisfied_Succeeds()
    {
        var log = new List<string>();
        using var provider = BuildProvider();
        var invoker = new TemporalCreateInvoker(provider, log, error: null);

        var result = await invoker.InvokeAsync(new CreateProductMutation { Name = "Widget", Price = 5m });

        result.IsSuccess.Should().BeTrue();
        log.Should().ContainSingle();
    }

    [Fact]
    public async Task Update_TemporalConstraint_IsNotChecked()
    {
        // On update the record is already counted; re-checking it against itself would false-positive,
        // so the pipeline skips the temporal check on update.
        var log = new List<string>();
        using var provider = BuildProvider();
        var entity = new ValidatableProduct();
        entity.SetName("Original");
        entity.ResetModifiedProperties();
        var invoker = new TemporalUpdateInvoker(provider, log);
        invoker.SetEntity(entity);

        var result = await invoker.InvokeAsync(new UpdateProductNameMutation { NewName = "Updated" });

        result.IsSuccess.Should().BeTrue();
        log.Should().BeEmpty("the temporal check must not run on update");
    }

    // =========================================================================
    // Tests — L2 Change-Tracking-Aware Entity Validation (Update mode)
    // =========================================================================

    [Fact]
    public async Task Update_EntityWithISyncValidator_ValidatesOnlyModifiedProperties()
    {
        using var provider = BuildProvider();
        var entity = new ValidatableProduct();
        entity.SetName("Original");
        entity.SetPrice(10m);
        entity.ResetModifiedProperties(); // simulate loaded from DB

        var invoker = new UpdateProductNameInvoker(provider);
        invoker.SetEntity(entity);

        // Only changing Name, Price stays valid
        var mutation = new UpdateProductNameMutation { NewName = "Updated" };
        var result = await invoker.InvokeAsync(mutation);

        result.IsSuccess.Should().BeTrue();
        entity.ValidateCallCount.Should().Be(1);
        // Should pass only modified properties (Name), NOT null (which means all)
        entity.LastValidatedProperties.Should().NotBeNull();
        entity.LastValidatedProperties.Should().Contain("Name");
        entity.LastValidatedProperties.Should().NotContain("Price");
    }

    [Fact]
    public async Task Update_EntityValidationFails_ReturnsFailure()
    {
        using var provider = BuildProvider();
        var entity = new ValidatableProduct();
        entity.SetName("Original");
        entity.SetPrice(10m);
        entity.ResetModifiedProperties();

        var invoker = new UpdateProductNameInvoker(provider);
        invoker.SetEntity(entity);

        // Setting empty name → Name is required
        var mutation = new UpdateProductNameMutation { NewName = "" };
        var result = await invoker.InvokeAsync(mutation);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Update_OnlyModifiedPropertyValidated_UnchangedInvalidPropertySkipped()
    {
        using var provider = BuildProvider();
        var entity = new ValidatableProduct();
        entity.SetName("Valid");
        entity.SetPrice(-5m); // Invalid price, but won't be modified
        entity.ResetModifiedProperties();

        var invoker = new UpdateProductNameInvoker(provider);
        invoker.SetEntity(entity);

        // Update Name only → Price validation should be SKIPPED
        var mutation = new UpdateProductNameMutation { NewName = "NewValid" };
        var result = await invoker.InvokeAsync(mutation);

        // Should pass because Price is NOT validated (not modified)
        result.IsSuccess.Should().BeTrue();
        entity.LastValidatedProperties.Should().NotContain("Price");
    }

    // =========================================================================
    // Tests — L2 Create Mode (validates ALL properties)
    // =========================================================================

    [Fact]
    public async Task Create_EntityWithISyncValidator_ValidatesAllProperties()
    {
        using var provider = BuildProvider();
        var invoker = new CreateProductInvoker(provider);

        var mutation = new CreateProductMutation { Name = "Widget", Price = 10m };
        var result = await invoker.InvokeAsync(mutation);

        result.IsSuccess.Should().BeTrue();
        var entity = result.Value;
        entity.ValidateCallCount.Should().Be(1);
        // Create mode → modifiedProperties is null → validates ALL
        entity.LastValidatedProperties.Should().BeNull();
    }

    [Fact]
    public async Task Create_EntityValidationFails_ReturnsFailure()
    {
        using var provider = BuildProvider();
        var invoker = new CreateProductInvoker(provider);

        // Empty name → validation fails
        var mutation = new CreateProductMutation { Name = "", Price = 10m };
        var result = await invoker.InvokeAsync(mutation);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Create_NegativePrice_ValidationFails()
    {
        using var provider = BuildProvider();
        var invoker = new CreateProductInvoker(provider);

        var mutation = new CreateProductMutation { Name = "Widget", Price = -1m };
        var result = await invoker.InvokeAsync(mutation);

        // In create mode, ALL properties validated including Price
        result.IsFailure.Should().BeTrue();
    }

    // =========================================================================
    // Tests — Entity WITHOUT ISyncValidator (no L2 validation)
    // =========================================================================

    [Fact]
    public async Task Update_EntityWithoutISyncValidator_SkipsL2Validation()
    {
        using var provider = BuildProvider();
        var entity = new NonValidatableProduct();
        entity.SetName("Original");
        entity.ResetModifiedProperties();

        var invoker = new UpdateNonValidatableInvoker(provider);
        invoker.SetEntity(entity);

        var mutation = new UpdateNonValidatableMutation { NewName = "" };
        var result = await invoker.InvokeAsync(mutation);

        // Should succeed — no ISyncValidator, so no L2 validation
        result.IsSuccess.Should().BeTrue();
    }

    // =========================================================================
    // Tests — Entity with ISyncValidator but WITHOUT IChangeTracking
    // =========================================================================

    [Fact]
    public async Task Update_EntityWithoutIChangeTracking_ValidatesAllProperties()
    {
        using var provider = BuildProvider();
        var entity = new UntrackedProduct { Name = "Original" };

        var invoker = new UpdateUntrackedInvoker(provider);
        invoker.SetEntity(entity);

        var mutation = new UpdateUntrackedMutation { NewName = "Updated" };
        var result = await invoker.InvokeAsync(mutation);

        result.IsSuccess.Should().BeTrue();
        // Without IChangeTracking → modifiedProperties is null → validates ALL
        entity.ValidateCallCount.Should().Be(1);
        entity.LastValidatedProperties.Should().BeNull();
    }

    // =========================================================================
    // Tests — IAsyncValidator<TEntity> via CompositeValidator (unified L2)
    // =========================================================================

    [Fact]
    public async Task Update_WithAsyncEntityValidator_CallsAsyncValidation()
    {
        var asyncValidator = new FailingEntityAsyncValidator();

        using var provider = BuildProvider(services =>
        {
            // Register async validator + CompositeValidator as IValidator<T>
            services.AddSingleton<IAsyncValidator<ValidatableProduct>>(asyncValidator);
            services.AddSyncOnlyValidator<ValidatableProduct>(ServiceLifetime.Singleton);
        });

        var entity = new ValidatableProduct();
        entity.SetName("Valid");
        entity.SetPrice(10m);
        entity.ResetModifiedProperties();

        var invoker = new UpdateProductNameInvoker(provider);
        invoker.SetEntity(entity);

        var mutation = new UpdateProductNameMutation { NewName = "Updated" };
        var result = await invoker.InvokeAsync(mutation);

        // Sync passes, but async fails (via CompositeValidator)
        result.IsFailure.Should().BeTrue();
        asyncValidator.WasCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Update_WithoutAsyncEntityValidator_SkipsAsyncValidation()
    {
        using var provider = BuildProvider(); // No IAsyncValidator registered

        var entity = new ValidatableProduct();
        entity.SetName("Valid");
        entity.SetPrice(10m);
        entity.ResetModifiedProperties();

        var invoker = new UpdateProductNameInvoker(provider);
        invoker.SetEntity(entity);

        var mutation = new UpdateProductNameMutation { NewName = "Updated" };
        var result = await invoker.InvokeAsync(mutation);

        // No async validator → success
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Update_SyncPassesAsyncPasses_ReturnsSuccess()
    {
        var asyncValidator = new PassingEntityAsyncValidator();

        using var provider = BuildProvider(services =>
        {
            // Register async validator + CompositeValidator as IValidator<T>
            services.AddSingleton<IAsyncValidator<ValidatableProduct>>(asyncValidator);
            services.AddSyncOnlyValidator<ValidatableProduct>(ServiceLifetime.Singleton);
        });

        var entity = new ValidatableProduct();
        entity.SetName("Valid");
        entity.SetPrice(10m);
        entity.ResetModifiedProperties();

        var invoker = new UpdateProductNameInvoker(provider);
        invoker.SetEntity(entity);

        var mutation = new UpdateProductNameMutation { NewName = "Updated" };
        var result = await invoker.InvokeAsync(mutation);

        result.IsSuccess.Should().BeTrue();
        asyncValidator.WasCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Update_SyncFails_WithFailFast_AsyncNotCalled()
    {
        var asyncValidator = new PassingEntityAsyncValidator();

        using var provider = BuildProvider(services =>
        {
            // Enable FailFast so CompositeValidator stops after sync failure
            services.AddSingleton(Microsoft.Extensions.Options.Options.Create(
                new ValidationOptions { FailFast = true }));
            // Register async validator + CompositeValidator as IValidator<T>
            services.AddSingleton<IAsyncValidator<ValidatableProduct>>(asyncValidator);
            services.AddSyncOnlyValidator<ValidatableProduct>(ServiceLifetime.Singleton);
        });

        var entity = new ValidatableProduct();
        entity.SetName("Valid");
        entity.SetPrice(10m);
        entity.ResetModifiedProperties();

        var invoker = new UpdateProductNameInvoker(provider);
        invoker.SetEntity(entity);

        // Empty name → sync validation fails
        var mutation = new UpdateProductNameMutation { NewName = "" };
        var result = await invoker.InvokeAsync(mutation);

        result.IsFailure.Should().BeTrue();
        // FailFast enabled: async validator should NOT be called when sync fails
        asyncValidator.WasCalled.Should().BeFalse();
    }

    // =========================================================================
    // Tests — Unified L2 via IValidator<TEntity>
    // =========================================================================

    /// <summary>
    ///     IValidator that tracks calls and combines sync + async results.
    /// </summary>
    private sealed class TrackingEntityValidator : IValidator<ValidatableProduct>
    {
        public bool WasCalled { get; private set; }
        public IReadOnlySet<string>? LastModifiedProperties { get; private set; }
        public bool ShouldFail { get; set; }

        public Task<ValidationError> ValidateAsync(ValidatableProduct instance, CancellationToken ct = default)
            => ValidateAsync(instance, null, ct);

        public Task<ValidationError> ValidateAsync(
            ValidatableProduct instance,
            IReadOnlySet<string>? modifiedProperties,
            CancellationToken ct = default)
        {
            WasCalled = true;
            LastModifiedProperties = modifiedProperties;
            return Task.FromResult(ShouldFail
                ? ValidationError.For("Custom", "Unified validation failed")
                : ValidationError.Valid);
        }
    }

    [Fact]
    public async Task Update_WithIValidatorRegistered_UsesUnifiedValidation()
    {
        var unifiedValidator = new TrackingEntityValidator();

        using var provider = BuildProvider(services =>
        {
            services.AddSingleton<IValidator<ValidatableProduct>>(unifiedValidator);
        });

        var entity = new ValidatableProduct();
        entity.SetName("Original");
        entity.SetPrice(10m);
        entity.ResetModifiedProperties();

        var invoker = new UpdateProductNameInvoker(provider);
        invoker.SetEntity(entity);

        var mutation = new UpdateProductNameMutation { NewName = "Updated" };
        var result = await invoker.InvokeAsync(mutation);

        result.IsSuccess.Should().BeTrue();
        unifiedValidator.WasCalled.Should().BeTrue();
        // Should receive modifiedProperties (update mode)
        unifiedValidator.LastModifiedProperties.Should().NotBeNull();
        unifiedValidator.LastModifiedProperties.Should().Contain("Name");
    }

    [Fact]
    public async Task Update_WithIValidatorThatFails_ReturnsFailure()
    {
        var unifiedValidator = new TrackingEntityValidator { ShouldFail = true };

        using var provider = BuildProvider(services =>
        {
            services.AddSingleton<IValidator<ValidatableProduct>>(unifiedValidator);
        });

        var entity = new ValidatableProduct();
        entity.SetName("Original");
        entity.SetPrice(10m);
        entity.ResetModifiedProperties();

        var invoker = new UpdateProductNameInvoker(provider);
        invoker.SetEntity(entity);

        var mutation = new UpdateProductNameMutation { NewName = "Updated" };
        var result = await invoker.InvokeAsync(mutation);

        result.IsFailure.Should().BeTrue();
        unifiedValidator.WasCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Update_WithoutIValidator_FallsBackToISyncValidator()
    {
        // No IValidator<T> registered — should fall back to ISyncValidator
        using var provider = BuildProvider();

        var entity = new ValidatableProduct();
        entity.SetName("Original");
        entity.SetPrice(10m);
        entity.ResetModifiedProperties();

        var invoker = new UpdateProductNameInvoker(provider);
        invoker.SetEntity(entity);

        var mutation = new UpdateProductNameMutation { NewName = "Updated" };
        var result = await invoker.InvokeAsync(mutation);

        result.IsSuccess.Should().BeTrue();
        entity.ValidateCallCount.Should().Be(1);
        entity.LastValidatedProperties.Should().Contain("Name");
    }

    [Fact]
    public async Task Update_WithoutIValidatorOrISyncValidator_PassesThrough()
    {
        // Entity with no validation at all
        using var provider = BuildProvider();

        var entity = new NonValidatableProduct();
        entity.SetName("Original");
        entity.ResetModifiedProperties();

        var invoker = new UpdateNonValidatableInvoker(provider);
        invoker.SetEntity(entity);

        var mutation = new UpdateNonValidatableMutation { NewName = "Updated" };
        var result = await invoker.InvokeAsync(mutation);

        result.IsSuccess.Should().BeTrue();
    }

    // =========================================================================
    // Tests — ResetModifiedProperties on Load
    // =========================================================================

    [Fact]
    public async Task Update_ResetsModifiedPropertiesBeforeApply()
    {
        using var provider = BuildProvider();
        var entity = new ValidatableProduct();
        entity.SetName("Loaded");
        entity.SetPrice(10m);
        // Simulate entity with "dirty" tracking state from prior use
        // MutationInvoker should reset before apply

        var invoker = new UpdateProductNameInvoker(provider);
        invoker.SetEntity(entity);

        var mutation = new UpdateProductNameMutation { NewName = "Updated" };
        var result = await invoker.InvokeAsync(mutation);

        result.IsSuccess.Should().BeTrue();
        // After reset + SetName, only Name should be in modified
        entity.LastValidatedProperties.Should().HaveCount(1);
        entity.LastValidatedProperties.Should().Contain("Name");
    }
}
