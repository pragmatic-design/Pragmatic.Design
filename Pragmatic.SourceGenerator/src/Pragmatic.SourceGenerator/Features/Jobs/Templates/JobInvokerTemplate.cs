using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Jobs.Models;

namespace Pragmatic.SourceGenerator.Features.Jobs.Templates;

/// <summary>
///     Generates <c>{Job}.Invoker.g.cs</c> — nested <c>Invoker</c> class inside a partial
///     extension of the user's job class, so <c>F12</c> on the job type surfaces the generated
///     invoker alongside the user code. Resolves from DI and executes with retry + timeout + telemetry.
/// </summary>
internal sealed class JobInvokerTemplate : CSharpTemplate
{
    private readonly JobModel _model;

    public JobInvokerTemplate(JobModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Jobs";
    protected override string? TriggerInfo => $"[{(_model.IsRecurring ? "RecurringJob" : "Job")}] on {_model.TypeName}";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_model.TypeName, "Invoker", _model.Namespace),
        ToSourceText());

    // A class that implements neither IJob nor IJob<T> gets PRAG2500; emitting an invoker for it
    // would bury that diagnostic under CS1061 inside generated code.
    protected override bool Validate() => _model.ImplementsJobInterface;

    public override void RenderFile()
    {
        AddUsing("System.Diagnostics");
        AddUsing("Microsoft.Extensions.DependencyInjection");
        AddUsing("Microsoft.Extensions.Logging");
        AddUsing("Pragmatic.Jobs");
        AddUsing("Pragmatic.Jobs.Diagnostics");

        AppendNamespace(_model.Namespace);
        AppendLine();

        // Wrap in partial of the user's job class so Go-to-Definition on the job type
        // surfaces both the user source and the nested SG-emitted Invoker.
        XmlSummary($"SG-generated partial for <see cref=\"{_model.TypeName}\"/>. Contains the nested invoker used by the job processor.");
        Class(_model.TypeName, RenderBody,
            accessModifier: ParseAccessibility(_model.Accessibility),
            modifiers: new ClassModifiers { Partial = true });
    }

    private static AccessModifier ParseAccessibility(string accessibility)
    {
        return accessibility.ToLowerInvariant() switch
        {
            "public" => AccessModifier.Public,
            "internal" => AccessModifier.Internal,
            "protected" => AccessModifier.Protected,
            "private" => AccessModifier.Private,
            _ => AccessModifier.Public
        };
    }

    private void RenderBody()
    {
        XmlSummary($"Generated invoker for <see cref=\"{_model.TypeName}\"/>. Resolves from DI, executes with resilience.");
        Class("Invoker", RenderInvokerBody,
            accessModifier: AccessModifier.Internal,
            modifiers: new ClassModifiers { Sealed = true });
    }

    private void RenderInvokerBody()
    {
        // Static execute method — called by JobProcessorService via IJobTypeRegistry
        AppendLine("public static async global::System.Threading.Tasks.Task ExecuteAsync(");
        IncreaseIndent();
        AppendLine("string? parametersJson,");
        AppendLine("JobContext context,");
        // Fully qualified: the invoker must compile in consumers that disable ImplicitUsings.
        AppendLine("global::System.IServiceProvider serviceProvider,");
        AppendLine("global::System.Threading.CancellationToken ct)");
        DecreaseIndent();
        Block(() =>
        {
            AppendLine($"using var activity = JobsDiagnostics.ActivitySource.StartActivity(\"Job.{_model.TypeName}\");");
            AppendLine($"activity?.SetTag(\"job.type\", \"{_model.TypeName}\");");
            AppendLine($"activity?.SetTag(\"job.id\", context.JobId.ToString());");
            AppendLine();

            // Resolve from the caller's scope rather than nesting a child scope: the processor
            // already opens one scope per job, and a nested scope would give the job a *different*
            // scoped ITenantContext than the one the processor set the tenant on.
            AppendLine($"var job = serviceProvider.GetRequiredService<{_model.TypeName}>();");
            AppendLine("var logger = serviceProvider.GetRequiredService<ILogger<Invoker>>();");
            AppendLine();

            // Retries are NOT applied here: an in-process loop is lost when the worker crashes and
            // never advances the persisted Attempt. [Retry] is surfaced via
            // IJobTypeRegistry.GetRetryPolicy and applied durably by the store.
            // Metrics are likewise owned by the processor, which sees the real outcome — counting
            // in both places double-counted every completion and duration sample.
            RenderDirectExecution();

            AppendLine();
            AppendLine("activity?.SetStatus(ActivityStatusCode.Ok);");
        });
    }

    private void RenderDirectExecution()
    {
        AppendLine("try");
        Block(() => RenderJobCall());
        AppendLine("catch (global::System.Exception ex) when (ex is not global::System.OperationCanceledException)");
        Block(() =>
        {
            AppendLine("activity?.SetStatus(ActivityStatusCode.Error, ex.Message);");
            AppendLine("throw;");
        });
    }

    private void RenderJobCall()
    {
        if (_model.HasTimeout)
        {
            AppendLine("using var cts = global::System.Threading.CancellationTokenSource.CreateLinkedTokenSource(ct);");
            AppendLine($"cts.CancelAfter(global::System.TimeSpan.FromSeconds({_model.TimeoutSeconds}));");
        }

        var ctArg = _model.HasTimeout ? "cts.Token" : "ct";

        if (_model.HasParameters)
        {
            // Deserialize through the shared PragmaticJsonOptions seam so the parameters use the same
            // options (camelCase policy + host-registered generated contexts) that JobScheduler +
            // PragmaticJobTypeRegistry serialize with. AOT-clean: the JsonTypeInfo-based overload carries
            // no IL2026/IL3050 (the generated context covers the parameter type).
            AppendLine("var jsonOptions = (serviceProvider.GetService(typeof(global::Pragmatic.Serialization.PragmaticJsonOptions)) as global::Pragmatic.Serialization.PragmaticJsonOptions ?? global::Pragmatic.Serialization.PragmaticJsonOptions.Default).Build();");
            AppendLine($"var parameters = global::System.Text.Json.JsonSerializer.Deserialize(parametersJson!, (global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<{_model.ParameterTypeFqn}>)jsonOptions.GetTypeInfo(typeof({_model.ParameterTypeFqn})));");
            AppendLine($"await job.ExecuteAsync(parameters!, context, {ctArg}).ConfigureAwait(false);");
        }
        else
        {
            AppendLine($"await job.ExecuteAsync(context, {ctArg}).ConfigureAwait(false);");
        }
    }
}
