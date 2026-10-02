using System.Text.Json;
using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Diff.Changes;
using Pragmatic.Migrations.Runner;

namespace Pragmatic.Migrations.Cli.Interaction;

/// <summary>
///     Bidirectional NDJSON handler for AI agent integration (--agent mode).
///     Outputs structured events to stdout, reads commands from stdin.
///     Protocol: one JSON object per line (NDJSON).
/// </summary>
public sealed class AgentHandler : IInteractionHandler
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public void Status(string message, StatusLevel level = StatusLevel.Info)
        => Emit(new { type = "status", level = level.ToString().ToLowerInvariant(), message });

    public async Task<bool> ConfirmAsync(string question, bool defaultValue = false)
    {
        Emit(new { type = "confirm_required", question, @default = defaultValue });
        var response = await ReadResponseAsync().ConfigureAwait(false);
        return response?.TryGetProperty("value", out var val) == true && val.GetBoolean();
    }

    public async Task<int> ChooseAsync(string question, IReadOnlyList<string> options)
    {
        Emit(new { type = "choose_required", question, options });
        var response = await ReadResponseAsync().ConfigureAwait(false);
        return response?.TryGetProperty("index", out var val) == true ? val.GetInt32() : 0;
    }

    public async Task<string> PromptAsync(string question, string? defaultValue = null)
    {
        Emit(new { type = "prompt_required", question, @default = defaultValue });
        var response = await ReadResponseAsync().ConfigureAwait(false);
        return response?.TryGetProperty("value", out var val) == true
            ? val.GetString() ?? defaultValue ?? ""
            : defaultValue ?? "";
    }

    public void ShowSql(string sql, string? label = null)
        => Emit(new { type = "sql", label, sql = sql.Trim() });

    public void ShowDiff(SchemaDiff diff)
    {
        var changes = diff.Changes.Select(c => new
        {
            description = c.Description,
            isBreaking = c.IsBreaking,
            changeType = c.GetType().Name
        });
        Emit(new { type = "diff", changes, breakingCount = diff.Changes.Count(c => c.IsBreaking) });
    }

    public void StartProgress(string label) => Emit(new { type = "progress_start", label });
    public void UpdateProgress(int current, int total, string description, bool isBreaking = false)
        => Emit(new { type = "progress", current, total, description, isBreaking });
    public void EndProgress() => Emit(new { type = "progress_end" });

    public void Complete(MigrationResult result)
        => Emit(new
        {
            type = "complete",
            success = result.Success,
            changesApplied = result.ChangesApplied,
            durationMs = result.Duration.TotalMilliseconds,
            error = result.Error,
            failedChangeIndex = result.FailedChangeIndex,
            suggestions = result.Suggestions.IsDefaultOrEmpty ? null : result.Suggestions.ToArray()
        });

    public void Error(string message, string? detail = null, IReadOnlyList<string>? suggestions = null)
        => Emit(new { type = "error", message, detail, suggestions });

    private static void Emit(object data)
        => Console.WriteLine(JsonSerializer.Serialize(data, JsonOpts));

    private static async Task<JsonElement?> ReadResponseAsync()
    {
        var line = await Console.In.ReadLineAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(line)) return null;
        try
        {
            // Clone the root element: the JsonDocument's pooled buffer is released on Dispose,
            // and returning a live RootElement would reference freed memory.
            using var doc = JsonDocument.Parse(line);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null; // Malformed input line — treat as no response.
        }
    }
}
