using System.Text.Json;
using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Diff.Changes;
using Pragmatic.Migrations.Runner;

namespace Pragmatic.Migrations.Cli.Interaction;

/// <summary>
///     JSON handler for programmatic consumption (--json mode).
///     Outputs NDJSON to stdout. Status messages go to stderr.
/// </summary>
public sealed class JsonHandler : IInteractionHandler
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public void Status(string message, StatusLevel level = StatusLevel.Info)
    {
        Emit(new { type = "status", level = level.ToString().ToLowerInvariant(), message });
    }

    public Task<bool> ConfirmAsync(string question, bool defaultValue = false)
    {
        // JSON mode is non-interactive — auto-confirm
        Emit(new { type = "confirm_auto", question, value = defaultValue });
        return Task.FromResult(defaultValue);
    }

    public Task<int> ChooseAsync(string question, IReadOnlyList<string> options)
    {
        Emit(new { type = "choose_auto", question, options, selected = 0 });
        return Task.FromResult(0);
    }

    public Task<string> PromptAsync(string question, string? defaultValue = null)
    {
        Emit(new { type = "prompt_auto", question, value = defaultValue ?? "" });
        return Task.FromResult(defaultValue ?? "");
    }

    public void ShowSql(string sql, string? label = null) =>
        Emit(new { type = "sql", label, sql = sql.Trim() });

    public void ShowDiff(SchemaDiff diff)
    {
        var changes = diff.Changes.Select(c => new
        {
            description = c.Description,
            isBreaking = c.IsBreaking,
            changeType = c.GetType().Name
        });
        Emit(new { type = "diff", changes });
    }

    public void StartProgress(string label) =>
        Emit(new { type = "progress_start", label });

    public void UpdateProgress(int current, int total, string description, bool isBreaking = false) =>
        Emit(new { type = "progress", current, total, description, isBreaking });

    public void EndProgress() =>
        Emit(new { type = "progress_end" });

    public void Complete(MigrationResult result) =>
        Emit(new
        {
            type = "complete",
            success = result.Success,
            changesApplied = result.ChangesApplied,
            durationMs = result.Duration.TotalMilliseconds,
            error = result.Error
        });

    public void Error(string message, string? detail = null, IReadOnlyList<string>? suggestions = null) =>
        Emit(new { type = "error", message, detail, suggestions });

    private static void Emit(object data) =>
        Console.WriteLine(JsonSerializer.Serialize(data, JsonOpts));
}
