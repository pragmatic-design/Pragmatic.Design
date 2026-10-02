using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Templates;

public class TemporalValidationTemplateTests
{
    [Fact]
    public void MaxActive1_GeneratesValidateTemporalConstraints()
    {
        var model = CreateModel(maxActive: 1);
        var template = new TemporalValidationTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("ValidateTemporalConstraints");
        source.Should().Contain("TemporalOverlapError?");
        source.Should().Contain("activeCount >= 1");
        source.Should().Contain("MaxActiveExceeded");
    }

    [Fact]
    public void MaxActive1_GeneratesAutoClosePrevious()
    {
        var model = CreateModel(maxActive: 1);
        var template = new TemporalValidationTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("AutoClosePrevious");
        source.Should().Contain("record.ValidTo = closedAt");
        source.Should().Contain("return active;");
        source.Should().Contain("static global::System.Collections.Generic.List<");
    }

    [Fact]
    public void MaxActive0_NoAutoClosePrevious()
    {
        var model = CreateModel(maxActive: 0, allowOverlap: false);
        var template = new TemporalValidationTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        // No MaxActive constraint, but overlap check
        source.Should().NotContain("AutoClosePrevious");
        source.Should().Contain("OverlapDetected");
    }

    [Fact]
    public void AllowOverlap_NoOverlapCheck()
    {
        var model = CreateModel(maxActive: 2, allowOverlap: true);
        var template = new TemporalValidationTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("MaxActiveExceeded");
        source.Should().NotContain("OverlapDetected");
    }

    [Fact]
    public void NoConstraints_ReturnsNoSource()
    {
        // MaxActive = 0 and AllowOverlap = true → nothing to validate
        var model = CreateModel(maxActive: 0, allowOverlap: true);
        var template = new TemporalValidationTemplate(model);
        var artifact = template.RenderOutput();

        var hasContent = artifact.Text.Contains("class") == true;
        hasContent.Should().BeFalse();
    }

    [Fact]
    public void GeneratesPartialClass()
    {
        var model = CreateModel(maxActive: 1);
        var template = new TemporalValidationTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("partial class UserRole");
    }

    [Fact]
    public void GeneratesCorrectHintName()
    {
        var model = CreateModel(maxActive: 1);
        var template = new TemporalValidationTemplate(model);
        var artifact = template.RenderOutput();

        artifact.HintName.Should().Contain("UserRole");
        artifact.HintName.Should().Contain("TemporalValidation");
    }

    [Fact]
    public void MaxActive3_GeneratesCorrectThreshold()
    {
        var model = CreateModel(maxActive: 3);
        var template = new TemporalValidationTemplate(model);
        var artifact = template.RenderOutput();
        var source = artifact.Text;

        source.Should().Contain("activeCount >= 3");
        source.Should().Contain("MaxActive = 3");
        // MaxActive != 1 → no AutoClosePrevious
        source.Should().NotContain("AutoClosePrevious");
    }

    private static EntityMetadataModel CreateModel(int maxActive = 1, bool allowOverlap = false)
    {
        return new EntityMetadataModel
        {
            TypeName = "UserRole",
            FullTypeName = "MyApp.Entities.UserRole",
            Namespace = "MyApp.Entities",
            IdType = "Guid",
            IsTemporalRelation = true,
            TemporalMaxActive = maxActive,
            TemporalAllowOverlap = allowOverlap,
            Properties = ImmutableArray<PropertyMetadataModel>.Empty,
            Navigations = ImmutableArray<NavigationMetadataModel>.Empty,
            RelationAttributes = ImmutableArray<RelationAttributeModel>.Empty,
            AllSourceMemberNames = ImmutableArray<string>.Empty,
            GeneratedRelationProperties = ImmutableArray<GeneratedRelationPropertyModel>.Empty
        };
    }
}
