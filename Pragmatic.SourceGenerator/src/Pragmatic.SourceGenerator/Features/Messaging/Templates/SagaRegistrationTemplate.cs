using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Messaging.Models;

namespace Pragmatic.SourceGenerator.Features.Messaging.Templates;

/// <summary>
///     Generates <c>_Infra.Messaging.SagaRegistration.g.cs</c> with DI registration
///     for ISagaRepository&lt;T&gt; and saga orchestrators.
/// </summary>
internal sealed class SagaRegistrationTemplate : CSharpTemplate
{
    private readonly ImmutableArray<SagaModel> _sagas;
    private readonly bool _hasEfCore;

    public SagaRegistrationTemplate(ImmutableArray<SagaModel> sagas, bool hasEfCore)
    {
        _sagas = sagas;
        _hasEfCore = hasEfCore;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Messaging";
    protected override string? TriggerInfo => $"{_sagas.Length} saga(s)";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForAssembly("Messaging", "SagaRegistration"),
        ToSourceText());

    protected override bool Validate() => !_sagas.IsDefaultOrEmpty;

    public override void RenderFile()
    {
        AddUsing("System.Linq");
        AddUsing("Microsoft.Extensions.DependencyInjection");
        AddUsing("Microsoft.Extensions.DependencyInjection.Extensions");
        AddUsing("Pragmatic.Messaging.Saga");
        AppendLine();

        AppendLine("namespace Pragmatic.Messaging.Generated;");
        AppendLine();

        XmlSummary("Registers saga orchestrators and repositories discovered in this assembly.");
        Class("PragmaticSagaRegistration", RenderBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Partial = true, IsStatic = true });
    }

    private void RenderBody()
    {
        var anySagaHasTimeout = _sagas.Any(s => s.Steps.Any(st => !string.IsNullOrEmpty(st.TimeoutDuration)));

        XmlSummary("Adds saga orchestrators, repositories, and event handlers for all discovered sagas.");
        Method("AddPragmaticSagas", () =>
        {
            foreach (var saga in _sagas)
            {
                var sagaFqn = string.IsNullOrEmpty(saga.Namespace)
                    ? $"global::{saga.TypeName}"
                    : $"global::{saga.Namespace}.{saga.TypeName}";

                var orchestratorFqn = string.IsNullOrEmpty(saga.Namespace)
                    ? $"global::{saga.TypeName}.Orchestrator"
                    : $"global::{saga.Namespace}.{saga.TypeName}.Orchestrator";

                var timeoutRunnerFqn = $"{orchestratorFqn}.TimeoutRunner";
                var sagaHasTimeout = saga.Steps.Any(st => !string.IsNullOrEmpty(st.TimeoutDuration));

                Comment($"Saga: {saga.TypeName} ({saga.Steps.Length} steps, state: {saga.StateTypeShortName}, timeout: {(sagaHasTimeout ? "yes" : "no")})");
                AppendLine($"services.TryAddScoped<{sagaFqn}>();");
                AppendLine($"services.TryAddScoped<{orchestratorFqn}>();");

                // Repository registration (guarded so a consumer that pre-registered its own wins).
                // A saga in an [EnableSagaPersistence] boundary (PersistenceDbContextFqn set) that
                // implements ISaga<TState> gets an EF Core-backed repository (Scoped) resolving that
                // boundary's DbContext lazily — so registration order does not matter. Otherwise it
                // stays on the in-memory Singleton (state kept across per-request scopes, lost on restart).
                var useEfCore = _hasEfCore && saga.ImplementsISaga && !string.IsNullOrEmpty(saga.PersistenceDbContextFqn);
                AppendLine($"if (!services.Any(d => d.ServiceType == typeof(ISagaRepository<{sagaFqn}>)))");
                AppendLine("{");
                IncreaseIndent();
                if (useEfCore)
                {
                    // Resolve the boundary DbContext via the keyed SagaPersistenceMarker (registered by the
                    // host DbContext registration). The lookup is inside the factory so it runs at resolution
                    // time — the boundary library cannot reference the host-generated DbContext type, and
                    // lazy resolution makes this independent of DI registration order.
                    var markerKey = StripGlobal(saga.PersistenceDbContextFqn!);
                    AppendLine($"services.AddScoped<ISagaRepository<{sagaFqn}>>(sp =>");
                    AppendLine("{");
                    IncreaseIndent();
                    AppendLine($"var __marker = sp.GetRequiredKeyedService<global::Pragmatic.Messaging.Configuration.SagaPersistenceMarker>(\"{markerKey}\");");
                    AppendLine($"return new global::Pragmatic.Messaging.Saga.EfCoreSagaRepository<{sagaFqn}, {saga.StateTypeFqn}>(");
                    AppendLine("    (global::Microsoft.EntityFrameworkCore.DbContext)sp.GetRequiredService(__marker.DbContextType),");
                    AppendLine($"    sp.GetRequiredService<global::Microsoft.Extensions.Logging.ILogger<global::Pragmatic.Messaging.Saga.EfCoreSagaRepository<{sagaFqn}, {saga.StateTypeFqn}>>>(),");
                    // Pass the DI-configured PragmaticJsonOptions so the repository resolves the saga's
                    // JsonTypeInfo from the app's generated JsonSerializerContext (AOT-safe). Null-safe:
                    // the repository falls back to PragmaticJsonOptions.Default when it is not registered.
                    AppendLine("    sp.GetService<global::Pragmatic.Serialization.PragmaticJsonOptions>(),");
                    // ⚠️ And the tenant context. SaveWithStepAndOutboxAsync writes
                    // TenantId = tenantContext?.TenantId onto the outbox row, so with it null every
                    // message a saga publishes leaves without a tenant — and because the tenant
                    // interceptor is fail-closed the handler receiving it cannot write: the operation
                    // fails, the handler throws, the delivery dead-letters. The symptom is two adjacent
                    // rows from one scope: the saga instance with its tenant (the interceptor stamps that
                    // from the ambient scope) and the outbox row beside it with null.
                    //
                    // GetService and not GetRequiredService: a single-tenant application registers no
                    // tenant context, and its sagas have to run.
                    AppendLine("    sp.GetService<global::Pragmatic.MultiTenancy.ITenantContext>());");
                    DecreaseIndent();
                    AppendLine("});");
                }
                else
                {
                    RenderInMemoryRepo(sagaFqn);
                }
                DecreaseIndent();
                AppendLine("}");

                // The per-saga timeout runner bridges the non-generic SagaTimeoutBackgroundService
                // to this saga's typed repository + orchestrator. Only registered when at least one
                // step declares [SagaTimeout] — otherwise the runner would poll a deadline column
                // that never gets set.
                if (sagaHasTimeout)
                {
                    AppendLine($"services.AddScoped<global::Pragmatic.Messaging.Saga.ISagaTimeoutRunner, {timeoutRunnerFqn}>();");
                }

                // Ops registry: type-erased active-instance enumeration for dashboards —
                // the delegate captures the typed repository, so no reflection anywhere.
                AppendLine($"services.AddSingleton(new global::Pragmatic.Messaging.Saga.SagaDescriptor(");
                AppendLine($"    typeof({sagaFqn}), \"{saga.TypeName}\", \"{saga.StateTypeShortName}\",");
                AppendLine("    static async (sp, ct) =>");
                AppendLine("    {");
                IncreaseIndent();
                AppendLine($"    var repository = sp.GetRequiredService<ISagaRepository<{sagaFqn}>>();");
                AppendLine("    var active = await repository.GetActiveAsync(ct).ConfigureAwait(false);");
                AppendLine("    var snapshots = new System.Collections.Generic.List<global::Pragmatic.Messaging.Saga.SagaInstanceInfo>(active.Count);");
                AppendLine("    foreach (var instance in active)");
                AppendLine("        snapshots.Add(new global::Pragmatic.Messaging.Saga.SagaInstanceInfo(");
                AppendLine("            instance.Id, instance.CorrelationId, instance.State.ToString(), instance.StartedAt, instance.CompletedAt));");
                AppendLine("    return (System.Collections.Generic.IReadOnlyList<global::Pragmatic.Messaging.Saga.SagaInstanceInfo>)snapshots;");
                DecreaseIndent();
                AppendLine("    }));");

                // Register SG-generated IMessageHandler<TEvent> per distinct event the saga consumes.
                // The orchestrator is the state machine; these handlers bridge IMessageBus → orchestrator.
                //
                // ⚠️ And a TRANSPORT SUBSCRIPTION for each: a local IMessageHandler only answers a
                // message the process already has. Without a queue bound for a saga's events, a step
                // whose message comes over a broker never runs — the start step included, which means
                // the saga never begins and there is nothing to read anywhere, a failure easily
                // mistaken for a queue-name collision.
                // A [MessageHandler] elsewhere in the module registers the same marker; the
                // binder deduplicates by message type, so the pair is harmless.
                var subscriber = Subscriber(saga);
                var seenEvents = new System.Collections.Generic.HashSet<string>();
                foreach (var step in saga.Steps)
                {
                    if (!seenEvents.Add(step.EventTypeFqn)) continue;
                    var handlerFqn = $"{sagaFqn}.EventHandler_{step.EventTypeShortName}";
                    AppendLine($"services.AddScoped<global::Pragmatic.Messaging.IMessageHandler<{step.EventTypeFqn}>, {handlerFqn}>();");
                    AppendLine(
                        "global::Pragmatic.Messaging.Extensions.MessagingSubscriptionExtensions"
                        + $".AddMessageSubscription<{step.EventTypeFqn}>(services, \"{subscriber}\");");
                }

                AppendLine();
            }

            // Register the polling background service once per assembly — but only when at least
            // one saga actually uses [SagaTimeout]. No timeouts = no hosted service = no idle poll.
            if (anySagaHasTimeout)
            {
                AppendLine("// At least one saga declares [SagaTimeout]: register the polling hosted service.");
                AppendLine("if (!services.Any(d => d.ImplementationType == typeof(global::Pragmatic.Messaging.Saga.SagaTimeoutBackgroundService)))");
                AppendLine("{");
                IncreaseIndent();
                AppendLine("services.AddHostedService<global::Pragmatic.Messaging.Saga.SagaTimeoutBackgroundService>();");
                DecreaseIndent();
                AppendLine("}");
                AppendLine();
            }

            AppendLine("return services;");
        },
        "global::Microsoft.Extensions.DependencyInjection.IServiceCollection",
        new System.Collections.Generic.List<MethodParameter>
        {
            new() { Type = "global::Microsoft.Extensions.DependencyInjection.IServiceCollection", Name = "services", IsExtension = true }
        },
        AccessModifier.Public,
        new MethodModifiers { IsStatic = true });
    }

    /// <summary>
    ///     The name this saga's subscriptions are known by at the broker: its module.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The same rule as <c>HandlerRegistrationTemplate.Subscriber</c>, and the two have to agree —
    ///     a module whose saga and whose handlers named themselves differently would bind two queues for
    ///     one message type, receive two copies in one process, and run every handler of it twice. The
    ///     second segment of the assembly name, kebab-cased, so a queue reads <c>intake.…</c> beside a
    ///     topic reading <c>intake.events</c>.
    /// </remarks>
    private static string Subscriber(SagaModel saga)
    {
        var name = !string.IsNullOrEmpty(saga.AssemblyName) ? saga.AssemblyName : saga.Namespace;
        if (string.IsNullOrEmpty(name))
            return "";

        var parts = name.Split('.');
        return Core.PermissionNaming.ToKebabCase(parts.Length >= 2 ? parts[1] : parts[0]);
    }

    // Keyed-marker key = the DbContext full name without the global:: prefix. Must match the key the
    // host DbContext registration uses when it registers the SagaPersistenceMarker.
    private static string StripGlobal(string fqn) =>
        fqn.StartsWith("global::", System.StringComparison.Ordinal) ? fqn.Substring("global::".Length) : fqn;

    // In-memory repository: Singleton so state survives across the per-request scopes that dispatch
    // saga events. Accessors read the ISaga core members without requiring the interface.
    private void RenderInMemoryRepo(string sagaFqn)
    {
        AppendLine($"services.AddSingleton<ISagaRepository<{sagaFqn}>>(sp =>");
        AppendLine($"    new global::Pragmatic.Messaging.Saga.InMemorySagaRepository<{sagaFqn}>(");
        AppendLine("        s => s.Id, s => s.CorrelationId, s => s.CompletedAt));");
    }
}
