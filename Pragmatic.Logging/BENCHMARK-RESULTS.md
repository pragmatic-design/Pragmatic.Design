# Pragmatic.Logging: Benchmark Results

Every number on this page, except the JSON line's section, which says where its own come from, comes from
one run, on 2026-10-05, of:

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

⚠️ **Not the run of the rest of this page.** These numbers come from a run on 2026-10-09 on the machine
above, after the changes of #109 and #129. The report is
[`benchmarks/reports/local-129_JsonSinkBenchmarks-report-github.md`](benchmarks/reports/local-129_JsonSinkBenchmarks-report-github.md).
The run before #109 on the GitHub runner put ZLogger 1.63× ahead (447.7 ns against 730.2,
[report](benchmarks/reports/Pragmatic.Logging.Benchmarks.Json.JsonSinkBenchmarks-report-github.md)); the
benchmarks workflow measures head against base on that runner for every change. Compare rows within this
table, not with the tables above.

The same call (`Order {OrderId} placed by {Customer} for {Amount}`: an int, a string, a decimal) written
as a JSON line by each library's own JSON writer, on the logging thread. Pragmatic logs through its
generated call site; Microsoft's `[LoggerMessage]` goes through the Pragmatic JSON provider, Serilog's
`JsonFormatter` and NLog's `JsonLayout`; ZLogger through its own `[ZLoggerMessage]` and JSON formatter, in
three shapes:

- **into a buffer**, as the other sinks here: no lock, no stream, the formatter's default fields;
- **to a stream**, with the sink Pragmatic's provider has: under a lock, a line break after the line, a flush
  per line;
- **the same fields, to a stream**: also the event id and name, a UTC timestamp, the arguments under
  `@properties`, under Pragmatic's names. Everything Pragmatic's line has but the message template, which
  ZLogger's formatter has no field for. This is the even comparison.

`GlobalSetup` stops the run unless every line carries the rendered message and the three values, and the
same-fields line every field it claims. Pragmatic's context enrichment is off, as nothing like it is
configured for the others.

| Library | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| **Pragmatic, generated call site** | **166.9 ns** | baseline | **0 B** |
| ZLogger, `[ZLoggerMessage]`, into a buffer | 281.6 ns | 1.71× slower | 0 B |
| ZLogger, to a stream | 304.0 ns | 1.84× slower | 0 B |
| ZLogger, the same fields, to a stream | 297.2 ns | 1.80× slower | 0 B |
| Serilog, `[LoggerMessage]` | 885.4 ns | 5.36× slower | 1,448 B |
| NLog, `[LoggerMessage]` | 998.3 ns | 6.05× slower | 1,704 B |
| Pragmatic, `[LoggerMessage]` | 1,176.5 ns | 7.13× slower | 6,992 B |

- **The generated call site is the fastest row and allocates nothing**, ahead of ZLogger in all three
  shapes, the lighter ones included. The run's standard deviation is about 20 ns on every row; the lead is
  115 ns and more.
- **On the GitHub runner, after #109 alone, it was level, not first.** The benchmarks workflow on that change
  (AMD EPYC, Ubuntu 24.04, [head](benchmarks/reports/runner-109_JsonSinkBenchmarks-report-github.md) and
  [base](benchmarks/reports/runner-109-base_JsonSinkBenchmarks-report-github.md)): the call site 263.5 ns,
  down from 405.8 on the base; ZLogger with the same fields to a stream 264.2, to a stream 251.0, into a
  buffer 224.0. #129 is what was meant to take the lead there; its own runner numbers are in its pull
  request's benchmarks job.
- **Microsoft's `[LoggerMessage]` through the Pragmatic JSON provider is the slowest row and the largest
  allocation:** that is the classic path, which builds an entry, a message string and a dictionary.

### Where the time went, and what #109 changed

Measured by subtraction with `ProbeBenchmarks` (`dotnet run -c Release -- probe`): the provider's UTF-8 path
written field by field, one thing taken out or done differently per row, every row checked to write the
bytes it claims, against ZLogger with the same fields and sink in the same run. From
[`probe-subtraction`](benchmarks/reports/probe-subtraction_ProbeBenchmarks-report-github.md), against the
provider of that run (276 ns, the timestamp already read once):

