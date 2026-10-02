using System.Collections.Immutable;
using Pragmatic.SourceGenerator.Features.Serialization.Models;
using Pragmatic.SourceGenerator.Features.Serialization.Templates;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Tests.Features.Serialization;

/// <summary>
///     Verify snapshot tests for the generated JsonSerializerContext. The emitted shape must match the
///     runtime-verified targets (HandAuthoredContextSpikeTests + HandAuthoredCollectionSpikeTests).
/// </summary>
public class SerializationSnapshotTests
{
    [Fact]
    public Task JsonContext_ObjectLeavesAndCollections_MatchesSnapshot()
    {
        // Type expressions mirror the extractor's SymbolDisplayFormat.FullyQualifiedFormat output
        // (UseSpecialTypes → primitive keywords like string/int; global:: on other types).
        var order = new JsonObjectModel(
            TypeExpr: "global::MyApp.Events.OrderPlaced",
            MethodToken: "MyApp_Events_OrderPlaced",
            Properties: ImmutableArray.Create(
                new JsonPropertyModel("OrderId", "orderId", "string", IsValueType: false, IsInitOnly: false, DeclaringTypeExpr: "global::MyApp.Events.OrderPlaced"),
                new JsonPropertyModel("Quantity", "quantity", "int", IsValueType: true, IsInitOnly: false, DeclaringTypeExpr: "global::MyApp.Events.OrderPlaced"),
                new JsonPropertyModel("Status", "status", "global::MyApp.Events.OrderStatus", IsValueType: true, IsInitOnly: false, DeclaringTypeExpr: "global::MyApp.Events.OrderPlaced"),
                new JsonPropertyModel("Tags", "tags", "global::System.Collections.Generic.List<string>", IsValueType: false, IsInitOnly: false, DeclaringTypeExpr: "global::MyApp.Events.OrderPlaced")));

        var model = new JsonContextModel(
            Namespace: "MyApp.Events",
            Objects: ImmutableArray.Create(order),
            Leaves: ImmutableArray.Create(
                new JsonLeafModel("string", "global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.StringConverter"),
                new JsonLeafModel("int", "global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.Int32Converter"),
                new JsonLeafModel("global::MyApp.Events.OrderStatus", "global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.GetEnumConverter<global::MyApp.Events.OrderStatus>(options)")),
            Collections: ImmutableArray.Create(
                new JsonCollectionModel("global::System.Collections.Generic.List<string>",
                    "global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreateListInfo<global::System.Collections.Generic.List<string>, string>(options, new global::System.Text.Json.Serialization.Metadata.JsonCollectionInfoValues<global::System.Collections.Generic.List<string>> { ObjectCreator = static () => new global::System.Collections.Generic.List<string>() })")));

        var source = new PragmaticJsonContextTemplate(model, "_Infra.Json.Context.g.cs").RenderOutput().Text;
        return Verify(source);
    }
}
