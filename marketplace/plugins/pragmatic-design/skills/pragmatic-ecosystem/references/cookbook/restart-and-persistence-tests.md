# Cookbook — Proving the data survives a restart

"It must not lose anything" is the requirement a line-of-business application is asked for most, and
the test template in `scaffold/solution-template.md` cannot prove it: `WebApplicationFactory` runs the
host in memory, inside the test process, and there is no process to kill. This recipe runs the **built
host as its own process** on a real TCP port, against PostgreSQL in a container, and restarts either
one under the other.

> Everything below was built and run against the `1.0.0-alpha` packages: the code on this page, with
> `{{App}}` = `CondoLibrary` and a `Book` resource (`POST api/books`, `GET api/books/{id}` as a
> `Single` query), passes three tests out of three, twice in a row. With the restarts sabotaged to
> lose the data, the two restart tests fail. Replace `api/books` and the `Book` record with a resource
> of yours.

## What each test proves

| Test | Red means |
|---|---|
| a book written before the service is killed is read after it restarts | the data lived in the process — an in-memory store, a cache answering for the database, a write never committed |
| a book written before the database restarts is read after it | the data lived in something the container restart discards |
| a written book is a row in the database | the service answered from somewhere other than PostgreSQL — read around the service, with `psql` inside the container |

The restart is a **kill**, not a graceful stop: `Process.Kill`, the way a crash or a pulled plug ends
the process. A write that reached the client as `201` must be there afterwards; one that only waited
for a clean shutdown to be flushed would not be.

## 1. The test project — `{{App}}.RestartTests.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <!-- Built first, never loaded: the tests start the host as its own process and talk to it over TCP. -->
    <ProjectReference Include="..\..\src\{{App}}.Host\{{App}}.Host.csproj" ReferenceOutputAssembly="false" />
  </ItemGroup>

  <ItemGroup>
    <!-- Where that build puts the host, read back by ServiceProcess. -->
    <AssemblyAttribute Include="System.Reflection.AssemblyMetadataAttribute">
      <_Parameter1>HostAssemblyPath</_Parameter1>
      <_Parameter2>$(MSBuildThisFileDirectory)..\..\src\{{App}}.Host\bin\$(Configuration)\$(TargetFramework)\{{App}}.Host.dll</_Parameter2>
    </AssemblyAttribute>
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Testcontainers.PostgreSql" />
  </ItemGroup>
</Project>
```

`ReferenceOutputAssembly="false"` makes `dotnet test` build the host first without loading it into the
test process. The assembly attribute records where that build puts `{{App}}.Host.dll`, so the tests
find it without a hard-coded path. Versions come from `Directory.Packages.props` as in the solution
template (`Testcontainers.PostgreSql` 4.15.0 or later).

## 2. The host as a process — `ServiceProcess.cs`

```csharp
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;

namespace {{App}}.RestartTests;

