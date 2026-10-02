using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Messaging.Models;

namespace Pragmatic.SourceGenerator.Features.Messaging.Templates;

/// <summary>
///     Generates <c>_Infra.Messaging.Registration.g.cs</c> with DI registration
///     for all [MessageHandler] and [RequestHandler] classes in the assembly.
/// </summary>
internal sealed class HandlerRegistrationTemplate : CSharpTemplate
{
    private readonly ImmutableArray<MessageHandlerModel> _handlers;
    private readonly ImmutableArray<RequestHandlerModel> _requestHandlers;
    private readonly bool _hasPartitionKeys;
    private readonly ImmutableArray<MessageTypeModel> _registryTypes;
    private readonly ImmutableArray<MessageMiddlewareModel> _middlewares;
    private readonly string _assemblyName;

    public HandlerRegistrationTemplate(
        ImmutableArray<MessageHandlerModel> handlers,
        ImmutableArray<RequestHandlerModel> requestHandlers = default,
        bool hasPartitionKeys = false,
        ImmutableArray<MessageTypeModel> registryTypes = default,
        ImmutableArray<MessageMiddlewareModel> middlewares = default,
        string assemblyName = "")
    {
        _handlers = handlers;
        _requestHandlers = requestHandlers.IsDefault ? ImmutableArray<RequestHandlerModel>.Empty : requestHandlers;
        _hasPartitionKeys = hasPartitionKeys;
        _registryTypes = registryTypes.IsDefault ? ImmutableArray<MessageTypeModel>.Empty : registryTypes;
        _middlewares = middlewares.IsDefault ? ImmutableArray<MessageMiddlewareModel>.Empty : middlewares;
        _assemblyName = assemblyName;
    }

