# Pragmatic.Logging: Benchmark Results

Every number on this page comes from one run, on 2026-10-05, of:

```bash
cd Pragmatic.Logging/benchmarks/Pragmatic.Logging.Benchmarks
dotnet run -c Release -- all
```

| | |
|---|---|
| Machine | AMD Ryzen 9 9950X (16 cores), 126 GiB RAM, Windows 11 (10.0.26200) |
| Runtime | .NET 10.0.12, SDK 10.0.303, X64 RyuJIT AVX-512 |
| Harness | BenchmarkDotNet 0.14.0, default job, `[MemoryDiagnoser]` |

The reports that run wrote are committed in [`benchmarks/reports/`](benchmarks/reports/), one per suite. A
number here that does not appear there is a mistake. The run reported no `MinIterationTime` or baseline
warning, and one multimodal distribution (`ExpressionDslBenchmarks.HighVolume_MixedFilters`).

## Library comparison: `LoggingBenchmarks`

Pragmatic, Serilog, NLog and ZLogger, each called through `Microsoft.Extensions.Logging` and each writing
into a sink that **consumes** the event. The sink renders the message into a reused buffer and reads
every structured property, scope property and the exception (`Comparison/EventConsumer.cs`).

Before anything is timed, `GlobalSetup` runs every scenario once per library and stops the run unless the
four sinks produced the same message, the same property set and the same exception. Taking the work out
of any one sink makes it fail. That was checked once per sink when the check was written.

Pragmatic is the baseline of every category. Per call; lower is better.

### Simple: `logger.LogInformation(template, int, string)`

| Library | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| ZLogger | 164.7 ns | 1.30× faster | 216 B |
| **Pragmatic** | **214.5 ns** | baseline | **592 B** |
| NLog | 220.5 ns | 1.03× slower | 760 B |
| Serilog | 259.5 ns | 1.21× slower | 528 B |

### Source-generated call site (`[LoggerMessage]`)

| Library | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| ZLogger | 149.1 ns | 1.35× faster | 192 B |
| **Pragmatic** | **201.6 ns** | baseline | **544 B** |
| Serilog | 302.2 ns | 1.50× slower | 800 B |
| NLog | 382.8 ns | 1.90× slower | 1,416 B |

### Structured: a scope with four properties, a call with two

| Library | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| ZLogger | 432.6 ns | 1.20× faster | 520 B |
| **Pragmatic** | **517.5 ns** | baseline | **1,176 B** |
| NLog | 671.5 ns | 1.30× slower | 1,328 B |
| Serilog | 813.2 ns | 1.57× slower | 1,912 B |

### Exception

| Library | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| ZLogger | 175.7 ns | 1.33× faster | 232 B |
| **Pragmatic** | **233.2 ns** | baseline | **608 B** |
| NLog | 240.2 ns | 1.03× slower | 776 B |
| Serilog | 268.5 ns | 1.15× slower | 528 B |

### High volume: the simple call 1,000 times, reported per call

| Library | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| ZLogger | 168.4 ns | 1.24× faster | 215 B |
| **Pragmatic** | **209.1 ns** | baseline | **591 B** |
| NLog | 230.8 ns | 1.10× slower | 759 B |
| Serilog | 256.1 ns | 1.22× slower | 518 B |

### Production: two request-context properties and the structured scope

Each library adds the two context properties through its own mechanism: Pragmatic's `LogContextScope`,
Serilog's `LogContext`, NLog's `ScopeContext`. ZLogger has no ambient context of its own, so for it the
two properties are a second MEL scope. Pragmatic runs its production preset, with context enrichment on.

| Library | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| ZLogger | 519.8 ns | 1.44× faster | 832 B |
| **Pragmatic** | **747.5 ns** | baseline | **2,368 B** |
| NLog | 968.0 ns | 1.30× slower | 2,872 B |
| Serilog | 1,007.9 ns | 1.35× slower | 3,000 B |

### What the comparison says

- **ZLogger is faster than Pragmatic in every category and allocates 2.26× to 2.85× less.** Its UTF-8
  pipeline is the reference for what a logger on `Microsoft.Extensions.Logging` reaches.
