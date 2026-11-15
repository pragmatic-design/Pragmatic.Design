# Worker

A worker host: the generic host `PragmaticApp.RunWorkerAsync` builds, with no HTTP and no database. It runs one job, `SendDigestJob`,
which sends a digest through `IDigestSender`. The worker's own implementation writes the digest to the log.

| | |
|---|---|
| `src/Worker.Host` | The host: `Program.cs` calls `RunWorkerAsync`; `WorkerHostModule` is its `[Module]`, `[AnonymousHost]` because a worker serves no request. Under `Infrastructure/`, the job and the sender. |
| `tests/Worker.Tests` | The host built from its entry point by `PragmaticWorkerFactory` (`Pragmatic.Testing.Hosting`). One test replaces the sender with one that records, starts the host, schedules the job, and waits for the job to run on the host's processor. |

```
dotnet run --project examples/worker/src/Worker.Host
dotnet test examples/worker/tests/Worker.Tests/Worker.Tests.csproj
```

The tests need no container, so they run in `node scripts/check.mjs --tier full`.

The job is the host's own. A module included with `[Include<TModule>]` would need a database (`PRAG1651`), and this worker has none.
