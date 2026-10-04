```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                    | Categories      | Mean       | Error    | StdDev   | Ratio        | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|-------------------------- |---------------- |-----------:|---------:|---------:|-------------:|--------:|-------:|-------:|----------:|------------:|
| Pragmatic_Exception       | Exception       |   235.5 ns |  4.63 ns |  6.94 ns |     baseline |         | 0.0362 |      - |     608 B |             |
| Serilog_Exception         | Exception       |   279.8 ns |  5.52 ns |  7.91 ns | 1.19x slower |   0.05x | 0.0315 |      - |     528 B |  1.15x less |
| NLog_Exception            | Exception       |   244.8 ns |  4.82 ns |  8.69 ns | 1.04x slower |   0.05x | 0.0463 |      - |     776 B |  1.28x more |
| ZLogger_Exception         | Exception       |   172.1 ns |  3.45 ns |  5.57 ns | 1.37x faster |   0.06x | 0.0138 |      - |     232 B |  2.62x less |
|                           |                 |            |          |          |              |         |        |        |           |             |
| Pragmatic_HighVolume      | HighVolume      |   211.5 ns |  4.16 ns |  4.63 ns |     baseline |         | 0.0352 |      - |     591 B |             |
| Serilog_HighVolume        | HighVolume      |   256.7 ns |  4.74 ns |  4.87 ns | 1.21x slower |   0.03x | 0.0308 |      - |     518 B |  1.14x less |
| NLog_HighVolume           | HighVolume      |   225.2 ns |  4.50 ns |  7.52 ns | 1.07x slower |   0.04x | 0.0452 |      - |     759 B |  1.28x more |
| ZLogger_HighVolume        | HighVolume      |   167.1 ns |  3.33 ns |  5.48 ns | 1.27x faster |   0.05x | 0.0127 |      - |     215 B |  2.75x less |
|                           |                 |            |          |          |              |         |        |        |           |             |
| Pragmatic_Production      | Production      | 1,376.2 ns | 27.09 ns | 30.11 ns |     baseline |         | 0.2441 | 0.0019 |    4112 B |             |
| Serilog_Production        | Production      | 1,054.5 ns | 20.96 ns | 30.72 ns | 1.31x faster |   0.05x | 0.1793 |      - |    3000 B |  1.37x less |
| NLog_Production           | Production      | 1,023.2 ns | 19.57 ns | 32.70 ns | 1.35x faster |   0.05x | 0.1717 | 0.0010 |    2872 B |  1.43x less |
| ZLogger_Production        | Production      |   536.0 ns | 10.66 ns | 12.27 ns | 2.57x faster |   0.08x | 0.0496 |      - |     832 B |  4.94x less |
|                           |                 |            |          |          |              |         |        |        |           |             |
| Pragmatic_Simple          | Simple          |   214.3 ns |  3.96 ns |  3.51 ns |     baseline |         | 0.0353 |      - |     592 B |             |
| Serilog_Simple            | Simple          |   263.9 ns |  5.14 ns |  5.05 ns | 1.23x slower |   0.03x | 0.0315 |      - |     528 B |  1.12x less |
| NLog_Simple               | Simple          |   228.4 ns |  4.39 ns |  4.11 ns | 1.07x slower |   0.03x | 0.0453 |      - |     760 B |  1.28x more |
| ZLogger_Simple            | Simple          |   167.6 ns |  3.33 ns |  6.01 ns | 1.28x faster |   0.05x | 0.0129 |      - |     216 B |  2.74x less |
|                           |                 |            |          |          |              |         |        |        |           |             |
| Pragmatic_SourceGenerated | SourceGenerated |   203.9 ns |  4.10 ns |  5.75 ns |     baseline |         | 0.0324 |      - |     544 B |             |
| Serilog_SourceGenerated   | SourceGenerated |   321.6 ns |  6.21 ns |  8.30 ns | 1.58x slower |   0.06x | 0.0477 |      - |     800 B |  1.47x more |
| NLog_SourceGenerated      | SourceGenerated |   384.0 ns |  6.72 ns | 10.46 ns | 1.88x slower |   0.07x | 0.0844 |      - |    1416 B |  2.60x more |
| ZLogger_SourceGenerated   | SourceGenerated |   155.2 ns |  3.06 ns |  3.87 ns | 1.31x faster |   0.05x | 0.0114 |      - |     192 B |  2.83x less |
|                           |                 |            |          |          |              |         |        |        |           |             |
| Pragmatic_Structured      | Structured      |   501.7 ns |  8.81 ns |  7.36 ns |     baseline |         | 0.0701 |      - |    1176 B |             |
| Serilog_Structured        | Structured      |   839.6 ns | 15.33 ns | 13.59 ns | 1.67x slower |   0.04x | 0.1135 |      - |    1912 B |  1.63x more |
| NLog_Structured           | Structured      |   675.4 ns | 12.85 ns | 13.19 ns | 1.35x slower |   0.03x | 0.0792 |      - |    1328 B |  1.13x more |
| ZLogger_Structured        | Structured      |   404.8 ns |  7.10 ns |  9.48 ns | 1.24x faster |   0.03x | 0.0310 |      - |     520 B |  2.26x less |
