using Pragmatic.SourceGen;
using Pragmatic.SourceGen.Testing;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Core;

/// <summary>
///     Incrementality regression for the Layer-2 integration features (Actions, Endpoints, Messaging,
///     Composition, Resource). Same contract as <see cref="CapabilityIncrementalityTests"/>: adding an
///     unrelated class must not re-run a feature's transforms or aggregates.
/// </summary>
public class IntegrationIncrementalityTests
{
    // ── Actions ──

    private const string ActionsSource = """
        namespace Pragmatic.Actions.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class DomainActionAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class BoundaryAttribute : System.Attribute { }
        }
        namespace Pragmatic.Actions.Mutation
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class MutationAttribute : System.Attribute { }
        }
        namespace MyApp.Billing
        {
            [Pragmatic.Actions.Attributes.Boundary]
            public partial class BillingBoundary { }

            [Pragmatic.Actions.Attributes.DomainAction]
            public partial class RefundInvoice { public System.Guid InvoiceId { get; set; } }

            [Pragmatic.Actions.Mutation.Mutation]
            public partial class UpdateInvoice { public System.Guid InvoiceId { get; set; } }
        }
        """;

    [Fact]
    public void Actions_AddingUnrelatedClass_KeepsPipelineCached()
    {
        var result = GeneratorTestHelper.RunGeneratorIncremental<PragmaticSourceGenerator>(
            ActionsSource, IncrementalityAssert.UnrelatedAddition);

        IncrementalityAssert.StepsStayCached(result,
            TrackingNames.ActionsActions,
            TrackingNames.ActionsAllActions,
            TrackingNames.ActionsMutations,
            TrackingNames.ActionsAllMutations,
            TrackingNames.ActionsBoundaries);
    }

    // ── Endpoints ──

    private const string EndpointsSource = """
        namespace Pragmatic.Endpoints.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class EndpointAttribute : System.Attribute
            {
                public EndpointAttribute(string route) { }
            }
        }
        namespace MyApp.Api
        {
            [Pragmatic.Endpoints.Attributes.Endpoint("/invoices/{invoiceId}")]
            public partial class GetInvoiceEndpoint
            {
                public System.Guid InvoiceId { get; set; }
            }
        }
        """;

    [Fact]
    public void Endpoints_AddingUnrelatedClass_KeepsPipelineCached()
    {
        var result = GeneratorTestHelper.RunGeneratorIncremental<PragmaticSourceGenerator>(
            EndpointsSource, IncrementalityAssert.UnrelatedAddition);

        IncrementalityAssert.StepsStayCached(result,
            TrackingNames.EndpointsEndpoints,
            TrackingNames.EndpointsManualEndpoints,
            TrackingNames.EndpointsAllEndpoints);
    }

    // ── Messaging ──

    private const string MessagingSource = """
        namespace Pragmatic.Messaging
        {
            public sealed record MessageContext(string MessageId);
            public interface IMessageHandler<in T>
            {
                System.Threading.Tasks.Task HandleAsync(T message, MessageContext context, System.Threading.CancellationToken ct = default);
            }
            public interface IRequestHandler<in TRequest, TResponse>
            {
                System.Threading.Tasks.Task<TResponse> HandleAsync(TRequest request, System.Threading.CancellationToken ct = default);
            }
        }
        namespace Pragmatic.Messaging.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class MessageHandlerAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class RequestHandlerAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class SagaAttribute<TState> : System.Attribute where TState : struct { }
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class SagaStartAttribute : System.Attribute { }
        }
        namespace MyApp.Orders
        {
            public sealed class OrderPlaced { public System.Guid OrderId { get; set; } }

            [Pragmatic.Messaging.Attributes.MessageHandler]
            public sealed partial class OrderPlacedHandler : Pragmatic.Messaging.IMessageHandler<OrderPlaced>
            {
                public System.Threading.Tasks.Task HandleAsync(OrderPlaced message, Pragmatic.Messaging.MessageContext context, System.Threading.CancellationToken ct = default)
                    => System.Threading.Tasks.Task.CompletedTask;
            }

            public sealed class GetOrder { public System.Guid OrderId { get; set; } }
            public sealed class OrderView { public System.Guid OrderId { get; set; } }

            [Pragmatic.Messaging.Attributes.RequestHandler]
            public sealed partial class GetOrderHandler : Pragmatic.Messaging.IRequestHandler<GetOrder, OrderView>
            {
                public System.Threading.Tasks.Task<OrderView> HandleAsync(GetOrder request, System.Threading.CancellationToken ct = default)
                    => System.Threading.Tasks.Task.FromResult(new OrderView());
            }

            public enum OrderState { Started, Done }

            [Pragmatic.Messaging.Attributes.Saga<OrderState>]
            public sealed partial class OrderSaga { }
        }
        """;

