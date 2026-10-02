using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Validation.Tests.Generator;

/// <summary>
///     Tests for the unified ValidationSourceGenerator's validator registration feature.
///     The generator registers validators for DI using CompositeValidator at runtime.
/// </summary>
public class ValidatorGeneratorTests : ValidatorGeneratorTestBase
{
    [Fact]
    public void Generator_WithValidatorAndValidatableType_GeneratesCompositeRegistration()
    {
        const string source = """
                              using Pragmatic.Validation;
                              using Pragmatic.Validation.Attributes;
                              using Pragmatic.Validation.Types;
                              using System.Threading;
                              using System.Threading.Tasks;

                              namespace TestNamespace;

                              // Type with sync validation (attributes)
                              public partial record CreateUserRequest
                              {
                                  [Required]
                                  [Email]
                                  public string Email { get; init; }
                              }

                              // Validator for async validation
                              [Validator]
                              public class CreateUserValidator : IAsyncValidator<CreateUserRequest>
                              {
                                  public Task<ValidationError> ValidateAsync(
                                      CreateUserRequest instance,
                                      CancellationToken ct = default)
                                  {
                                      return Task.FromResult(ValidationError.Valid);
                                  }
                              }
                              """;

        var result = RunGenerator(source);

        // Should generate DI registration using AddValidatorWithComposite
        var registrationSource = GetGeneratedSource(result, "ValidatorRegistration");
        registrationSource.Should().NotBeNull("Registration should be generated");
        registrationSource.Should().Contain("AddValidatorWithComposite");
        registrationSource.Should().Contain("CreateUserValidator");
        registrationSource.Should().Contain("CreateUserRequest");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    [Fact]
    public void Generator_WithValidatorAndValidatableType_GeneratesSyncValidation()
    {
        const string source = """
                              using Pragmatic.Validation;
                              using Pragmatic.Validation.Attributes;
                              using Pragmatic.Validation.Types;
                              using System.Threading;
                              using System.Threading.Tasks;

                              namespace TestNamespace;

                              public partial record CreateUserRequest
                              {
                                  [Required]
                                  public string Email { get; init; }
                              }

                              [Validator]
                              public class CreateUserValidator : IAsyncValidator<CreateUserRequest>
                              {
                                  public Task<ValidationError> ValidateAsync(
                                      CreateUserRequest instance,
                                      CancellationToken ct = default)
                                  {
                                      return Task.FromResult(ValidationError.Valid);
                                  }
                              }
                              """;

        var result = RunGenerator(source);

        // Should generate ISyncValidator (Validator)
        var validatableSource = GetGeneratedSource(result, "Validator");
        validatableSource.Should().NotBeNull("Validator should be generated");
        validatableSource.Should().Contain("Validate()");
        validatableSource.Should().Contain("\"validation.required\"");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    [Fact]
    public void Generator_WithValidatorOnly_NoSyncValidation_GeneratesAsyncOnlyRegistration()
    {
        const string source = """
                              using Pragmatic.Validation;
                              using Pragmatic.Validation.Attributes;
                              using Pragmatic.Validation.Types;
                              using System.Threading;
                              using System.Threading.Tasks;

                              namespace TestNamespace;

                              // Type WITHOUT sync validation (no attributes)
                              public record CreateUserRequest
                              {
                                  public string Email { get; init; }
                              }

                              // Validator for async validation only
                              [Validator]
                              public class CreateUserValidator : IAsyncValidator<CreateUserRequest>
                              {
                                  public Task<ValidationError> ValidateAsync(
                                      CreateUserRequest instance,
                                      CancellationToken ct = default)
                                  {
                                      return Task.FromResult(ValidationError.Valid);
                                  }
                              }
                              """;

        var result = RunGenerator(source);

        // Should generate DI registration using AddAsyncValidator + AddSyncOnlyValidator
        var registrationSource = GetGeneratedSource(result, "ValidatorRegistration");
        registrationSource.Should().NotBeNull();
        registrationSource.Should().Contain("AddAsyncValidator<TestNamespace.CreateUserValidator, TestNamespace.CreateUserRequest>");
        registrationSource.Should().Contain("AddSyncOnlyValidator<TestNamespace.CreateUserRequest>");
        registrationSource.Should().NotContain("AddValidatorWithComposite",
            "Composite should not be used when type has no sync validation");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    [Fact]
    public void Generator_WithSingletonLifetime_GeneratesSingletonRegistration()
    {
        const string source = """
                              using Pragmatic.Validation;
                              using Pragmatic.Validation.Attributes;
                              using Pragmatic.Validation.Types;
                              using Microsoft.Extensions.DependencyInjection;
                              using System.Threading;
                              using System.Threading.Tasks;

                              namespace TestNamespace;

                              public partial record CacheableRequest
                              {
                                  [Required]
                                  public string Key { get; init; }
                              }

                              [Validator(Lifetime = ServiceLifetime.Singleton)]
                              public class CacheableRequestAsyncValidator : IAsyncValidator<CacheableRequest>
                              {
                                  public Task<ValidationError> ValidateAsync(
                                      CacheableRequest instance,
                                      CancellationToken ct = default)
                                  {
                                      return Task.FromResult(ValidationError.Valid);
                                  }
                              }
                              """;

        var result = RunGenerator(source);

        // Check compilation first to get better error messages
        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));

        var registrationSource = GetGeneratedSource(result, "ValidatorRegistration");
        registrationSource.Should().NotBeNull();
        registrationSource.Should().Contain("Singleton");
        registrationSource.Should().NotContain("Scoped");
        registrationSource.Should().NotContain("Transient");
    }

    [Fact]
    public void Generator_WithMultipleValidators_GeneratesAllRegistrations()
    {
        const string source = """
                              using Pragmatic.Validation;
                              using Pragmatic.Validation.Attributes;
                              using Pragmatic.Validation.Types;
                              using System.Threading;
                              using System.Threading.Tasks;

                              namespace TestNamespace;

                              public partial record UserRequest
                              {
                                  [Required]
                                  public string Name { get; init; }
                              }

                              public partial record OrderRequest
                              {
                                  [Required]
                                  public string OrderNumber { get; init; }
                              }

                              [Validator]
                              public class UserValidator : IAsyncValidator<UserRequest>
                              {
                                  public Task<ValidationError> ValidateAsync(
                                      UserRequest instance,
                                      CancellationToken ct = default)
                                  {
                                      return Task.FromResult(ValidationError.Valid);
                                  }
                              }

                              [Validator]
                              public class OrderValidator : IAsyncValidator<OrderRequest>
                              {
                                  public Task<ValidationError> ValidateAsync(
                                      OrderRequest instance,
                                      CancellationToken ct = default)
                                  {
                                      return Task.FromResult(ValidationError.Valid);
                                  }
                              }
                              """;

        var result = RunGenerator(source);

        // Should generate ISyncValidator for both types
        var sources = GetAllGeneratedSources(result);
        sources.Should().ContainKey("TestNamespace.UserRequest.Validator.g.cs");
        sources.Should().ContainKey("TestNamespace.OrderRequest.Validator.g.cs");

        // Registration should include both validators with composite
        var registrationSource = GetGeneratedSource(result, "ValidatorRegistration");
        registrationSource.Should().Contain("UserValidator");
        registrationSource.Should().Contain("OrderValidator");
        registrationSource.Should().Contain("AddValidatorWithComposite");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    [Fact]
    public void Generator_CompositeValidator_IsRegisteredAtRuntime()
    {
        // CompositeValidator<T> is a runtime class that combines sync + async validation
        // The generator registers it via AddValidatorWithComposite extension method

        const string source = """
                              using Pragmatic.Validation;
                              using Pragmatic.Validation.Attributes;
                              using Pragmatic.Validation.Types;
                              using System.Threading;
                              using System.Threading.Tasks;

                              namespace TestNamespace;

                              public partial record TestRequest
                              {
                                  [Required]
                                  public string Value { get; init; }
                              }

                              [Validator]
                              public class TestValidator : IAsyncValidator<TestRequest>
                              {
                                  public Task<ValidationError> ValidateAsync(
                                      TestRequest instance,
                                      CancellationToken ct = default)
                                  {
                                      return Task.FromResult(ValidationError.Valid);
                                  }
                              }
                              """;

        var result = RunGenerator(source);

        var registrationSource = GetGeneratedSource(result, "ValidatorRegistration");
        registrationSource.Should().NotBeNull();

        // AddValidatorWithComposite registers CompositeValidator<T> as IValidator<T>
        registrationSource.Should()
            .Contain("AddValidatorWithComposite<TestNamespace.TestValidator, TestNamespace.TestRequest>");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    [Fact]
    public void Generator_NoValidators_NoGeneratedFiles()
    {
        const string source = """
                              namespace TestNamespace;

                              public class SimpleClass
                              {
                                  public string Name { get; set; }
                              }
                              """;

        var result = RunGenerator(source);

        // Should not generate registration file
        var registrationSource = GetGeneratedSource(result, "ValidatorRegistration");
        registrationSource.Should().BeNull();
    }

    [Fact]
    public void Generator_TransientLifetime_GeneratesTransientRegistration()
    {
        const string source = """
                              using Pragmatic.Validation;
                              using Pragmatic.Validation.Attributes;
                              using Pragmatic.Validation.Types;
                              using Microsoft.Extensions.DependencyInjection;
                              using System.Threading;
                              using System.Threading.Tasks;

                              namespace TestNamespace;

                              // Type without sync validation - uses TryAddTransient
                              public record LightweightRequest
                              {
                                  public string Value { get; init; }
                              }

                              [Validator(Lifetime = ServiceLifetime.Transient)]
                              public class LightweightValidator : IAsyncValidator<LightweightRequest>
                              {
                                  public Task<ValidationError> ValidateAsync(
                                      LightweightRequest instance,
                                      CancellationToken ct = default)
                                  {
                                      return Task.FromResult(ValidationError.Valid);
                                  }
                              }
                              """;

        var result = RunGenerator(source);

        // Check compilation first to get better error messages
        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));

        var registrationSource = GetGeneratedSource(result, "ValidatorRegistration");
        registrationSource.Should().NotBeNull();
        registrationSource.Should().Contain("Transient");
    }
}