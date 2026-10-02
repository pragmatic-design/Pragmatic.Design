# Pragmatic.Logging — Benchmark Results

> Runtime: .NET 10 | BenchmarkDotNet (DefaultJob, null sinks for every library)
> Full HTML/CSV reports: `BenchmarkDotNet.Artifacts/results/`

## LoggingBenchmarks — Library Comparison (vs Serilog, NLog)

All libraries write through their real pipeline into a null sink (pure pipeline cost, no I/O).
Per call unless noted; lower is better. **Pragmatic ranks first in every category.**

### Source-generated call sites (`[LoggerMessage]` — the recommended hot-path pattern)

| Method | Mean | Allocated |
|---|---:|---:|
| **Pragmatic** | **33.6 ns** | **0 B** ✅ |
| Serilog | 294.6 ns | 712 B |
| NLog | 297.6 ns | 1,032 B |

> With Microsoft's `[LoggerMessage]` source generator at the call site (zero-boxing struct state)
> and Pragmatic's deferred pipeline, a log call is **allocation-free end-to-end** and ~8.8× faster
> than both Serilog and NLog.

### Simple logging (`logger.LogInformation(template, args)`)

| Method | Mean | Allocated |
|---|---:|---:|
| NullLogger *(baseline)* | 27.6 ns | 64 B |
| **Pragmatic** | **44.0 ns** | **64 B** ✅ |
| NLog | 170.8 ns | 488 B |
| Serilog | 202.3 ns | 440 B |

> The 64 B is the call's own `params` array — identical to the do-nothing `NullLogger` baseline.
> The Pragmatic pipeline itself adds **zero allocations** and ~16 ns.

### Structured logging (with a `BeginScope` per call)

| Method | Mean | Allocated |
|---|---:|---:|
| **Pragmatic** | **117.2 ns** | **328 B** ✅ |
| NLog | 292.5 ns | 696 B |
| Serilog | 670.8 ns | 2,016 B |
| Pragmatic (production preset: redaction + full pipeline) | 941.1 ns | 1,624 B |

### Exception logging

| Method | Mean | Allocated |
|---|---:|---:|
| **Pragmatic** | **51.0 ns** | **64 B** ✅ |
| NLog | 229.4 ns | 504 B |
| Serilog | 233.9 ns | 440 B |

### High volume (1,000 calls per op)

| Method | Mean | Allocated |
|---|---:|---:|
| **Pragmatic** | **46.4 µs** | **64 KB** ✅ |
| NLog | 193.7 µs | 487 KB |
| Serilog | 210.1 µs | 440 KB |

### Where the numbers come from

- **Deferred pipeline**: when no pipeline feature needs a materialized entry (no advanced filters,
  no context enrichment, no redaction), the typed log state flows straight to the sink — no
  `LogEntry`, no dictionaries, no eager message rendering. Enabling any pipeline feature routes the call
  through the full pipeline — the "production preset" rows above measure that path, redaction
  included. Ambient scopes remain readable by the sink during the deferred call.
- **Lazy everything**: `LogEntry` allocates its dictionaries only if someone writes to them; scope
  capture is allocation-free when no scope is active; provider metrics use a fixed ring buffer.
