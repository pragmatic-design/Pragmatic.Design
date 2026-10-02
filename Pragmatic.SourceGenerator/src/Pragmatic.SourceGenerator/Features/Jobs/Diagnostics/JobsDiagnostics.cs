using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Jobs.Diagnostics;

/// <summary>
///     Diagnostic descriptors for Pragmatic.Jobs source generator (PRAG2500-2549).
/// </summary>
internal static class JobsDiagnostics
{
    // PRAG2500: [Job]/[RecurringJob] on class that doesn't implement IJob or IJob<T>
    public static readonly DiagnosticDescriptor MustImplementInterface = DiagnosticFactory.Error(
        "PRAG2500",
        "Job class must implement IJob or IJob<T>",
        "Type '{0}' is decorated with [Job]/[RecurringJob] but does not implement IJob or IJob<T>",
        "Add IJob or IJob<T> interface to the job class.");

    // PRAG2501: Invalid cron expression
    public static readonly DiagnosticDescriptor InvalidCronExpression = DiagnosticFactory.Error(
        "PRAG2501",
        "Invalid cron expression",
        "[RecurringJob] on '{0}' has an empty or missing cron expression",
        "Provide a valid cron expression (e.g., \"0 2 * * *\" for daily at 2 AM).");

    // PRAG2502 (job class should be partial) is the companion analyzer's, which reports it on the
    // declaration (NotPartialDiagnosticDescriptors).

    // PRAG2503: Duplicate recurring job ID
    public static readonly DiagnosticDescriptor DuplicateRecurringJobId = DiagnosticFactory.Error(
        "PRAG2503",
        "Duplicate recurring job ID",
        "Recurring job ID '{0}' is used by both '{1}' and '{2}'. Each recurring job must have a unique ID.",
        "Specify a unique Id in [RecurringJob(cron, Id = \"unique-id\")].");

    // PRAG2504: [Retry] with MaxAttempts <= 0
    public static readonly DiagnosticDescriptor InvalidRetryMaxAttempts = DiagnosticFactory.Error(
        "PRAG2504",
        "Invalid retry configuration",
        "[Retry] on '{0}' has MaxAttempts = {1}. MaxAttempts must be greater than 0.",
        "Set MaxAttempts to a positive integer.");

    // PRAG2505: [Continuation<T>] where T doesn't implement IJob or IJob<T>
    public static readonly DiagnosticDescriptor ContinuationMustBeJob = DiagnosticFactory.Error(
        "PRAG2505",
        "Continuation must be a job",
        "[Continuation<{0}>] on '{1}': type '{0}' does not implement IJob or IJob<T>",
        "The continuation type must implement IJob or IJob<T>.");

    // PRAG2506: Continuation cycle detected (A→B→A or A→B→C→A)
    public static readonly DiagnosticDescriptor ContinuationCycleDetected = DiagnosticFactory.Error(
        "PRAG2506",
        "Continuation cycle detected",
        "Job '{0}' creates a cycle in continuation chain: {1}. Remove [ContinueWith<T>] to break the cycle.",
        "Job continuation chains must be acyclic (no loops).");

    /// <summary>
    ///     A second (or later) <c>[RecurringJob]</c> that does not name itself.
    /// </summary>
    /// <remarks>
    ///     The default id is derived from the class name, so two schedules of one job would both want
    ///     it. The generator asks for a name rather than inventing one: a positional suffix is a name
    ///     nobody chose, it is what an operator reads in the schedule list, and it would renumber
    ///     silently the day the attributes are reordered - while the id is a persisted key.
    /// </remarks>
    public static readonly DiagnosticDescriptor RecurringScheduleNeedsAnId = DiagnosticFactory.Error(
        "PRAG2507",
        "A second schedule has to name itself",
        "Job '{0}' declares more than one [RecurringJob] and the schedule for '{1}' has no Id. "
        + "Give it one: the default is derived from the class name and both schedules would take it",
        "Only the first [RecurringJob] may rely on the id derived from the class name. Write "
        + "Id = \"...\" on the others, so the id an operator sees is one somebody chose.");

    /// <summary>
    ///     PRAG2508: <c>[EnableJobPersistence]</c> on a boundary in a compilation with no
    ///     <c>Pragmatic.Jobs.EFCore</c> to supply the two configurations.
    /// </summary>
    /// <remarks>
    ///     The generated context names <c>JobEntityTypeConfiguration</c> and
    ///     <c>RecurringJobEntityTypeConfiguration</c>, which live in that package. Without it the
    ///     attribute maps nothing — and a <c>jobs.UseEfCore()</c> then fails at startup against tables
    ///     that were never created.
    /// </remarks>
    public static readonly DiagnosticDescriptor JobPersistenceWithoutEfCore = DiagnosticFactory.Warning(
        "PRAG2508",
        "[EnableJobPersistence] requires Pragmatic.Jobs.EFCore",
        "Boundary '{0}' is marked [EnableJobPersistence] but this project does not reference "
        + "Pragmatic.Jobs.EFCore, so __Jobs and __RecurringJobs are not mapped and no migration creates "
        + "them (the attribute is a no-op)",
        "Reference Pragmatic.Jobs.EFCore, whose two IEntityTypeConfiguration the generated DbContext "
        + "applies — then jobs.UseEfCore() has the tables it refuses to run without.");

    /// <summary>
    ///     PRAG2509: more than one boundary carries <c>[EnableJobPersistence]</c>.
    /// </summary>
    /// <remarks>
    ///     The job store is one store for the application — a lease taken in one database says nothing
    ///     about a queue in another — so the tables live in one boundary's database. Two would be two
    ///     queues, each half-served by the workers that poll the other.
    /// </remarks>
    public static readonly DiagnosticDescriptor JobPersistenceOnMoreThanOneBoundary = DiagnosticFactory.Error(
        "PRAG2509",
        "[EnableJobPersistence] must mark exactly one boundary",
        "Boundary '{0}' is marked [EnableJobPersistence], but the job store is a single store and "
        + "another boundary already owns it; only one boundary may host __Jobs and __RecurringJobs",
        "Keep [EnableJobPersistence] on exactly one boundary — the one whose database should hold the "
        + "queue and the schedules.");
}
