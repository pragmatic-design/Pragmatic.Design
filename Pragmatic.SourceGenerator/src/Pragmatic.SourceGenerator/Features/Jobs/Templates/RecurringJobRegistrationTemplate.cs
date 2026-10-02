using Pragmatic.SourceGenerator.Core;
using System.Collections.Immutable;
using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Jobs.Models;

namespace Pragmatic.SourceGenerator.Features.Jobs.Templates;

/// <summary>
///     Generates <c>_Infra.Jobs.RecurringJobs.g.cs</c> — an <c>IRecurringJobProvider</c> exposing the
///     recurring job definitions declared in this assembly.
/// </summary>
/// <remarks>
///     The generator emits declarations only; seeding <c>NextExecutionAt</c> and persisting them is
///     the runtime registrar's job, because the first occurrence depends on the wall clock and the
///     host's timezone database — neither of which exists at compile time.
/// </remarks>
internal sealed class RecurringJobRegistrationTemplate : CSharpTemplate
{
    private readonly ImmutableArray<JobModel> _recurringJobs;
    private readonly string _namespace;

    public RecurringJobRegistrationTemplate(ImmutableArray<JobModel> recurringJobs, string ns)
    {
        _recurringJobs = recurringJobs;
        _namespace = ns;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Jobs";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForAssembly("Jobs", "RecurringJobs"),
        ToSourceText());

    protected override bool Validate() => !_recurringJobs.IsEmpty;

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Jobs");

        AppendNamespace(_namespace);
        AppendLine();

        XmlSummary("Supplies all SG-discovered recurring job definitions to the scheduler.");
        Class("PragmaticRecurringJobProvider", () =>
        {
            XmlInheritDoc();
            Method("GetDefinitions", RenderDefinitions,
                "global::System.Collections.Generic.IReadOnlyList<RecurringJobDefinition>",
                [],
                AccessModifier.Public);
        },
        interfaces: new System.Collections.Generic.List<string> { "IRecurringJobProvider" },
        accessModifier: AccessModifier.Public,
        modifiers: new ClassModifiers { Sealed = true });
    }

    private void RenderDefinitions()
    {
        AppendLine("return");
        AppendLine("[");
        IncreaseIndent();

        // One definition per SCHEDULE, not per job: a job may run on more than one clock, and the
        // ordering is by id so the emitted file does not reshuffle when an attribute is added.
        var schedules = _recurringJobs
            .SelectMany(job => job.EffectiveSchedules.AsImmutableArray().Select(s => (Job: job, Schedule: s)))
            .OrderBy(pair => pair.Schedule.Id)
            .ThenBy(pair => pair.Schedule.CronExpression);

        foreach (var (job, schedule) in schedules)
        {
            var fqn = $"{job.Namespace}.{job.TypeName}";
            AppendLine("new RecurringJobDefinition");
            AppendLine("{");
            IncreaseIndent();
            AppendLine($"Id = \"{StringHelper.CSharpLiteral(schedule.Id)}\",");
            AppendLine($"JobType = \"{fqn}\",");
            AppendLine($"CronExpression = \"{StringHelper.CSharpLiteral(schedule.CronExpression)}\",");
            AppendLine("IsEnabled = true,");
            if (schedule.TimeZoneId is not null)
                AppendLine($"TimeZoneId = \"{StringHelper.CSharpLiteral(schedule.TimeZoneId)}\",");
            if (schedule.MisfirePolicy != 0)
                AppendLine($"MisfirePolicy = (global::Pragmatic.Jobs.Attributes.MisfirePolicy){schedule.MisfirePolicy},");
            DecreaseIndent();
            AppendLine("},");
        }

        DecreaseIndent();
        AppendLine("];");
    }
}
