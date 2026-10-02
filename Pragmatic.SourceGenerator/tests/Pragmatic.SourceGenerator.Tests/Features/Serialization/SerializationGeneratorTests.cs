using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen.Testing;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Serialization;

/// <summary>
///     End-to-end: the unified generator discovers a message payload and emits the JsonSerializerContext
///     when the assembly opts in. Marker types are stubbed so FeatureDetector + the attribute pipelines
///     trigger without referencing the runtime packages.
/// </summary>
public class SerializationGeneratorTests
{
    private const string Stubs = """
        namespace Pragmatic.Serialization
        {
            public sealed class PragmaticJsonOptions { }
            [System.AttributeUsage(System.AttributeTargets.Assembly)]
            public sealed class PragmaticGenerateJsonContextAttribute : System.Attribute { }
        }
        namespace Pragmatic.Messaging
        {
            public interface IMessageHandler<T> { }
            namespace Attributes
            {
                [System.AttributeUsage(System.AttributeTargets.Class)]
                public sealed class MessageHandlerAttribute : System.Attribute { }
            }
        }
        """;

    [Fact]
    public void OptIn_MessagePayload_EmitsJsonContext()
    {
        var source = "[assembly: Pragmatic.Serialization.PragmaticGenerateJsonContext]\n" + Stubs + """

            namespace App.Events
            {
                public class OrderPlaced
                {
                    public string OrderId { get; set; }
                    public int Quantity { get; set; }
                }
            }
            namespace App.Handlers
            {
                using Pragmatic.Messaging;
                using Pragmatic.Messaging.Attributes;
                [MessageHandler]
                public class OrderPlacedHandler : IMessageHandler<App.Events.OrderPlaced> { }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);
        var generated = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);

        var context = generated.Where(kv => kv.Key.Contains("Json.Context")).Select(kv => kv.Value).FirstOrDefault();
        context.Should().NotBeNull("the generator should emit a JSON context when opted in");
        context!.Should().Contain("class PragmaticJsonContext")
            .And.Contain("JsonSerializerContext")
            .And.Contain("Create_App_Events_OrderPlaced")
            .And.Contain("CreateValueInfo<string>")
            .And.Contain("CreateValueInfo<int>")
            .And.Contain("\"orderId\"")
            .And.Contain("\"quantity\"");

        generated.Keys.Should().Contain(k => k.Contains("Json.Registration"));
        generated.Keys.Should().Contain(k => k.Contains("JsonContexts"));
    }

    [Fact]
    public void OptIn_NestedDto_CoversClosure()
    {
        var source = "[assembly: Pragmatic.Serialization.PragmaticGenerateJsonContext]\n" + Stubs + """

            namespace App.Events
            {
                public class Customer { public string Name { get; set; } }
                public class Order
                {
                    public string Id { get; set; }
                    public Customer Buyer { get; set; }
                }
            }
            namespace App.Handlers
            {
                using Pragmatic.Messaging;
                using Pragmatic.Messaging.Attributes;
                [MessageHandler]
                public class OrderHandler : IMessageHandler<App.Events.Order> { }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);
        var context = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Where(kv => kv.Key.Contains("Json.Context")).Select(kv => kv.Value).FirstOrDefault();

        context.Should().NotBeNull();
        // Both the root and the nested DTO get a JsonTypeInfo; the root's property references the nested type.
        context!.Should().Contain("Create_App_Events_Order")
            .And.Contain("Create_App_Events_Customer")
            .And.Contain("typeof(global::App.Events.Customer)")
            .And.Contain("\"buyer\"");
    }