    [Fact]
    public void Messaging_AddingUnrelatedClass_KeepsPipelineCached()
    {
        var result = GeneratorTestHelper.RunGeneratorIncremental<PragmaticSourceGenerator>(
            MessagingSource, IncrementalityAssert.UnrelatedAddition);

        IncrementalityAssert.StepsStayCached(result,
            TrackingNames.MessagingHandlers,
            TrackingNames.MessagingRequestHandlers,
            TrackingNames.MessagingSagas);
    }

    // ── Composition ──

    private const string CompositionSource = """
        namespace Pragmatic.Composition.Abstractions
        {
            public interface IStartupStep
            {
                int Order { get; }
            }
        }
        namespace Pragmatic.Composition.Hosting { public class PragmaticBuilder { } }
        namespace Pragmatic.Composition.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class ServiceAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class ModuleAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class StartupStepAttribute : System.Attribute { }
        }
        namespace Pragmatic.Events.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class EventHandlerAttribute : System.Attribute { }
        }
        namespace MyApp.Billing
        {
            public interface IInvoiceService { }

            [Pragmatic.Composition.Attributes.Service]
            public sealed class InvoiceService : IInvoiceService { }

            [Pragmatic.Composition.Attributes.Module]
            public sealed class BillingModule { }

            [Pragmatic.Composition.Attributes.StartupStep]
            public sealed class BillingStartup : Pragmatic.Composition.Abstractions.IStartupStep
            {
                public int Order => 100;
            }

            public sealed class InvoicePaid { }

            [Pragmatic.Events.Attributes.EventHandler]
            public sealed class InvoicePaidHandler { }
        }
        """;

    [Fact]
    public void Composition_AddingUnrelatedClass_KeepsPipelineCached()
    {
        var result = GeneratorTestHelper.RunGeneratorIncremental<PragmaticSourceGenerator>(
            CompositionSource, IncrementalityAssert.UnrelatedAddition);

        IncrementalityAssert.StepsStayCached(result,
            TrackingNames.CompositionServices,
            TrackingNames.CompositionStartupSteps,
            TrackingNames.CompositionModules,
            TrackingNames.CompositionEventHandlers);
    }

    // ── Resource ──

    private const string ResourceSource = """
        namespace Pragmatic.Persistence.Entity
        {
            public sealed class EntityAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class ResourceAttribute : System.Attribute
            {
                public ResourceAttribute(string segment) { }
                public int Capabilities { get; set; }
            }
        }
        namespace MyApp.Catalog
        {
            [Pragmatic.Persistence.Entity.Entity]
            [Pragmatic.Persistence.Entity.Resource("products", Capabilities = 63)]
            public partial class Product
            {
                public System.Guid Id { get; set; }
                public string Name { get; set; } = "";
                public decimal Price { get; set; }
            }
        }
        """;

    [Fact]
    public void Resource_AddingUnrelatedClass_KeepsPipelineCached()
    {
        var result = GeneratorTestHelper.RunGeneratorIncremental<PragmaticSourceGenerator>(
            ResourceSource, IncrementalityAssert.UnrelatedAddition);

        IncrementalityAssert.StepsStayCached(result,
            TrackingNames.ResourceResources,
            TrackingNames.ResourceCrudModels,
            TrackingNames.ResourceQueries,
            TrackingNames.ResourceEndpoints,
            // The write half moved from actions to mutations. Endpoints now also depend on the
            // overrides provider — the partial parts a developer decorates — so this is what says
            // that an unrelated class does not invalidate them through that new edge.
            TrackingNames.ResourceMutations);
    }
}
