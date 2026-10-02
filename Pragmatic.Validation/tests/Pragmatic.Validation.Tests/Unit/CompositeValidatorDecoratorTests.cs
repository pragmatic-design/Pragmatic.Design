// Tests for IValidatorDecorator unwrapping in CompositeValidator<T>.
// When an async validator is wrapped by a decorator, the runtime must resolve bindings
// against the decorator's InnerValidatorType, not the decorator's own concrete type.

using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Options;
using Pragmatic.Validation.Types;
using Xunit;

namespace Pragmatic.Validation.Tests.Unit;

public class CompositeValidatorDecoratorTests
{
    // =========================================================================
    // Test types
    // =========================================================================

    private sealed class Entity
    {
        public string Email { get; set; } = "test@example.com";
    }

    /// <summary>Concrete async validator that the decorator wraps.</summary>
    private sealed class EmailValidator : IAsyncValidator<Entity>
    {
        public bool WasCalled { get; private set; }
        public bool ShouldFail { get; init; }

        public Task<ValidationError> ValidateAsync(Entity instance, CancellationToken ct = default)
        {
            WasCalled = true;
            return Task.FromResult(ShouldFail
                ? ValidationError.For("Email", "email failed")
                : ValidationError.Valid);
        }
    }

    /// <summary>
    ///     A decorator (e.g. logging/metrics) that wraps a concrete validator and
    ///     exposes its inner type via <see cref="IValidatorDecorator.InnerValidatorType" />.
    /// </summary>
    private sealed class LoggingDecorator(IAsyncValidator<Entity> inner, Type innerType)
        : IAsyncValidator<Entity>, IValidatorDecorator
    {
        public Type InnerValidatorType => innerType;

        public Task<ValidationError> ValidateAsync(Entity instance, CancellationToken ct = default)
            => inner.ValidateAsync(instance, ct);
    }

    /// <summary>
    ///     Bindings keyed on the CONCRETE EmailValidator type. If the runtime fails to
    ///     unwrap the decorator, ShouldInvoke receives the decorator type and returns false.
    /// </summary>
    private sealed class EmailBindings : IAsyncValidatorBindings<Entity>
    {
        public Type? LastQueriedType { get; private set; }

        public bool ShouldInvoke(Type validatorType, IReadOnlySet<string>? modifiedProperties)
        {
            LastQueriedType = validatorType;

            if (modifiedProperties is null)
                return true; // create mode

            return validatorType == typeof(EmailValidator)
                   && modifiedProperties.Contains("Email");
        }
    }

    private static IOptions<ValidationOptions> DefaultOptions(bool failFast = false) =>
        Options.Create(new ValidationOptions { FailFast = failFast });

    // =========================================================================
    // Tests
    // =========================================================================

    [Fact]
    public async Task DecoratedValidator_BindingResolvedAgainstInnerType_NotDecoratorType()
    {
        var inner = new EmailValidator();
        var decorator = new LoggingDecorator(inner, typeof(EmailValidator));
        var bindings = new EmailBindings();

        var validator = new CompositeValidator<Entity>(
            DefaultOptions(),
            [decorator],
            bindings);

        var modified = new HashSet<string> { "Email" };
        await validator.ValidateAsync(new Entity(), modified);

        // The binding must have been queried with the inner concrete type, not the decorator.
        bindings.LastQueriedType.Should().Be<EmailValidator>();
        inner.WasCalled.Should().BeTrue();
    }

    [Fact]
    public async Task DecoratedValidator_Invoked_WhenTriggerPropertyModified()
    {
        var inner = new EmailValidator();
        var decorator = new LoggingDecorator(inner, typeof(EmailValidator));

        var validator = new CompositeValidator<Entity>(
            DefaultOptions(),
            [decorator],
            new EmailBindings());

        var modified = new HashSet<string> { "Email" };
        var result = await validator.ValidateAsync(new Entity(), modified);

        result.IsSuccess.Should().BeTrue();
        inner.WasCalled.Should().BeTrue();
    }

    [Fact]
    public async Task DecoratedValidator_Skipped_WhenTriggerPropertyNotModified()
    {
        var inner = new EmailValidator();
        var decorator = new LoggingDecorator(inner, typeof(EmailValidator));

        var validator = new CompositeValidator<Entity>(
            DefaultOptions(),
            [decorator],
            new EmailBindings());

        var modified = new HashSet<string> { "SomethingElse" };
        var result = await validator.ValidateAsync(new Entity(), modified);

        result.IsSuccess.Should().BeTrue();
        // Inner type bound to "Email"; unrelated change → unwrapped binding correctly skips it.
        inner.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task DecoratedValidator_CreateMode_AlwaysInvoked()
    {
        var inner = new EmailValidator();
        var decorator = new LoggingDecorator(inner, typeof(EmailValidator));

        var validator = new CompositeValidator<Entity>(
            DefaultOptions(),
            [decorator],
            new EmailBindings());

        await validator.ValidateAsync(new Entity(), modifiedProperties: null);

        inner.WasCalled.Should().BeTrue();
    }

    [Fact]
    public async Task DecoratedValidator_FailurePropagatesThroughDecorator()
    {
        var inner = new EmailValidator { ShouldFail = true };
        var decorator = new LoggingDecorator(inner, typeof(EmailValidator));

        var validator = new CompositeValidator<Entity>(
            DefaultOptions(),
            [decorator],
            bindings: null);

        var result = await validator.ValidateAsync(new Entity(), modifiedProperties: null);

        result.IsFailure.Should().BeTrue();
        result.Issues.Should().ContainSingle().Which.PropertyPath.Should().Be("Email");
    }
}
