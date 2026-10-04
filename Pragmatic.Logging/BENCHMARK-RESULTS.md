# Pragmatic.Logging: Benchmark Results

Every number on this page comes from one run, on 2026-10-04, of:

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
number here that does not appear there is a mistake. The run reported no `MinIterationTime`, baseline or
multimodal-distribution warning.

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
| ZLogger | 167.6 ns | 1.28× faster | 216 B |
| **Pragmatic** | **214.3 ns** | baseline | **592 B** |
| NLog | 228.4 ns | 1.07× slower | 760 B |
| Serilog | 263.9 ns | 1.23× slower | 528 B |

### Source-generated call site (`[LoggerMessage]`)

| Library | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| ZLogger | 155.2 ns | 1.31× faster | 192 B |
| **Pragmatic** | **203.9 ns** | baseline | **544 B** |
| Serilog | 321.6 ns | 1.58× slower | 800 B |
| NLog | 384.0 ns | 1.88× slower | 1,416 B |

### Structured: a scope with four properties, a call with two

| Library | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| ZLogger | 404.8 ns | 1.24× faster | 520 B |
| **Pragmatic** | **501.7 ns** | baseline | **1,176 B** |
| NLog | 675.4 ns | 1.35× slower | 1,328 B |
| Serilog | 839.6 ns | 1.67× slower | 1,912 B |

### Exception

| Library | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| ZLogger | 172.1 ns | 1.37× faster | 232 B |
| **Pragmatic** | **235.5 ns** | baseline | **608 B** |
| NLog | 244.8 ns | 1.04× slower | 776 B |
| Serilog | 279.8 ns | 1.19× slower | 528 B |

### High volume: the simple call 1,000 times, reported per call

| Library | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| ZLogger | 167.1 ns | 1.27× faster | 215 B |
| **Pragmatic** | **211.5 ns** | baseline | **591 B** |
| NLog | 225.2 ns | 1.07× slower | 759 B |
| Serilog | 256.7 ns | 1.21× slower | 518 B |

### Production: two request-context properties and the structured scope

Each library adds the two context properties through its own mechanism: Pragmatic's `LogContextScope`,
Serilog's `LogContext`, NLog's `ScopeContext`. ZLogger has no ambient context of its own, so for it the
two properties are a second MEL scope. Pragmatic runs its production preset, with context enrichment on.

| Library | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| ZLogger | 536.0 ns | 2.57× faster | 832 B |
| NLog | 1,023.2 ns | 1.35× faster | 2,872 B |
| Serilog | 1,054.5 ns | 1.31× faster | 3,000 B |
| **Pragmatic** | **1,376.2 ns** | baseline | **4,112 B** |

### What the comparison says

- **ZLogger is faster than Pragmatic in every category and allocates 2.26× to 4.94× less.** Its UTF-8
  pipeline is the reference for what a logger on `Microsoft.Extensions.Logging` reaches.
- **Pragmatic is ahead of Serilog and NLog on the plain paths** (simple, source-generated, structured,
  high volume), by 1.07× to 1.88×.
- **Pragmatic is the slowest in the production scenario**, 1.31× to 2.57× behind the others, and allocates
  the most (4,112 B). Context enrichment and the production preset are where its per-call costs are.
- **No library is allocation-free here.** Rendering and reading the event costs something everywhere.
  Pragmatic allocates more than ZLogger in every category. Against Serilog it allocates more on the
  simple, exception and high-volume calls (591–608 B against 518–528 B), and less on the
  source-generated and structured ones.
- ZLogger's row is, if anything, pessimistic: the sink reads its property values with
  `GetParameterValue(int)`, which boxes value types. Its typed and JSON accessors would avoid that, but
  the sink does not know the types any more than the other three do.

## Declared redaction overhead: `RedactionOverheadBenchmarks`

Pragmatic only, because no other library in the comparison masks the members a type declares. The same
call (`Registered {Customer}`, a record whose `Email` is declared) goes through the same consuming
provider with the production preset, without a redactor and with one.

