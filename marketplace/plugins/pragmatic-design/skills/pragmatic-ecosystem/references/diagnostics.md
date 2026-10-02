# Diagnostic Ranges

Prefer live code when in doubt. Search existing `*Diagnostics.cs` and tests before allocating a new ID.

| Range | Owner |
|---|---|
| `PRAG0001-0099` | Result (`Pragmatic.Result.Analyzers`) |
| `PRAG0100-0199` | Ensure — reserved, nothing emitted today |
| `PRAG0200-0299` | Validation |
| `PRAG0300-0399` | Mapping |
| `PRAG0400-0499` | Actions (0450-0462: loads, package imports) |
| `PRAG0500-0599` | Endpoints |
| `PRAG0600-0699` | Persistence and EF Core (0680-0688 in `Pragmatic.Persistence.Analyzers`) |
| `PRAG0700-0799` | Query pipeline |
| `PRAG0800-0899` | Messaging |
| `PRAG0900-0999` | Temporal (0900-0904 in `Pragmatic.Temporal.Analyzers`, 0905 in the SG) |
| `PRAG1000-1099` | Identity and Authorization |
| `PRAG1100-1199` | Persistence — data ownership |
| `PRAG1400-1499` | Dependency injection (`Pragmatic.Abstractions.Analyzers`) |
| `PRAG1600-1699` | Composition, including DI service registration and host aggregation |
| `PRAG1700-1799` | Caching |
| `PRAG1800-1899` | Internationalization |
| `PRAG1900-1999` | Documents (`Pragmatic.Documents.Csv.Generator`) |
| `PRAG2000-2099` | Configuration |
| `PRAG2200-2249` | Patch |
| `PRAG2300-2349` | Client (`Pragmatic.Client.SourceGenerator`) |
| `PRAG2350-2399` | Testing (`Pragmatic.Testing.SourceGenerator` — mocks, comparers) |
| `PRAG2500-2549` | Jobs |
| `PRAG2600-2699` | Traits and Resource (shared) |
| `PRAG2700-2749` | Value objects |
| `PRAG2750-2799` | Entity lifecycle events |
| `PRAG2800-2899` | Serialization / AOT (`Pragmatic.Abstractions.Analyzers`) |
| `PRAG2900-2999` | Privacy (2900-2913; 2905 deliberately unused) |
| `PRAG9000-9099` | Source generator infrastructure |
| `PRAGS001-PRAGS999` | Diagnostic suppressors |

Descriptors live outside the unified generator too: `Pragmatic.SourceGenerator.Analyzers` is the sole
source of `PRAG0600`, `PRAG0602`, `PRAG1100`, `PRAG0822` and `PRAG0210`, and it mirrors the "must be
partial" family so the IDE can offer the code fix.

Every `*.Analyzers` assembly travels **inside** the package it companions, not as a package of its
own: referencing `Pragmatic.Result` is enough for the Result analyzers to run.

## Allocation Procedure

1. Open the owning diagnostics file.
2. Grep the **whole repo** for the candidate ID — nothing detects a collision automatically, and it has already happened.
3. Pick the next unused ID within the owner range.
4. Add a descriptor with actionable title and message.
5. Report it somewhere. A descriptor nothing emits is a promise the build does not keep.
6. Add or update tests that assert the diagnostic is emitted and not emitted.
7. Update docs only after verifying the code.
