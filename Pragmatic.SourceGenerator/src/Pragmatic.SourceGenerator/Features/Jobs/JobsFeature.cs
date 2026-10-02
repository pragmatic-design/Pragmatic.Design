using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Jobs.Diagnostics;
using Pragmatic.SourceGenerator.Features.Jobs.Models;
using Pragmatic.SourceGenerator.Features.Jobs.Templates;
using Pragmatic.SourceGenerator.Features.Jobs.Transforms;

namespace Pragmatic.SourceGenerator.Features.Jobs;

/// <summary>
///     Registers the [RecurringJob] and [Job] source generation pipelines.
/// </summary>
internal static class JobsFeature
{
    private const string SchemaVersion = "1.0";

    /// <param name="context">The incremental generator initialization context.</param>
    /// <param name="programmaticJobs">
    ///     Jobs whose class is emitted by another feature (currently the
    ///     <c>[HasAttachments(PurgeDeletedAfterDays = N)]</c> purge job). They cannot come from
    ///     <c>ForAttributeWithMetadataName</c>: a generator never sees the attributes on its own output,
    ///     so without this injection the class would exist with no invoker, no DI registration and no
    ///     recurring definition — a job that is never scheduled and never runs.
    /// </param>
    /// <returns>
    ///     The job registration this compilation generates, for a host that declares its jobs itself;
    ///     and every job described as the service it is, so the dependency validator can see what it
    ///     takes.
    /// </returns>
    public static (IncrementalValueProvider<EquatableArray<Composition.Models.MetadataEntry>> Registrations,
        IncrementalValueProvider<EquatableArray<Composition.Models.ServiceModel>> Services) Register(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<ImmutableArray<JobModel>>? programmaticJobs = null)
    {
        // Pipeline: [RecurringJob] and [Job] → JobModel
        //
        // ⚠️ A non-partial job is dropped here, before anything is generated for it. The invoker is a
        // nested class, emitted in a `partial class {Job}` the compiler refuses on a non-partial type
        // (CS0260, in a file the author cannot open), and the registry and registration name that
        // invoker. PRAG2502, the companion analyzer's, says why on the declaration.
        var recurringJobs = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeNames.RecurringJob,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: JobTransform.Transform)
            .Where(static m => m is { IsPartial: true })
            .Select(static (m, _) => m!)
            .WithTrackingName(TrackingNames.JobsRecurringJobs);

