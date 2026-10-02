using Pragmatic.SourceGenerator.Core;
using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Jobs.Models;

namespace Pragmatic.SourceGenerator.Features.Jobs.Templates;

/// <summary>
///     Generates <c>_Infra.Jobs.Registration.g.cs</c> — DI registration for all jobs, the AOT
///     dispatch registry, and the recurring job provider.
/// </summary>
/// <remarks>
///     The generated registry is added as one <c>IJobTypeRegistrySource</c> of the
///     <c>CompositeJobTypeRegistry</c> that <c>AddPragmaticJobs()</c> registers; without it the composite
///     has no source and refuses every job with "No registry is registered". Registering here — rather
///     than from the host template — keeps the generated registry <c>internal</c> and same-assembly,
///     which is what its dispatch switch requires.
/// </remarks>
internal sealed class JobRegistrationTemplate : CSharpTemplate
{
    private readonly ImmutableArray<JobModel> _jobs;
    private readonly string _namespace;

    public JobRegistrationTemplate(ImmutableArray<JobModel> jobs, string ns)
    {
        _jobs = jobs;
        _namespace = ns;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Jobs";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForAssembly("Jobs", "Registration"),
        ToSourceText());

    protected override bool Validate() => !_jobs.IsEmpty;

    public override void RenderFile()
    {
        AddUsing("Microsoft.Extensions.DependencyInjection");
        AddUsing("Microsoft.Extensions.DependencyInjection.Extensions");

        AppendNamespace(_namespace);
        AppendLine();

        XmlSummary("Registers all SG-discovered jobs, the AOT dispatch registry and recurring definitions.");
        Class(GeneratedRegistrationNames.JobsClass, () =>
        {
            XmlSummary("Registers job classes, the generated job type registry and recurring job definitions.");
            Method(GeneratedRegistrationNames.JobsMethod, RenderBody,
            "global::Microsoft.Extensions.DependencyInjection.IServiceCollection",
            new System.Collections.Generic.List<MethodParameter>
            {
                new("global::Microsoft.Extensions.DependencyInjection.IServiceCollection", "services") { IsExtension = true }
            },
            AccessModifier.Public,
            new MethodModifiers { IsStatic = true });
        },
        accessModifier: AccessModifier.Public,
        modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderBody()
    {
        foreach (var job in _jobs.OrderBy(j => j.TypeName))
        {
            AppendLine($"services.TryAddScoped<global::{job.Namespace}.{job.TypeName}>();");
        }

        AppendLine();
        AppendLine("// Contributes this assembly's job types to the composite AddPragmaticJobs() registers.");
        AppendLine("// ⚠️ Enumerable, not Replace: a host has one registry per assembly that declares a");
        AppendLine("// [Job] — its modules, and any package shipping one — and replacing meant the last");
        AppendLine("// one loaded was the only one that answered. The others' jobs were refused as");
        AppendLine("// \"Unknown job type\", after whatever they carried had been acknowledged.");
        AppendLine("services.TryAddEnumerable(global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor");
        IncreaseIndent();
        // ⚠️ Both type arguments: TryAddEnumerable deduplicates on the IMPLEMENTATION type and refuses a
        // descriptor that has none — "indistinguishable from other services registered". A factory
        // alone gives it none.
        AppendLine(".Singleton<global::Pragmatic.Jobs.IJobTypeRegistrySource, PragmaticJobTypeRegistry>(static sp =>");
        IncreaseIndent();
        AppendLine("new PragmaticJobTypeRegistry(sp.GetService<global::Pragmatic.Serialization.PragmaticJsonOptions>())));");
        DecreaseIndent();
        DecreaseIndent();

        if (_jobs.Any(j => j.IsRecurring))
        {
            AppendLine();
            AppendLine("// One provider per assembly; the scheduler seeds and persists them at startup.");
            AppendLine("services.TryAddEnumerable(global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor");
            IncreaseIndent();
            AppendLine(".Singleton<global::Pragmatic.Jobs.IRecurringJobProvider, PragmaticRecurringJobProvider>());");
            DecreaseIndent();
        }

        AppendLine();
        AppendLine("return services;");
    }
}
