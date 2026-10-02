using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Validation.Models;
using Pragmatic.SourceGenerator.Features.Validation.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Validation;

/// <summary>
/// Template unit tests — pure model → output, zero Roslyn compilation.
/// Verifies hint name convention and basic code generation.
/// </summary>
public class ValidationTemplateTests
{
    // ─── ValidatableTemplate ─────────────────────────────────────────────────

    [Fact]
    public void ValidatableTemplate_HintName_FollowsVirtualFolderConvention()
    {
        var model = BuildValidatableModel("MyApp.Catalog", "CreatePropertyCommand");

        var artifact = new ValidatableTemplate(model).RenderOutput();

        artifact.HintName.Should().Be("MyApp.Catalog.CreatePropertyCommand.Validator.g.cs");
    }

    [Fact]
    public void ValidatableTemplate_NoNamespace_HintNameStillCorrect()
    {
        var model = BuildValidatableModel("", "MyDto");

        var artifact = new ValidatableTemplate(model).RenderOutput();

        artifact.HintName.Should().Be("MyDto.Validator.g.cs");
    }

    [Fact]
    public void ValidatableTemplate_Required_GeneratesRequiredCheck()
    {
        var model = BuildValidatableModel("MyApp", "CreateCmd", props:
        [
            new()
            {
                PropertyName = "Name",
                PropertyType = "string",
                IsString = true,
                Attributes = ImmutableArray.Create(
                    new ValidationAttributeModel { AttributeType = "Required", Kind = ValidationKind.Required, AttributeName = "Required", MessageKey = "Required" })
            }
        ]);

        var source = new ValidatableTemplate(model).RenderOutput().Text;

        source.Should().Contain("string.IsNullOrEmpty(Name)");
        source.Should().Contain("error = error.WithFor(nameof(Name)");
    }

    [Fact]
    public void ValidatableTemplate_IsEntity_GeneratesChangeAwareOverload()
    {
        var model = BuildValidatableModel("MyApp", "Order", isEntity: true, props:
        [
            new()
            {
                PropertyName = "Title",
                PropertyType = "string",
                IsString = true,
                Attributes = ImmutableArray.Create(
                    new ValidationAttributeModel { AttributeType = "Required", Kind = ValidationKind.Required, AttributeName = "Required", MessageKey = "Required" })
            }
        ]);

        var source = new ValidatableTemplate(model).RenderOutput().Text;

        source.Should().Contain("IReadOnlySet<string>?");
        source.Should().Contain("modifiedProperties");
    }

    // ─── AsyncValidatorBindingsTemplate ──────────────────────────────────────

    [Fact]
    public void AsyncValidatorBindingsTemplate_HintName_FollowsVirtualFolderConvention()
    {
        var model = new AsyncValidatorBindingsModel
        {
            Namespace = "MyApp.Domain",
            TypeName = "Order",
            FullTypeName = "MyApp.Domain.Order",
            Bindings = ImmutableArray<AsyncValidatorBindingModel>.Empty
        };

        var artifact = new AsyncValidatorBindingsTemplate(model).RenderOutput();

        artifact.HintName.Should().Be("MyApp.Domain.Order.AsyncValidatorBindings.g.cs");
    }

    [Fact]
    public void AsyncValidatorBindingsTemplate_GeneratesShouldInvokeMethod()
    {
        var model = new AsyncValidatorBindingsModel
        {
            Namespace = "MyApp.Domain",
            TypeName = "Order",
            FullTypeName = "MyApp.Domain.Order",
            Bindings = ImmutableArray.Create(
                new AsyncValidatorBindingModel { ValidatorFullTypeName = "MyApp.Validators.OrderValidator", TriggerPropertyName = null })
        };

        var source = new AsyncValidatorBindingsTemplate(model).RenderOutput().Text;

        source.Should().Contain("OrderAsyncValidatorBindings");
        source.Should().Contain("IAsyncValidatorBindings<Order>");
        source.Should().Contain("ShouldInvoke");
    }

    // ─── ValidatorRegistrationTemplate ───────────────────────────────────────