    [Fact]
    public void OptIn_CollectionProperties_Covered()
    {
        var source = "[assembly: Pragmatic.Serialization.PragmaticGenerateJsonContext]\n" + Stubs + """

            namespace App.Events
            {
                public class Bag
                {
                    public System.Collections.Generic.List<string> Tags { get; set; }
                    public int[] Numbers { get; set; }
                    public System.Collections.Generic.Dictionary<string, int> Counts { get; set; }
                }
            }
            namespace App.Handlers
            {
                using Pragmatic.Messaging;
                using Pragmatic.Messaging.Attributes;
                [MessageHandler]
                public class BagHandler : IMessageHandler<App.Events.Bag> { }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);
        var context = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Where(kv => kv.Key.Contains("Json.Context")).Select(kv => kv.Value).FirstOrDefault();

        context.Should().NotBeNull("collection properties must no longer defer the root");
        context!.Should().Contain("CreateListInfo<global::System.Collections.Generic.List<string>")
            .And.Contain("CreateArrayInfo<int>")
            .And.Contain("CreateDictionaryInfo<global::System.Collections.Generic.Dictionary<string, int>");
    }

    [Fact]
    public void OptIn_NullableValueType_Covered()
    {
        var source = "[assembly: Pragmatic.Serialization.PragmaticGenerateJsonContext]\n" + Stubs + """

            namespace App.Events { public class N { public int? Optional { get; set; } } }
            namespace App.Handlers
            {
                using Pragmatic.Messaging;
                using Pragmatic.Messaging.Attributes;
                [MessageHandler]
                public class NHandler : IMessageHandler<App.Events.N> { }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);
        var context = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Where(kv => kv.Key.Contains("Json.Context")).Select(kv => kv.Value).FirstOrDefault();

        context.Should().NotBeNull();
        context!.Should().Contain("CreateValueInfo<int?>")
            .And.Contain("GetNullableConverter<int>");
    }

