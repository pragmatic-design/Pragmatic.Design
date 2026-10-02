using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Messaging.Diagnostics;
using Pragmatic.SourceGenerator.Features.Messaging.Models;
using Pragmatic.SourceGenerator.Features.Messaging.Templates;
using Pragmatic.SourceGenerator.Features.Messaging.Transforms;

namespace Pragmatic.SourceGenerator.Features.Messaging;

/// <summary>
///     Messaging feature orchestrator. Registers pipelines:
///     1. [MessageHandler] → Registration + Pipeline wrappers
///     2. Message types → TypeRegistry (AOT-safe)
///     4. [Saga&lt;TState&gt;] → Orchestrator with state machine
///     5. [RequestHandler] → DI registration for IRequestHandler&lt;TReq, TRes&gt;
///     ([EnableOutbox] is boundary-level, wired by the Persistence DbContext feature.)
/// </summary>
internal static class MessagingFeature
{
    private const string SchemaVersion = "1.0";

    /// <returns>
    ///     The handler registration this compilation generates, for a host that declares its
    ///     <c>[MessageHandler]</c> classes itself.
    /// </returns>
    public static IncrementalValueProvider<EquatableArray<Composition.Models.MetadataEntry>> Register(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<DetectedFeatures> features)
    {
        // =====================================================================
        // Pipeline 1: [MessageHandler] → Handler models
        // =====================================================================
        // ⚠️ A non-partial handler is dropped here: its pipeline is a nested class emitted in a
        // `partial class {Handler}` the compiler refuses on a non-partial type (CS0260), and the
        // registration and dispatch table name that pipeline. PRAG0801, the companion analyzer's, says
        // why on the declaration.
        var handlerProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeNames.MessageHandler,
                GeneratorHelpers.IsClass,
                MessageHandlerTransform.Transform)
            .Where(m => m is { IsPartial: true });

        // Per-handler: generate Pipeline wrapper (enriched with feature flags)
        context.RegisterSourceOutputSafe(
            handlerProvider.Combine(features).Where(x => x.Right.HasMessaging),
            static (ctx, x) =>
            {
                var model = x.Left! with
                {
                    FeatureHasValidation = x.Right.HasValidation,
                    FeatureHasMultiTenancy = x.Right.HasMultiTenancy,
                    FeatureHasAuthorization = x.Right.HasAuthorization,
                };
                GenerateHandlerPipeline(ctx, model);
            });

        // =====================================================================
        // Shape diagnostics — run alongside the model transforms (which stay null-returning for
        // invalid triggers) so a wrong-shaped attribute is reported instead of silently doing nothing.
        // =====================================================================
        RegisterShapeDiagnostic(context, AttributeNames.MessageHandler, GeneratorHelpers.IsClass,
            MessagingShapeDiagnosticTransform.HandlerShape);
        RegisterShapeDiagnostic(context, AttributeNames.MessageMiddleware, GeneratorHelpers.IsClass,
            MessagingShapeDiagnosticTransform.MiddlewareShape);
        RegisterShapeDiagnostic(context, AttributeNames.Saga, GeneratorHelpers.IsClass,
            MessagingShapeDiagnosticTransform.SagaStateShape);
        // ⚠️ IsClassOrRecord, not IsClass: a published contract is almost always a record, and that is
        // exactly the shape this one is about — a record marked [PublicEvent] and implementing nothing.
        // A class-only predicate would have made the diagnostic miss its own motivating case.
        RegisterShapeDiagnostic(context, AttributeNames.PublicEvent, GeneratorHelpers.IsClassOrRecord,
            MessagingShapeDiagnosticTransform.PublicEventShape);

        // [EnableOutbox] is a boundary-level attribute wired by the Persistence DbContext feature
        // (mirror of [EnableSagaPersistence]/[EnableEventOutbox]): PRAG0831 ([EnableOutbox] without
        // Pragmatic.Messaging.EFCore) is reported there from a boundary-level FAWMN pipeline. There is
        // no per-DbContext shape check; PRAG0830 ("must be a DbContext") is retired and not reused.

