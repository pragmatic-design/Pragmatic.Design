using System.Collections.Immutable;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Messaging.Models;
using Pragmatic.SourceGenerator.Features.Messaging.Templates;

namespace Pragmatic.SourceGenerator.Tests.Features.Messaging;

/// <summary>
///     Verify snapshot tests for Messaging SG templates.
///     These capture the full generated output and detect any unintended changes.
/// </summary>
public class MessagingSnapshotTests
{
    [Fact]
    public Task HandlerRegistration_SingleHandler_MatchesSnapshot()
    {
        var handlers = ImmutableArray.Create(new MessageHandlerModel
        {
            Namespace = "MyApp.Billing.EventHandlers",
            TypeName = "InvoicePaidHandler",
            Accessibility = "public",
            TypeKind = "class",
            IsPartial = true,
            MessageTypeFqn = "global::MyApp.Billing.Events.InvoicePaid",
            MessageTypeShortName = "InvoicePaid",
            // An invoice being paid is a domain event as well as a message, which is what keeps the
            // IDomainEventHandler<> bridge in this snapshot. A message that is only a message does not
            // get it — AMessageThatIsNotADomainEventTests is where that is asserted.
            MessageIsDomainEvent = true,
            Order = 0,
        });

        var source = new HandlerRegistrationTemplate(handlers).RenderOutput().Text;
        return Verify(source);
    }

    [Fact]
    public Task DispatchTable_TwoMessageTypes_MatchesSnapshot()
    {
        var source = new MessageDispatchTableTemplate(
            ["global::MyApp.Billing.Events.InvoicePaid", "global::MyApp.Booking.Events.OrderPlaced"],
            "MyApp").RenderOutput().Text;
        return Verify(source);
    }

    [Fact]
    public Task HandlerPipeline_WithTimeout_MatchesSnapshot()
    {
        var model = new MessageHandlerModel
        {
            Namespace = "MyApp.Handlers",
            TypeName = "SlowHandler",
            Accessibility = "public",
            TypeKind = "class",
            IsPartial = true,
            MessageTypeFqn = "global::MyApp.Events.SlowEvent",
            MessageTypeShortName = "SlowEvent",
            HasTimeout = true,
            TimeoutSeconds = 30,
            FeatureHasAuthorization = true,
        };

        var source = new HandlerPipelineTemplate(model).RenderOutput().Text;
        return Verify(source);
    }

    [Fact]
    public Task HandlerPipeline_RedeliveryConcurrencyRateLimit_MatchesSnapshot()
    {
        var model = new MessageHandlerModel
        {
            Namespace = "MyApp.Handlers",
            TypeName = "ThrottledHandler",
            Accessibility = "public",
            TypeKind = "class",
            IsPartial = true,
            MessageTypeFqn = "global::MyApp.Events.BulkImportRequested",
            MessageTypeShortName = "BulkImportRequested",
            HasRetry = true,
            RetryMaxAttempts = 2,
            // ⚠️ A raw number, because the model carries one: BackoffStrategy lives in the runtime
            // assembly the generator cannot reference. Name it through the generator's own mirror so
            // a renumbering moves this with it — writing 1 here once meant Exponential and now means
            // Fixed, and the snapshot would have absorbed the change without anyone noticing.
            RetryStrategy = BackoffStrategyValues.Exponential,
            RetryBaseDelayMs = 100,
            HasRedelivery = true,
            RedeliveryMaxAttempts = 4,
            RedeliveryBaseDelaySeconds = 15,
            HasConcurrencyLimit = true,
            MaxConcurrent = 2,
            HasRateLimit = true,
            RatePermitsPerPeriod = 5,
            RatePeriodSeconds = 10,
        };

        var source = new HandlerPipelineTemplate(model).RenderOutput().Text;
        return Verify(source);
    }

    [Fact]
    public Task PartitionKeyResolver_MixedTypes_MatchesSnapshot()
    {
        var keys = ImmutableArray.Create(
            new PartitionKeyModel
            {
                MessageTypeFqn = "global::MyApp.Events.OrderPlaced",
                PropertyName = "CustomerId",
                IsString = false,
                IsNullable = false,
            },
            new PartitionKeyModel
            {
                MessageTypeFqn = "global::MyApp.Events.InvoicePaid",
                PropertyName = "Region",
                IsString = true,
                IsNullable = true,
            });

        var source = new PartitionKeyResolverTemplate(keys, "MyApp").RenderOutput().Text;
        return Verify(source);
    }

    [Fact]
    public Task HandlerRegistration_WithPartitionKeys_RegistersResolver()
    {
        var handlers = ImmutableArray.Create(new MessageHandlerModel
        {
            Namespace = "MyApp.Handlers",
            TypeName = "OrderHandler",
            Accessibility = "public",
            TypeKind = "class",
            IsPartial = true,
            AssemblyName = "MyApp",
            MessageTypeFqn = "global::MyApp.Events.OrderPlaced",
            MessageTypeShortName = "OrderPlaced",
            // An order being placed is a domain event too: the three registration snapshots all say so,
            // which is how they stay byte-identical — the bridge is left out only for a
            // message that is *not* one.
            MessageIsDomainEvent = true,
        });

        var source = new HandlerRegistrationTemplate(handlers, hasPartitionKeys: true)
            .RenderOutput().Text;
        return Verify(source);
    }

