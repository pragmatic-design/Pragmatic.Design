# Pragmatic.Endpoints: Benchmark Results

What writing one response body costs: the host as it answers through `System.Text.Json`, STJ's fastest path,
RE:Dox, and the generated UTF-8 writer of the response type (#51). Every number on this page comes from runs
on 2026-10-08 of:

```bash
cd Pragmatic.Endpoints/benchmarks/Pragmatic.Endpoints.Benchmarks
dotnet run -c Release -- --filter "*"
```

| | |
|---|---|
| Machine | the one `Pragmatic.Logging/BENCHMARK-RESULTS.md` names (AMD Ryzen 9 9950X); BenchmarkDotNet reported it as "Unknown processor" |
| Runtime | .NET 10.0.12, SDK 10.0.303, X64 RyuJIT AVX-512 |
| Harness | BenchmarkDotNet 0.14.0, default job, `[MemoryDiagnoser]` |

The reports are committed in [`benchmarks/reports/`](benchmarks/reports/): one per workload from the full run,
and the `ab-*` files from the targeted run described below. A number here that does not appear there is a
mistake.

## What is compared

Each competitor turns a graph already in memory into UTF-8 bytes, as an endpoint writing its result does.

| Competitor | What it is |
|---|---|
| `Host_Reflection` *(baseline)* | The options a Pragmatic host answers with: ASP.NET's `JsonOptions` as they start, and what `PragmaticEntryTemplate.RenderJsonDefaults` sets on them (camelCase, nulls left out, `JsonStringEnumConverter`, `IgnoreCycles`), over the `PragmaticJsonOptions` seam with nothing registered: reflection for the type. |
| `Host_GeneratedMetadata` | The same options with source-generated metadata for the type in front of the seam. |
| `Stj_FastPath` | STJ's own fastest path: a context in default mode with camelCase and nulls left out, and no converter or reference handling, so its serialization handler is used wherever it can be. |
| `REDox` | `CAPCOM.REDox` 1.0.0, `DoxSerializerSettings` with camelCase and nulls left out on write. |
| `Generated_Writer` | The writer the Pragmatic generator emits for the type, through an endpoint that answers with it (`Endpoints/`), into a buffer and a `Utf8JsonWriter` kept across calls, as `GeneratedJsonResponse` writes a response; copied out at the end, as `SerializeToUtf8Bytes` copies its pooled buffer. |
| `Generated_Writer_DefaultEncoder` | The same writer with STJ's default encoder, the one `Stj_FastPath` writes with: what the writer costs apart from what the host's encoder costs. |

⚠️ **The host's encoder is ASP.NET's, `UnsafeRelaxedJsonEscaping`**, which leaves non-ASCII text and `<`, `>`,
`&` unescaped. The run of 2026-10-05 built the host from `JsonSerializerDefaults.Web` instead, which escapes
them, so its Twitter numbers compared a host that does not exist: 523 KB of output where a real host writes 422
KB. The competitors are corrected; the 2026-10-05 numbers are not reused.

Before anything is timed, `GlobalSetup` refuses to run unless every competitor writes the same document
(parsed, `JsonNode.DeepEquals`), **and the generated writer writes the host's very bytes**.
`dotnet run -c Release -- verify` runs that check alone.

## Workloads

| Workload | What | Generated writer |
|---|---|---|
| `twitter.json` | public `simdjson-data` document, downloaded at setup, never committed | **none**: it declares members typed `object`, which the serializer writes as whatever they hold at run time. PRAG0555 says so on `TwitterEndpoint`. |
| `citm_catalog.json` | same | yes (dictionaries keyed by integer) |
| `canada.json` | same | yes (nested arrays of doubles) |
| `ReservationPage` | fifty items in the shape of Showcase's `ReservationSummaryDto` | yes |

## Results

### Full run

Per serialization; lower is better. Ratio against `Host_Reflection`. Run after a day of container suites on
the same machine, which is what its error columns show.

| Workload | Host_Reflection | Stj_FastPath | REDox | Generated_Writer | Allocated, writer vs fast path |
|---|---:|---:|---:|---:|---|
| `twitter.json` | 559.6 μs | 598.8 μs | 330.5 μs | NA | — |
| `citm_catalog.json` | 997.9 μs | 439.2 μs ±12.3 | 553.8 μs | 465.4 μs ±20.8 | 468.85 KB vs 471.05 KB |
| `canada.json` | 10.211 ms | 8.868 ms | 8.923 ms | **8.355 ms** | 1.99 MB vs 1.99 MB |
| `ReservationPage` | 14.86 μs | 13.12 μs ±0.56 | 16.26 μs | 14.05 μs ±0.85 | 19.59 KB vs 19.76 KB |

### Targeted run: writer against the fast path

The two workloads where the full run put the writer behind the fast path, run again with only those two
competitors, at once, the machine otherwise idle: the same number of runs for each side.

| Workload | Stj_FastPath | Generated_Writer | Allocated |
|---|---:|---:|---|
| `citm_catalog.json` | 497.8 μs ±14.1 | **441.6 μs** ±15.4 | 468.85 KB vs 471.03 KB |
| `ReservationPage` | 12.65 μs ±0.25 | **10.48 μs** ±0.21 | 19.59 KB vs 19.76 KB |

## What the measurement says

- **Canada: the writer is ahead of every competitor**, in every run of the day (8.36 to 8.59 ms against
  8.78 to 8.87 for the fast path).
- **CITM and the page: the writer is at the fast path's level, and which side is ahead changes between runs.**
  Six runs of CITM put the fast path anywhere between 418 and 498 μs, the same code on the same machine. The
  targeted run, the one that measured both sides alike, puts the writer ahead on both. What is stable across
  runs is that neither is ahead by more than the drift between runs.
- **CITM was 9% behind before one change**, measured three times: the writer wrote a member's name and its value
  in two calls (`WritePropertyName`, then `WriteNumberValue`) where the handler writes them in one
  (`WriteNumber(name, value)`), read from the handler the STJ source generator emits for the benchmark's own
  context. The template now writes a leaf in one call; the encoder was measured as the other suspect and ruled
  out (`Generated_Writer_DefaultEncoder`, within the noise of `Generated_Writer`).
- **Allocation is the output, and the writer allocates no more** than the fast path in any workload.
- **Against the host as it answers today**, in the full run: 53% faster on CITM, 18% on Canada, 5% on the
  page, where that run's errors are wider than the difference.

## What it decides, and what it does not

**Step 4 of #51, a writer of our own instead of `Utf8JsonWriter`, is not started.** Its condition was that the
generated writer on `Utf8JsonWriter` does not beat RE:Dox: it beats it on every workload it covers,
in both full runs of the day (CITM 465 and 560 μs against 554 and 579, Canada 8.36 and 8.59 against 8.92 and
9.03 ms, the page 13.5 and 14.1 against 14.9 and 16.3 μs, each pair from the same run).
`Utf8JsonWriter` is not the ceiling here; the way it was called was.

RE:Dox stays ahead on Twitter, where the writer does not apply.

## Not covered

- **Writing to the response body.** Every competitor returns a `byte[]`; a response writes into the body's
  `PipeWriter`. The container suites prove that path answers right, not what it costs.
- **Enums.** No workload carries one.
- **Repetition across days, and Linux.** One day, one machine.
