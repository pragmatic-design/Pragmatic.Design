// Tests for ServiceCollectionExtensions DI registration helpers.
// Covers AddValidator, AddAsyncValidator, AddSyncOnlyValidator, AddAsyncValidatorBindings,
// AddValidatorWithComposite, and end-to-end FailFast behaviour through a built ServiceProvider.

using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Validation.Extensions;
using Pragmatic.Validation.Types;
using Xunit;

namespace Pragmatic.Validation.Tests.Unit;

public class ServiceCollectionExtensionsTests
{
    // =========================================================================
    // Test types
    // =========================================================================

    private sealed class SyncEntity : ISyncValidator
    {
        public string Name { get; set; } = "Valid";

        public ValidationError Validate() => Validate(null);

        public ValidationError Validate(IReadOnlySet<string>? modifiedProperties)
            => string.IsNullOrEmpty(Name)
                ? ValidationError.For(nameof(Name), "Name is required")
                : ValidationError.Valid;
    }

    private sealed class Dto
    {
        public string Email { get; set; } = "test@example.com";
    }

    private sealed class DtoAsyncValidator : IAsyncValidator<Dto>
    {
        public Task<ValidationError> ValidateAsync(Dto instance, CancellationToken ct = default)
            => Task.FromResult(ValidationError.Valid);
    }

    private sealed class SyncEntityAsyncValidator : IAsyncValidator<SyncEntity>
    {
        public bool ShouldFail { get; init; }

        public Task<ValidationError> ValidateAsync(SyncEntity instance, CancellationToken ct = default)
            => Task.FromResult(ShouldFail
                ? ValidationError.For("Async", "async failed")
                : ValidationError.Valid);
    }

    private sealed class FullValidator : IValidator<Dto>
    {
        public Task<ValidationError> ValidateAsync(Dto instance, CancellationToken ct = default)
            => Task.FromResult(ValidationError.Valid);
    }

    private sealed class EntityBindings : IAsyncValidatorBindings<SyncEntity>
    {
        public bool ShouldInvoke(Type validatorType, IReadOnlySet<string>? modifiedProperties) => true;
    }

    // =========================================================================
    // AddValidator<TValidator, T>
    // =========================================================================

    [Fact]
    public void AddValidator_RegistersValidatorAsIValidator()
    {
        var services = new ServiceCollection();

        services.AddValidator<FullValidator, Dto>();

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetService<IValidator<Dto>>();

        resolved.Should().BeOfType<FullValidator>();
    }

