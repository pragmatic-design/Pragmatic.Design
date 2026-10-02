using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Diff.Changes;
using Pragmatic.Migrations.Runner;

namespace Pragmatic.Migrations.Cli.Interaction;

/// <summary>
///     Non-interactive handler for CI/CD (--yes mode).
///     Auto-confirms safe changes, blocks on breaking changes unless --force.
/// </summary>
public sealed class NonInteractiveHandler(bool force) : IInteractionHandler
{
    public void Status(string message, StatusLevel level = StatusLevel.Info)
    {
        var prefix = level switch
        {
            StatusLevel.Success => "[OK]",
            StatusLevel.Warning => "[WARN]",
            StatusLevel.Error => "[ERR]",
            StatusLevel.Debug => "[DBG]",
            _ => "[INFO]"
        };
        Console.WriteLine($"{prefix} {message}");
    }

    public Task<bool> ConfirmAsync(string question, bool defaultValue = false)
    {
        // Non-interactive: auto-confirm if force, otherwise use default
        Console.WriteLine($"[AUTO] {question} → {(force ? "yes (--force)" : defaultValue ? "yes" : "no")}");
        return Task.FromResult(force || defaultValue);
    }

    public Task<int> ChooseAsync(string question, IReadOnlyList<string> options)
    {
        Console.WriteLine($"[AUTO] {question} → {options[0]} (first option)");
        return Task.FromResult(0);
    }

    public Task<string> PromptAsync(string question, string? defaultValue = null)
    {
        Console.WriteLine($"[AUTO] {question} → {defaultValue ?? "(empty)"}");
        return Task.FromResult(defaultValue ?? "");
    }

    public void ShowSql(string sql, string? label = null)
    {
        Console.WriteLine($"--- {label ?? "SQL"} ---");
        Console.WriteLine(sql.Trim());
        Console.WriteLine("---");
    }

    public void ShowDiff(SchemaDiff diff)
    {
        foreach (var change in diff.Changes)
        {
            var prefix = change switch
            {
                CreateTable or AddColumn or AddIndex or AddForeignKey => "+",
                DropTable or DropColumn or DropIndex or DropForeignKey => "-",
                _ => "~"
            };
            var breaking = change.IsBreaking ? " [BREAKING]" : "";
            Console.WriteLine($"  {prefix} {change.Description}{breaking}");
        }
    }

    public void StartProgress(string label) => Console.WriteLine(label);

    public void UpdateProgress(int current, int total, string description, bool isBreaking = false)
        => Console.WriteLine($"  [{(isBreaking ? "!!" : "OK")}] {current}/{total}: {description}");

    public void EndProgress() { }

    public void Complete(MigrationResult result)
    {
        if (result.Success && result.ChangesApplied > 0)
            Console.WriteLine($"Migration complete: {result.ChangesApplied} changes in {result.Duration.TotalMilliseconds:F0}ms");
        else if (result.Success)
            Console.WriteLine("No changes needed.");
        else
            Console.WriteLine($"Migration failed: {result.Error}");
    }

    public void Error(string message, string? detail = null, IReadOnlyList<string>? suggestions = null)
    {
        Console.Error.WriteLine($"ERROR: {message}");
        if (detail is not null) Console.Error.WriteLine(detail);
    }
}
