using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Configuration.Models;
using Pragmatic.SourceGenerator.Features.Configuration.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Configuration;

/// <summary>
/// Template unit tests — pure model → output, zero Roslyn compilation.
/// </summary>
public class ConfigurationTemplateTests
{
    [Fact]
    public void RenderOutput_HintName_FollowsVirtualFolderConvention()
    {
        var model = BuildModel("MyApp.Options", "SmtpOptions", "Smtp");

        var artifact = new RegistrationTemplate(model).RenderOutput();

        artifact.HintName.Should().Be("MyApp.Options.SmtpOptions.Registration.g.cs");
    }

    [Fact]
    public void RenderOutput_GeneratesAddOptionsExtensionWithDedupedSuffix()
    {
        var model = BuildModel("MyApp", "CacheOptions", "Cache");

        var source = new RegistrationTemplate(model).RenderOutput().Text;

        // NamingHelper dedups "Options" so the method is AddCacheOptions, not AddCacheOptionsOptions.
        source.Should().Contain("class CacheOptionsConfigurationExtensions");
        source.Should().Contain("AddCacheOptions(");
    }

    [Fact]
    public void RenderOutput_BindsConfiguredSectionPath()
    {
        var model = BuildModel("MyApp", "SmtpOptions", "Email:Smtp");

        var source = new RegistrationTemplate(model).RenderOutput().Text;

        source.Should().Contain("configuration.GetSection(\"Email:Smtp\")");
        source.Should().Contain("AddOptions<global::MyApp.SmtpOptions>()");
    }

    [Fact]
    public void RenderOutput_WithValidation_EmitsValidateDataAnnotations()
    {
        var model = BuildModel("MyApp", "SmtpOptions", "Smtp", hasValidation: true);

        var source = new RegistrationTemplate(model).RenderOutput().Text;

        source.Should().Contain(".ValidateDataAnnotations()");
    }

    [Fact]
    public void RenderOutput_WithoutValidation_OmitsValidateDataAnnotations()
    {
        var model = BuildModel("MyApp", "SmtpOptions", "Smtp", hasValidation: false);

        var source = new RegistrationTemplate(model).RenderOutput().Text;

        source.Should().NotContain(".ValidateDataAnnotations()");
    }

    [Fact]
    public void RenderOutput_ValidateOnStart_EmitsValidateOnStart()
    {
        var model = BuildModel("MyApp", "SmtpOptions", "Smtp", validateOnStart: true);

        var source = new RegistrationTemplate(model).RenderOutput().Text;

        source.Should().Contain(".ValidateOnStart()");
    }

    private static ConfigurationModel BuildModel(
        string ns,
        string typeName,
        string sectionPath,
        bool hasValidation = false,
        bool validateOnStart = false) => new()
        {
            TypeName = typeName,
            Namespace = ns,
            Accessibility = "public",
            TypeKind = "class",
            SectionPath = sectionPath,
            // HasValidation is computed from Properties; add a validated property to enable it.
            Properties = hasValidation
                ? ImmutableArray.Create(new ConfigurationPropertyModel
                {
                    Name = "Host",
                    TypeFullName = "string",
                    HasValidationAttributes = true
                })
                : ImmutableArray<ConfigurationPropertyModel>.Empty,
            ValidateOnStart = validateOnStart,
            IsPartial = true,
            IsStaticOrAbstract = false
        };
}