    [Fact]
    public void ValidatorRegistrationTemplate_HintName_FollowsVirtualFolderConvention()
    {
        var validators = ImmutableArray.Create(BuildValidatorModel("MyApp.Validators", "OrderValidator", "MyApp.Domain.Order"));
        var template = new ValidatorRegistrationTemplate(validators, ImmutableArray<AsyncValidatorBindingsModel>.Empty);

        var artifact = template.RenderOutput();

        artifact.HintName.Should().Be("_Infra.Validation.ValidatorRegistration.g.cs");
    }

    [Fact]
    public void ValidatorRegistrationTemplate_GeneratesAddGeneratedValidators()
    {
        var validators = ImmutableArray.Create(BuildValidatorModel("MyApp.Validators", "OrderValidator", "MyApp.Domain.Order"));
        var template = new ValidatorRegistrationTemplate(validators, ImmutableArray<AsyncValidatorBindingsModel>.Empty);

        var source = template.RenderOutput().Text;

        source.Should().Contain("AddGeneratedValidators");
        source.Should().Contain("IServiceCollection");
        source.Should().Contain("OrderValidator");
    }

    // ─── ValidationMetadataTemplate ──────────────────────────────────────────

    [Fact]
    public void ValidationMetadataTemplate_HintName_FollowsVirtualFolderConvention()
    {
        var validators = ImmutableArray.Create(BuildValidatorModel("MyApp.Validators", "OrderValidator", "MyApp.Domain.Order"));
        var template = new ValidationMetadataTemplate(validators, ImmutableArray<AsyncValidatorBindingsModel>.Empty, false);

        var artifact = template.RenderOutput();

        artifact.HintName.Should().Be("_Metadata.Validation.g.cs");
    }

    [Fact]
    public void ValidationMetadataTemplate_NoValidators_ReturnsEmpty()
    {
        var template = new ValidationMetadataTemplate(
            ImmutableArray<ValidatorModel>.Empty,
            ImmutableArray<AsyncValidatorBindingsModel>.Empty,
            false);

        var artifact = template.RenderOutput();

        artifact.Text.Length.Should().Be(0);
    }

    [Fact]
    public void ValidationMetadataTemplate_GeneratesPragmaticMetadataAttribute()
    {
        var validators = ImmutableArray.Create(BuildValidatorModel("MyApp.Validators", "OrderValidator", "MyApp.Domain.Order"));
        var template = new ValidationMetadataTemplate(validators, ImmutableArray<AsyncValidatorBindingsModel>.Empty, false);

        var source = template.RenderOutput().Text;

        source.Should().Contain("[assembly: PragmaticMetadata(MetadataCategory.Validation");
        source.Should().Contain("validatorsCount");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static ValidatableModel BuildValidatableModel(
        string ns,
        string typeName,
        bool isEntity = false,
        ImmutableArray<PropertyValidationModel>? props = null)
    {
        return new ValidatableModel
        {
            Namespace = ns,
            TypeName = typeName,
            Accessibility = "public",
            TypeKind = "class",
            IsPartial = true,
            IsRecord = false,
            IsValueType = false,
            IsEntity = isEntity,
            Properties = props ?? ImmutableArray<PropertyValidationModel>.Empty
        };
    }

    private static ValidatorModel BuildValidatorModel(
        string ns,
        string validatorTypeName,
        string validatedTypeFullName)
    {
        var validatedTypeName = validatedTypeFullName.Split('.').Last();
        return new ValidatorModel
        {
            Namespace = ns,
            ValidatorTypeName = validatorTypeName,
            ValidatorFullName = $"{ns}.{validatorTypeName}",
            Accessibility = "internal",
            ValidatedType = new ValidatedTypeModel
            {
                Namespace = string.Join(".", validatedTypeFullName.Split('.').SkipLast(1)),
                TypeName = validatedTypeName,
                FullName = validatedTypeFullName,
                IsValueType = false
            },
            Lifetime = ServiceLifetimeKind.Scoped,
            ValidatedTypeHasSyncValidation = true
        };
    }
}
