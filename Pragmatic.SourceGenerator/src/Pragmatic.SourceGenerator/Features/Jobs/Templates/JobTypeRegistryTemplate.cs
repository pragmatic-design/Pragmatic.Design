using Pragmatic.SourceGenerator.Core;
using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Jobs.Models;

namespace Pragmatic.SourceGenerator.Features.Jobs.Templates;

/// <summary>
///     Generates <c>_Infra.Jobs.TypeRegistry.g.cs</c> — AOT-safe switch expression
///     for job parameter serialization and execution dispatch.
/// </summary>
internal sealed class JobTypeRegistryTemplate : CSharpTemplate
{
    private readonly ImmutableArray<JobModel> _jobs;
    private readonly string _namespace;

    public JobTypeRegistryTemplate(ImmutableArray<JobModel> jobs, string ns)
    {
        _jobs = jobs;
        _namespace = ns;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Jobs";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForAssembly("Jobs", "TypeRegistry"),
        ToSourceText());

    protected override bool Validate() => !_jobs.IsEmpty;

    public override void RenderFile()
    {
        AddUsing("System.Text.Json");
        AddUsing("System.Text.Json.Serialization.Metadata");
        AddUsing("Pragmatic.Jobs");

        AppendNamespace(_namespace);
        AppendLine();

        XmlSummary("AOT-safe job type registry. Maps FQN → parameter deserialization + execution.");
        Class("PragmaticJobTypeRegistry", RenderBody,
            // The source interface: one registry per assembly, contributed to the composite the
            // runtime consumes. See IJobTypeRegistrySource for why it is not IJobTypeRegistry here.
            interfaces: new System.Collections.Generic.List<string> { "IJobTypeRegistrySource" },
            accessModifier: AccessModifier.Internal,
            modifiers: new ClassModifiers { Sealed = true });
    }

    // Emits an `int Method(string jobTypeFqn)` switch returning a per-job integer, default 0.
    private void RenderIntLookup(string methodName, System.Collections.Generic.IEnumerable<JobModel> jobs,
        System.Func<JobModel, int> value)
    {
        XmlInheritDoc();
        Method(methodName, () =>
        {
            AppendLine("return jobTypeFqn switch");
            AppendLine("{");
            IncreaseIndent();
            foreach (var job in jobs)
            {
                var fqn = $"{job.Namespace}.{job.TypeName}";
                AppendLine($"\"{fqn}\" => {value(job)},");
            }
            AppendLine("_ => 0");
            DecreaseIndent();
            AppendLine("};");
        },
        "int",
        new System.Collections.Generic.List<MethodParameter> { new("string", "jobTypeFqn") },
        AccessModifier.Public);
    }