| Method | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| Without redaction | 749.0 ns | baseline | 2.58 KB |
| With declared redaction | 1,453.5 ns | 1.94× slower | 3.34 KB |

Declared redaction serializes the value, parses it, masks the paths and serializes again, which is what
the 1.94× is. ⚠️ This row was measured before
[#77](https://github.com/pragmatic-design/Pragmatic.Design/issues/77) was fixed. At that point the
pipeline masked the structured property after the message had been rendered with the member in clear.
Since the fix, the value is masked once, before rendering, and the message is rendered from the masked
values. The path is different and has not been re-measured.

## Formatting internals: `ZeroAllocationBenchmark`

| Method | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| `ZeroAllocMessageFormatter.TryFormat` | 60.14 ns | 1.11× faster | 0 B |
| String interpolation *(baseline)* | 66.46 ns | baseline | 240 B |
| `ZeroAllocMessageFormatter.Format` | 81.11 ns | 1.22× slower | 176 B |
| `string.Format` | 84.90 ns | 1.28× slower | 296 B |
| `LogMessage.ToString` | 85.32 ns | 1.29× slower | 344 B |
| `LogMessage.TryFormat` | 88.14 ns | 1.33× slower | 344 B |
| `MessageFormatter.Format` (reflection-based) | 111.71 ns | 1.68× slower | 528 B |
| Structured logging through `ILogger` | 179.74 ns | 2.71× slower | 904 B |

- `ZeroAllocMessageFormatter.TryFormat`, into a caller-provided span, is the only path with 0 B.
- `LogMessage.TryFormat` is slower than the baseline and allocates 344 B, despite its name. Prefer
  `ZeroAllocMessageFormatter.TryFormat` on a hot path.

## Bulk formatting, 1,000 messages: `AllocationComparisonBenchmark`

| Method | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| `ZeroAllocFormatting` | 69.92 µs | 1.30× faster | 257.81 KB |
| Standard formatting *(baseline)* | 91.04 µs | baseline | 390.63 KB |
| `ZeroAllocFormattingWithStackBuffer` | 102.02 µs | 1.12× slower | 117.19 KB |

The stack-buffer path allocates the least, 3.33× less than the baseline, and is the slowest of the three.

## Filter DSL: `ExpressionDslBenchmarks`

### Single evaluation

| Method | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| Simple level *(baseline)* | 71.57 ns | baseline | 400 B |
| Security auditing | 97.87 ns | 1.37× slower | 400 B |
| Complex business logic | 98.32 ns | 1.37× slower | 400 B |
| Enterprise compliance | 119.83 ns | 1.67× slower | 400 B |
| Performance monitoring | 154.34 ns | 2.16× slower | 400 B |

### Batch: 5 log entries

| Method | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| Simple level *(baseline)* | 381.81 ns | baseline | 2,000 B |
| Complex business logic | 520.94 ns | 1.37× slower | 2,000 B |
| Enterprise compliance | 613.51 ns | 1.61× slower | 2,000 B |
| All five filters (5 × 5) | 2,938.82 ns | 7.70× slower | 10,000 B |

### Property-based filtering

| Method | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| Property exists *(baseline)* | 89.10 ns | baseline | 480 B |
| Property value | 87.84 ns | 1.01× faster | 480 B |
| Has structured properties | 87.57 ns | 1.02× faster | 504 B |
| Numeric properties | 102.99 ns | 1.16× slower | 496 B |

### High volume: 10,000 evaluations per operation

| Method | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| Simple filter *(baseline)* | 751.28 µs | baseline | 4,000,000 B |
| Complex filter | 976.73 µs | 1.30× slower | 4,000,000 B |
| Mixed filters | 1,040.10 µs | 1.39× slower | 4,000,000 B |

### Cache

| Method | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| One expression, 1,001 evaluations *(baseline)* | 71.48 µs | baseline | 400,400 B |
| Five expressions, 5,005 evaluations | 588.65 µs | 8.24× slower | 2,002,000 B |

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
