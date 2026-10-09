```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                    | Categories      | Mean       | Error    | StdDev    | Median     | Ratio        | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|-------------------------- |---------------- |-----------:|---------:|----------:|-----------:|-------------:|--------:|-------:|-------:|----------:|------------:|
| Pragmatic_CallSite        | CallSite        |   164.7 ns |  3.31 ns |   4.75 ns |   165.6 ns |     baseline |         | 0.0076 |      - |     128 B |             |
| Serilog_CallSite          | CallSite        |   428.2 ns |  8.28 ns |  10.77 ns |   429.8 ns | 2.60x slower |   0.10x | 0.0472 |      - |     792 B |  6.19x more |
| NLog_CallSite             | CallSite        |   543.2 ns | 10.87 ns |  14.88 ns |   541.0 ns | 3.30x slower |   0.13x | 0.0839 |      - |    1408 B | 11.00x more |
| ZLogger_CallSite          | CallSite        |   201.7 ns |  4.04 ns |   6.97 ns |   201.5 ns | 1.23x slower |   0.05x | 0.0110 |      - |     184 B |  1.44x more |
|                           |                 |            |          |           |            |              |         |        |        |           |             |
| Pragmatic_Exception       | Exception       |   208.8 ns |  4.17 ns |   8.97 ns |   209.4 ns |     baseline |         | 0.0110 |      - |     184 B |             |
| Serilog_Exception         | Exception       |   367.0 ns |  7.24 ns |  15.90 ns |   369.5 ns | 1.76x slower |   0.11x | 0.0315 |      - |     528 B |  2.87x more |
| NLog_Exception            | Exception       |   316.2 ns |  8.37 ns |  24.69 ns |   312.4 ns | 1.52x slower |   0.14x | 0.0463 |      - |     776 B |  4.22x more |
| ZLogger_Exception         | Exception       |   218.9 ns |  7.96 ns |  23.46 ns |   219.8 ns | 1.05x slower |   0.12x | 0.0138 |      - |     232 B |  1.26x more |
|                           |                 |            |          |           |            |              |         |        |        |           |             |
| Pragmatic_HighVolume      | HighVolume      |   168.1 ns |  6.74 ns |  19.88 ns |   166.9 ns |     baseline |         | 0.0098 |      - |     167 B |             |
| Serilog_HighVolume        | HighVolume      |   322.9 ns |  7.81 ns |  23.02 ns |   323.8 ns | 1.95x slower |   0.27x | 0.0308 |      - |     518 B |  3.10x more |
| NLog_HighVolume           | HighVolume      |   283.4 ns |  8.34 ns |  24.60 ns |   284.2 ns | 1.71x slower |   0.25x | 0.0452 |      - |     759 B |  4.54x more |
| ZLogger_HighVolume        | HighVolume      |   197.5 ns |  6.68 ns |  19.70 ns |   195.0 ns | 1.19x slower |   0.18x | 0.0127 |      - |     215 B |  1.29x more |
|                           |                 |            |          |           |            |              |         |        |        |           |             |
| Pragmatic_Production      | Production      |   695.5 ns | 19.44 ns |  57.33 ns |   700.2 ns |     baseline |         | 0.0515 |      - |     864 B |             |
| Serilog_Production        | Production      | 1,299.4 ns | 41.53 ns | 122.44 ns | 1,303.6 ns | 1.88x slower |   0.24x | 0.1793 |      - |    3000 B |  3.47x more |
| NLog_Production           | Production      | 1,065.9 ns | 43.65 ns | 128.69 ns | 1,011.1 ns | 1.54x slower |   0.23x | 0.1717 | 0.0010 |    2872 B |  3.32x more |
| ZLogger_Production        | Production      |   589.9 ns | 16.61 ns |  47.92 ns |   580.7 ns | 1.19x faster |   0.13x | 0.0496 |      - |     832 B |  1.04x less |
|                           |                 |            |          |           |            |              |         |        |        |           |             |
| Pragmatic_Simple          | Simple          |   147.0 ns |  3.85 ns |  11.12 ns |   144.3 ns |     baseline |         | 0.0100 |      - |     168 B |             |
| Serilog_Simple            | Simple          |   276.2 ns |  5.48 ns |  12.02 ns |   275.2 ns | 1.89x slower |   0.16x | 0.0315 |      - |     528 B |  3.14x more |
| NLog_Simple               | Simple          |   237.7 ns |  4.37 ns |   9.59 ns |   235.8 ns | 1.63x slower |   0.13x | 0.0453 |      - |     760 B |  4.52x more |
| ZLogger_Simple            | Simple          |   170.0 ns |  3.20 ns |   5.93 ns |   169.7 ns | 1.16x slower |   0.09x | 0.0129 |      - |     216 B |  1.29x more |
|                           |                 |            |          |           |            |              |         |        |        |           |             |
| Pragmatic_SourceGenerated | SourceGenerated |   132.2 ns |  2.41 ns |   4.41 ns |   131.0 ns |     baseline |         | 0.0076 |      - |     128 B |             |
| Serilog_SourceGenerated   | SourceGenerated |   329.5 ns |  6.61 ns |   8.60 ns |   328.1 ns | 2.49x slower |   0.10x | 0.0477 |      - |     800 B |  6.25x more |
| NLog_SourceGenerated      | SourceGenerated |   389.7 ns |  7.39 ns |   6.92 ns |   389.4 ns | 2.95x slower |   0.11x | 0.0844 |      - |    1416 B | 11.06x more |
| ZLogger_SourceGenerated   | SourceGenerated |   156.9 ns |  3.15 ns |   8.45 ns |   154.9 ns | 1.19x slower |   0.07x | 0.0114 |      - |     192 B |  1.50x more |
|                           |                 |            |          |           |            |              |         |        |        |           |             |
| Pragmatic_Structured      | Structured      |   378.9 ns |  7.59 ns |  14.80 ns |   378.3 ns |     baseline |         | 0.0277 |      - |     464 B |             |
| Serilog_Structured        | Structured      |   860.5 ns | 17.16 ns |  38.39 ns |   860.4 ns | 2.27x slower |   0.13x | 0.1135 |      - |    1912 B |  4.12x more |
| NLog_Structured           | Structured      |   674.2 ns | 13.38 ns |  13.74 ns |   670.5 ns | 1.78x slower |   0.08x | 0.0792 |      - |    1328 B |  2.86x more |
| ZLogger_Structured        | Structured      |   432.8 ns |  8.32 ns |  20.87 ns |   430.1 ns | 1.14x slower |   0.07x | 0.0310 |      - |     520 B |  1.12x more |
