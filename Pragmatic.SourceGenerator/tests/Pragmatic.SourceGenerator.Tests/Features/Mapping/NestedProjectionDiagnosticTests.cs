using Pragmatic.Testing.Assertions;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Mapping.Diagnostics;
using Pragmatic.SourceGenerator.Features.Mapping.Models;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Mapping;

/// <summary>
///     Nested DTO inlining drops complex mappings (converter, concatenation, format,
///     flattening). The model must flag the drop so PRAG0326 is emitted instead of silently
///     degrading the projection. Mirrors the model-condition test style used for query diagnostics.
/// </summary>
public class NestedProjectionDiagnosticTests
{
    [Fact]
    public void PRAG0326_Descriptor_IsWarningWithExpectedId()
    {
        var descriptor = MappingDiagnostics.NestedProjectionMappingDropped;

        descriptor.Id.Should().Be("PRAG0326");
        descriptor.DefaultSeverity.Should().Be(DiagnosticSeverity.Warning);
        descriptor.IsEnabledByDefault.Should().BeTrue();
    }

    [Fact]
    public void DroppedNestedMapping_InProjection_TriggersDiagnosticCondition()
    {
        var model = ProjectionModelWith(new PropertyMappingModel
        {
            PropertyName = "Customer",
            PropertyType = "CustomerDto",
            Resolution = MappingResolution.DirectMatch,
            IsNestedDto = true,
            HasDroppedNestedProjectionMappings = true
        });

        // This is the exact predicate MappingFeature uses to report PRAG0326.
        var shouldReport = model.GenerateProjection
                           && model.Properties[0].HasDroppedNestedProjectionMappings;

        shouldReport.Should().BeTrue();
    }

    [Fact]
    public void NoDroppedNestedMapping_DoesNotTriggerDiagnosticCondition()
    {
        var model = ProjectionModelWith(new PropertyMappingModel
        {
            PropertyName = "Customer",
            PropertyType = "CustomerDto",
            Resolution = MappingResolution.DirectMatch,
            IsNestedDto = true,
            HasDroppedNestedProjectionMappings = false
        });

        var shouldReport = model.GenerateProjection
                           && model.Properties[0].HasDroppedNestedProjectionMappings;

        shouldReport.Should().BeFalse();
    }

    [Fact]
    public void DroppedNestedMapping_WithoutProjection_DoesNotTrigger()
    {
        // FromEntity-only DTOs honor complex mappings at runtime, so no projection diagnostic.
        var model = ProjectionModelWith(
            new PropertyMappingModel
            {
                PropertyName = "Customer",
                PropertyType = "CustomerDto",
                Resolution = MappingResolution.DirectMatch,
                IsNestedDto = true,
                HasDroppedNestedProjectionMappings = true
            },
            generateProjection: false);

        var shouldReport = model.GenerateProjection
                           && model.Properties[0].HasDroppedNestedProjectionMappings;

        shouldReport.Should().BeFalse();
    }

    private static MappingModel ProjectionModelWith(PropertyMappingModel prop, bool generateProjection = true) => new()
    {
        Namespace = "MyApp",
        TypeName = "OrderDto",
        Accessibility = "public",
        TypeKind = "class",
        HasMapFrom = true,
        GenerateProjection = generateProjection,
        SourceTypeFullName = "MyApp.Domain.Order",
        SourceTypeName = "Order",
        Properties = [prop]
    };
}