| Taken out, or done differently | Change |
|---|---:|
| The timestamp format parsed on every line, as before #109 | **+90 ns** |
| No `@messageTemplate` (the field ZLogger cannot write) | −20 ns |
| The timestamp written by `Utf8JsonWriter` (STJ's text, not the configured one) | −10 ns |
| No lock; no stream; `AutoFlush` read once | within the run's deviation |
| The message written once, straight into the JSON string | +6 ns: slower |
| Nothing written at all: the call before formatting | 23 ns (ZLogger's: 51 ns) |

What was kept, each by its row:

1. **The timestamp format read once** (`TimestampLayout`), and the default format written in a straight line.
   Alone, [`timestamp`](benchmarks/reports/timestamp_TimestampBenchmarks-report-github.md): `DateTime.TryFormat`
   with the format string 85.5 ns, the layout 16.0 ns, `Utf8Formatter` with `O` (STJ's and ZLogger's) 10.9 ns.
2. **The constant parts of the line copied as blocks encoded once** (`JsonLineBlock`): level and logger
   together per logger, event id, event name and template per call site. In the quiet run
   [`probe-run2`](benchmarks/reports/probe-run2_ProbeBenchmarks-report-github.md) (deviation 1–2 ns) the
   blocks took the line from 259 to 209 ns, while the provider, then copying level and logger as two blocks
   and finding the logger's by category, was at 223. With one block per logger and level it times as the
   probe does: 257.20 against 257.32 ns in `probe-final`.

What was not kept: the event name and template encoded once but still written by the writer — 6 ns in
[`probe-encoded-fields`](benchmarks/reports/probe-encoded-fields_ProbeBenchmarks-report-github.md), within
the deviation; the message written raw, slower; one `IsEnabled` instead of two, 3.7 ns in all.

In one run, [`probe-final`](benchmarks/reports/probe-final_ProbeBenchmarks-report-github.md): the path
before #109 **446 ns**, the provider after it **257 ns**, ZLogger with the same fields and sink **303 ns**. On
the runner the same change measured 405.8 → 263.5 ns against ZLogger's 264.2 (above).

### What #129 changed

After #109 the arguments' properties were the largest part left: each value formatted a second time for
`@properties` and written by a writer call with its escaping. Measured by subtraction and by emulation — the
benchmark's call written by hand as the generator would emit it — in one run,
[`probe-129-subtraction`](benchmarks/reports/probe-129-subtraction_ProbeBenchmarks-report-github.md), against
the provider after #109 (261.8 ns):

| Row | Mean |
|---|---:|
| By hand, as the generated state writes it after #109 (checks the emulation) | 260.5 ns |
| No message written | 260.8 ns |
| **No `@properties` at all** | **191.0 ns** |
| Each value formatted once, still through the writer | 251.9 ns |
| The properties as bytes, each value formatted once | 206.2 ns |
| The whole line as bytes | 150.0 ns |
| **The provider now** | **160.7 ns** |
| ZLogger into a buffer / with the same fields and sink | 280.9 / 300.1 ns |

Kept, for real:

1. **The properties as JSON bytes, each value formatted once.** The generated state renders the message and
   the properties' JSON in one pass (`TryFormatMessageAndJson`): a number or a string the message renders
   without a format lends its bytes to the property; names are encoded at compile time. Only for numbers
   (integers and decimal), strings, booleans and masked values, and a string the encoder would escape sends
   the call back to the writer, so the escaping is the writer's own.
2. **The whole line as bytes, without `Utf8JsonWriter`**, when nothing in it needs the writer: the timestamp
   from its layout, the blocks of #109, the message when the encoder escapes nothing in it, the properties of
   (1). An exception, a message to escape, properties for the writer, a timestamp format the layout does not
   read, an indented line: the writer, as before.

In one run, [`probe-129`](benchmarks/reports/probe-129_ProbeBenchmarks-report-github.md): the provider after
#109 **259.0 ns**, the provider now **158.0 ns**; ZLogger into a buffer 275.7, with the same fields and sink
298.8. The line is unchanged byte for byte.

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
dotnet run -c Release -- json        # a JSON line, every library
dotnet run -c Release -- probe       # where a JSON line's time goes, by subtraction (not in `all`)
dotnet run -c Release -- timestamp   # the JSON line's timestamp alone (not in `all`)
dotnet run -c Release -- redaction   # declared redaction overhead
dotnet run -c Release -- expression  # filter DSL
dotnet run -c Release -- zero        # formatting internals and bulk formatting
```

Reports land in `BenchmarkDotNet.Artifacts/results/`. To update this page, run `all`, copy the
`*-report-github.md` files into `benchmarks/reports/`, and take every number from them.
