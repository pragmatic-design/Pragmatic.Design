using Pragmatic.Migrations.Diff;
using Pragmatic.Migrations.Diff.Changes;
using Pragmatic.Migrations.Runner;
using Spectre.Console;

namespace Pragmatic.Migrations.Cli.Interaction;

/// <summary>
///     Rich interactive terminal experience via Spectre.Console.
///     Colored output, progress bars, SQL panels, confirmation prompts.
/// </summary>
public sealed class InteractiveHandler : IInteractionHandler
{
    public void Status(string message, StatusLevel level = StatusLevel.Info)
    {
        var markup = level switch
        {
            StatusLevel.Success => $"[green]{Markup.Escape(message)}[/]",
            StatusLevel.Warning => $"[yellow]{Markup.Escape(message)}[/]",
            StatusLevel.Error => $"[red bold]{Markup.Escape(message)}[/]",
            StatusLevel.Debug => $"[dim]{Markup.Escape(message)}[/]",
            _ => Markup.Escape(message)
        };
        AnsiConsole.MarkupLine($"  {markup}");
    }

    public Task<bool> ConfirmAsync(string question, bool defaultValue = false)
    {
        var result = AnsiConsole.Confirm($"  {question}", defaultValue);
        return Task.FromResult(result);
    }

    public Task<int> ChooseAsync(string question, IReadOnlyList<string> options)
    {
        var prompt = new SelectionPrompt<string>()
            .Title($"  {question}")
            .AddChoices(options);
        var selected = AnsiConsole.Prompt(prompt);
        var index = 0;
        for (var i = 0; i < options.Count; i++)
        {
            if (options[i] == selected) { index = i; break; }
        }
        return Task.FromResult(index);
    }

    public Task<string> PromptAsync(string question, string? defaultValue = null)
    {
        var prompt = new TextPrompt<string>($"  {question}");
        if (defaultValue is not null)
            prompt.DefaultValue(defaultValue);
        return Task.FromResult(AnsiConsole.Prompt(prompt));
    }

    public void ShowSql(string sql, string? label = null)
    {
        var panel = new Panel(Markup.Escape(sql.Trim()))
        {
            Header = new PanelHeader(label ?? "SQL"),
            Border = BoxBorder.Rounded,
            Padding = new Padding(1, 0)
        };
        AnsiConsole.Write(panel);
    }

    public void ShowDiff(SchemaDiff diff)
    {
        foreach (var change in diff.Changes)
        {
            var (prefix, color) = change switch
            {
                CreateTable or AddColumn or AddIndex or AddForeignKey => ("+", "green"),
                DropTable or DropColumn or DropIndex or DropForeignKey => ("-", "red"),
                RenameColumn => ("~", "blue"),
                _ => ("~", "yellow")
            };

            var breaking = change.IsBreaking ? " [red bold]BREAKING[/]" : "";
            AnsiConsole.MarkupLine($"    [{color}]{prefix} {Markup.Escape(change.Description)}[/]{breaking}");
        }
    }

    public void StartProgress(string label)
    {
        AnsiConsole.MarkupLine($"\n  {Markup.Escape(label)}");
    }

    public void UpdateProgress(int current, int total, string description, bool isBreaking = false)
    {
        var icon = isBreaking ? "[yellow]!![/]" : "[green]\u2713[/]";
        AnsiConsole.MarkupLine($"    {icon} {current}/{total} {Markup.Escape(description)}");
    }

    public void EndProgress() { }

    public void Complete(MigrationResult result)
    {
        AnsiConsole.WriteLine();
        if (result.Success && result.ChangesApplied > 0)
            AnsiConsole.MarkupLine($"  [green bold]\u2713 Migration complete:[/] {result.ChangesApplied} changes applied in {result.Duration.TotalMilliseconds:F0}ms");
        else if (result.Success)
            AnsiConsole.MarkupLine("  [green]\u2713 No changes needed — schema is up to date.[/]");
        else
            AnsiConsole.MarkupLine($"  [red bold]\u2717 Migration failed:[/] {Markup.Escape(result.Error ?? "unknown error")}");
    }

    public void Error(string message, string? detail = null, IReadOnlyList<string>? suggestions = null)
    {
        AnsiConsole.MarkupLine($"\n  [red bold]Error:[/] {Markup.Escape(message)}");
        if (detail is not null)
            AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(detail)}[/]");
        if (suggestions is { Count: > 0 })
        {
            AnsiConsole.MarkupLine("\n  [yellow]Suggestions:[/]");
            foreach (var s in suggestions)
                AnsiConsole.MarkupLine($"    - {Markup.Escape(s)}");
        }
    }
}
