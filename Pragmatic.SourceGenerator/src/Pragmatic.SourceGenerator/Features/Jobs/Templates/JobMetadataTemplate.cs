using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Composition;
using Pragmatic.SourceGenerator.Features.Jobs.Models;

namespace Pragmatic.SourceGenerator.Features.Jobs.Templates;

/// <summary>
///     Generates <c>_Metadata.Jobs.g.cs</c> — assembly metadata
///     for Composition host aggregation.
/// </summary>
internal sealed class JobMetadataTemplate : CSharpTemplate
{
    private readonly ImmutableArray<JobModel> _jobs;
    private readonly string _namespace;

    public JobMetadataTemplate(ImmutableArray<JobModel> jobs, string ns)
    {
        _jobs = jobs;
        _namespace = ns;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Jobs";
    protected override string? TriggerInfo => $"{_jobs.Length} job(s)";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForMetadata("Jobs"),
        ToSourceText());

    protected override bool Validate() => !_jobs.IsDefaultOrEmpty;

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Composition.Attributes");
        AddUsing("Pragmatic.Composition.Metadata");
        AppendLine();

        var registrationFqn = JsonEscape(GeneratedRegistrationNames.JobsFqn(_namespace));
        var recurringCount = _jobs.Count(j => j.IsRecurring);

        AppendLine($"[assembly: PragmaticMetadata(MetadataCategory.Jobs, \"1.0\", \"\"\"");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("\"generator\": \"Pragmatic.SourceGenerator/Jobs\",");
        AppendLine($"\"registrationMethod\": \"{registrationFqn}\",");
        AppendLine("\"data\": {");
        IncreaseIndent();
        AppendLine($"\"jobCount\": {_jobs.Length},");
        AppendLine($"\"recurringCount\": {recurringCount},");
        AppendLine("\"jobs\": [");
        IncreaseIndent();

        for (var i = 0; i < _jobs.Length; i++)
        {
            var job = _jobs[i];
            var comma = i < _jobs.Length - 1 ? "," : "";
            var fqn = JsonEscape($"{job.Namespace}.{job.TypeName}");
            var recurring = job.IsRecurring ? "true" : "false";

            AppendLine($"{{ \"type\": \"{fqn}\", \"recurring\": {recurring}" +
                       (job.RecurringJobId is not null ? $", \"id\": \"{JsonEscape(job.RecurringJobId)}\"" : "") +
                       $" }}{comma}");
        }

        DecreaseIndent();
        AppendLine("]");
        DecreaseIndent();
        AppendLine("}");
        DecreaseIndent();
        AppendLine("}");
        AppendLine("\"\"\")]");
    }

    // Escapes a value for safe embedding inside a JSON string literal
    // (backslash and double-quote). Prevents a TypeName / RecurringJobId
    // containing '\' or '"' from corrupting the emitted metadata JSON.
    private static string JsonEscape(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
