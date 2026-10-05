```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                    | Categories      | Mean       | Error    | StdDev   | Ratio        | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|-------------------------- |---------------- |-----------:|---------:|---------:|-------------:|--------:|-------:|-------:|----------:|------------:|
| Pragmatic_Exception       | Exception       |   233.2 ns |  3.75 ns |  5.26 ns |     baseline |         | 0.0362 |      - |     608 B |             |
| Serilog_Exception         | Exception       |   268.5 ns |  5.16 ns |  6.71 ns | 1.15x slower |   0.04x | 0.0315 |      - |     528 B |  1.15x less |
| NLog_Exception            | Exception       |   240.2 ns |  4.78 ns |  5.32 ns | 1.03x slower |   0.03x | 0.0463 |      - |     776 B |  1.28x more |
| ZLogger_Exception         | Exception       |   175.7 ns |  3.52 ns |  5.04 ns | 1.33x faster |   0.05x | 0.0138 |      - |     232 B |  2.62x less |
|                           |                 |            |          |          |              |         |        |        |           |             |
| Pragmatic_HighVolume      | HighVolume      |   209.1 ns |  3.54 ns |  2.96 ns |     baseline |         | 0.0352 |      - |     591 B |             |
| Serilog_HighVolume        | HighVolume      |   256.1 ns |  4.99 ns |  6.13 ns | 1.22x slower |   0.03x | 0.0308 |      - |     518 B |  1.14x less |
| NLog_HighVolume           | HighVolume      |   230.8 ns |  4.56 ns | 10.85 ns | 1.10x slower |   0.05x | 0.0452 |      - |     759 B |  1.28x more |
| ZLogger_HighVolume        | HighVolume      |   168.4 ns |  3.33 ns |  6.72 ns | 1.24x faster |   0.05x | 0.0127 |      - |     215 B |  2.75x less |
|                           |                 |            |          |          |              |         |        |        |           |             |
| Pragmatic_Production      | Production      |   747.5 ns | 14.66 ns | 19.06 ns |     baseline |         | 0.1411 |      - |    2368 B |             |
| Serilog_Production        | Production      | 1,007.9 ns | 19.85 ns | 19.50 ns | 1.35x slower |   0.04x | 0.1793 |      - |    3000 B |  1.27x more |
| NLog_Production           | Production      |   968.0 ns | 19.12 ns | 17.88 ns | 1.30x slower |   0.04x | 0.1717 | 0.0010 |    2872 B |  1.21x more |
| ZLogger_Production        | Production      |   519.8 ns | 10.03 ns | 11.55 ns | 1.44x faster |   0.05x | 0.0496 |      - |     832 B |  2.85x less |
|                           |                 |            |          |          |              |         |        |        |           |             |
| Pragmatic_Simple          | Simple          |   214.5 ns |  2.95 ns |  3.15 ns |     baseline |         | 0.0353 |      - |     592 B |             |
| Serilog_Simple            | Simple          |   259.5 ns |  4.74 ns |  9.58 ns | 1.21x slower |   0.05x | 0.0315 |      - |     528 B |  1.12x less |
| NLog_Simple               | Simple          |   220.5 ns |  4.32 ns |  4.63 ns | 1.03x slower |   0.03x | 0.0453 |      - |     760 B |  1.28x more |
| ZLogger_Simple            | Simple          |   164.7 ns |  3.22 ns |  4.30 ns | 1.30x faster |   0.04x | 0.0129 |      - |     216 B |  2.74x less |
|                           |                 |            |          |          |              |         |        |        |           |             |
| Pragmatic_SourceGenerated | SourceGenerated |   201.6 ns |  4.01 ns |  5.88 ns |     baseline |         | 0.0324 |      - |     544 B |             |
| Serilog_SourceGenerated   | SourceGenerated |   302.2 ns |  4.34 ns |  3.85 ns | 1.50x slower |   0.05x | 0.0477 |      - |     800 B |  1.47x more |
| NLog_SourceGenerated      | SourceGenerated |   382.8 ns |  6.81 ns |  8.36 ns | 1.90x slower |   0.07x | 0.0844 |      - |    1416 B |  2.60x more |
| ZLogger_SourceGenerated   | SourceGenerated |   149.1 ns |  2.98 ns |  4.98 ns | 1.35x faster |   0.06x | 0.0114 |      - |     192 B |  2.83x less |
|                           |                 |            |          |          |              |         |        |        |           |             |
| Pragmatic_Structured      | Structured      |   517.5 ns | 10.35 ns | 15.17 ns |     baseline |         | 0.0696 |      - |    1176 B |             |
| Serilog_Structured        | Structured      |   813.2 ns | 16.18 ns | 24.21 ns | 1.57x slower |   0.06x | 0.1135 |      - |    1912 B |  1.63x more |
| NLog_Structured           | Structured      |   671.5 ns | 12.89 ns | 14.32 ns | 1.30x slower |   0.05x | 0.0792 |      - |    1328 B |  1.13x more |
| ZLogger_Structured        | Structured      |   432.6 ns |  8.49 ns | 11.03 ns | 1.20x faster |   0.05x | 0.0310 |      - |     520 B |  2.26x less |
