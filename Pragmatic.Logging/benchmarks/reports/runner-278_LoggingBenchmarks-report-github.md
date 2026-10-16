```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.303
  [Host]     : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX2


```
| Method                    | Categories      | Mean       | Error    | StdDev   | Ratio        | RatioSD | Gen0   | Allocated | Alloc Ratio |
|-------------------------- |---------------- |-----------:|---------:|---------:|-------------:|--------:|-------:|----------:|------------:|
| Pragmatic_CallSite        | CallSite        |   271.4 ns |  0.99 ns |  0.88 ns |     baseline |         | 0.0076 |     128 B |             |
| Serilog_CallSite          | CallSite        |   651.4 ns |  4.18 ns |  3.91 ns | 2.40x slower |   0.02x | 0.0467 |     792 B |  6.19x more |
| NLog_CallSite             | CallSite        |   826.2 ns |  4.87 ns |  4.31 ns | 3.04x slower |   0.02x | 0.0839 |    1408 B | 11.00x more |
| ZLogger_CallSite          | CallSite        |   309.6 ns |  0.69 ns |  0.65 ns | 1.14x slower |   0.00x | 0.0110 |     184 B |  1.44x more |
|                           |                 |            |          |          |              |         |        |           |             |
| Pragmatic_Exception       | Exception       |   328.7 ns |  0.69 ns |  0.62 ns |     baseline |         | 0.0110 |     184 B |             |
| Serilog_Exception         | Exception       |   548.6 ns |  3.28 ns |  3.07 ns | 1.67x slower |   0.01x | 0.0315 |     528 B |  2.87x more |
| NLog_Exception            | Exception       |   581.4 ns |  3.42 ns |  3.04 ns | 1.77x slower |   0.01x | 0.0458 |     776 B |  4.22x more |
| ZLogger_Exception         | Exception       |   413.5 ns |  1.25 ns |  1.17 ns | 1.26x slower |   0.00x | 0.0138 |     232 B |  1.26x more |
|                           |                 |            |          |          |              |         |        |           |             |
| Pragmatic_HighVolume      | HighVolume      |   343.4 ns |  0.70 ns |  0.58 ns |     baseline |         | 0.0098 |     167 B |             |
| Serilog_HighVolume        | HighVolume      |   500.9 ns |  3.30 ns |  2.75 ns | 1.46x slower |   0.01x | 0.0303 |     518 B |  3.10x more |
| NLog_HighVolume           | HighVolume      |   544.3 ns |  5.04 ns |  4.71 ns | 1.59x slower |   0.01x | 0.0449 |     759 B |  4.54x more |
| ZLogger_HighVolume        | HighVolume      |   383.3 ns |  1.41 ns |  1.32 ns | 1.12x slower |   0.00x | 0.0127 |     215 B |  1.29x more |
|                           |                 |            |          |          |              |         |        |           |             |
| Pragmatic_Production      | Production      | 1,120.9 ns |  5.10 ns |  4.52 ns |     baseline |         | 0.0458 |     784 B |             |
| Serilog_Production        | Production      | 2,320.7 ns |  8.79 ns |  7.34 ns | 2.07x slower |   0.01x | 0.1793 |    3000 B |  3.83x more |
| NLog_Production           | Production      | 2,183.4 ns | 16.09 ns | 14.26 ns | 1.95x slower |   0.01x | 0.1717 |    2872 B |  3.66x more |
| ZLogger_Production        | Production      | 1,138.3 ns |  4.19 ns |  3.92 ns | 1.02x slower |   0.01x | 0.0496 |     832 B |  1.06x more |
|                           |                 |            |          |          |              |         |        |           |             |
| Pragmatic_Simple          | Simple          |   300.7 ns |  0.59 ns |  0.49 ns |     baseline |         | 0.0100 |     168 B |             |
| Serilog_Simple            | Simple          |   518.6 ns |  1.71 ns |  1.51 ns | 1.72x slower |   0.01x | 0.0315 |     528 B |  3.14x more |
| NLog_Simple               | Simple          |   552.9 ns |  3.05 ns |  2.85 ns | 1.84x slower |   0.01x | 0.0448 |     760 B |  4.52x more |
| ZLogger_Simple            | Simple          |   355.6 ns |  0.53 ns |  0.44 ns | 1.18x slower |   0.00x | 0.0129 |     216 B |  1.29x more |
|                           |                 |            |          |          |              |         |        |           |             |
| Pragmatic_SourceGenerated | SourceGenerated |   263.8 ns |  0.51 ns |  0.45 ns |     baseline |         | 0.0076 |     128 B |             |
| Serilog_SourceGenerated   | SourceGenerated |   638.3 ns |  4.32 ns |  4.04 ns | 2.42x slower |   0.02x | 0.0477 |     800 B |  6.25x more |
| NLog_SourceGenerated      | SourceGenerated |   854.0 ns |  3.95 ns |  3.51 ns | 3.24x slower |   0.01x | 0.0839 |    1416 B | 11.06x more |
| ZLogger_SourceGenerated   | SourceGenerated |   343.6 ns |  0.52 ns |  0.44 ns | 1.30x slower |   0.00x | 0.0114 |     192 B |  1.50x more |
|                           |                 |            |          |          |              |         |        |           |             |
| Pragmatic_Structured      | Structured      |   894.8 ns |  2.92 ns |  2.59 ns |     baseline |         | 0.0277 |     464 B |             |
| Serilog_Structured        | Structured      | 1,781.8 ns |  8.50 ns |  7.10 ns | 1.99x slower |   0.01x | 0.1125 |    1912 B |  4.12x more |
| NLog_Structured           | Structured      | 1,507.9 ns |  5.09 ns |  4.76 ns | 1.69x slower |   0.01x | 0.0782 |    1328 B |  2.86x more |
| ZLogger_Structured        | Structured      |   922.6 ns |  3.10 ns |  2.75 ns | 1.03x slower |   0.00x | 0.0305 |     520 B |  1.12x more |