    [Fact]
    public void AddValidator_DefaultLifetime_IsScoped()
    {
        var services = new ServiceCollection();

        services.AddValidator<FullValidator, Dto>();

        var descriptor = services.Single(d => d.ServiceType == typeof(IValidator<Dto>));
        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddValidator_ExplicitLifetime_IsHonored()
    {
        var services = new ServiceCollection();

        services.AddValidator<FullValidator, Dto>(ServiceLifetime.Singleton);

        var descriptor = services.Single(d => d.ServiceType == typeof(IValidator<Dto>));
        descriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddValidator_NullServices_Throws()
    {
        IServiceCollection services = null!;

        var act = () => services.AddValidator<FullValidator, Dto>();

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddValidator_ReturnsServiceCollectionForChaining()
    {
        var services = new ServiceCollection();

        var returned = services.AddValidator<FullValidator, Dto>();

        returned.Should().BeSameAs(services);
    }

    // =========================================================================
    // AddAsyncValidator<TValidator, T>
    // =========================================================================

    [Fact]
    public void AddAsyncValidator_RegistersAsIAsyncValidator()
    {
        var services = new ServiceCollection();

        services.AddAsyncValidator<DtoAsyncValidator, Dto>();

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetService<IAsyncValidator<Dto>>();

        resolved.Should().BeOfType<DtoAsyncValidator>();
    }

    [Fact]
    public void AddAsyncValidator_UsesAdd_AllowsMultipleRegistrations()
    {
        var services = new ServiceCollection();

        services.AddAsyncValidator<DtoAsyncValidator, Dto>();
        services.AddAsyncValidator<DtoAsyncValidator, Dto>();

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetServices<IAsyncValidator<Dto>>();

        resolved.Should().HaveCount(2);
    }

    [Fact]
    public void AddAsyncValidator_NullServices_Throws()
    {
        IServiceCollection services = null!;

        var act = () => services.AddAsyncValidator<DtoAsyncValidator, Dto>();

        act.Should().Throw<ArgumentNullException>();
    }

    // =========================================================================
    // AddSyncOnlyValidator<T>
    // =========================================================================

    [Fact]
    public void AddSyncOnlyValidator_RegistersCompositeValidatorAsIValidator()
    {
        var services = new ServiceCollection();
        services.AddPragmaticValidation();

        services.AddSyncOnlyValidator<SyncEntity>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var resolved = scope.ServiceProvider.GetService<IValidator<SyncEntity>>();

        resolved.Should().BeOfType<CompositeValidator<SyncEntity>>();
    }

    [Fact]
    public void AddSyncOnlyValidator_IsIdempotent_DoesNotDuplicate()
    {
        var services = new ServiceCollection();

        services.AddSyncOnlyValidator<SyncEntity>();
        services.AddSyncOnlyValidator<SyncEntity>();

        services.Count(d => d.ServiceType == typeof(IValidator<SyncEntity>)).Should().Be(1);
    }

    [Fact]
    public void AddSyncOnlyValidator_NullServices_Throws()
    {
        IServiceCollection services = null!;

        var act = () => services.AddSyncOnlyValidator<SyncEntity>();

        act.Should().Throw<ArgumentNullException>();
    }

    // =========================================================================
    // AddAsyncValidatorBindings<TBindings, T>
    // =========================================================================

    [Fact]
    public void AddAsyncValidatorBindings_RegistersBindingsAsSingleton()
    {
        var services = new ServiceCollection();

        services.AddAsyncValidatorBindings<EntityBindings, SyncEntity>();

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetService<IAsyncValidatorBindings<SyncEntity>>();

        resolved.Should().BeOfType<EntityBindings>();

        var descriptor = services.Single(d => d.ServiceType == typeof(IAsyncValidatorBindings<SyncEntity>));
        descriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddAsyncValidatorBindings_IsIdempotent_DoesNotDuplicate()
    {
        var services = new ServiceCollection();

        services.AddAsyncValidatorBindings<EntityBindings, SyncEntity>();
        services.AddAsyncValidatorBindings<EntityBindings, SyncEntity>();

        services.Count(d => d.ServiceType == typeof(IAsyncValidatorBindings<SyncEntity>)).Should().Be(1);
    }

    [Fact]
    public void AddAsyncValidatorBindings_NullServices_Throws()
    {
        IServiceCollection services = null!;

        var act = () => services.AddAsyncValidatorBindings<EntityBindings, SyncEntity>();

        act.Should().Throw<ArgumentNullException>();
    }

    // =========================================================================
    // AddValidatorWithComposite<TValidator, T>
    // =========================================================================

    [Fact]
    public void AddValidatorWithComposite_RegistersBothAsyncValidatorAndComposite()
    {
        var services = new ServiceCollection();
        services.AddPragmaticValidation();

        services.AddValidatorWithComposite<SyncEntityAsyncValidator, SyncEntity>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetService<IAsyncValidator<SyncEntity>>()
            .Should().BeOfType<SyncEntityAsyncValidator>();
        scope.ServiceProvider.GetService<IValidator<SyncEntity>>()
            .Should().BeOfType<CompositeValidator<SyncEntity>>();
    }

    // =========================================================================
    // End-to-end resolution + FailFast through a built provider
    // =========================================================================

    [Fact]
    public async Task ResolvedCompositeValidator_RunsSyncAndAsync_WhenWiredViaDi()
    {
        var services = new ServiceCollection();
        services.AddPragmaticValidation();
        services.AddValidatorWithComposite<SyncEntityAsyncValidator, SyncEntity>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<IValidator<SyncEntity>>();

        var result = await validator.ValidateAsync(new SyncEntity { Name = "Valid" });

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task FailFast_True_ViaDi_SyncFailureSkipsAsync()
    {
        var services = new ServiceCollection();
        services.AddPragmaticValidation(options => options.FailFast = true);
        services.AddValidatorWithComposite<SyncEntityAsyncValidator, SyncEntity>(ServiceLifetime.Scoped);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<IValidator<SyncEntity>>();

        // Name empty → sync fails; with FailFast the single async issue must not be added.
        var result = await validator.ValidateAsync(new SyncEntity { Name = "" });

        result.IsFailure.Should().BeTrue();
        result.Issues.Should().ContainSingle()
            .Which.PropertyPath.Should().Be("Name");
    }

    [Fact]
    public async Task FailFast_False_ViaDi_AccumulatesSyncAndAsyncErrors()
    {
        var services = new ServiceCollection();
        services.AddPragmaticValidation(options => options.FailFast = false);
        services.AddAsyncValidatorBindings<EntityBindings, SyncEntity>();

        // Register a failing async validator + composite manually so both phases fail.
        services.AddSingleton<IAsyncValidator<SyncEntity>>(new SyncEntityAsyncValidator { ShouldFail = true });
        services.AddScoped<IValidator<SyncEntity>, CompositeValidator<SyncEntity>>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<IValidator<SyncEntity>>();

        var result = await validator.ValidateAsync(new SyncEntity { Name = "" });

        result.IsFailure.Should().BeTrue();
        result.Count.Should().Be(2); // sync "Name" + async "Async"
    }

    [Fact]
    public void AddPragmaticValidation_RegistersValidationOptions()
    {
        var services = new ServiceCollection();

        services.AddPragmaticValidation(options => options.FailFast = true);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetService<Microsoft.Extensions.Options.IOptions<ValidationOptions>>();

        options.Should().NotBeNull();
        options!.Value.FailFast.Should().BeTrue();
    }

    [Fact]
    public void AddPragmaticValidation_NullServices_Throws()
    {
        IServiceCollection services = null!;

        var act = () => services.AddPragmaticValidation();

        act.Should().Throw<ArgumentNullException>();
    }
}
