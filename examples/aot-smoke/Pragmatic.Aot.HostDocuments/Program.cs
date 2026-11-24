using Pragmatic.Aot.Host;
using Pragmatic.Aot.Host.Notes;
using Pragmatic.Authorization;
using Pragmatic.Composition.Hosting;
using Pragmatic.Identity;
using Pragmatic.Migrations.Extensions;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Documents.Mongo;

// A composed Pragmatic application on the documents engine, published Native AOT: the generated entry point, the Notes module's mutation
// and query exposed over HTTP, the boundary's documents schema applied at start with the migrations, and a caller identified by the
// development header middleware. MongoDB from PRAGMATIC_AOT_MONGO, as the documents smoke, on a database of its own named in the
// connection string's path. SmokeClient calls the running host and stops it; the exit code is the number of checks that failed.
if (Environment.GetEnvironmentVariable("PRAGMATIC_AOT_MONGO") is not { } mongo)
{
    Console.WriteLine("FAIL PRAGMATIC_AOT_MONGO is not set");
    return 1;
}

var builder = new UriBuilder(mongo) { Path = "/h" + Guid.NewGuid().ToString("N")[..12] };
var connectionString = builder.Uri.ToString();

await PragmaticApp.RunAsync(
    // A startup that fails exits, rather than serving the maintenance page: the smoke reads the exit code.
    [.. args, "--environment=Development", "--urls=http://127.0.0.1:0", $"--ConnectionStrings:Notes={connectionString}",
        "--Pragmatic:MaintenanceMode:EnableOnStartupFailure=false"],
    app =>
    {
        app.Services.AddSingleton(static sp => new MongoClient(MongoSettings.Parse(NotesConnectionString(sp))));
        app.Services.AddNotesBoundaryPersistence(static (sp, filters) => new DocSession(sp.GetRequiredService<MongoClient>(),
            MongoSettings.Parse(NotesConnectionString(sp)).Database ?? throw new InvalidOperationException("ConnectionStrings:Notes names no database."))
        {
            Filters = filters,
        });
        app.UsePragmaticMigrations();

        // The caller from the X-User-* headers, and the permissions they carry trusted as the caller's.
        app.UseDevelopmentIdentity();
        app.UseAuthorization(authz => authz.TrustPermissionClaims = true);

        app.Services.AddHostedService<SmokeClient>();
    });

// The HTTP checks cannot tell that the host applied the schema at start: MongoDB creates a collection on its first write. Its index it
// does not (Note.Index.cs), so a second EnsureAsync finding the schema unchanged can: nothing created the index but the host's start,
// which skips the schema without a migration runner.
var failures = SmokeClient.Failures < 0 ? 1 : SmokeClient.Failures;
await using (var check = new MongoClient(MongoSettings.Parse(connectionString)))
{
    var schema = await NotesBoundaryDocSchema.EnsureAsync(new DocSession(check, MongoSettings.Parse(connectionString).Database!));
    Console.WriteLine($"{(schema.Unchanged ? "ok  " : "FAIL")} the host applied the documents schema at start: unchanged={schema.Unchanged}");
    failures += schema.Unchanged ? 0 : 1;
}

Console.WriteLine(failures == 0 ? "AOT-HOST-OK" : $"{failures} failed");
return failures == 0 ? 0 : Math.Max(failures, 1);

static string NotesConnectionString(IServiceProvider services)
    => services.GetRequiredService<IConfiguration>().GetConnectionString("Notes") ?? throw new InvalidOperationException("ConnectionStrings:Notes is required.");
