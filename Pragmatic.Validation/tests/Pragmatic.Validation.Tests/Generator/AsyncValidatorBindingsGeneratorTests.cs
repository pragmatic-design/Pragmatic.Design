// Tests for [AsyncValidate<T>] source generator pipeline.
// Verifies detection on properties and classes, and generation of IAsyncValidatorBindings<T>.

using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Validation.Tests.Generator;

public class AsyncValidatorBindingsGeneratorTests : ValidatorGeneratorTestBase
{
    private const string CommonUsings = """
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Validation;
        using Pragmatic.Validation.Attributes;
        using Pragmatic.Validation.Types;
        """;

    [Fact]
    public void PropertyLevel_AsyncValidate_GeneratesBindingsClass()
    {
        var source = CommonUsings + """

            namespace TestApp;

            public class EmailUniquenessValidator : IAsyncValidator<Reservation>
            {
                public Task<ValidationError> ValidateAsync(
                    Reservation instance, CancellationToken ct = default)
                    => Task.FromResult(ValidationError.Valid);
            }

            public partial class Reservation
            {
                [AsyncValidate<EmailUniquenessValidator>]
                public string Email { get; set; } = "";
            }
            """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetGeneratedSource(result, "AsyncValidatorBindings");
        generated.Should().NotBeNull();
        generated.Should().Contain("IAsyncValidatorBindings<Reservation>");
        generated.Should().Contain("ReservationAsyncValidatorBindings");
        generated.Should().Contain("modifiedProperties.Contains(\"Email\")");
    }

    [Fact]
    public void ClassLevel_AsyncValidate_GeneratesEntityLevelBinding()
    {
        var source = CommonUsings + """

            namespace TestApp;

            public class AvailabilityValidator : IAsyncValidator<Reservation>
            {
                public Task<ValidationError> ValidateAsync(
                    Reservation instance, CancellationToken ct = default)
                    => Task.FromResult(ValidationError.Valid);
            }

            [AsyncValidate<AvailabilityValidator>]
            public partial class Reservation
            {
                public string Name { get; set; } = "";
            }
            """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetGeneratedSource(result, "AsyncValidatorBindings");
        generated.Should().NotBeNull();
        generated.Should().Contain("IAsyncValidatorBindings<Reservation>");
        // Entity-level: always returns true
        generated.Should().Contain("return true");
    }

    [Fact]
    public void BothPropertyAndClassBindings_CombinedInSingleClass()
    {
        var source = CommonUsings + """

            namespace TestApp;

            public class EmailUniquenessValidator : IAsyncValidator<Reservation>
            {
                public Task<ValidationError> ValidateAsync(
                    Reservation instance, CancellationToken ct = default)
                    => Task.FromResult(ValidationError.Valid);
            }

            public class AvailabilityValidator : IAsyncValidator<Reservation>
            {
                public Task<ValidationError> ValidateAsync(
                    Reservation instance, CancellationToken ct = default)
                    => Task.FromResult(ValidationError.Valid);
            }

            [AsyncValidate<AvailabilityValidator>]
            public partial class Reservation
            {
                [AsyncValidate<EmailUniquenessValidator>]
                public string Email { get; set; } = "";

                public string Name { get; set; } = "";
            }
            """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetGeneratedSource(result, "AsyncValidatorBindings");
        generated.Should().NotBeNull();
        // Both bindings in same class
        generated.Should().Contain("AvailabilityValidator");
        generated.Should().Contain("EmailUniquenessValidator");
        generated.Should().Contain("modifiedProperties.Contains(\"Email\")");
    }

    [Fact]
    public void MultipleAsyncValidate_OnSameProperty_MultipleEntries()
    {
        var source = CommonUsings + """

            namespace TestApp;

            public class EmailUniquenessValidator : IAsyncValidator<User>
            {
                public Task<ValidationError> ValidateAsync(
                    User instance, CancellationToken ct = default)
                    => Task.FromResult(ValidationError.Valid);
            }

            public class EmailFormatValidator : IAsyncValidator<User>
            {
                public Task<ValidationError> ValidateAsync(
                    User instance, CancellationToken ct = default)
                    => Task.FromResult(ValidationError.Valid);
            }

            public partial class User
            {
                [AsyncValidate<EmailUniquenessValidator>]
                [AsyncValidate<EmailFormatValidator>]
                public string Email { get; set; } = "";
            }
            """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetGeneratedSource(result, "AsyncValidatorBindings");
        generated.Should().NotBeNull();
        generated.Should().Contain("EmailUniquenessValidator");
        generated.Should().Contain("EmailFormatValidator");
    }

    [Fact]
    public void NoAsyncValidate_NoBindingsGenerated()
    {
        var source = """
            using Pragmatic.Validation.Attributes;

            namespace TestApp;

            public partial class SimpleDto
            {
                [Required]
                public string Name { get; set; } = "";
            }
            """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetGeneratedSource(result, "AsyncValidatorBindings");
        generated.Should().BeNull();
    }

    [Fact]
    public void GeneratedBindingsClass_ImplementsInterface()
    {
        var source = CommonUsings + """

            namespace TestApp;

            public class NameValidator : IAsyncValidator<Entity>
            {
                public Task<ValidationError> ValidateAsync(
                    Entity instance, CancellationToken ct = default)
                    => Task.FromResult(ValidationError.Valid);
            }

            public partial class Entity
            {
                [AsyncValidate<NameValidator>]
                public string Name { get; set; } = "";
            }
            """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetGeneratedSource(result, "AsyncValidatorBindings");
        generated.Should().NotBeNull();
        generated.Should().Contain("internal sealed class EntityAsyncValidatorBindings");
        generated.Should().Contain(": IAsyncValidatorBindings<Entity>");
        generated.Should().Contain("bool ShouldInvoke(Type validatorType");
        generated.Should().Contain("IReadOnlySet<string>? modifiedProperties)");
    }
}
