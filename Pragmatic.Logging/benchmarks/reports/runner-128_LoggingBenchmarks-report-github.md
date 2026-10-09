```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX2


```
| Method                    | Categories      | Mean       | Error    | StdDev  | Ratio        | RatioSD | Gen0   | Allocated | Alloc Ratio |
|-------------------------- |---------------- |-----------:|---------:|--------:|-------------:|--------:|-------:|----------:|------------:|
| Pragmatic_CallSite        | CallSite        |   272.1 ns |  0.93 ns | 0.82 ns |     baseline |         | 0.0076 |     128 B |             |
| Serilog_CallSite          | CallSite        |   660.1 ns |  2.69 ns | 2.10 ns | 2.43x slower |   0.01x | 0.0467 |     792 B |  6.19x more |
| NLog_CallSite             | CallSite        |   845.8 ns |  5.04 ns | 4.72 ns | 3.11x slower |   0.02x | 0.0839 |    1408 B | 11.00x more |
| ZLogger_CallSite          | CallSite        |   320.3 ns |  1.41 ns | 1.25 ns | 1.18x slower |   0.01x | 0.0110 |     184 B |  1.44x more |
|                           |                 |            |          |         |              |         |        |           |             |
| Pragmatic_Exception       | Exception       |   328.3 ns |  1.64 ns | 1.37 ns |     baseline |         | 0.0110 |     184 B |             |
| Serilog_Exception         | Exception       |   545.7 ns |  1.78 ns | 1.58 ns | 1.66x slower |   0.01x | 0.0315 |     528 B |  2.87x more |
| NLog_Exception            | Exception       |   586.2 ns |  2.23 ns | 1.98 ns | 1.79x slower |   0.01x | 0.0458 |     776 B |  4.22x more |
| ZLogger_Exception         | Exception       |   390.4 ns |  1.71 ns | 1.60 ns | 1.19x slower |   0.01x | 0.0138 |     232 B |  1.26x more |
|                           |                 |            |          |         |              |         |        |           |             |
| Pragmatic_HighVolume      | HighVolume      |   299.2 ns |  1.36 ns | 1.14 ns |     baseline |         | 0.0098 |     167 B |             |
| Serilog_HighVolume        | HighVolume      |   513.4 ns |  1.77 ns | 1.38 ns | 1.72x slower |   0.01x | 0.0303 |     518 B |  3.10x more |
| NLog_HighVolume           | HighVolume      |   537.7 ns |  2.17 ns | 1.81 ns | 1.80x slower |   0.01x | 0.0449 |     759 B |  4.54x more |
| ZLogger_HighVolume        | HighVolume      |   384.8 ns |  0.82 ns | 0.73 ns | 1.29x slower |   0.01x | 0.0127 |     215 B |  1.29x more |
|                           |                 |            |          |         |              |         |        |           |             |
| Pragmatic_Production      | Production      | 1,175.5 ns |  5.47 ns | 5.11 ns |     baseline |         | 0.0515 |     864 B |             |
| Serilog_Production        | Production      | 2,337.6 ns | 10.27 ns | 9.61 ns | 1.99x slower |   0.01x | 0.1793 |    3000 B |  3.47x more |
| NLog_Production           | Production      | 2,165.9 ns | 10.51 ns | 9.32 ns | 1.84x slower |   0.01x | 0.1717 |    2872 B |  3.32x more |
| ZLogger_Production        | Production      | 1,132.0 ns |  2.77 ns | 2.31 ns | 1.04x faster |   0.00x | 0.0496 |     832 B |  1.04x less |
|                           |                 |            |          |         |              |         |        |           |             |
| Pragmatic_Simple          | Simple          |   309.2 ns |  1.04 ns | 0.87 ns |     baseline |         | 0.0100 |     168 B |             |
| Serilog_Simple            | Simple          |   518.9 ns |  2.03 ns | 1.80 ns | 1.68x slower |   0.01x | 0.0315 |     528 B |  3.14x more |
| NLog_Simple               | Simple          |   557.3 ns |  3.27 ns | 2.90 ns | 1.80x slower |   0.01x | 0.0448 |     760 B |  4.52x more |
| ZLogger_Simple            | Simple          |   375.6 ns |  1.25 ns | 1.11 ns | 1.21x slower |   0.00x | 0.0129 |     216 B |  1.29x more |
|                           |                 |            |          |         |              |         |        |           |             |
| Pragmatic_SourceGenerated | SourceGenerated |   287.9 ns |  0.77 ns | 0.72 ns |     baseline |         | 0.0076 |     128 B |             |
| Serilog_SourceGenerated   | SourceGenerated |   665.5 ns |  7.69 ns | 7.19 ns | 2.31x slower |   0.02x | 0.0477 |     800 B |  6.25x more |
| NLog_SourceGenerated      | SourceGenerated |   910.1 ns |  4.68 ns | 4.38 ns | 3.16x slower |   0.02x | 0.0839 |    1416 B | 11.06x more |
| ZLogger_SourceGenerated   | SourceGenerated |   337.4 ns |  0.56 ns | 0.46 ns | 1.17x slower |   0.00x | 0.0114 |     192 B |  1.50x more |
|                           |                 |            |          |         |              |         |        |           |             |
| Pragmatic_Structured      | Structured      |   819.6 ns |  3.04 ns | 2.70 ns |     baseline |         | 0.0277 |     464 B |             |
| Serilog_Structured        | Structured      | 1,750.5 ns |  4.52 ns | 4.01 ns | 2.14x slower |   0.01x | 0.1125 |    1912 B |  4.12x more |
| NLog_Structured           | Structured      | 1,468.9 ns |  5.52 ns | 4.89 ns | 1.79x slower |   0.01x | 0.0782 |    1328 B |  2.86x more |
| ZLogger_Structured        | Structured      |   967.5 ns |  3.35 ns | 2.80 ns | 1.18x slower |   0.00x | 0.0305 |     520 B |  1.12x more |
