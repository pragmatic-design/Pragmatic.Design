```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX2


```
| Method                    | Categories      | Mean       | Error    | StdDev   | Ratio        | RatioSD | Gen0   | Allocated | Alloc Ratio |
|-------------------------- |---------------- |-----------:|---------:|---------:|-------------:|--------:|-------:|----------:|------------:|
| Pragmatic_CallSite        | CallSite        |   424.8 ns |  2.22 ns |  2.08 ns |     baseline |         | 0.0319 |     536 B |             |
| Serilog_CallSite          | CallSite        |   633.6 ns |  3.08 ns |  2.88 ns | 1.49x slower |   0.01x | 0.0467 |     792 B |  1.48x more |
| NLog_CallSite             | CallSite        |   858.8 ns |  4.59 ns |  3.83 ns | 2.02x slower |   0.01x | 0.0839 |    1408 B |  2.63x more |
| ZLogger_CallSite          | CallSite        |   344.9 ns |  0.69 ns |  0.54 ns | 1.23x faster |   0.01x | 0.0110 |     184 B |  2.91x less |
|                           |                 |            |          |          |              |         |        |           |             |
| Pragmatic_Exception       | Exception       |   468.5 ns |  1.00 ns |  0.89 ns |     baseline |         | 0.0362 |     608 B |             |
| Serilog_Exception         | Exception       |   549.1 ns |  2.32 ns |  2.17 ns | 1.17x slower |   0.00x | 0.0315 |     528 B |  1.15x less |
| NLog_Exception            | Exception       |   591.3 ns |  3.81 ns |  3.38 ns | 1.26x slower |   0.01x | 0.0458 |     776 B |  1.28x more |
| ZLogger_Exception         | Exception       |   402.3 ns |  1.44 ns |  1.28 ns | 1.16x faster |   0.00x | 0.0138 |     232 B |  2.62x less |
|                           |                 |            |          |          |              |         |        |           |             |
| Pragmatic_HighVolume      | HighVolume      |   435.4 ns |  1.86 ns |  1.65 ns |     baseline |         | 0.0352 |     591 B |             |
| Serilog_HighVolume        | HighVolume      |   536.8 ns |  5.73 ns |  5.36 ns | 1.23x slower |   0.01x | 0.0303 |     518 B |  1.14x less |
| NLog_HighVolume           | HighVolume      |   553.3 ns |  3.62 ns |  3.38 ns | 1.27x slower |   0.01x | 0.0449 |     759 B |  1.28x more |
| ZLogger_HighVolume        | HighVolume      |   362.9 ns |  0.95 ns |  0.84 ns | 1.20x faster |   0.01x | 0.0127 |     215 B |  2.75x less |
|                           |                 |            |          |          |              |         |        |           |             |
| Pragmatic_Production      | Production      | 1,886.2 ns | 20.87 ns | 18.50 ns |     baseline |         | 0.1411 |    2368 B |             |
| Serilog_Production        | Production      | 2,360.2 ns | 14.49 ns | 12.84 ns | 1.25x slower |   0.01x | 0.1793 |    3000 B |  1.27x more |
| NLog_Production           | Production      | 2,167.0 ns | 17.66 ns | 14.75 ns | 1.15x slower |   0.01x | 0.1717 |    2872 B |  1.21x more |
| ZLogger_Production        | Production      | 1,140.3 ns |  3.49 ns |  3.09 ns | 1.65x faster |   0.02x | 0.0496 |     832 B |  2.85x less |
|                           |                 |            |          |          |              |         |        |           |             |
| Pragmatic_Simple          | Simple          |   463.5 ns |  1.97 ns |  1.74 ns |     baseline |         | 0.0353 |     592 B |             |
| Serilog_Simple            | Simple          |   497.9 ns |  2.43 ns |  2.27 ns | 1.07x slower |   0.01x | 0.0315 |     528 B |  1.12x less |
| NLog_Simple               | Simple          |   559.4 ns |  1.85 ns |  1.73 ns | 1.21x slower |   0.01x | 0.0448 |     760 B |  1.28x more |
| ZLogger_Simple            | Simple          |   386.4 ns |  1.22 ns |  1.01 ns | 1.20x faster |   0.01x | 0.0129 |     216 B |  2.74x less |
|                           |                 |            |          |          |              |         |        |           |             |
| Pragmatic_SourceGenerated | SourceGenerated |   467.8 ns |  2.29 ns |  2.03 ns |     baseline |         | 0.0324 |     544 B |             |
| Serilog_SourceGenerated   | SourceGenerated |   675.3 ns |  4.41 ns |  3.69 ns | 1.44x slower |   0.01x | 0.0477 |     800 B |  1.47x more |
| NLog_SourceGenerated      | SourceGenerated |   879.2 ns |  3.71 ns |  3.47 ns | 1.88x slower |   0.01x | 0.0839 |    1416 B |  2.60x more |
| ZLogger_SourceGenerated   | SourceGenerated |   342.6 ns |  0.97 ns |  0.81 ns | 1.37x faster |   0.01x | 0.0114 |     192 B |  2.83x less |
|                           |                 |            |          |          |              |         |        |           |             |
| Pragmatic_Structured      | Structured      | 1,096.5 ns |  5.61 ns |  5.25 ns |     baseline |         | 0.0687 |    1176 B |             |
| Serilog_Structured        | Structured      | 1,714.4 ns |  5.19 ns |  4.33 ns | 1.56x slower |   0.01x | 0.1125 |    1912 B |  1.63x more |
| NLog_Structured           | Structured      | 1,458.6 ns |  5.21 ns |  4.62 ns | 1.33x slower |   0.01x | 0.0782 |    1328 B |  1.13x more |
| ZLogger_Structured        | Structured      |   900.3 ns |  1.67 ns |  1.30 ns | 1.22x faster |   0.01x | 0.0305 |     520 B |  2.26x less |