/// <summary>
/// The built host, running as its own process on a real TCP port: the executable a user would start,
/// not an in-memory test server. Killing it is a crash; starting it again is a restart.
/// </summary>
internal sealed class ServiceProcess : IAsyncDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(90);

    // Logged by Microsoft.Hosting.Lifetime once Kestrel listens, which is after the migrations ran.
    private const string ReadyLine = "Application started.";

    private readonly Process _process;
    private readonly StringBuilder _output = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private ServiceProcess(Process process, Uri baseAddress)
    {
        _process = process;
        Client = new HttpClient { BaseAddress = baseAddress };
    }

    public HttpClient Client { get; }

    public int ProcessId => _process.Id;

    public string Output
    {
        get
        {
            lock (_output)
                return _output.ToString();
        }
    }

    public static async Task<ServiceProcess> StartAsync(string connectionString)
    {
        var hostAssembly = HostAssemblyPath();
        var baseAddress = new Uri($"http://127.0.0.1:{FreeTcpPort()}");

        var startInfo = new ProcessStartInfo
        {
            // Set by `dotnet test` to the dotnet that is running it.
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            WorkingDirectory = Path.GetDirectoryName(hostAssembly)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(hostAssembly);
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        startInfo.Environment["ASPNETCORE_URLS"] = baseAddress.ToString();
        startInfo.Environment["ConnectionStrings__App"] = connectionString;
        // A host that fails to start must exit, not stay up serving 503 (see the page).
        startInfo.Environment["Pragmatic__MaintenanceMode__EnableOnStartupFailure"] = "false";

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var service = new ServiceProcess(process, baseAddress);

        process.OutputDataReceived += (_, e) => service.Record(e.Data);
        process.ErrorDataReceived += (_, e) => service.Record(e.Data);
        process.Exited += (_, _) => service._ready.TrySetException(
            new InvalidOperationException($"The service exited before it was ready.{Environment.NewLine}{service.Output}"));

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await service._ready.Task.WaitAsync(StartupTimeout);
        }
        catch (TimeoutException)
        {
            await service.DisposeAsync();
            throw new TimeoutException(
                $"The service did not log '{ReadyLine}' within {StartupTimeout}.{Environment.NewLine}{service.Output}");
        }

        return service;
    }

    /// <summary>Ends the process abruptly, as a crash or a pulled plug would: no graceful shutdown.</summary>
    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }

        _process.Dispose();
    }

    private static int FreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private void Record(string? line)
    {
        if (line is null)
            return;

        lock (_output)
            _output.AppendLine(line);

        if (line.Contains(ReadyLine, StringComparison.Ordinal))
            _ready.TrySetResult();
    }

    private static string HostAssemblyPath()
    {
        var path = typeof(ServiceProcess).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "HostAssemblyPath")
            .Value!;

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The host has not been built where the test project expects it.", fullPath);

        return fullPath;
    }
}
```

- **Readiness is the host's own statement.** `Application started.` is logged by
  `Microsoft.Hosting.Lifetime` after Kestrel listens, and Pragmatic's migrations run before that. So
  there is no polling and no sleep, and a test never talks to a half-migrated schema.
- **The configuration goes in as environment variables**: `ConnectionStrings__App` is
  `ConnectionStrings:App`, the key the host's `[PragmaticDatabase(ConfigKey = …)]` reads.
  `ASPNETCORE_ENVIRONMENT=Production` runs the service the way a user would, without
  `appsettings.Development.json`.
- **A failed start fails the test with the host's log**, instead of hanging until the timeout. That
  is why `Pragmatic__MaintenanceMode__EnableOnStartupFailure=false` is set. Without it, a host whose
  startup throws does not exit: it enters maintenance mode and **also logs `Application started.`**.
  The wait succeeds and every request then answers `503`, so the test fails on a status code, far
  from the cause. With the key, a start against an unreachable database ends the wait after about 40
  seconds (the migration runner's three connection attempts), with
  `Migration failed - host startup aborted` in the message. (With the default, the maintenance host
  listens on the configured address and logs, at `Critical`, "Startup failed (…). The host is in
  maintenance mode and answers 503 on …".)

## 3. The service and its database — `ServiceUnderTest.cs`

```csharp
using Testcontainers.PostgreSql;
using Xunit;

namespace {{App}}.RestartTests;

/// <summary>
/// A PostgreSQL container and the service running against it. The data lives in the container, so
/// either can be restarted under the other.
/// </summary>
public sealed class ServiceUnderTest : IAsyncLifetime
{
    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private ServiceProcess? _service;

    public HttpClient Client => Service.Client;

    public int ProcessId => Service.ProcessId;

    /// <summary>What the running service has logged: the first thing to read when a test fails.</summary>
    public string Output => Service.Output;

    private ServiceProcess Service => _service ?? throw new InvalidOperationException("The service is not running.");

    public async Task InitializeAsync()
    {
        await _database.StartAsync();
        _service = await ServiceProcess.StartAsync(_database.GetConnectionString());
    }

    /// <summary>Kills the service and starts a new process over the same database.</summary>
    public async Task RestartServiceAsync()
    {
        await Service.DisposeAsync();
        _service = null;
        _service = await ServiceProcess.StartAsync(_database.GetConnectionString());
    }

    /// <summary>
    /// Stops the database container and starts it again, then the service with it. A restarted
    /// container comes back on a new host port, so the service is started with the new address.
    /// </summary>
    public async Task RestartDatabaseAsync()
    {
        await Service.DisposeAsync();
        _service = null;
        await _database.StopAsync();
        await _database.StartAsync();
        _service = await ServiceProcess.StartAsync(_database.GetConnectionString());
    }

    /// <summary>Runs SQL inside the container, around the service.</summary>
    public async Task<string> QueryDatabaseAsync(string sql)
    {
        var result = await _database.ExecScriptAsync(sql);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"psql failed ({result.ExitCode}): {result.Stderr}");

