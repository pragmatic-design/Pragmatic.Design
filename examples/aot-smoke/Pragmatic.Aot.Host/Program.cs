using Microsoft.Data.Sqlite;
using Pragmatic.Aot.Host;
using Pragmatic.Aot.Host.Notes;
using Pragmatic.Authorization;
using Pragmatic.Composition.Hosting;
using Pragmatic.Identity;
using Pragmatic.Migrations.Extensions;
using Pragmatic.Persistence.Sql;

// A composed Pragmatic application on the Sql engine, published Native AOT: the generated entry point, the Notes module's mutation and query
// exposed over HTTP, the schema applied at start by Pragmatic Migrations, and a caller identified by the development header middleware.
// SmokeClient calls the running host and stops it; the exit code is the number of checks that failed.
var database = Path.Combine(Path.GetTempPath(), $"pragmatic-aot-host-{Guid.NewGuid():N}.db");
var connectionString = $"Data Source={database}";

try
{
    await PragmaticApp.RunAsync(
        // A startup that fails exits, rather than serving the maintenance page: the smoke reads the exit code.
        [.. args, "--environment=Development", "--urls=http://127.0.0.1:0", $"--ConnectionStrings:Notes={connectionString}",
            "--Pragmatic:MaintenanceMode:EnableOnStartupFailure=false"],
        app =>
        {
            app.Services.AddNotesBoundaryPersistence((_, filters) => new SqlSession(Open(connectionString), SqlDialect.Sqlite) { Filters = filters });
            app.UsePragmaticMigrations();

            // The caller from the X-User-* headers, and the permissions they carry trusted as the caller's.
            app.UseDevelopmentIdentity();
            app.UseAuthorization(authz => authz.TrustPermissionClaims = true);

            app.Services.AddHostedService<SmokeClient>();
        });
}
finally
{
    SqliteConnection.ClearAllPools();
    File.Delete(database);
}

Console.WriteLine(SmokeClient.Failures == 0 ? "AOT-HOST-OK" : $"{SmokeClient.Failures} failed");
return SmokeClient.Failures == 0 ? 0 : Math.Max(SmokeClient.Failures, 1);

static SqliteConnection Open(string connectionString)
{
    var connection = new SqliteConnection(connectionString);
    connection.Open();
    return connection;
}
