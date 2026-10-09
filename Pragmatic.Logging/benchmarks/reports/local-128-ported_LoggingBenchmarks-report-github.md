```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                    | Categories      | Mean       | Error    | StdDev    | Median     | Ratio        | RatioSD | Gen0   | Allocated | Alloc Ratio |
|-------------------------- |---------------- |-----------:|---------:|----------:|-----------:|-------------:|--------:|-------:|----------:|------------:|
| Pragmatic_CallSite        | CallSite        |   162.1 ns |  6.43 ns |  18.96 ns |   159.2 ns |     baseline |         | 0.0076 |     128 B |             |
| Serilog_CallSite          | CallSite        |   416.9 ns | 11.43 ns |  33.71 ns |   411.9 ns | 2.61x slower |   0.37x | 0.0472 |     792 B |  6.19x more |
| NLog_CallSite             | CallSite        |   492.3 ns | 19.06 ns |  56.19 ns |   515.1 ns | 3.08x slower |   0.50x | 0.0839 |    1408 B | 11.00x more |
| ZLogger_CallSite          | CallSite        |   193.8 ns |  6.23 ns |  18.36 ns |   192.3 ns | 1.21x slower |   0.18x | 0.0110 |     184 B |  1.44x more |
|                           |                 |            |          |           |            |              |         |        |           |             |
| Pragmatic_Exception       | Exception       |   184.1 ns |  5.36 ns |  15.54 ns |   184.1 ns |     baseline |         | 0.0110 |     184 B |             |
| Serilog_Exception         | Exception       |   361.5 ns |  7.80 ns |  23.00 ns |   357.3 ns | 1.98x slower |   0.22x | 0.0315 |     528 B |  2.87x more |
| NLog_Exception            | Exception       |   324.9 ns | 15.75 ns |  46.45 ns |   329.6 ns | 1.78x slower |   0.30x | 0.0463 |     776 B |  4.22x more |
| ZLogger_Exception         | Exception       |   294.3 ns |  5.80 ns |   7.54 ns |   295.3 ns | 1.61x slower |   0.15x | 0.0138 |     232 B |  1.26x more |
|                           |                 |            |          |           |            |              |         |        |           |             |
| Pragmatic_HighVolume      | HighVolume      |   233.7 ns |  4.63 ns |   5.85 ns |   233.5 ns |     baseline |         | 0.0098 |     167 B |             |
| Serilog_HighVolume        | HighVolume      |   410.6 ns |  7.40 ns |   6.92 ns |   410.8 ns | 1.76x slower |   0.05x | 0.0308 |     518 B |  3.10x more |
| NLog_HighVolume           | HighVolume      |   400.5 ns |  7.10 ns |   7.60 ns |   400.3 ns | 1.71x slower |   0.05x | 0.0449 |     759 B |  4.54x more |
| ZLogger_HighVolume        | HighVolume      |   262.3 ns |  5.24 ns |   8.76 ns |   263.8 ns | 1.12x slower |   0.05x | 0.0127 |     215 B |  1.29x more |
|                           |                 |            |          |           |            |              |         |        |           |             |
| Pragmatic_Production      | Production      |   958.8 ns | 55.80 ns | 164.52 ns |   976.4 ns |     baseline |         | 0.0896 |    1528 B |             |
| Serilog_Production        | Production      | 1,251.8 ns | 41.13 ns | 121.28 ns | 1,283.7 ns | 1.35x slower |   0.28x | 0.1793 |    3000 B |  1.96x more |
| NLog_Production           | Production      | 1,130.7 ns | 25.50 ns |  73.99 ns | 1,121.6 ns | 1.22x slower |   0.24x | 0.1717 |    2872 B |  1.88x more |
| ZLogger_Production        | Production      |   672.8 ns | 24.62 ns |  72.58 ns |   690.1 ns | 1.44x faster |   0.30x | 0.0496 |     832 B |  1.84x less |
|                           |                 |            |          |           |            |              |         |        |           |             |
| Pragmatic_Simple          | Simple          |   180.0 ns |  7.48 ns |  22.05 ns |   183.0 ns |     baseline |         | 0.0100 |     168 B |             |
| Serilog_Simple            | Simple          |   329.9 ns | 13.44 ns |  39.62 ns |   326.5 ns | 1.86x slower |   0.33x | 0.0315 |     528 B |  3.14x more |
| NLog_Simple               | Simple          |   250.0 ns |  6.86 ns |  20.00 ns |   247.8 ns | 1.41x slower |   0.22x | 0.0453 |     760 B |  4.52x more |
| ZLogger_Simple            | Simple          |   225.0 ns | 13.52 ns |  39.85 ns |   221.8 ns | 1.27x slower |   0.28x | 0.0129 |     216 B |  1.29x more |
|                           |                 |            |          |           |            |              |         |        |           |             |
| Pragmatic_SourceGenerated | SourceGenerated |   163.9 ns |  5.28 ns |  15.58 ns |   159.8 ns |     baseline |         | 0.0076 |     128 B |             |
| Serilog_SourceGenerated   | SourceGenerated |   379.4 ns | 11.58 ns |  34.14 ns |   376.2 ns | 2.34x slower |   0.31x | 0.0477 |     800 B |  6.25x more |
| NLog_SourceGenerated      | SourceGenerated |   520.7 ns | 11.27 ns |  32.88 ns |   524.8 ns | 3.21x slower |   0.37x | 0.0844 |    1416 B | 11.06x more |
| ZLogger_SourceGenerated   | SourceGenerated |   196.6 ns |  3.89 ns |   7.12 ns |   197.1 ns | 1.21x slower |   0.12x | 0.0114 |     192 B |  1.50x more |
|                           |                 |            |          |           |            |              |         |        |           |             |
| Pragmatic_Structured      | Structured      |   544.2 ns | 23.69 ns |  69.84 ns |   527.7 ns |     baseline |         | 0.0348 |     584 B |             |
| Serilog_Structured        | Structured      | 1,305.3 ns | 25.80 ns |  64.74 ns | 1,301.1 ns | 2.44x slower |   0.33x | 0.1125 |    1912 B |  3.27x more |
| NLog_Structured           | Structured      |   885.8 ns | 20.54 ns |  60.23 ns |   888.6 ns | 1.65x slower |   0.23x | 0.0792 |    1328 B |  2.27x more |
| ZLogger_Structured        | Structured      |   537.1 ns | 14.88 ns |  43.87 ns |   535.9 ns | 1.00x slower |   0.15x | 0.0310 |     520 B |  1.12x less |
