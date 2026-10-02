using System.Text.Json;
using Pragmatic.Migrations.Cli.Discovery;
using Pragmatic.Migrations.Cli.Interaction;
using Pragmatic.Migrations.Cli.Pipeline;
using Pragmatic.Migrations.Introspection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Pragmatic.Migrations.Cli.Commands;

/// <summary>
///     Shows migration audit history from the __PragmaticSchema table.
/// </summary>
public sealed class HistoryCommand : AsyncCommand<CommonSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, CommonSettings settings)
    {
        var (schemas, handler, resolver) = CommandHelper.Bootstrap(settings);
        using var cts = ConsoleCancellation.CreateLinkedTokenSource();
        var ct = cts.Token;

        handler.Status("\u25c6 Pragmatic Migrations — History\n");

        foreach (var schema in schemas)
        {
            var dbName = schema.DatabaseName ?? "default";
            if (settings.DatabaseFilter is not null &&
                !string.Equals(settings.DatabaseFilter, dbName, StringComparison.OrdinalIgnoreCase))
                continue;

            var connectionString = resolver.Resolve(dbName, schema.ConfigKey);
            if (connectionString is null)
            {
                handler.Error($"No connection string for '{dbName}'");
                continue;
            }

            var factory = ProviderFactoryHelper.CreateConnectionFactory(schema.ProviderName);
            var connection = await factory.CreateOpenConnectionAsync(connectionString, ct)
                .ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                // The audit table name lives in the host's DI configuration, which the CLI does not
                // build — so it has to be told when the host renamed it.
                var history = await SchemaAuditStore.ReadHistoryAsync(
                        connection, ct, settings.AuditTableName ?? MigrationConstants.AuditTableName)
                    .ConfigureAwait(false);

                handler.Status($"{dbName}:", StatusLevel.Info);

                if (history.Count == 0)
                {
                    handler.Status("  No migration history found.", StatusLevel.Warning);
                    continue;
                }

                if (settings.Json)
                {
                    var jsonOutput = JsonSerializer.Serialize(new
                    {
                        database = dbName,
                        history = history.Select(h => new
                        {
                            hash = h.Hash,
                            appliedAt = h.AppliedAt,
                            changeCount = h.ChangeCount,
                            durationMs = h.DurationMs,
                            appliedBy = h.AppliedBy
                        })
                    }, new JsonSerializerOptions { WriteIndented = true });
                    Console.WriteLine(jsonOutput);
                }
                else
                {
                    var table = new Table();
                    table.AddColumn("#");
                    table.AddColumn("Hash");
                    table.AddColumn("Applied At");
                    table.AddColumn("Changes");
                    table.AddColumn("Duration");
                    table.AddColumn("Applied By");

                    for (var i = 0; i < history.Count; i++)
                    {
                        var h = history[i];
                        table.AddRow(
                            (i + 1).ToString(),
                            h.Hash ?? "",
                            h.AppliedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                            h.ChangeCount.ToString(),
                            $"{h.DurationMs}ms",
                            h.AppliedBy ?? "");
                    }

                    AnsiConsole.Write(table);
                }
            }
        }

        return 0;
    }
}