        var oneOffJobs = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeNames.Job,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: JobTransform.Transform)
            .Where(static m => m is { IsPartial: true })
            .Select(static (m, _) => m!)
            .WithTrackingName(TrackingNames.JobsOneOffJobs);

        // Combine both job types
        var allJobs = recurringJobs.Collect().Combine(oneOffJobs.Collect())
            .Select(static (pair, _) => pair.Left.AddRange(pair.Right))
            .WithTrackingName(TrackingNames.JobsAllJobs);

        if (programmaticJobs is not null)
        {
            allJobs = allJobs.Combine(programmaticJobs.Value)
                .Select(static (pair, _) => pair.Left.AddRange(pair.Right));

            context.RegisterSourceOutputSafe(
                programmaticJobs.Value.SelectMany(static (models, _) => models),
                static (ctx, model) => AddInvoker(ctx, model));
        }

        // Per-job: generate invoker
        context.RegisterSourceOutputSafe(recurringJobs, static (ctx, model) => AddInvoker(ctx, model));
        context.RegisterSourceOutputSafe(oneOffJobs, static (ctx, model) => AddInvoker(ctx, model));

        // Aggregate: registration + type registry
        var rootNs = context.CompilationProvider.Select(static (c, _) =>
            c.AssemblyName ?? "Global");

        var jobsWithNs = allJobs.Combine(rootNs);

        context.RegisterSourceOutputSafe(jobsWithNs, static (ctx, pair) =>
        {
            var (jobs, ns) = pair;
            if (jobs.IsEmpty) return;

            // ── Diagnostics ──
            ValidateJobs(ctx, jobs);

            // Registration
            var regArtifact = new JobRegistrationTemplate(jobs, ns).RenderOutput();
            if (!regArtifact.IsEmpty)
                ctx.AddSource(regArtifact);

            // Type registry
            var registryArtifact = new JobTypeRegistryTemplate(jobs, ns).RenderOutput();
            if (!registryArtifact.IsEmpty)
                ctx.AddSource(registryArtifact);

            // Recurring job definitions
            var recurring = jobs.Where(j => j.IsRecurring).ToImmutableArray();
            if (!recurring.IsEmpty)
            {
                var recurringArtifact = new RecurringJobRegistrationTemplate(recurring, ns).RenderOutput();
                if (!recurringArtifact.IsEmpty)
                    ctx.AddSource(recurringArtifact);
            }

            // Metadata for host aggregation
            var metadataArtifact = new JobMetadataTemplate(jobs, ns).RenderOutput();
            if (!metadataArtifact.IsEmpty)
                ctx.AddSource(metadataArtifact);
        });

        // The host is told to call the registration under the same condition that emits it.
        var registrations = jobsWithNs.Select(static (pair, _) =>
        {
            var (jobs, ns) = pair;
            if (jobs.IsDefaultOrEmpty)
                return EquatableArray<Composition.Models.MetadataEntry>.Empty;

            return ImmutableArray.Create(
                Composition.Models.HostLocalRegistration.Create(
                    Composition.MetadataCategoryIds.Jobs,
                    SchemaVersion,
                    Core.GeneratedRegistrationNames.JobsFqn(ns)));
        });

        return (registrations, allJobs.Select(static (jobs, _) => AsServices(jobs)));
    }

    /// <summary>
    ///     Each job described as the service it is, so the dependency validator sees what it takes.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The generated registration writes <c>TryAddScoped&lt;TJob&gt;()</c>, so a job is resolved
    ///         from the container exactly like a <c>[Service]</c> — so its dependencies are checked the
    ///         same way, not left to the validator that runs over <c>[Service]</c> classes only. A
    ///         missing registration would otherwise surface when the scheduler first ran the job: in a background
    ///         worker, on a schedule, as a log line. The <c>[Service]</c> beside it was refused at build
    ///         time for the same mistake.
    ///     </para>
    ///     <para>
    ///         ⚠️ <c>AsSelf</c>, because that is what the registration does: the job is resolvable by
    ///         its own type and by no interface. Describing it as implementing <c>IJob</c> would put a
    ///         contract in the lookup that nothing registers.
    ///     </para>
    /// </remarks>
    private static EquatableArray<Composition.Models.ServiceModel> AsServices(ImmutableArray<JobModel> jobs)
    {
        if (jobs.IsDefaultOrEmpty)
            return EquatableArray<Composition.Models.ServiceModel>.Empty;

        var services = ImmutableArray.CreateBuilder<Composition.Models.ServiceModel>(jobs.Length);

        foreach (var job in jobs)
        {
            var fullName = string.IsNullOrEmpty(job.Namespace)
                ? $"global::{job.TypeName}"
                : $"global::{job.Namespace}.{job.TypeName}";

            services.Add(new Composition.Models.ServiceModel
            {
                Namespace = job.Namespace,
                TypeName = job.TypeName,
                FullTypeName = fullName,
                ServiceTypeName = fullName,
                Lifetime = "Scoped",
                AsSelf = true,
                Dependencies = job.Dependencies,
                LocationInfo = job.LocationInfo
            });
        }

        return services.ToImmutable();
    }

    // A template whose Validate() fails still yields an EMPTY SourceText rather than null, so the
    // usual `Source is not null` guard would emit a blank file. Length is what actually says
    // "nothing was rendered".
    private static void AddInvoker(SourceProductionContext ctx, JobModel model)
    {
        var artifact = new JobInvokerTemplate(model).RenderOutput();
        if (!artifact.IsEmpty)
            ctx.AddSource(artifact);
    }

    private static void ValidateJobs(SourceProductionContext ctx, ImmutableArray<JobModel> jobs)
    {
        // Per-job validations
        foreach (var job in jobs)
        {
            // PRAG2507: a job on more than one clock where more than one schedule would take the id
            // derived from the class name. Reported rather than suffixed: the id is a persisted key
            // and an operator reads it, so it has to be a name somebody chose.
            if (job.HasUnnamedExtraSchedule)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    JobsDiagnostics.RecurringScheduleNeedsAnId,
                    job.Location ?? Location.None,
                    job.TypeName,
                    job.RecurringJobId));
            }

            // PRAG2500: [Job] on a class that implements neither IJob nor IJob<TParams>.
            // Without this the invoker generates a call to a method that does not exist and the
            // user gets CS1061 inside generated code instead of a diagnostic pointing at their class.
            if (!job.ImplementsJobInterface)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    JobsDiagnostics.MustImplementInterface,
                    job.Location ?? Location.None,
                    job.TypeName));
            }

            // PRAG2505: [Continuation<T>] pointing at a type that is not a job.
            if (job.ContinuationJobTypeFqn is not null && !job.ContinuationImplementsJob)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    JobsDiagnostics.ContinuationMustBeJob,
                    job.Location ?? Location.None,
                    job.ContinuationJobTypeFqn,
                    job.TypeName));
            }

            // PRAG2501: Invalid cron expression
            if (job.IsRecurring && string.IsNullOrWhiteSpace(job.CronExpression))
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    JobsDiagnostics.InvalidCronExpression,
                    job.Location ?? Location.None,
                    job.TypeName));
            }

            // PRAG2504: Retry MaxAttempts <= 0
            if (job.HasRetry && job.RetryMaxAttempts <= 0)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    JobsDiagnostics.InvalidRetryMaxAttempts,
                    job.Location ?? Location.None,
                    job.TypeName,
                    job.RetryMaxAttempts));
            }
        }

        // Aggregate: PRAG2503 — Duplicate recurring job IDs
        var recurringById = jobs
            .Where(j => j.IsRecurring && j.RecurringJobId is not null)
            .GroupBy(j => j.RecurringJobId!)
            .Where(g => g.Count() > 1);

        foreach (var group in recurringById)
        {
            var jobList = group.ToList();
            for (var i = 1; i < jobList.Count; i++)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    JobsDiagnostics.DuplicateRecurringJobId,
                    jobList[i].Location ?? Location.None,
                    group.Key,
                    jobList[0].TypeName,
                    jobList[i].TypeName));
            }
        }

        // Aggregate: PRAG2506 — Continuation cycle detection
        var jobsByFqn = new Dictionary<string, JobModel>();
        foreach (var job in jobs)
        {
            var fqn = string.IsNullOrEmpty(job.Namespace) ? job.TypeName : $"{job.Namespace}.{job.TypeName}";
            jobsByFqn[fqn] = job;
        }

        foreach (var job in jobs.Where(j => j.ContinuationJobTypeFqn is not null))
        {
            var startFqn = string.IsNullOrEmpty(job.Namespace) ? job.TypeName : $"{job.Namespace}.{job.TypeName}";
            var visited = new HashSet<string> { startFqn };
            var path = new List<string> { startFqn };
            var current = job.ContinuationJobTypeFqn;

            while (current is not null)
            {
                // Strip global:: prefix if present
                var cleanFqn = current.StartsWith("global::") ? current.Substring(8) : current;

                if (!visited.Add(cleanFqn))
                {
                    path.Add(cleanFqn);
                    ctx.ReportDiagnostic(Diagnostic.Create(
                        JobsDiagnostics.ContinuationCycleDetected,
                        job.Location ?? Location.None,
                        job.TypeName,
                        string.Join(" → ", path)));
                    break;
                }

                path.Add(cleanFqn);

                if (jobsByFqn.TryGetValue(cleanFqn, out var nextJob))
                    current = nextJob.ContinuationJobTypeFqn;
                else
                    break; // External job, can't trace further
            }
        }
    }
}