    [Fact]
    public Task HandlerRegistration_OnBusAndRequestHandler_MatchesSnapshot()
    {
        var handlers = ImmutableArray.Create(new MessageHandlerModel
        {
            Namespace = "MyApp.Analytics",
            TypeName = "PageViewHandler",
            Accessibility = "public",
            TypeKind = "class",
            IsPartial = true,
            AssemblyName = "MyApp",
            MessageTypeFqn = "global::MyApp.Events.PageViewed",
            MessageTypeShortName = "PageViewed",
            // As above: a page view is published as a domain event too, so the bridge belongs in this
            // snapshot.
            MessageIsDomainEvent = true,
            BusName = "analytics",
        });

        var requestHandlers = ImmutableArray.Create(new RequestHandlerModel
        {
            Namespace = "MyApp.Quotes",
            TypeName = "QuoteHandler",
            Accessibility = "public",
            TypeKind = "class",
            RequestTypeFqn = "global::MyApp.Quotes.GetQuote",
            ResponseTypeFqn = "global::MyApp.Quotes.Quote",
        });

        var source = new HandlerRegistrationTemplate(handlers, requestHandlers)
            .RenderOutput().Text;
        return Verify(source);
    }

    [Fact]
    public Task MessageTypeRegistry_TwoTypes_MatchesSnapshot()
    {
        var types = ImmutableArray.Create(
            new MessageTypeModel { Fqn = "global::MyApp.Events.OrderPlaced", ShortName = "OrderPlaced" },
            new MessageTypeModel { Fqn = "global::MyApp.Events.InvoicePaid", ShortName = "InvoicePaid" });

        var source = new MessageTypeRegistryTemplate(types).RenderOutput().Text;
        return Verify(source);
    }

    [Fact]
    public Task Topology_WithHandlers_MatchesSnapshot()
    {
        var handlers = ImmutableArray.Create(
            new MessageHandlerModel
            {
                Namespace = "MyApp.Billing.Handlers",
                TypeName = "ReservationConfirmedHandler",
                Accessibility = "public",
                TypeKind = "class",
                MessageTypeFqn = "global::MyApp.Booking.Events.ReservationConfirmed",
                MessageTypeShortName = "ReservationConfirmed",
            },
            new MessageHandlerModel
            {
                Namespace = "MyApp.Billing.Handlers",
                TypeName = "InvoicePaidHandler",
                Accessibility = "public",
                TypeKind = "class",
                MessageTypeFqn = "global::MyApp.Billing.Events.InvoicePaid",
                MessageTypeShortName = "InvoicePaid",
            });

        var source = new TopologyTemplate(handlers).RenderOutput().Text;
        return Verify(source);
    }

    [Fact]
    public Task SagaOrchestrator_SimpleFlow_MatchesSnapshot()
    {
        var model = new SagaModel
        {
            Namespace = "MyApp.Ordering",
            TypeName = "OrderSaga",
            Accessibility = "public",
            TypeKind = "class",
            IsPartial = true,
            StateTypeFqn = "global::MyApp.Ordering.OrderState",
            StateTypeShortName = "OrderState",
            StateValues = ImmutableArray.Create("Created", "PaymentPending", "Completed", "Cancelled"),
            StartStep = new SagaStepModel
            {
                MethodName = "HandleOrderRequested",
                EventTypeFqn = "global::MyApp.Ordering.Events.OrderRequested",
                EventTypeShortName = "OrderRequested",
                IsStart = true,
                NextState = "PaymentPending",
                ReturnTypeFqn = "global::MyApp.Ordering.Actions.ProcessPayment",
            },
            Steps = ImmutableArray.Create(
                new SagaStepModel
                {
                    MethodName = "HandleOrderRequested",
                    EventTypeFqn = "global::MyApp.Ordering.Events.OrderRequested",
                    EventTypeShortName = "OrderRequested",
                    IsStart = true,
                    NextState = "PaymentPending",
                    ReturnTypeFqn = "global::MyApp.Ordering.Actions.ProcessPayment",
                },
                new SagaStepModel
                {
                    MethodName = "HandlePaymentReceived",
                    EventTypeFqn = "global::MyApp.Ordering.Events.PaymentReceived",
                    EventTypeShortName = "PaymentReceived",
                    ValidStates = ImmutableArray.Create("PaymentPending"),
                    NextState = "Completed",
                    CompensationActionFqn = "global::MyApp.Ordering.Actions.RefundPayment",
                }),
        };

        var source = new SagaOrchestratorTemplate(model).RenderOutput().Text;
        return Verify(source);
    }

