# Pragmatic.Endpoints: Benchmark Results

The measurement #51 asks for before a generated response writer is built: what writing one response body
costs today, against STJ's fastest path and against RE:Dox. Every number on this page comes from one run,
on 2026-10-05, of:

```bash
cd Pragmatic.Endpoints/benchmarks/Pragmatic.Endpoints.Benchmarks
dotnet run -c Release -- --filter "*"
```

| | |
|---|---|
| Machine | the one `Pragmatic.Logging/BENCHMARK-RESULTS.md` names (AMD Ryzen 9 9950X); BenchmarkDotNet reported it as "Unknown processor" |
| Runtime | .NET 10.0.12, SDK 10.0.303, X64 RyuJIT AVX-512 |
| Harness | BenchmarkDotNet 0.14.0, default job, `[MemoryDiagnoser]` |

The reports that run wrote are committed in [`benchmarks/reports/`](benchmarks/reports/), one per workload.
A number here that does not appear there is a mistake. The run reported one multimodal distribution,
on `Host_GeneratedMetadata` for `canada.json`.

## What is compared

Each competitor turns a graph already in memory into UTF-8 bytes, as an endpoint writing its result does.

| Competitor | What it is |
|---|---|
| `Host_Reflection` *(baseline)* | The options a Pragmatic host answers with (`PragmaticEntryTemplate.RenderJsonDefaults`: web defaults, camelCase, nulls left out, `JsonStringEnumConverter`, `IgnoreCycles`) over the `PragmaticJsonOptions` seam with nothing registered: reflection for the type. A host that has not opted into the generated context. |
| `Host_GeneratedMetadata` | The same options with source-generated metadata for the type in front of the seam: an opted-in host. The metadata is STJ's (metadata mode), standing in for the context Pragmatic generates, which these shapes are not response types for. |
| `Stj_FastPath` | STJ's own fastest path: a context in default mode with camelCase and nulls left out, and no converter or reference handling, so its serialization handler is used wherever it can be. |
| `REDox` | `CAPCOM.REDox` 1.0.0, `DoxSerializerSettings` with camelCase and nulls left out on write. |

⚠️ Before anything is timed, `GlobalSetup` parses all four outputs and refuses to run unless they are the
same document (`JsonNode.DeepEquals`), so key order, number formatting and escaping may differ and nothing
else may. `dotnet run -c Release -- verify` runs that check alone. It was checked once by removal: with
RE:Dox writing the nulls the host leaves out, it refused the Twitter workload and named RE:Dox.

## Workloads

| Workload | What |
|---|---|
| `twitter.json`, `citm_catalog.json`, `canada.json` | The public `simdjson-data` documents other JSON libraries publish numbers on, at commit `4197c425`, downloaded at setup into the temp directory and never committed (the repository states no licence). Their models are RE:Dox's, copied under Apache-2.0 with attribution in each file. |
| `ReservationPage` | Fifty items in the shape of Showcase's `ReservationSummaryDto`, without its enum (see below). |

## Results

Per serialization; lower is better. Ratio against `Host_Reflection`.

### `twitter.json`

| Competitor | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| Host_Reflection | 868.6 μs | 1.00 | 523.40 KB |
| Host_GeneratedMetadata | 887.5 μs | 1.02 | 523.36 KB |
| Stj_FastPath | 671.6 μs | 0.77 | 551.54 KB |
| REDox | 354.3 μs | 0.41 | 418.96 KB |

### `citm_catalog.json`

| Competitor | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| Host_Reflection | 1,093.9 μs | 1.00 | 471.52 KB |
| Host_GeneratedMetadata | 1,056.9 μs | 0.97 | 471.53 KB |
| Stj_FastPath | 454.6 μs | 0.42 | 471.01 KB |
| REDox | 538.8 μs | 0.49 | 470.15 KB |

### `canada.json`

| Competitor | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| Host_Reflection | 10.595 ms | 1.00 | 2 MB |
| Host_GeneratedMetadata | 12.564 ms | 1.19 | 2 MB |
| Stj_FastPath | 11.256 ms | 1.07 | 1.99 MB |
| REDox | 9.981 ms | 0.95 | 1.99 MB |

### `ReservationPage` (50 items)

| Competitor | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| Host_Reflection | 21.26 μs | 1.00 | 20.17 KB |
| Host_GeneratedMetadata | 20.80 μs | 0.98 | 20.17 KB |
| Stj_FastPath | 17.06 μs | 0.80 | 19.76 KB |
| REDox | 17.44 μs | 0.82 | 19.63 KB |

## What the measurement says

- **The host pays for not reaching STJ's serialization handler**: the fast path is 20% faster on the
  Showcase-shaped page, 23% on Twitter and 58% on the CITM catalog. Generated metadata alone changes
  nothing measurable: within 3% on three workloads and slower on the fourth, which is the multimodal one.
  That is the issue's premise, now measured: the options keep the response on the generic path, and
  metadata does not change that.
- **`canada.json` is number formatting.** Every competitor is within about 20% of the others, and the
  handler does not help: the cost is writing about 2 MB of doubles, not walking the graph.
- ⚠️ **Twitter favours RE:Dox for a reason that is not the serializer's structure.** The document is
  full of Japanese text. The host's options keep STJ's default encoder, which writes every non-ASCII
  character as `\uXXXX`; RE:Dox's default escaping writes it as UTF-8. The documents are structurally
  equal and the bytes are not: 419 KB against 523 KB. The 0.41 ratio is therefore partly escaping, and
  how much is not measured here.
- **Allocation follows the output size** in every workload: the serializers allocate the bytes they
  return and little else.

## What it decides, and what it does not

The issue's step 2 (a generated `Write(Utf8JsonWriter, T)` per response type) is aimed at exactly the
gap between `Host_*` and `Stj_FastPath`, and the measurement says that gap is real on the shapes
responses have: 20% to 58% outside number-heavy documents.

It does not yet say whether step 4 (a writer of our own instead of `Utf8JsonWriter`) is needed. The
issue's test for that is whether the generated writer beats RE:Dox. On the shapes that are not dominated
by escaping or numbers (the page, the catalog), RE:Dox and the fast path are within 20% of each other,
in both directions. On Twitter the escaping difference has to be removed from the comparison first.

## Not covered

- **Enums.** The host writes enums by name through `JsonStringEnumConverter`. Neither RE:Dox nor the fast
  path writes names under the same contract here, so the work would not be equal; the page carries no
  enum, and what the converter costs is not in these numbers.
- **The Pragmatic generator's own metadata.** `Host_GeneratedMetadata` uses STJ's metadata as the stand-in.
- **Escaping-equal Twitter.** RE:Dox with an escaping policy equal to STJ's default was not configured.
- **Writing to the response body.** Every competitor returns a `byte[]`; an endpoint writes to a
  `PipeWriter`, which the generated writer of step 3 will.
- **One run, one machine.** The run is not repeated, and not reproduced on Linux.