- **`[LoggerMessage]` call sites** (Microsoft's source generator, the pattern Pragmatic recommends)
  add zero-boxing struct state on top — reaching 0 B end-to-end.

---

## ZeroAllocationBenchmark — Formatting Internals

| Method | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| **ZeroAllocMessageFormatterTryFormat** | **57.56 ns** | **1.47x faster** ✅ | **0 B** |
| **LogMessageTryFormat** | **59.53 ns** | **1.42x faster** ✅ | 344 B ⚠️ |
| StandardStringInterpolation *(baseline)* | 84.33 ns | 1.00 | 240 B |
| ZeroAllocMessageFormatterFormat | 84.23 ns | 1.00x | 176 B |
| LogMessageToString | 88.01 ns | 1.04x slower | 288 B |
| StandardStringFormat | 107.29 ns | 1.27x slower | 296 B |
| StructuredLoggingWithILogger | 193.13 ns | 2.29x slower | 904 B |
| MessageFormatter.Format *(reflection-based)* | 266.14 ns | 3.16x slower | 616 B |

### Key findings

- `ZeroAllocMessageFormatter.TryFormat` (caller-provided `Span<char>` buffer): **0 B heap**.
  `LogMessage.TryFormat` allocates ~344 B — prefer `ZeroAllocMessageFormatter` on the hot path.
- `TryFormat` is faster than baseline string interpolation.
- The string-returning `Format` overload shaves allocations to 176 B but matches baseline speed — prefer `TryFormat`.
- The reflection path (`MessageFormatter`) and `ILogger.BeginScope`-based structured logging are
  slower and allocate more than the zero-alloc path — use the zero-alloc formatter on hot paths.


---

## AllocationComparisonBenchmark — Bulk Formatting (1000 messages)

| Method | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| **ZeroAllocFormattingWithStackBuffer** | **123.0 µs** | **1.32x faster** ✅ | **117 KB** |
| StandardFormatting *(baseline, reflection)* | 162.2 µs | 1.00 | 391 KB |
| ZeroAllocFormatting | 245.5 µs | 1.51x slower | 600 KB |

> Stack-buffer TryFormat path is **24% faster** and allocates **3.3× less** than reflection-based formatting for 1000 messages.

---

## ExpressionDslBenchmarks — Filter DSL Performance

### Single Filter Evaluation

| Method | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| SimpleLevel_Evaluation *(baseline)* | 93.54 ns | 1.00 | **0 B** ✅ |
| ComplexBusinessLogic_Evaluation | 117.16 ns | 1.25x slower | **0 B** ✅ |
| SecurityAuditing_Evaluation | 124.86 ns | 1.33x slower | **0 B** ✅ |
| EnterpriseCompliance_Evaluation | 140.57 ns | 1.50x slower | **0 B** ✅ |
| PerformanceMonitoring_Evaluation | 185.11 ns | 1.98x slower | **0 B** ✅ |

> Expression DSL cache lookups: **0 B per evaluation**.

### Batch Evaluation (5 log entries)

| Method | Mean | Allocated |
|---|---:|---:|
| SimpleLevel_BatchEvaluation | 529.68 ns | **0 B** ✅ |
| ComplexBusinessLogic_BatchEvaluation | 669.20 ns | **0 B** ✅ |
| EnterpriseCompliance_BatchEvaluation | 742.45 ns | **0 B** ✅ |
| AllFilters_BatchEvaluation (5×5) | 3,381.87 ns | **0 B** ✅ |

### Property-Based Filtering

| Method | Mean | Allocated |
|---|---:|---:|
| PropertyValue_Evaluation | 166.55 ns | **0 B** ✅ |
| StructuredProperties_Evaluation | 166.88 ns | **0 B** ✅ |
| NumericProperties_Evaluation | 180.73 ns | **0 B** ✅ |
| PropertyExists_Evaluation | 198.85 ns | **0 B** ✅ |

### High Volume (10,000 iterations)

| Method | Mean | Throughput |
|---|---:|---:|
| HighVolume_SimpleFilter | 1.04 ms | ~9.7M evals/s |
| HighVolume_ComplexFilter | 1.22 ms | ~8.2M evals/s |
| HighVolume_MixedFilters | 1.31 ms | ~7.7M evals/s |

### Key findings

- Simple level filters: **~94 ns** per evaluation — suitable for hot paths.
- Complex multi-condition business logic: **~117–185 ns** — acceptable for per-request filtering.
- Property-based filters: **~167–199 ns** — uniform cost regardless of property type.
- High-volume throughput: **7.7–9.7 million evaluations/second**.
- **All evaluation paths: 0 B heap allocation**.

---

## Running benchmarks

```bash
cd Pragmatic.Logging/benchmarks/Pragmatic.Logging.Benchmarks

# All suites (generates HTML + Markdown in BenchmarkDotNet.Artifacts/results/)
dotnet run -c Release -- all

# Individual suites
dotnet run -c Release -- logging    # vs Serilog/NLog comparison
dotnet run -c Release -- expression # Expression DSL performance
dotnet run -c Release -- zero       # Zero allocation formatting
```

Full HTML reports: `BenchmarkDotNet.Artifacts/results/*.html`
