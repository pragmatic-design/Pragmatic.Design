# Pragmatic.Endpoints: Benchmark Results

What writing one response body costs: the host as it answers through `System.Text.Json`, STJ's fastest path,
RE:Dox, and the generated UTF-8 writer of the response type (#51, #131). The numbers come from runs on
2026-10-08 (before #131) and 2026-10-09 (the subtraction probes and the final run of #131) of:

```bash
cd Pragmatic.Endpoints/benchmarks/Pragmatic.Endpoints.Benchmarks
dotnet run -c Release -- --filter "*"                       # everything
dotnet run -c Release -- --filter "*ProbeBenchmarks*"       # the subtraction probes of #131
```

| | |
|---|---|
| Machine | the one `Pragmatic.Logging/BENCHMARK-RESULTS.md` names (AMD Ryzen 9 9950X); BenchmarkDotNet reported it as "Unknown processor" |
| Runtime | .NET 10.0.12, SDK 10.0.303, X64 RyuJIT AVX-512 |
| Harness | BenchmarkDotNet 0.14.0, default job, `[MemoryDiagnoser]` |

The reports are committed in [`benchmarks/reports/`](benchmarks/reports/): `final-131_*` for the current
numbers, `probe-131-*` for the subtraction probes, and the per-workload and `ab-*` files of 2026-10-08 for
the numbers before #131. A number here that does not appear there is a mistake.

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

### Current: runs of what the encoder cannot change (#131)

Per serialization; lower is better; ± is the run's standard deviation. The three workloads with a writer, run
together on an idle machine, 2026-10-09 (`final-131_*`).

| Workload | Host_Reflection | Stj_FastPath | REDox | Generated_Writer | Allocated, writer vs fast path |
|---|---:|---:|---:|---:|---|
| `citm_catalog.json` | 1,002.6 μs | 445.9 μs ±8.9 | 541.8 μs | **230.9 μs** ±3.1 | 468.81 KB vs 471.04 KB |
| `canada.json` | 9.603 ms | 8.835 ms ±0.29 | 9.188 ms | **7.919 ms** ±0.24 | 1.99 MB vs 1.99 MB |
| `ReservationPage` | 15.822 μs | 12.965 μs ±0.16 | 13.776 μs | **9.436 μs** ±0.15 | 19.59 KB vs 19.76 KB |

The writer is ahead of the fast path by 48% on CITM, 10% on Canada and 27% on the page, each beyond both
sides' deviation, and allocates no more than it on any of them. Twitter is unchanged: the writer does not
apply.

### Measured by subtraction

Each probe writes one workload several ways in the same run, against the fast path: the writer's calls
written out by hand (`Probe_Plain`, the row the others are read against), the same with one part taken out
(`Minus_*`, a different document on purpose), and candidates that write the host's very bytes another way
(`Raw_*`, refused at setup if they do not).

**CITM** (`probe-131-subtraction_*`):

| Row | Mean | vs `Probe_Plain` |
|---|---:|---:|
| `Stj_FastPath` | 395.6 μs | 0.99 |
| `Probe_Plain` | 399.0 μs | 1.00 |
| `Minus_KeyFormatting` (integer keys as a constant name) | 389.6 μs | 0.98 |
| `Minus_StringValues` (strings as a constant already encoded) | 391.7 μs | 0.99 |
| `Raw_Numbers` (runs of number members, arrays of numbers) | 296.3 μs | 0.75 |
| `Raw_EncoderFree` (everything the encoder cannot change) | 223.0 μs | 0.56 |

Neither the keys nor the strings are where the time goes: the writer's calls are. Every `Utf8JsonWriter` call
checks its state, asks for buffer space and decides on a separator; on a document of small objects of
numbers that is most of the work, and a run pays it once.

**The page** (`probe-131-page_*`): dates are the largest part (`Minus_DateFormatting` 0.76), then strings
(`Minus_StringValues` 0.85); `Raw_EncoderFree` 0.84, which takes the dates, Guids and numbers of each item into
one run.

**Canada** (`probe-131-canada-1_*`, `probe-131-canada-2_*`): formatting the doubles is 91% of the document
(`Minus_NumberFormatting` 0.09). A run takes 2% and 5.5% off the writer's calls in the two runs
(`Probe_Run` against `Probe_Plain`). The fast path is the same formatter behind the same calls, and the
two probe runs put it at 7.68 and 8.95 ms around a writer at 8.24 and 8.28: ahead or behind is decided
there by the drift between processes, which the second run's `Generated_Writer_Rented` row (the writer into
a buffer rented and grown as `SerializeToUtf8Bytes` does, 8.15 ms) rules out as an effect of the harness's
buffer.

**The run's buffer** (`probe-131-scratch-runs_*`): the first generated runs started in a stack buffer cleared
per run and rented a pooled one past 256 bytes, and were 280 μs on CITM against 228 for the same runs written
by hand in the same benchmark run. A scratch buffer the thread keeps took them to 252, against 271.

### Before #131

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

- **#131: the writer is ahead of the fast path and RE:Dox on every workload it covers, beyond the
  deviation**, in the final run. Before it, CITM and the page were at the fast path's level, and which side
  was ahead changed between runs (six runs of CITM put the fast path anywhere between 418 and 498 μs).
- **The lead comes from fewer writer calls, not from cheaper values**: what the encoder cannot change is
  formatted into one run and handed to the writer once. On CITM that halves the time; on the page it is a
  quarter; on Canada, where the doubles' formatting is nine tenths of the work and the same on both sides,
  it is what is left to take.
- **Canada is decided by the drift on this machine.** Across the runs of 2026-10-09 the fast path went from
  7.68 to 8.95 ms with the same code; of the three committed runs that have both, the writer is ahead in two
  (`probe-131-canada-2_*`, `final-131_*`) and behind in one (`probe-131-canada-1_*`). The
  benchmarks workflow's A/B on the GitHub runner, where the drift is about 1%, is the measurement that says
  whether it is ahead in every run.
- **Before #131, CITM was 9% behind until one change**, measured three times: the writer wrote a member's name and its value
  in two calls (`WritePropertyName`, then `WriteNumberValue`) where the handler writes them in one
  (`WriteNumber(name, value)`), read from the handler the STJ source generator emits for the benchmark's own
  context. The template now writes a leaf in one call; the encoder was measured as the other suspect and ruled
  out (`Generated_Writer_DefaultEncoder`, within the noise of `Generated_Writer`).
- **Allocation is the output, and the writer allocates no more** than the fast path in any workload.
- **Against the host as it answers today**, in the final run: 77% faster on CITM, 18% on Canada, 40% on the
  page.

## What it decides, and what it does not

**Step 4 of #51, a writer of our own instead of `Utf8JsonWriter`, is not started.** Its condition was that the
generated writer on `Utf8JsonWriter` does not beat RE:Dox: it beats it on every workload it covers,
in both full runs of the day (CITM 465 and 560 μs against 554 and 579, Canada 8.36 and 8.59 against 8.92 and
9.03 ms, the page 13.5 and 14.1 against 14.9 and 16.3 μs, each pair from the same run).
`Utf8JsonWriter` is not the ceiling here; the way it was called was. #131 kept it: a run writes what the
encoder cannot change and hands it to the writer, which still writes every string and every name it has to
escape.

**A faster double formatter is the one lever left on Canada.** It is not in #131: it would have to write the
digits `double.TryFormat` writes, for every value, and that is a piece of work of its own.

RE:Dox stays ahead on Twitter, where the writer does not apply.

## Not covered

- **Writing to the response body.** Every competitor returns a `byte[]`; a response writes into the body's
  `PipeWriter`. The container suites prove that path answers right, not what it costs.
- **Enums.** No workload carries one.
- **Repetition across days, and Linux.** One day, one machine.