        return result.Stdout;
    }

    public async Task DisposeAsync()
    {
        if (_service is not null)
            await _service.DisposeAsync();

        await _database.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class ServiceCollection : ICollectionFixture<ServiceUnderTest>
{
    public const string Name = "service under test";
}
```

- **`StopAsync` keeps the container, `DisposeAsync` removes it.** A stopped and restarted container
  still has its data; that is the whole point of the second test.
- ⚠️ **A restarted container gets a new host port** (measured: 59612 before the stop, 59616 after).
  `GetConnectionString()` returns the new one, but a service started with the old one points at
  nothing, which is why `RestartDatabaseAsync` starts the service again. A fixed
  `.WithPortBinding(hostPort, 5432)` keeps the port; before using it to keep the service running
  across the restart, read the last section.
- **One collection, one service, tests in sequence.** xUnit runs the tests of a collection one at a
  time, so a restart never lands in the middle of another test's request.

## 4. The tests — `RestartTests.cs`

```csharp
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace {{App}}.RestartTests;

[Collection(ServiceCollection.Name)]
public sealed class RestartTests(ServiceUnderTest service)
{
    [Fact]
    public async Task A_book_written_before_the_service_is_killed_is_read_after_it_restarts()
    {
        var written = await WriteAsync();
        var before = service.ProcessId;

        await service.RestartServiceAsync();

        Assert.NotEqual(before, service.ProcessId);
        Assert.Equal(written, await ReadAsync(written.Id));
    }

    [Fact]
    public async Task A_book_written_before_the_database_restarts_is_read_after_it()
    {
        var written = await WriteAsync();

        await service.RestartDatabaseAsync();

        Assert.Equal(written, await ReadAsync(written.Id));
    }

    [Fact]
    public async Task A_written_book_is_a_row_in_the_database()
    {
        var written = await WriteAsync();

        var row = await service.QueryDatabaseAsync(
            $"""SELECT 'row:' || "Title" FROM "Books" WHERE "PersistenceId" = '{written.Id}';""");

        Assert.Contains($"row:{written.Title}", row);
    }

    private async Task<Book> WriteAsync()
    {
        var response = await service.Client.PostAsJsonAsync("api/books",
            new { title = "Se questo è un uomo", author = "Primo Levi", year = 1947 });
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"{response.StatusCode}{Environment.NewLine}{service.Output}");
        return (await response.Content.ReadFromJsonAsync<Book>())!;
    }

    private async Task<Book> ReadAsync(Guid id)
    {
        var response = await service.Client.GetAsync($"api/books/{id}");
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}{Environment.NewLine}{service.Output}");
        return (await response.Content.ReadFromJsonAsync<Book>())!;
    }

    private sealed record Book(Guid Id, string Title, string Author, int Year);
}
```

- **The whole record is compared**, not only the id: a restart that kept the row but lost a column,
  or mangled the encoding of "è", fails here.
- **`Assert.NotEqual(before, service.ProcessId)`** proves the restart happened. Without it, a
  `RestartServiceAsync` that quietly did nothing would pass the test.
- **The SQL names the schema as the generator writes it**: the table is the entity's plural
  (`"Books"`), the key column is `"PersistenceId"`, and both are quoted because PostgreSQL folds
  unquoted names to lower case.
- **A failed status carries the service's log** in the assertion message. It is the only place the
  cause is visible, because the exception happened in another process.

## 5. Check that the tests can fail

A restart test that passes proves nothing until you have seen it fail when the data is lost. Make both
restarts start the service against a **new, empty** container:

```csharp
// in RestartServiceAsync, before starting the service again — and the same in RestartDatabaseAsync
// in place of StopAsync/StartAsync (drop `readonly` from _database for the duration):
await _database.DisposeAsync();
_database = new PostgreSqlBuilder("postgres:17-alpine").Build();
await _database.StartAsync();
```

Both restart tests must go red with `NotFound`, and the row test must stay green. Then put the fixture
back.

## 6. What a running service does when the database restarts

The second test starts the service again after the database comes back. If you pin the port and leave
the service running instead, the first read **and** the first write after the restart succeed. The
pooled connection the database closed while it sat idle fails, and EF runs the command again on a
fresh one.

It is measured in the repository's Conformance suite (`TheDatabaseRestarts`: the port pinned, the
container restarted under a running host, `200` on the first read and `201` on the first write).

What the generated host does:

- **SQL Server, PostgreSQL and MySQL retry transient failures.** The generated call is
  `UseNpgsql(connectionString, o => o.EnableRetryOnFailure())` (likewise `UseSqlServer`, and Oracle's
  `UseMySQL`). SQLite has no retrying strategy.
- **`[Transactional]` runs its whole invocation as one retriable unit.** A transient failure before the
  commit runs the operation again, from a cleared change tracker. So an operation body must have **no
  effect outside its transaction**:
  - mail, messages and events go through the outbox, which is transactional;
  - a direct HTTP call, a file write, or a call into another boundary (which commits on its own) runs
    again.

  Without `[Transactional]`, EF retries each query and the closing save on its own.
- **An application can change it** through the boundary's configuration, which is applied after the
  generated call:

```csharp
services.AddBoundary<CatalogBoundary>(cfg => cfg.UseLocal().UseDatabase(options =>
    options.UseNpgsql(npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 10))));
```

A provider call there without a connection string keeps the one the generated registration set.
`npgsql.ExecutionStrategy(d => new NonRetryingExecutionStrategy(d))` turns the retry off.
