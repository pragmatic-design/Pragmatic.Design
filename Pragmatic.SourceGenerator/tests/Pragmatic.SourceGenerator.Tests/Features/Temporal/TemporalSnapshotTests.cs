using System.Collections.Immutable;
using Pragmatic.SourceGenerator.Features.Temporal.Models;
using Pragmatic.SourceGenerator.Features.Temporal.Templates;

namespace Pragmatic.SourceGenerator.Tests.Features.Temporal;

/// <summary>
///     Verify snapshots for the generated temporal behaviors registration and its
///     Composition metadata attribute.
/// </summary>
public class TemporalSnapshotTests
{
    private static ImmutableArray<TemporalBehaviorPropertyModel> SampleModels() =>
    [
        new TemporalBehaviorPropertyModel
        {
            ContainingTypeFqn = "MyApp.Orders.Dtos.OrderResponse",
            ContainingNamespace = "MyApp.Orders.Dtos",
            PropertyName = "CreatedAt",
            Behavior = "ToClientTimezone",
            IsSupportedPropertyType = true,
            PropertyTypeDisplay = "System.DateTimeOffset"
        },
        new TemporalBehaviorPropertyModel
        {
            ContainingTypeFqn = "MyApp.Orders.Dtos.CreateOrderRequest",
            ContainingNamespace = "MyApp.Orders.Dtos",
            PropertyName = "RequestedDelivery",
            Behavior = "FromClientTimezone",
            IsSupportedPropertyType = true,
            PropertyTypeDisplay = "System.DateTime"
        }
    ];

    [Fact]
    public Task BehaviorsRegistration_MatchesSnapshot()
    {
        var source = new TemporalBehaviorsTemplate(SampleModels()).RenderOutput().Text;
        return Verify(source);
    }

    [Fact]
    public Task BehaviorsMetadata_MatchesSnapshot()
    {
        var template = new TemporalBehaviorsTemplate(SampleModels());
        var source = new TemporalBehaviorsMetadataTemplate(
            template.RegistrationMethodFqn, 2).RenderOutput().Text;
        return Verify(source);
    }
}