    private static SagaModel SampleSaga(bool implementsISaga, string? persistenceDbContextFqn = null) => new()
    {
        Namespace = "MyApp.Ordering",
        TypeName = "OrderSaga",
        Accessibility = "public",
        TypeKind = "class",
        IsPartial = true,
        ImplementsISaga = implementsISaga,
        PersistenceDbContextFqn = persistenceDbContextFqn,
        StateTypeFqn = "global::MyApp.Ordering.OrderState",
        StateTypeShortName = "OrderState",
        StateValues = ImmutableArray.Create("Created", "Completed"),
        StartStep = new SagaStepModel
        {
            MethodName = "HandleOrderRequested",
            EventTypeFqn = "global::MyApp.Ordering.Events.OrderRequested",
            EventTypeShortName = "OrderRequested",
            IsStart = true,
            NextState = "Completed",
        },
        Steps = ImmutableArray.Create(new SagaStepModel
        {
            MethodName = "HandleOrderRequested",
            EventTypeFqn = "global::MyApp.Ordering.Events.OrderRequested",
            EventTypeShortName = "OrderRequested",
            IsStart = true,
            NextState = "Completed",
        }),
    };

    [Fact]
    public Task SagaRegistration_InMemory_MatchesSnapshot()
    {
        var source = new SagaRegistrationTemplate(ImmutableArray.Create(SampleSaga(implementsISaga: true)), hasEfCore: false)
            .RenderOutput().Text;
        return Verify(source);
    }

    [Fact]
    public Task SagaRegistration_EfCorePersistence_MatchesSnapshot()
    {
        // hasEfCore + ISaga<TState> + an [EnableSagaPersistence] boundary DbContext → EF-backed branch
        // resolving the concrete DbContext lazily (no runtime marker, no ordering dependency).
        var source = new SagaRegistrationTemplate(
                ImmutableArray.Create(SampleSaga(implementsISaga: true, persistenceDbContextFqn: "global::MyApp.Ordering.Entities.OrderingDbContext")),
                hasEfCore: true)
            .RenderOutput().Text;
        return Verify(source);
    }

    [Fact]
    public Task SagaRegistration_EfCoreButSagaNotISaga_StaysInMemory()
    {
        // Safety guard: EF referenced + a persistence DbContext, but the saga does not declare
        // : ISaga<TState> (EfCoreSagaRepository's constraint) → no EF branch, generated code still compiles.
        var source = new SagaRegistrationTemplate(
                ImmutableArray.Create(SampleSaga(implementsISaga: false, persistenceDbContextFqn: "global::MyApp.Ordering.Entities.OrderingDbContext")),
                hasEfCore: true)
            .RenderOutput().Text;
        return Verify(source);
    }

    [Fact]
    public Task SagaDiagram_TimeoutAndCompensation_MatchesSnapshot()
    {
        var model = new SagaModel
        {
            Namespace = "MyApp.Ordering",
            TypeName = "OrderSaga",
            Accessibility = "public",
            TypeKind = "class",
            IsPartial = true,
            StateTypeFqn = "global::MyApp.Ordering.OrderState",
            StateTypeShortName = "OrderState",
            StateValues = ImmutableArray.Create("Created", "PaymentPending", "Shipping", "Completed"),
            StartStep = new SagaStepModel
            {
                MethodName = "HandleOrderRequested",
                EventTypeFqn = "global::MyApp.Ordering.Events.OrderRequested",
                EventTypeShortName = "OrderRequested",
                IsStart = true,
                NextState = "PaymentPending",
            },
            Steps = ImmutableArray.Create(
                new SagaStepModel
                {
                    MethodName = "HandleOrderRequested",
                    EventTypeFqn = "global::MyApp.Ordering.Events.OrderRequested",
                    EventTypeShortName = "OrderRequested",
                    IsStart = true,
                    NextState = "PaymentPending",
                },
                new SagaStepModel
                {
                    MethodName = "HandlePaymentReceived",
                    EventTypeFqn = "global::MyApp.Ordering.Events.PaymentReceived",
                    EventTypeShortName = "PaymentReceived",
                    ValidStates = ImmutableArray.Create("PaymentPending"),
                    NextState = "Shipping",
                    TimeoutDuration = "00:30:00",
                    CompensationActionFqn = "global::MyApp.Ordering.Actions.RefundPayment",
                },
                new SagaStepModel
                {
                    MethodName = "HandleShipmentDelivered",
                    EventTypeFqn = "global::MyApp.Ordering.Events.ShipmentDelivered",
                    EventTypeShortName = "ShipmentDelivered",
                    ValidStates = ImmutableArray.Create("Shipping"),
                    NextState = "Completed",
                }),
        };

        var source = new SagaDiagramTemplate(ImmutableArray.Create(model)).RenderOutput().Text;
        return Verify(source);
    }
}