    private void RenderBody()
    {
        // Options come from the shared PragmaticJsonOptions seam so that host-registered
        // source-generated contexts (and the AOT fallback opt-out) apply to job parameters.
        AppendLine("private readonly JsonSerializerOptions _options;");
        AppendLine();
        AppendLine("public PragmaticJobTypeRegistry(global::Pragmatic.Serialization.PragmaticJsonOptions? jsonOptions = null)");
        IncreaseIndent();
        AppendLine("=> _options = (jsonOptions ?? global::Pragmatic.Serialization.PragmaticJsonOptions.Default).Build();");
        DecreaseIndent();
        AppendLine();

        // Knows — what this assembly declares, which is what lets the composite pick between several.
        XmlInheritDoc();
        Method("Knows", () =>
        {
            AppendLine("return jobTypeFqn switch");
            AppendLine("{");
            IncreaseIndent();
            foreach (var job in _jobs)
                AppendLine($"\"{job.Namespace}.{job.TypeName}\" => true,");
            AppendLine("_ => false");
            DecreaseIndent();
            AppendLine("};");
        },
        "bool",
        new System.Collections.Generic.List<MethodParameter> { new("string", "jobTypeFqn") },
        AccessModifier.Public);
        AppendLine();

        // DeserializeParameters
        XmlInheritDoc();
        Method("DeserializeParameters", () =>
        {
            AppendLine("if (json is null) return null;");
            AppendLine("return jobTypeFqn switch");
            AppendLine("{");
            IncreaseIndent();
            foreach (var job in _jobs.Where(j => j.HasParameters))
            {
                var fqn = $"{job.Namespace}.{job.TypeName}";
                // AOT-clean: JsonTypeInfo-based overload (no IL2026/IL3050); the seam's generated context covers this type.
                AppendLine($"\"{fqn}\" => JsonSerializer.Deserialize(json, (JsonTypeInfo<{job.ParameterTypeFqn}>)_options.GetTypeInfo(typeof({job.ParameterTypeFqn}))),");
            }
            AppendLine("_ => null");
            DecreaseIndent();
            AppendLine("};");
        },
        "object?",
        new System.Collections.Generic.List<MethodParameter>
        {
            new("string", "jobTypeFqn"),
            new("string?", "json")
        },
        AccessModifier.Public);

        AppendLine();

        // SerializeParameters
        XmlInheritDoc();
        Method("SerializeParameters", () =>
        {
            AppendLine("if (parameters is null) return null;");
            AppendLine("return jobTypeFqn switch");
            AppendLine("{");
            IncreaseIndent();
            foreach (var job in _jobs.Where(j => j.HasParameters))
            {
                var fqn = $"{job.Namespace}.{job.TypeName}";
                // AOT-clean: JsonTypeInfo-based overload (no IL2026/IL3050).
                AppendLine($"\"{fqn}\" => JsonSerializer.Serialize(({job.ParameterTypeFqn})parameters, (JsonTypeInfo<{job.ParameterTypeFqn}>)_options.GetTypeInfo(typeof({job.ParameterTypeFqn}))),");
            }
            // Default arm: unknown job type — resolve the runtime type's JsonTypeInfo (AOT-clean, still
            // throws under a fallback-disabled options set if the runtime type is not covered).
            AppendLine("_ => JsonSerializer.Serialize(parameters, _options.GetTypeInfo(parameters.GetType()))");
            DecreaseIndent();
            AppendLine("};");
        },
        "string?",
        new System.Collections.Generic.List<MethodParameter>
        {
            new("string", "jobTypeFqn"),
            new("object?", "parameters")
        },
        AccessModifier.Public);

        AppendLine();

        // GetRetryPolicy — declared [Retry] config, applied durably by the store
        XmlInheritDoc();
        Method("GetRetryPolicy", () =>
        {
            AppendLine("return jobTypeFqn switch");
            AppendLine("{");
            IncreaseIndent();
            foreach (var job in _jobs.Where(j => j.HasRetry))
            {
                var fqn = $"{job.Namespace}.{job.TypeName}";
                AppendLine($"\"{fqn}\" => new JobRetryPolicy({job.RetryMaxAttempts}, " +
                           $"global::System.TimeSpan.FromMilliseconds({job.RetryBaseDelayMs}), " +
                           $"(global::Pragmatic.Resilience.Attributes.BackoffStrategy){job.RetryStrategy}),");
            }
            AppendLine("_ => null");
            DecreaseIndent();
            AppendLine("};");
        },
        "JobRetryPolicy?",
        new System.Collections.Generic.List<MethodParameter>
        {
            new("string", "jobTypeFqn")
        },
        AccessModifier.Public);

        AppendLine();

        // GetPriority — declared scheduling priority
        RenderIntLookup("GetPriority", _jobs.Where(j => j.Priority != 0), j => j.Priority);

        AppendLine();

        // GetMaxConcurrency — declared per-host concurrency cap
        RenderIntLookup("GetMaxConcurrency", _jobs.Where(j => j.MaxConcurrency != 0), j => j.MaxConcurrency);

        AppendLine();

        // GetContinuationJobType — declared [Continuation<T>] target
        XmlInheritDoc();
        Method("GetContinuationJobType", () =>
        {
            AppendLine("return jobTypeFqn switch");
            AppendLine("{");
            IncreaseIndent();
            foreach (var job in _jobs.Where(j => j.ContinuationJobTypeFqn is not null))
            {
                var fqn = $"{job.Namespace}.{job.TypeName}";
                var target = job.ContinuationJobTypeFqn!.StartsWith("global::")
                    ? job.ContinuationJobTypeFqn.Substring(8)
                    : job.ContinuationJobTypeFqn;
                AppendLine($"\"{fqn}\" => \"{StringHelper.CSharpLiteral(target)}\",");
            }
            AppendLine("_ => null");
            DecreaseIndent();
            AppendLine("};");
        },
        "string?",
        new System.Collections.Generic.List<MethodParameter>
        {
            new("string", "jobTypeFqn")
        },
        AccessModifier.Public);

        AppendLine();

        // ExecuteAsync — dispatches to SG-generated invoker
        XmlInheritDoc();
        Method("ExecuteAsync", () =>
        {
            AppendLine("return jobTypeFqn switch");
            AppendLine("{");
            IncreaseIndent();
            foreach (var job in _jobs)
            {
                var fqn = $"{job.Namespace}.{job.TypeName}";
                AppendLine($"\"{fqn}\" => global::{job.Namespace}.{job.TypeName}.Invoker.ExecuteAsync(parametersJson, context, serviceProvider, ct),");
            }
            AppendLine("_ => throw new global::System.InvalidOperationException($\"Unknown job type: {jobTypeFqn}\")");
            DecreaseIndent();
            AppendLine("};");
        },
        "global::System.Threading.Tasks.Task",
        new System.Collections.Generic.List<MethodParameter>
        {
            new("string", "jobTypeFqn"),
            new("string?", "parametersJson"),
            new("JobContext", "context"),
            new("global::System.IServiceProvider", "serviceProvider"),
            new("global::System.Threading.CancellationToken", "ct")
        },
        AccessModifier.Public,
        new MethodModifiers { IsAsync = false });
    }
}