        // =====================================================================
        // Pipeline 5: [RequestHandler] → Request handler models
        // =====================================================================
        var requestHandlerProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeNames.RequestHandler,
                GeneratorHelpers.IsClass,
                RequestHandlerTransform.Transform)
            .Where(m => m is not null);

        var allRequestHandlers = requestHandlerProvider.Select((m, _) => m!)
            .WithTrackingName(TrackingNames.MessagingRequestHandlers)
            .Collect();

        // [PartitionKey] on a message property → typed IPartitionKeyResolver, in the assembly that
        // DECLARES the message. That is the one the resolver has to be in: TransportAwareMessageBus
        // resolves IPartitionKeyResolver from the container of the host that PUBLISHES, and a contract
        // lives in an assembly that host references.
        //
        // ⚠️ One mechanism, and this one. Neither a FAWMN pipeline over PropertyDeclarationSyntax nor a
        // symbol scan from MessageHandlerTransform can serve a positional record declared in a
        // contracts assembly: FAWMN does not surface [property: PartitionKey] on a record parameter
        // (measured, with the predicate widened and the parameter symbol mapped back to its property),
        // and the handler scan fires in the consumer, which is not where the resolver is resolved from.
        var allPartitionKeys = context.SyntaxProvider
            .CreateSyntaxProvider(
                DeclaredPartitionKeyTransform.CouldDeclareAPartitionKey,
                DeclaredPartitionKeyTransform.Transform)
            .Where(keys => keys.Count > 0)
            .SelectMany((keys, _) => keys.AsImmutableArray())
            .Collect();

        // [MessageMiddleware] → the DI registration that makes it run. ⚠️ With the shape diagnostic below
        // as the attribute's only reader, a middleware declared this way would compile, pass its own
        // check, and never be called — nothing fails when a wrapper is absent, the messages are simply
        // handled unwrapped.
        var allMiddlewares = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeNames.MessageMiddleware,
                GeneratorHelpers.IsClass,
                MessageMiddlewareTransform.Transform)
            // The transform answers with facts and this composes them: one that cannot serve is
            // reported by the shape diagnostic below, not dropped in silence by the transform.
            .Where(m => m.CanServe && m.TypeFqn.Length > 0)
            .Collect();

        // The domain events this compilation declares — what its outbox can carry. A publisher has no
        // handler to derive a message type from, and without these it could not read its own rows.
        // See DeclaredDomainEventTransform for why the set is the declared ones only.
        var declaredEvents = context.SyntaxProvider
            .CreateSyntaxProvider(
                DeclaredDomainEventTransform.CouldBeAnEvent,
                DeclaredDomainEventTransform.Transform)
            .Where(m => m is not null)
            .Select((m, _) => m!)
            .Collect();

        // Aggregate: generate Registration + TypeRegistry + Metadata
        var allHandlers = handlerProvider.Select((m, _) => m!)
            .WithTrackingName(TrackingNames.MessagingHandlers)
            .Collect();
        var handlersWithFeatures = allHandlers.Combine(features);
        var handlersWithEvents = handlersWithFeatures.Combine(declaredEvents);

        // The namespace of last resort for the registration: an assembly whose only messaging is a
        // request handler or a middleware has no handler and no event to read its name off.
        var assemblyName = context.CompilationProvider.Select(static (c, _) => c.AssemblyName ?? "");

        var handlersAndRequestHandlers = handlersWithEvents.Combine(allRequestHandlers).Combine(allPartitionKeys)
            .Combine(allMiddlewares).Combine(assemblyName)
            .Select(static (x, _) =>
            {
                var (((((handlersWithF, declared), requestHandlers), partitionKeys), middlewares), asm) = x;
                return new RegistrationInputs(
                    handlersWithF.Left, handlersWithF.Right, RegistryTypes(handlersWithF.Left, declared),
                    requestHandlers, partitionKeys, middlewares, asm);
            });

        // ⚠️ The registration file and the metadata that makes the host call it come from one output and
        // one predicate. Two outputs with two conditions — the file written for a request handler, a
        // middleware or a partition key, the metadata only for a message handler or a declared event —
        // would give a module whose only messaging is a [RequestHandler] a registration no host ever
        // calls, and every request to it would time out.
        context.RegisterSourceOutputSafe(handlersAndRequestHandlers,
            static (ctx, x) =>
            {
                if (!x.Features.HasMessaging) return;
                GenerateHandlerRegistration(ctx, x);
                GenerateDispatchTable(ctx, x.Handlers);
                GeneratePartitionKeyResolver(ctx, x.PartitionKeys);
            });

        // Type registry: every messaging assembly gets one — outbox delivery, scheduled
        // messages (Jobs bridge), and dashboard replay all resolve FQN → type through it.
        context.RegisterSourceOutputSafe(handlersWithEvents,
            static (ctx, x) =>
            {
                if (!x.Left.Right.HasMessaging) return;
                GenerateTypeRegistry(ctx, x.Left.Left, x.Right);
            });

        // Same predicate as the registration and its metadata: a host that declares the handlers itself
        // never sees that attribute, so it is handed the registration directly.
        var localRegistrations = handlersAndRequestHandlers.Select(static (x, _) =>
        {
            if (!x.Features.HasComposition || !x.Features.HasMessaging || !x.HasAnythingToRegister)
                return EquatableArray<Composition.Models.MetadataEntry>.Empty;

            return ImmutableArray.Create(
                Composition.Models.HostLocalRegistration.Create(
                    Composition.MetadataCategoryIds.MessageHandlers,
                    SchemaVersion,
                    GeneratedRegistrationNames.MessageHandlersFqn(x.RegistrationNamespace)));
        });

        // Topology + Routing: generate when any transport is referenced
        context.RegisterSourceOutputSafe(handlersWithFeatures,
            static (ctx, x) =>
            {
                if (!x.Right.HasMessaging) return;
                if (!x.Right.HasMessagingChannels && !x.Right.HasMessagingRabbitMq) return;
                GenerateTopology(ctx, x.Left);
                GenerateRouting(ctx, x.Left);
            });

        // BusResolver: generate when any handler has [OnBus]
        context.RegisterSourceOutputSafe(handlersWithFeatures,
            static (ctx, x) =>
            {
                if (!x.Right.HasMessaging) return;
                if (!x.Left.Any(h => h?.BusName is not null)) return;
                GenerateBusResolver(ctx, x.Left!);
            });

        // No per-DbContext OutboxSource is generated: the boundary-level [EnableOutbox] registers
        // EfCoreOutboxSource per boundary via the Persistence DbContext registration
        // (MessagingOutboxExtensions.AddMessagingOutbox).

        // =====================================================================
        // Pipeline 4: [Saga<TState>] → Orchestrator
        // =====================================================================
        var sagaProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeNames.Saga,
                GeneratorHelpers.IsClass,
                SagaTransform.Transform)
            .Where(m => m is not null);

        context.RegisterSourceOutputSafe(
            sagaProvider.Combine(features).Where(x => x.Right.HasMessaging),
            static (ctx, x) => GenerateSagaOrchestrator(ctx, x.Left!));

        // Aggregate: generate saga registration + metadata for host aggregation
        var allSagas = sagaProvider.Select((m, _) => m!)
            .WithTrackingName(TrackingNames.MessagingSagas)
            .Collect();
        context.RegisterSourceOutputSafe(allSagas.Combine(features),
            static (ctx, x) =>
            {
                if (!x.Right.HasMessaging || x.Left.IsDefaultOrEmpty) return;

                var registration = new SagaRegistrationTemplate(x.Left, x.Right.HasMessagingEFCore).RenderOutput();
                ctx.AddSource(registration);

                // Emit assembly metadata so PragmaticHost auto-invokes AddPragmaticSagas()
                var metadata = new SagaMetadataTemplate(x.Left).RenderOutput();
                ctx.AddSource(metadata);

                // Mermaid state diagrams — docs artifact rendered from the same model
                var diagrams = new SagaDiagramTemplate(x.Left).RenderOutput();
                ctx.AddSource(diagrams);
            });

        return localRegistrations;
    }

    // =========================================================================
    // Generation Methods
    // =========================================================================

    private static void GenerateHandlerPipeline(SourceProductionContext ctx, MessageHandlerModel model)
    {
        if (model.HasRetry && model.RetryMaxAttempts <= 0)
        {
            ctx.ReportDiagnostic(MessagingDiagnostics.InvalidRetryConfig, model.Location, model.TypeName);
        }

        var artifact = new HandlerPipelineTemplate(model).RenderOutput();
        ctx.AddSource(artifact);
    }

    /// <summary>
    ///     Registers a diagnostic-only FAWMN provider that reports a shape diagnostic for invalid
    ///     triggers (transform returns null when the trigger is valid).
    /// </summary>
    private static void RegisterShapeDiagnostic(
        IncrementalGeneratorInitializationContext context,
        string attributeName,
        Func<Microsoft.CodeAnalysis.SyntaxNode, System.Threading.CancellationToken, bool> predicate,
        Func<Microsoft.CodeAnalysis.GeneratorAttributeSyntaxContext, System.Threading.CancellationToken, MessagingDiagnosticInfo?> transform)
    {
        var provider = context.SyntaxProvider
            .ForAttributeWithMetadataName(attributeName, predicate, transform)
            .Where(d => d is not null);
        context.RegisterSourceOutputSafe(provider, static (ctx, d) => ReportShapeDiagnostic(ctx, d!));
    }

    private static void ReportShapeDiagnostic(SourceProductionContext ctx, MessagingDiagnosticInfo info)
    {
        var descriptor = info.Kind switch
        {
            MessagingDiagnosticKind.HandlerMustImplementInterface => MessagingDiagnostics.HandlerMustImplementInterface,
            MessagingDiagnosticKind.MiddlewareMustImplementInterface => MessagingDiagnostics.MiddlewareMustImplementInterface,
            MessagingDiagnosticKind.SagaStateNotEnum => MessagingDiagnostics.SagaStateNotEnum,
            MessagingDiagnosticKind.PublicEventOnNonDomainEvent => MessagingDiagnostics.PublicEventOnNonDomainEvent,
            _ => null,
        };
        if (descriptor is null)
            return;
        ctx.ReportDiagnostic(descriptor, info.Location?.ToLocation(), info.Args.AsImmutableArray().ToArray());
    }

    /// <summary>
    ///     The assembly's messaging registration. Emitted for handlers, for request handlers — and for
    ///     an assembly whose only messaging asset is a <b>type registry</b>, because the registry is
    ///     <c>internal</c> and nothing outside its own assembly can register it.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Also emits the metadata that makes a host call the registration, under the same predicate
    ///     (<see cref="RegistrationInputs.HasAnythingToRegister" />). The metadata is gated on
    ///     HasMessaging alone and not on HasComposition: a contracts assembly declares the events and
    ///     takes Messaging.Core for their attributes, but has no reason to reference Composition — and
    ///     without the declaration the host never calls its registration. The attribute and
    ///     the category enum both live in Pragmatic.Abstractions, which Messaging.Core brings.
    /// </remarks>
    private static void GenerateHandlerRegistration(SourceProductionContext ctx, RegistrationInputs inputs)
    {
        if (!inputs.HasAnythingToRegister)
            return;

        ctx.AddSource(new HandlerRegistrationTemplate(
                inputs.Handlers, inputs.RequestHandlers, !inputs.PartitionKeys.IsDefaultOrEmpty,
                inputs.RegistryTypes, inputs.Middlewares, inputs.AssemblyName)
            .RenderOutput());

        ctx.AddSource(new HandlerMetadataTemplate(inputs.Handlers, inputs.RegistrationNamespace).RenderOutput());
    }

    private static void GeneratePartitionKeyResolver(
        SourceProductionContext ctx,
        ImmutableArray<PartitionKeyModel> partitionKeys)
    {
        if (partitionKeys.IsDefaultOrEmpty)
            return;

        // One key per message type: extras warn (PRAG0819) and are ignored deterministically.
        var deduped = ImmutableArray.CreateBuilder<PartitionKeyModel>();
        foreach (var group in partitionKeys
                     .GroupBy(k => k.MessageTypeFqn)
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var ordered = group.OrderBy(k => k.PropertyName, StringComparer.Ordinal).ToList();
            if (ordered.Count > 1)
            {
                ctx.ReportDiagnostic(MessagingDiagnostics.MultiplePartitionKeys,
                    ordered[0].LocationInfo?.ToLocation(), group.Key, ordered[0].PropertyName);
            }

            deduped.Add(ordered[0]);
        }

        var assemblyNamespace = partitionKeys[0].AssemblyName;
        if (string.IsNullOrEmpty(assemblyNamespace))
            assemblyNamespace = "Pragmatic.Messaging";

        var artifact = new PartitionKeyResolverTemplate(deduped.ToImmutable(), assemblyNamespace).RenderOutput();
        ctx.AddSource(artifact);
    }

    private static void GenerateDispatchTable(SourceProductionContext ctx, ImmutableArray<MessageHandlerModel> handlers)
    {
        if (handlers.IsDefaultOrEmpty)
            return;

        var messageTypeFqns = handlers
            .Select(h => h.MessageTypeFqn)
            .Distinct()
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToImmutableArray();

        var assemblyNamespace = handlers[0].AssemblyName;
        if (string.IsNullOrEmpty(assemblyNamespace))
            assemblyNamespace = "Pragmatic.Messaging";

        var artifact = new MessageDispatchTableTemplate(messageTypeFqns, assemblyNamespace).RenderOutput();
        ctx.AddSource(artifact);
    }

    /// <summary>
    ///     What this assembly's registry can deserialize: the message types its handlers consume, plus
    ///     the domain events it declares.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The second half is what a <b>publisher</b> needs. With handlers as the only source, a service that publishes and does not consume
    ///     generated no registry, so <c>OutboxDeliveryService</c> could not read the rows that service
    ///     had written and dead-lettered every one of them as an "unknown message type". Both halves,
    ///     because an assembly can do both and the union is what its outbox and its subscriptions
    ///     together carry.
    /// </remarks>
    private static ImmutableArray<MessageTypeModel> RegistryTypes(
        ImmutableArray<MessageHandlerModel> handlers,
        ImmutableArray<MessageTypeModel> declaredEvents)
    {
        // ⚠️ No collection expressions on ImmutableArray here: the SG targets netstandard2.0, whose
        // System.Collections.Immutable predates [CollectionBuilder] — CS9210.
        var all = new List<MessageTypeModel>();

        if (!handlers.IsDefaultOrEmpty)
        {
            foreach (var handler in handlers)
            {
                all.Add(new MessageTypeModel
                {
                    Fqn = handler.MessageTypeFqn,
                    ShortName = handler.MessageTypeShortName,
                });
            }
        }

        if (!declaredEvents.IsDefaultOrEmpty)
            all.AddRange(declaredEvents);

        return all
            .GroupBy(m => Unqualified(m.Fqn), StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(m => m.Fqn, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    /// <summary>
    ///     The FQN without a <c>global::</c> prefix. ⚠️ The two sources spell the same type
    ///     differently — a handler's message type arrives qualified, a declared event's does not — so
    ///     deduplicating on the raw string would put one type in the switch twice and produce a
    ///     duplicate case label, which does not compile.
    /// </summary>
    private static string Unqualified(string fqn)
        => fqn.StartsWith("global::", StringComparison.Ordinal) ? fqn.Substring("global::".Length) : fqn;

    private static void GenerateTypeRegistry(
        SourceProductionContext ctx,
        ImmutableArray<MessageHandlerModel> handlers,
        ImmutableArray<MessageTypeModel> declaredEvents)
    {
        var messageTypes = RegistryTypes(handlers, declaredEvents);

        // Nothing to resolve, nothing emitted: an empty registry would be a service that answers null
        // to every type, and the pump's "no registry recognised this" would then be said with one
        // registered.
        if (messageTypes.IsDefaultOrEmpty)
            return;

        var artifact = new MessageTypeRegistryTemplate(messageTypes).RenderOutput();
        ctx.AddSource(artifact);
    }

    private static void GenerateTopology(SourceProductionContext ctx, ImmutableArray<MessageHandlerModel> handlers)
    {
        if (handlers.IsDefaultOrEmpty)
            return;

        var artifact = new TopologyTemplate(handlers).RenderOutput();
        ctx.AddSource(artifact);
    }

    private static void GenerateRouting(SourceProductionContext ctx, ImmutableArray<MessageHandlerModel> handlers)
    {
        if (handlers.IsDefaultOrEmpty)
            return;

        var artifact = new RoutingRegistryTemplate(handlers).RenderOutput();
        ctx.AddSource(artifact);
    }

    private static void GenerateBusResolver(SourceProductionContext ctx, ImmutableArray<MessageHandlerModel> handlers)
    {
        if (handlers.IsDefaultOrEmpty)
            return;

        var busHandlers = handlers.Where(h => h is not null && h.BusName is not null).ToImmutableArray();
        if (busHandlers.IsEmpty)
            return;

        var artifact = new BusResolverTemplate(busHandlers).RenderOutput();
        if (!artifact.IsEmpty)
            ctx.AddSource(artifact);
    }

    private static void GenerateSagaOrchestrator(SourceProductionContext ctx, SagaModel model)
    {
        // Validate
        if (model.StartStep is null)
        {
            ctx.ReportDiagnostic(MessagingDiagnostics.SagaMissingStart, model.Location, model.TypeName);
            return;
        }

        // Correlation: every consumed event needs ICorrelatedMessage or [CorrelationKey]
        var correlationChecked = new HashSet<string>();
        foreach (var step in model.Steps)
        {
            if (!correlationChecked.Add(step.EventTypeFqn))
                continue;

            if (step.CorrelationAccessor is null)
                ctx.ReportDiagnostic(MessagingDiagnostics.SagaEventWithoutCorrelation, model.Location, model.TypeName, step.EventTypeShortName);
            else if (step.HasMultipleCorrelationKeys)
                ctx.ReportDiagnostic(MessagingDiagnostics.MultipleCorrelationKeys, model.Location, step.EventTypeShortName);
        }

        // Check for orphaned states (states with no handler)
        var handledStates = new HashSet<string>(
            model.Steps
                .Where(s => !s.IsStart)
                .SelectMany(s => s.ValidStates));

        foreach (var stateValue in model.StateValues)
        {
            if (!handledStates.Contains(stateValue))
            {
                ctx.ReportDiagnostic(MessagingDiagnostics.SagaOrphanedState, model.Location, model.TypeName, stateValue);
            }
        }

        var artifact = new SagaOrchestratorTemplate(model).RenderOutput();
        ctx.AddSource(artifact);

        // Emit IMessageHandler<TEvent> per-step so events published via IMessageBus
        // are automatically routed to this saga's Orchestrator.
        var handlers = new SagaEventHandlerTemplate(model).RenderOutput();
        if (!handlers.IsEmpty)
            ctx.AddSource(handlers);
    }
}