- **Pragmatic is ahead of Serilog and NLog in every category**, by 1.03× to 1.90×.
- **In the production scenario Pragmatic is 1.30× faster than NLog and 1.35× faster than Serilog, and
  allocates less than both.** In the previous run (2026-10-04, in this file's history) it was the slowest
  of the four there, at 1,376.2 ns and 4,112 B per call; #53 changed that. Context filtering is now
  decided once per property name. `LogContext` no longer builds a `ConcurrentDictionary` per context.
  The filtered context properties are kept until the context providers change.
- **No library is allocation-free here.** Rendering and reading the event costs something everywhere.
  Pragmatic allocates less than NLog in every category. Against Serilog it allocates more on the simple,
  exception and high-volume calls (591–608 B against 518–528 B), and less on the source-generated,
  structured and production ones.
- ZLogger's row is, if anything, pessimistic: the sink reads its property values with
  `GetParameterValue(int)`, which boxes value types. Its typed and JSON accessors would avoid that, but
  the sink does not know the types any more than the other three do.

## A JSON line: `JsonSinkBenchmarks`

⚠️ **Not the machine of the rest of this page.** These numbers come from the benchmarks workflow run on
the change that added the category, on the GitHub runner: AMD EPYC 7763 (4 logical cores), Ubuntu 24.04,
.NET 10.0.12, BenchmarkDotNet 0.14.0, default job. The report is
[`benchmarks/reports/…JsonSinkBenchmarks-report-github.md`](benchmarks/reports/Pragmatic.Logging.Benchmarks.Json.JsonSinkBenchmarks-report-github.md).
Compare rows within this table, not with the tables above.

The same call (`Order {OrderId} placed by {Customer} for {Amount}`: an int, a string, a decimal) written
as a JSON line by each library's own JSON writer, on the logging thread, into a buffer that keeps only the
line being written. Pragmatic logs through its generated call site; Microsoft's `[LoggerMessage]` goes
through the Pragmatic JSON provider, Serilog's `JsonFormatter` and NLog's `JsonLayout`; ZLogger through its
own `[ZLoggerMessage]` and JSON formatter. `GlobalSetup` stops the run unless every line carries the
rendered message and the three values. Pragmatic's context enrichment is off, as nothing like it is
configured for the others.

| Library | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| ZLogger, `[ZLoggerMessage]` | 447.7 ns | 1.63× faster | 0 B |
| **Pragmatic, generated call site** | **730.2 ns** | baseline | **0 B** |
| Serilog, `[LoggerMessage]` | 1,562.4 ns | 2.14× slower | 1,448 B |
| NLog, `[LoggerMessage]` | 1,674.0 ns | 2.29× slower | 1,704 B |
| Pragmatic, `[LoggerMessage]` | 1,958.3 ns | 2.68× slower | 6,992 B |

With an argument declared `[PersonalData]`, masked by the call site: **604.0 ns, 0 B**. It is faster than
the call without one because the masked value is never formatted.

- **The generated call site allocates nothing** through the JSON provider, masked argument or not; so
  does ZLogger. The other three allocate per line.
- **ZLogger is 1.63× faster.** It writes from a UTF-8 state of its own too. Where the difference goes
  was not measured; the lines are not the same shape (Pragmatic's carries the event id and the message
  template, for one), and this table does not say how much of the gap that accounts for.
- **Microsoft's `[LoggerMessage]` through the Pragmatic JSON provider is the slowest row and the largest
  allocation:** that is the classic path, which builds an entry, a message string and a dictionary. The
  call-site path is 2.68× faster for the same line.

## Declared redaction overhead: `RedactionOverheadBenchmarks`

Pragmatic only, because no other library in the comparison masks the members a type declares. The same
call (`Registered {Customer}`, a record whose `Email` is declared) goes through the same consuming
provider with the production preset, without a redactor and with one.

| Method | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| Without redaction | 297.5 ns | baseline | 2 KB |
| With declared redaction | 962.9 ns | 3.24× slower | 2.26 KB |

The value is masked once, before the message is rendered, so neither the message nor the structured
property carries the declared member (#77). Masking serializes the value, parses it, masks the paths and
serializes again, and that is most of the 665 ns it adds.

**Budget: declared redaction adds at most 700 ns to a call that carries a declared value**, on this
machine. It is the measured cost with a small margin, stated so that a regression shows up as a number
over a line rather than as a ratio nobody remembers. The generated writer proposed in #54 is the change
expected to bring it down. A call that carries no declared value does not pay it: the redactor looks up
each value's type, finds nothing declared, and returns the value untouched.

## Formatting internals: `ZeroAllocationBenchmark`

| Method | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| `ZeroAllocMessageFormatter.TryFormat` | 62.86 ns | 1.01× faster | 0 B |
| String interpolation *(baseline)* | 63.76 ns | baseline | 240 B |
| `ZeroAllocMessageFormatter.Format` | 75.97 ns | 1.19× slower | 176 B |
| `string.Format` | 81.87 ns | 1.29× slower | 296 B |
| `MessageFormatter.Format` (reflection-based) | 109.04 ns | 1.71× slower | 528 B |
| Structured logging through `ILogger` | 170.68 ns | 2.68× slower | 904 B |

- `ZeroAllocMessageFormatter.TryFormat`, into a caller-provided span, is the only path with 0 B. It runs
  at the speed of plain interpolation.
- The committed report also has two `LogMessage` rows: that type was removed with the generated log call
  sites, which are the typed message it was meant to become.

## Bulk formatting, 1,000 messages: `AllocationComparisonBenchmark`

| Method | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| `ZeroAllocFormatting` | 62.69 µs | 1.36× faster | 257.81 KB |
| Standard formatting *(baseline)* | 84.96 µs | baseline | 390.63 KB |
| `ZeroAllocFormattingWithStackBuffer` | 99.05 µs | 1.17× slower | 117.19 KB |

The stack-buffer path allocates the least, 3.33× less than the baseline, and is the slowest of the three.

## Filter DSL: `ExpressionDslBenchmarks`

### Single evaluation

| Method | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| Simple level *(baseline)* | 69.91 ns | baseline | 400 B |
| Complex business logic | 93.57 ns | 1.34× slower | 400 B |
| Security auditing | 106.02 ns | 1.52× slower | 400 B |
| Enterprise compliance | 113.48 ns | 1.63× slower | 400 B |
| Performance monitoring | 147.10 ns | 2.11× slower | 400 B |

### Batch: 5 log entries

| Method | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| Simple level *(baseline)* | 368.35 ns | baseline | 2,000 B |
| Complex business logic | 495.88 ns | 1.35× slower | 2,000 B |
| Enterprise compliance | 593.86 ns | 1.61× slower | 2,000 B |
| All five filters (5 × 5) | 2,820.52 ns | 7.66× slower | 10,000 B |

### Property-based filtering

| Method | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| Property exists *(baseline)* | 85.44 ns | baseline | 480 B |
| Property value | 84.66 ns | 1.01× faster | 480 B |
| Has structured properties | 84.56 ns | 1.01× faster | 504 B |
| Numeric properties | 100.91 ns | 1.18× slower | 496 B |

### High volume: 10,000 evaluations per operation

| Method | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| Simple filter *(baseline)* | 704.77 µs | baseline | 4,000,000 B |
| Complex filter | 930.27 µs | 1.32× slower | 4,000,000 B |
| Mixed filters | 1,003.77 µs | 1.42× slower | 4,000,000 B |

### Cache

| Method | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| One expression, 1,001 evaluations *(baseline)* | 70.83 µs | baseline | 400,400 B |
| Five expressions, 5,005 evaluations | 568.09 µs | 8.03× slower | 2,002,000 B |

**Every evaluation allocates 400 B**, whatever the filter: 2,000 B for a batch of five, 4,000,000 B for
10,000. An earlier version of this page said "0 B per evaluation"; that was not what any recorded run
measured.

## Running the benchmarks

```bash
cd Pragmatic.Logging/benchmarks/Pragmatic.Logging.Benchmarks

dotnet run -c Release -- all         # every suite, as above
dotnet run -c Release -- logging     # the library comparison
dotnet run -c Release -- verify      # the comparison's equivalence check alone, nothing timed
dotnet run -c Release -- redaction   # declared redaction overhead
dotnet run -c Release -- expression  # filter DSL
dotnet run -c Release -- zero        # formatting internals and bulk formatting
```

Reports land in `BenchmarkDotNet.Artifacts/results/`. To update this page, run `all`, copy the
`*-report-github.md` files into `benchmarks/reports/`, and take every number from them.
