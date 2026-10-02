using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Runner;

namespace Pragmatic.Migrations.Cli.Interaction;

/// <summary>
///     Abstracts user interaction for different CLI modes:
///     interactive (Spectre.Console), non-interactive (--yes), JSON (--json).
/// </summary>
public interface IInteractionHandler
{
    void Status(string message, StatusLevel level = StatusLevel.Info);
    Task<bool> ConfirmAsync(string question, bool defaultValue = false);
    Task<int> ChooseAsync(string question, IReadOnlyList<string> options);
    Task<string> PromptAsync(string question, string? defaultValue = null);
    void ShowSql(string sql, string? label = null);
    void ShowDiff(SchemaDiff diff);
    void StartProgress(string label);
    void UpdateProgress(int current, int total, string description, bool isBreaking = false);
    void EndProgress();
    void Complete(MigrationResult result);
    void Error(string message, string? detail = null, IReadOnlyList<string>? suggestions = null);
}

public enum StatusLevel
{
    Info,
    Success,
    Warning,
    Error,
    Debug
}