    [Fact]
    public void OptIn_Polymorphic_Covered()
    {
        var source = "[assembly: Pragmatic.Serialization.PragmaticGenerateJsonContext]\n" + Stubs + """

            namespace App.Events
            {
                using System.Text.Json.Serialization;

                [JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
                [JsonDerivedType(typeof(Circle), "circle")]
                [JsonDerivedType(typeof(Square), "square")]
                public abstract class Shape { }
                public sealed class Circle : Shape { public double Radius { get; set; } }
                public sealed class Square : Shape { public double Side { get; set; } }
            }
            namespace App.Handlers
            {
                using Pragmatic.Messaging;
                using Pragmatic.Messaging.Attributes;
                [MessageHandler]
                public class ShapeHandler : IMessageHandler<App.Events.Shape> { }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            source, GeneratorTestHelper.FromType<System.Text.Json.Serialization.JsonPolymorphicAttribute>());
        var context = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Where(kv => kv.Key.Contains("Json.Context")).Select(kv => kv.Value).FirstOrDefault();

        context.Should().NotBeNull();
        context!.Should().Contain("PolymorphismOptions")
            .And.Contain("TypeDiscriminatorPropertyName = \"$kind\"")
            .And.Contain("new JsonDerivedType(typeof(global::App.Events.Circle), \"circle\")")
            .And.Contain("Create_App_Events_Circle")
            .And.Contain("Create_App_Events_Square");
    }

    [Fact]
    public void OptIn_Saga_CoversStateAndCompensation()
    {
        var sagaStubs = """
            namespace Pragmatic.Messaging.Attributes
            {
                [System.AttributeUsage(System.AttributeTargets.Class)]
                public sealed class SagaAttribute<TState> : System.Attribute where TState : struct, System.Enum { }
                [System.AttributeUsage(System.AttributeTargets.Method)]
                public sealed class CompensateWithAttribute<TAction> : System.Attribute { }
            }
            """;
        var source = "[assembly: Pragmatic.Serialization.PragmaticGenerateJsonContext]\n" + Stubs + sagaStubs + """

            namespace App.Sagas
            {
                using Pragmatic.Messaging.Attributes;
                public enum OrderState { Started, Paid }
                public class RefundAction { public string OrderId { get; set; } public decimal Amount { get; set; } }

                [Saga<OrderState>]
                public class OrderSaga
                {
                    public System.Guid Id { get; set; }
                    public OrderState State { get; set; }
                    public string CorrelationId { get; set; }

                    [CompensateWith<RefundAction>]
                    public void OnPaid() { }
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);
        var context = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Where(kv => kv.Key.Contains("Json.Context")).Select(kv => kv.Value).FirstOrDefault();

        context.Should().NotBeNull("the saga instance is persisted as JSON and must be covered");
        context!.Should().Contain("Create_App_Sagas_OrderSaga")
            .And.Contain("Create_App_Sagas_RefundAction")
            // The enum's converter comes from the options, not from a baked-in numeric one. This used
            // to assert GetEnumConverter<T>, which is how it worked rather than what it must do: the
            // host registers JsonStringEnumConverter, so a baked-in numeric converter made enums
            // serialize as "Role" without the context and 0 with it. Turning on <PublishAot> then
            // changed the wire format of every enum in the API, silently.
            .And.Contain("EnumConverter<global::App.Sagas.OrderState>(options)");
    }

    [Fact]
    public void OptIn_MappingDto_CoversHttpBoundary()
    {
        var mapStubs = """
            namespace Pragmatic.Mapping.Attributes
            {
                [System.AttributeUsage(System.AttributeTargets.Class)]
                public sealed class MapFromAttribute<TSource> : System.Attribute { }
                [System.AttributeUsage(System.AttributeTargets.Class)]
                public sealed class MapToAttribute<TTarget> : System.Attribute { }
            }
            """;
        var source = "[assembly: Pragmatic.Serialization.PragmaticGenerateJsonContext]\n" + Stubs + mapStubs + """

            namespace App.Api
            {
                using Pragmatic.Mapping.Attributes;
                public class Customer { public string Name { get; set; } }

                [MapFrom<Customer>]
                public class CustomerDto
                {
                    public string Name { get; set; }
                    public int Age { get; set; }
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);
        var context = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Where(kv => kv.Key.Contains("Json.Context")).Select(kv => kv.Value).FirstOrDefault();

        context.Should().NotBeNull("mapping DTOs are HTTP request/response shapes and must be covered");
        context!.Should().Contain("Create_App_Api_CustomerDto")
            .And.Contain("\"name\"")
            .And.Contain("\"age\"");
    }

    [Fact]
    public void OptIn_InitOnlyDto_CoveredViaUnsafeAccessor()
    {
        var source = "[assembly: Pragmatic.Serialization.PragmaticGenerateJsonContext]\n" + Stubs + """

            namespace App.Api
            {
                public class ProfileDto
                {
                    public string Name { get; init; }
                    public int Age { get; init; }
                }
            }
            namespace App.Handlers
            {
                using Pragmatic.Messaging;
                using Pragmatic.Messaging.Attributes;
                [MessageHandler]
                public class ProfileHandler : IMessageHandler<App.Api.ProfileDto> { }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);
        var context = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Where(kv => kv.Key.Contains("Json.Context")).Select(kv => kv.Value).FirstOrDefault();

        context.Should().NotBeNull("init-only DTOs are now covered via [UnsafeAccessor] setters");
        context!.Should().Contain("Create_App_Api_ProfileDto")
            .And.Contain("[UnsafeAccessor(UnsafeAccessorKind.Method, Name = \"set_Name\")]")
            .And.Contain("Set_App_Api_ProfileDto_Name")
            .And.Contain("static () => new global::App.Api.ProfileDto()");
    }

    [Fact]
    public void OptIn_PositionalRecord_CoveredViaUnsafeConstructor()
    {
        var source = "[assembly: Pragmatic.Serialization.PragmaticGenerateJsonContext]\n" + Stubs + """

            namespace App.Api
            {
                public record OrderLine(string Sku, int Quantity);
            }
            namespace App.Handlers
            {
                using Pragmatic.Messaging;
                using Pragmatic.Messaging.Attributes;
                [MessageHandler]
                public class OrderLineHandler : IMessageHandler<App.Api.OrderLine> { }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);
        var context = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Where(kv => kv.Key.Contains("Json.Context")).Select(kv => kv.Value).FirstOrDefault();

        context.Should().NotBeNull("positional records are now covered via an [UnsafeAccessor] constructor");
        context!.Should().Contain("Create_App_Api_OrderLine")
            .And.Contain("[UnsafeAccessor(UnsafeAccessorKind.Constructor)]")
            .And.Contain("Ctor_App_Api_OrderLine")
            .And.Contain("static () => Ctor_App_Api_OrderLine(default!, default!)")
            // Positional record props are init-only → assigned through UnsafeAccessor setters.
            .And.Contain("Set_App_Api_OrderLine_Sku");
    }

    [Fact]
    public void OptIn_DomainEventHandler_CoversEventPayload()
    {
        // Regression for the AttributeNames.EventHandler namespace bug: [EventHandler] lives in
        // Pragmatic.Events.Attributes; when the constant pointed at Pragmatic.Actions.Attributes the
        // pipeline never fired and domain-event types were silently missing from the JSON context
        // (leaving the outbox serialize/deserialize on reflection).
        var eventStubs = """
            namespace Pragmatic.Events
            {
                public interface IDomainEventHandler<T> { }
                namespace Attributes
                {
                    [System.AttributeUsage(System.AttributeTargets.Class)]
                    public sealed class EventHandlerAttribute : System.Attribute { }
                }
            }
            """;
        var source = "[assembly: Pragmatic.Serialization.PragmaticGenerateJsonContext]\n" + Stubs + eventStubs + """

            namespace App.Events
            {
                public class ReservationCreated
                {
                    public string ReservationId { get; set; }
                    public decimal Total { get; set; }
                }
            }
            namespace App.Handlers
            {
                using Pragmatic.Events;
                using Pragmatic.Events.Attributes;
                [EventHandler]
                public class ReservationCreatedHandler : IDomainEventHandler<App.Events.ReservationCreated> { }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);
        var context = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Where(kv => kv.Key.Contains("Json.Context")).Select(kv => kv.Value).FirstOrDefault();

        context.Should().NotBeNull("domain-event payloads are serialized through the outbox and must be covered");
        context!.Should().Contain("Create_App_Events_ReservationCreated")
            .And.Contain("\"reservationId\"")
            .And.Contain("\"total\"");
    }

    [Fact]
    public void NoOptIn_MessagePayload_EmitsNoJsonContext()
    {
        var source = Stubs + """
            namespace App.Events { public class OrderPlaced { public string OrderId { get; set; } } }
            namespace App.Handlers
            {
                using Pragmatic.Messaging;
                using Pragmatic.Messaging.Attributes;
                [MessageHandler]
                public class OrderPlacedHandler : IMessageHandler<App.Events.OrderPlaced> { }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);
        var generated = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);

        generated.Keys.Should().NotContain(k => k.Contains("Json.Context"));
    }

    [Fact]
    public void OptIn_StreamingEndpointItem_IsCoveredByJsonContext()
    {
        // SSE items cross the HTTP boundary per event: TItem of StreamingEndpoint<TItem,...>
        // (and StreamingDomainAction<TItem>) must land in the generated context.
        var source = "[assembly: Pragmatic.Serialization.PragmaticGenerateJsonContext]\n" + Stubs + """

            namespace Pragmatic.Endpoints
            {
                public enum HttpVerb { Get, Post }
                namespace Attributes
                {
                    [System.AttributeUsage(System.AttributeTargets.Class)]
                    public sealed class EndpointAttribute : System.Attribute
                    {
                        public EndpointAttribute(Pragmatic.Endpoints.HttpVerb method, string route) { }
                    }
                }
                namespace Base
                {
                    public abstract class StreamingEndpoint<TItem> { }
                }
            }
            namespace App.Feed
            {
                public class TickDto
                {
                    public string Symbol { get; set; }
                    public decimal Price { get; set; }
                }

                [Pragmatic.Endpoints.Attributes.Endpoint(Pragmatic.Endpoints.HttpVerb.Get, "/ticks")]
                public partial class TicksEndpoint : Pragmatic.Endpoints.Base.StreamingEndpoint<App.Feed.TickDto> { }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);
        var generated = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);

        var context = generated.Where(kv => kv.Key.Contains("Json.Context")).Select(kv => kv.Value).FirstOrDefault();
        context.Should().NotBeNull("the streaming item type is a JSON root");
        context!.Should().Contain("Create_App_Feed_TickDto")
            .And.Contain("\"symbol\"")
            .And.Contain("\"price\"");
    }
}
