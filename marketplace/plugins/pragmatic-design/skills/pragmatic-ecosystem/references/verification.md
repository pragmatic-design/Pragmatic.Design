# Verification Reference

## Consumer Build And Test

For an external app:

```powershell
dotnet restore
dotnet build
dotnet test
```

When generated code appears missing:

```powershell
dotnet clean
dotnet nuget locals all --clear
dotnet restore
dotnet build
```

## Monorepo Build And Test

The monorepo is verified through one script, not through `dotnet build`/`dotnet test` by hand:

| Scope | Command |
|---|---|
| A change (clean build `--warnaserror`, every ratchet, every hermetic suite) | `node scripts/check.mjs --tier full` |
| A test that lives in a container suite (Showcase, Conformance, Persistence.EFCore, …) | `node scripts/check.mjs --tier docker --only <Suite>` |
| Before a publish or a batch of commits | `node scripts/check.mjs --tier all` |
| Consumer sample | `dotnet run --project examples/consumer-samples/Pragmatic.{Sample}.Consumer` |

⚠️ A bare `dotnet build` can report 0 errors on sources that fail from clean — MSBuild skips projects
it considers up to date — and `dotnet test` on the whole solution starts the container suites in
parallel, saturates Docker and produces failures that look like regressions. The script does both
the right way and exits non-zero with `FAIL`.

## Consumer Validation

Use `examples/consumer-samples/` when validating package consumption, analyzer packaging, transitive dependencies, or source-generator behavior through `PackageReference`.

The consumer sample flow is:

1. Start local NuGet feed using `docs/howto/local-nuget-server.md`.
2. Publish with `node scripts/publish-local.mjs` (clean Release build, pack, push, HTTP cache cleared).
3. `dotnet restore <consumer>.slnx --force-evaluate`.
4. Run the relevant consumer sample.

## Repository Commands

The active environment is Windows/PowerShell. Prefer:

- `rg` for search.
- `Get-ChildItem` for listing.
- `dotnet test ... -v minimal` for verification.
- `dotnet build ...` before broader tests.

Avoid hard-coded bash-only commands in skills.
