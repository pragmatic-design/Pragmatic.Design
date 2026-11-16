---
title: "Testing a worker host"
description: "A host with no HTTP (jobs, message handlers, `PragmaticApp.RunWorkerAsync`) has no `WebApplicationFactory` to build it. The"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Testing/docs/worker-host.md
sidebar:
  order: 4
---
A host with no HTTP (jobs, message handlers, `PragmaticApp.RunWorkerAsync`) has no `WebApplicationFactory` to build it. The
`Pragmatic.Testing.Hosting` package gives it one: `PragmaticWorkerFactory<TEntryPoint>` builds the host from its entry point, as the
application builds it, applies the test's overrides, exposes its services, and starts and stops it.

It is a package of its own because it depends on `Microsoft.Extensions.Hosting`; `Pragmatic.Testing` keeps its single dependency.

## Setup

```xml
<ProjectReference Include="..\..\src\MyWorker\MyWorker.csproj" />
<PackageReference Include="Pragmatic.Testing.Hosting" Version="$(PragmaticVersion)" />
```

`TEntryPoint` is any type of the host's assembly: the factory runs that assembly's entry point. The host's `[Module]` class is public,
so it saves the `public partial class Program` a top-level `Program` would need.

## Running a job

```csharp
var sender = new RecordingDigestSender();
await using var worker = new PragmaticWorkerFactory<WorkerHostModule>()
    .ConfigureServices(services => services.Replace(ServiceDescriptor.Singleton<IDigestSender>(sender)))
    .UseEnvironment("Testing");
await worker.StartAsync();

Guid scheduled;
await using (var scope = worker.Services.CreateAsyncScope())
    scheduled = await scope.ServiceProvider.GetRequiredService<IJobScheduler>().ScheduleAsync<SendDigestJob>();

Assert.Equal(scheduled, await sender.Sent.WaitAsync(TimeSpan.FromSeconds(30)));
await worker.StopAsync();
```

The job runs on the host's own processor, started by `StartAsync`. The test waits on the job's effect (a sender that completes a
`TaskCompletionSource`), not on a poll of the job store. The whole example is `examples/worker`.

## What it does

| Member | |
|---|---|
| `ConfigureServices(Action<IServiceCollection>)` | Applied after the application's registrations, so a replacement wins. Refused once the host is built. |
| `UseSetting(key, value)` | A configuration value, passed to the entry point as the command-line argument `--key=value`. |
| `UseEnvironment(name)` | The host's environment. |
| `Services` / `Host` | The host's services, and the host itself. The host is built on first use and is not started. |
| `StartAsync` / `StopAsync` | Start and stop the hosted services: the job processor, the message consumers. |
| `BuildTimeout` | How long the entry point may take to build its host; one minute by default. |
| `DisposeAsync` | Stops the host if it is running, and disposes it. |

## How it builds the host

It uses the mechanism `WebApplicationFactory` uses:
- The entry point runs on a thread of its own.
- The factory listens to the `Microsoft.Extensions.Hosting` diagnostic source.
- On `HostBuilding` it adds the overrides.
- On `HostBuilt` it takes the host and stops the entry point with `HostAbortedException`, before the entry point runs the host itself.

Events from another factory's entry point, running in a parallel test, are ignored.

⚠️ Invoking the entry point is reflection (`Assembly.EntryPoint`). This is a test tool running the application it was pointed at, and
none of it ends up in the application.

## When the entry point fails

| Case | What the factory throws |
|---|---|
| The entry point throws before it builds its host (missing configuration, a failed validation) | `InvalidOperationException`, with the entry point's exception inside |
| The entry point builds no host within `BuildTimeout` | `TimeoutException` |
| The assembly has no entry point (`TEntryPoint` is from a library) | `InvalidOperationException` |