    /// <summary>Whether this assembly has a message-type registry to register.</summary>
    /// <remarks>
    ///     ⚠️ <b>Derived, not passed.</b> A handler always contributes its message type to the registry,
    ///     so "there are handlers" already means "there is a registry" — and a first version of this
    ///     took the answer as a constructor flag instead, which let the three template snapshot tests
    ///     (which construct handlers and no registry types) silently lose the registration. The snapshot
    ///     diff was the only thing that said so: one line gone, in generated code nobody reads by hand.
    /// </remarks>
    private bool HasTypeRegistry => !_registryTypes.IsDefaultOrEmpty || !_handlers.IsDefaultOrEmpty;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Messaging";
    protected override string? TriggerInfo => $"{_handlers.Length} message handler(s), {_requestHandlers.Length} request handler(s)";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForAssembly("Messaging", "Registration"),
        ToSourceText());

    // A partition key is enough on its own, for the same reason a middleware is: this file is what
    // registers the generated resolver, and a contracts assembly can declare the key and nothing else.
    protected override bool Validate()
        => !_handlers.IsDefaultOrEmpty || !_requestHandlers.IsDefaultOrEmpty || HasTypeRegistry
           || !_middlewares.IsEmpty || _hasPartitionKeys;

    public override void RenderFile()
    {
        AddUsing("Microsoft.Extensions.DependencyInjection");
        AddUsing("Microsoft.Extensions.DependencyInjection.Extensions");
        AppendLine();

        // Per-module namespace: the registration class name is fixed, so a fixed namespace would clash
        // across module assemblies referenced by the same host (CS0433). Namespace it by owning assembly.
        AppendLine($"namespace {RegistrationNamespace};");
        AppendLine();

        XmlSummary("Registers all message handlers discovered in this assembly.");
        Class(GeneratedRegistrationNames.MessageHandlersClass, RenderRegistrationBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Partial = true, IsStatic = true });
    }

    /// <summary>Owning-assembly namespace + <c>.Generated</c>; falls back to the legacy fixed namespace.</summary>
    internal string RegistrationNamespace => NamespaceFor(_handlers, _registryTypes, _assemblyName);

    /// <summary>
    ///     The namespace this assembly's messaging registration lands in. Whoever tells the host to call
    ///     it — the metadata attribute, or the local-registration channel — has to name the same one.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The registry types are the second source on purpose: an assembly that only <b>publishes</b>
    ///     has no handler to read the assembly name off, and the legacy fallback below is a <em>fixed</em>
    ///     namespace — so two such assemblies referenced by one host would declare the same class and
    ///     stop compiling (CS0433). A declared event knows its own assembly.
    ///     <para>
    ///         The compilation's own name is the third, for an assembly with neither — its only messaging a
    ///         request handler, a middleware or a partition key.
    ///     </para>
    /// </remarks>
    internal static string NamespaceFor(
        ImmutableArray<MessageHandlerModel> handlers,
        ImmutableArray<MessageTypeModel> registryTypes = default,
        string assemblyName = "")
    {
        var asm = !handlers.IsDefaultOrEmpty ? handlers[0].AssemblyName : "";

        if (string.IsNullOrEmpty(asm) && !registryTypes.IsDefaultOrEmpty)
        {
            foreach (var type in registryTypes)
            {
                if (string.IsNullOrEmpty(type.AssemblyName))
                    continue;

                asm = type.AssemblyName;
                break;
            }
        }

        if (string.IsNullOrEmpty(asm))
            asm = assemblyName;

        return string.IsNullOrEmpty(asm)
            ? $"Pragmatic.Messaging.{GeneratedRegistrationNames.GeneratedNamespace}"
            : GeneratedRegistrationNames.InGeneratedNamespace(asm);
    }

    /// <summary>
    ///     The name every subscription of this assembly is known by at the broker — its consumer group.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <b>Read off the assembly, not off the handlers, and that was measured rather than
    ///         assumed.</b> A subscription is per message <em>type</em>:
    ///         <c>TransportSubscriptionBinder</c> deduplicates by type, subscribes once, and dispatches
    ///         in process to every handler of it. So the name cannot be per handler — that would undo
    ///         the deduplication and deliver one message to one handler instead of all — and it cannot
    ///         be per boundary either, because the handlers of one type may sit in two while there is
    ///         still one subscription and one name to give it. The assembly is the unit that matches:
    ///         the module that registered the subscription.
    ///     </para>
    ///     <para>
    ///         ⚠️ Reading the handler's namespace instead produced names that identify nothing. A
    ///         fixture whose handler sits in <c>MyApp.Handlers</c> was called <c>"handlers"</c> — the
    ///         positional read of a namespace giving back the convention rather than the subject, which
    ///         is a known trap. An assembly name is the module's own name by
    ///         construction: <c>Casework.Verify</c> → <c>verify</c>, <c>MyApp</c> → <c>my-app</c>.
    ///     </para>
    ///     <para>
    ///         The segment is taken the way <c>DefaultMessageRouter.ExtractBoundary</c> takes it — the
    ///         second part, kebab-cased — so a queue named <c>verify.…</c> sits beside a topic named
    ///         <c>intake.events</c> in one vocabulary. Those two conversions have to keep agreeing; this
    ///         one cannot call that one, because the generator does not reference the runtime assembly.
    ///     </para>
    /// </remarks>
    internal string Subscriber
    {
        get
        {
            foreach (var handler in _handlers)
                if (!string.IsNullOrEmpty(handler.AssemblyName))
                    return BoundaryOf(handler.AssemblyName);

            // ⚠️ The namespace is the fallback and not the rule, for the reason in the remark above —
            // it can name a folder instead of a module. It is here because an empty subscriber does not
            // fail here: it is emitted as `(services, "")`, and the binder then throws at startup about
            // a subscription three files away. `NamespaceFor` falls back the same way and for the same
            // reason, so the two agree about what an assembly-less model means.
            foreach (var handler in _handlers)
                if (!string.IsNullOrEmpty(handler.Namespace))
                    return BoundaryOf(handler.Namespace);

            return "";
        }
    }

    /// <summary>The boundary segment of a dotted name, kebab-cased — the second part, or the only one.</summary>
    private static string BoundaryOf(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "";

        var parts = name.Split('.');
        return Core.PermissionNaming.ToKebabCase(parts.Length >= 2 ? parts[1] : parts[0]);
    }

    private void RenderRegistrationBody()
    {
        XmlSummary("Adds all message handler registrations to the service collection.");
        Method(GeneratedRegistrationNames.MessageHandlersMethod, RenderRegistrationMethod,
            "global::Microsoft.Extensions.DependencyInjection.IServiceCollection",
            [new MethodParameter { Type = "global::Microsoft.Extensions.DependencyInjection.IServiceCollection", Name = "services", IsExtension = true }],
            AccessModifier.Public,
            new MethodModifiers { IsStatic = true });
    }

    /// <summary>
    ///     The middlewares this assembly declared with <c>[MessageMiddleware]</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ If the attribute emitted nothing, a middleware declared this way would never be called. There is no failure to see when a wrapper is missing — the handler
    ///         runs, the message is acknowledged, and only whatever the middleware was for is absent.
    ///     </para>
    ///     <para>
    ///         One declaring <c>ForMessageType</c> is wrapped in <c>MessageTypeScopedMiddleware</c> with
    ///         the type written in: the comparison is on a value known at compile time rather than an
    ///         attribute read back at run time. The concrete type is registered as well, because the
    ///         wrapper resolves it — and <c>TryAddScoped</c> so an author who also registered it by hand
    ///         does not get two.
    ///     </para>
    /// </remarks>
    private void RenderMiddlewareRegistrations()
    {
        if (_middlewares.IsEmpty)
            return;

        Comment("[MessageMiddleware] — the pipeline resolves IEnumerable<IMessageMiddleware> and orders by Order");

        foreach (var middleware in _middlewares.OrderBy(m => m.TypeFqn, System.StringComparer.Ordinal))
        {
            if (middleware.ForMessageTypeFqn is not { } messageType)
            {
                AppendLine(
                    "services.TryAddEnumerable(global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor"
                    + $".Scoped<global::Pragmatic.Messaging.IMessageMiddleware, {middleware.TypeFqn}>());");
                continue;
            }

            AppendLine($"services.TryAddScoped<{middleware.TypeFqn}>();");
            AppendLine(
                "services.TryAddEnumerable(global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor"
                + ".Scoped<global::Pragmatic.Messaging.IMessageMiddleware>(sp => "
                + "new global::Pragmatic.Messaging.MessageTypeScopedMiddleware("
                + $"global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<{middleware.TypeFqn}>(sp), "
                + $"typeof({messageType}))));");
        }

        AppendLine();
    }

    private void RenderRegistrationMethod()
    {
        RenderMiddlewareRegistrations();

        if (!_handlers.IsDefaultOrEmpty)
        {
            Comment("Register handlers behind their SG-generated Pipeline (retry/CB/timeout/idempotency)");
            foreach (var handler in _handlers)
            {
                var handlerFqn = string.IsNullOrEmpty(handler.Namespace)
                    ? $"global::{handler.TypeName}"
                    : $"global::{handler.Namespace}.{handler.TypeName}";

                var messageTypeFqn = handler.MessageTypeFqn;

                // TryAddScoped/TryAddEnumerable: idempotent if a module is registered twice. Handlers
                // are resolved via GetServices<IMessageHandler<T>>() (multiple per message), so the
                // interface registration must be enumerable-deduped — plain AddScoped would run a
                // double-registered handler twice.
                AppendLine($"services.TryAddScoped<{handlerFqn}>();");
                AppendLine($"services.TryAddEnumerable(global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Scoped<global::Pragmatic.Messaging.IMessageHandler<{messageTypeFqn}>, {handlerFqn}.Pipeline>());");
            }

            AppendLine();

            // Bridge: register MessageHandlerEventAdapter<T> as IDomainEventHandler<T>
            // so that domain events dispatched by InMemoryEventDispatcher also reach message handlers.
            //
            // ⚠️ Only for message types that ARE domain events, which is the condition this comment
            // always implied and nobody checked: IDomainEventHandler<T> and the adapter are both
            // constrained to IDomainEvent, so emitting it for a plain message put two CS0311 inside this
            // generated file — and a cross-service contract has no reason to be a domain event. Found by
            // the first application here whose message crosses a process boundary.
            var uniqueDomainEventTypes = _handlers
                .Where(h => h.MessageIsDomainEvent)
                .Select(h => h.MessageTypeFqn)
                .Distinct()
                .OrderBy(t => t)
                .ToList();

            if (uniqueDomainEventTypes.Count > 0)
            {
                Comment("Bridge: IDomainEventHandler<T> adapter for domain events dispatched via InMemoryEventDispatcher");
                foreach (var messageTypeFqn in uniqueDomainEventTypes)
                {
                    AppendLine($"services.TryAddEnumerable(global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Scoped<global::Pragmatic.Events.IDomainEventHandler<{messageTypeFqn}>, global::Pragmatic.Messaging.MessageHandlerEventAdapter<{messageTypeFqn}>>());");
                }

                AppendLine();
            }

            // AOT-safe typed dispatch: the generated table pattern-matches this assembly's message
            // types to typed PublishAsync calls, so DispatchAsync never falls back to the DLR.
            // TryAddEnumerable: one table per module assembly, deduped on double registration.
            Comment("Typed dispatch table (AOT-safe DispatchAsync for this assembly's message types)");
            AppendLine(
                "services.TryAddEnumerable(global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton" +
                $"<global::Pragmatic.Messaging.ITypedMessageDispatchTable, global::{RegistrationNamespace}.GeneratedMessageDispatchTable>());");

            AppendLine();

            // Transport subscriptions: one marker per (message type, bus) so each consumer service
            // (default transport or named-bus transport) knows what to subscribe to at startup.
            // No-op when no transport is configured (in-memory only).
            Comment("Transport subscriptions (consumed by the active transport's consumer service)");
            var subscriptionTargets = _handlers
                .Select(h => (h.MessageTypeFqn, h.BusName))
                .Distinct()
                .OrderBy(t => t.MessageTypeFqn)
                .ThenBy(t => t.BusName);
            foreach (var (messageTypeFqn, subscriptionBus) in subscriptionTargets)
            {
                // Who is listening, so the broker can tell this application's queue from the queue of
                // the next application subscribing to the same event. Without it the subscription name
                // was the transport plus the message type, and two services divided one event between
                // them instead of each getting a copy.
                var subscriber = Subscriber;

                AppendLine(subscriptionBus is null
                    ? $"global::Pragmatic.Messaging.Extensions.MessagingSubscriptionExtensions.AddMessageSubscription<{messageTypeFqn}>(services, \"{subscriber}\");"
                    : $"global::Pragmatic.Messaging.Extensions.MessagingSubscriptionExtensions.AddMessageSubscriptionOnBus<{messageTypeFqn}>(services, \"{subscriptionBus}\", \"{subscriber}\");");
            }

            AppendLine();

            // When any handler declares [OnBus], replace DefaultBusResolver with the
            // SG-generated instance resolver so [OnBus] routing decisions are honored at runtime.
            // NOTE: this wires the resolver (which bus a handler targets); materializing a *second*
            // transport per named bus (NamedBusMessageBus with isolated transports) remains
            // [Experimental] — see MessagingBuilder.AddBus.
            if (_handlers.Any(h => h.BusName is not null))
            {
                Comment("Bus resolver: [OnBus] handler → named-bus routing (replaces DefaultBusResolver)");
                AppendLine(
                    "global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.Replace(" +
                    "services, global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton" +
                    "<global::Pragmatic.Messaging.IBusResolver>(" +
                    "global::Pragmatic.Messaging.Generated.PragmaticBusResolver.Instance));");
                AppendLine();
            }
        }

        // AOT-safe FQN → type resolution for THIS assembly's message types. Enumerable: every assembly
        // contributes its own registry, and the consumers (outbox delivery, scheduled messages,
        // dashboard replay) try each until one recognizes the type.
        //
        // ⚠️ Outside the handlers block, deliberately: the registry also covers the
        // domain events this assembly DECLARES, so a service that only publishes has one — and it is
        // the only thing that can read the rows its own outbox wrote. While this line lived inside the
        // handlers block, such a service registered nothing and the pump dead-lettered every row of its
        // own outbox as an "unknown message type".
        if (HasTypeRegistry)
        {
            Comment("Message type registry (outbox delivery, scheduled messages, dashboard replay)");
            AppendLine(
                "services.TryAddEnumerable(global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton" +
                "<global::Pragmatic.Messaging.Entities.IMessageTypeRegistry, global::Pragmatic.Messaging.Generated.PragmaticMessageTypeRegistry>());");
            AppendLine();
        }

        if (_hasPartitionKeys)
        {
            Comment("Partition-key resolver ([PartitionKey] message properties → Kafka message key)");
            AppendLine(
                "services.TryAddEnumerable(global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton" +
                $"<global::Pragmatic.Messaging.IPartitionKeyResolver, global::{RegistrationNamespace}.GeneratedPartitionKeyResolver>());");
            AppendLine();
        }

        if (!_requestHandlers.IsDefaultOrEmpty)
        {
            Comment("Register IRequestHandler<TRequest, TResponse> implementations");
            foreach (var handler in _requestHandlers)
            {
                var handlerFqn = string.IsNullOrEmpty(handler.Namespace)
                    ? $"global::{handler.TypeName}"
                    : $"global::{handler.Namespace}.{handler.TypeName}";

                AppendLine($"services.AddScoped<global::Pragmatic.Messaging.IRequestHandler<{handler.RequestTypeFqn}, {handler.ResponseTypeFqn}>, {handlerFqn}>();");
            }

            AppendLine();

            // Distributed request/reply (responder side): one typed executor per request handler —
            // the transport consumer binds the request queue and the lambda deserializes, executes
            // and serializes with ZERO reflection. Queue naming shares the runtime convention with
            // the requester (RequestReplyConventions.QueueFor).
            Comment("Distributed request/reply executors (bound by the transport consumer service)");
            foreach (var handler in _requestHandlers)
            {
                var req = handler.RequestTypeFqn;
                var res = handler.ResponseTypeFqn;
                AppendLine("services.AddSingleton(new global::Pragmatic.Messaging.RequestReply.RequestSubscription(");
                AppendLine($"    typeof({req}),");
                AppendLine($"    global::Pragmatic.Messaging.RequestReply.RequestReplyConventions.QueueFor(typeof({req})),");
                AppendLine("    static async (sp, payload, context, ct) =>");
                AppendLine("    {");
                AppendLine("        var serializer = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Pragmatic.Messaging.IMessageSerializer>(sp);");
                AppendLine($"        var request = ({req})serializer.Deserialize(payload, typeof({req}))!;");
                AppendLine($"        var handler = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Pragmatic.Messaging.IRequestHandler<{req}, {res}>>(sp);");
                AppendLine("        var response = await handler.HandleAsync(request, context, ct).ConfigureAwait(false);");
                AppendLine($"        return serializer.Serialize(response, typeof({res}));");
                AppendLine("    }));");
            }

            AppendLine();
        }

        AppendLine("return services;");
    }
}
