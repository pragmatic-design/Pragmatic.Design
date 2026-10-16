```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9711)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                    | Categories      | Mean       | Error    | StdDev    | Median     | Ratio        | RatioSD | Gen0   | Allocated | Alloc Ratio |
|-------------------------- |---------------- |-----------:|---------:|----------:|-----------:|-------------:|--------:|-------:|----------:|------------:|
| Pragmatic_CallSite        | CallSite        |   173.4 ns |  5.43 ns |  16.01 ns |   175.1 ns |     baseline |         | 0.0076 |     128 B |             |
| Serilog_CallSite          | CallSite        |   438.2 ns | 13.08 ns |  38.57 ns |   441.6 ns | 2.55x slower |   0.33x | 0.0472 |     792 B |  6.19x more |
| NLog_CallSite             | CallSite        |   548.7 ns | 21.49 ns |  63.35 ns |   556.8 ns | 3.19x slower |   0.48x | 0.0839 |    1408 B | 11.00x more |
| ZLogger_CallSite          | CallSite        |   214.1 ns |  4.29 ns |   8.37 ns |   213.5 ns | 1.25x slower |   0.13x | 0.0110 |     184 B |  1.44x more |
|                           |                 |            |          |           |            |              |         |        |           |             |
| Pragmatic_Exception       | Exception       |   219.0 ns |  5.28 ns |  15.58 ns |   221.0 ns |     baseline |         | 0.0110 |     184 B |             |
| Serilog_Exception         | Exception       |   386.4 ns | 11.60 ns |  34.20 ns |   389.6 ns | 1.77x slower |   0.21x | 0.0315 |     528 B |  2.87x more |
| NLog_Exception            | Exception       |   412.4 ns |  9.29 ns |  27.41 ns |   416.9 ns | 1.89x slower |   0.19x | 0.0463 |     776 B |  4.22x more |
| ZLogger_Exception         | Exception       |   237.9 ns |  6.64 ns |  19.58 ns |   234.9 ns | 1.09x slower |   0.12x | 0.0138 |     232 B |  1.26x more |
|                           |                 |            |          |           |            |              |         |        |           |             |
| Pragmatic_HighVolume      | HighVolume      |   178.8 ns |  5.43 ns |  16.01 ns |   176.3 ns |     baseline |         | 0.0098 |     167 B |             |
| Serilog_HighVolume        | HighVolume      |   316.1 ns |  8.53 ns |  25.14 ns |   308.5 ns | 1.78x slower |   0.21x | 0.0308 |     518 B |  3.10x more |
| NLog_HighVolume           | HighVolume      |   299.2 ns |  8.81 ns |  25.99 ns |   293.1 ns | 1.69x slower |   0.21x | 0.0449 |     759 B |  4.54x more |
| ZLogger_HighVolume        | HighVolume      |   210.9 ns |  5.72 ns |  16.85 ns |   204.1 ns | 1.19x slower |   0.14x | 0.0127 |     215 B |  1.29x more |
|                           |                 |            |          |           |            |              |         |        |           |             |
| Pragmatic_Production      | Production      |   686.5 ns | 18.96 ns |  55.91 ns |   701.6 ns |     baseline |         | 0.0467 |     784 B |             |
| Serilog_Production        | Production      | 1,321.0 ns | 42.21 ns | 124.45 ns | 1,286.4 ns | 1.94x slower |   0.24x | 0.1793 |    3000 B |  3.83x more |
| NLog_Production           | Production      | 1,231.6 ns | 38.30 ns | 112.33 ns | 1,198.8 ns | 1.81x slower |   0.22x | 0.1717 |    2872 B |  3.66x more |
| ZLogger_Production        | Production      |   694.1 ns | 19.44 ns |  57.33 ns |   693.1 ns | 1.02x slower |   0.12x | 0.0496 |     832 B |  1.06x more |
|                           |                 |            |          |           |            |              |         |        |           |             |
| Pragmatic_Simple          | Simple          |   187.2 ns |  5.34 ns |  15.76 ns |   188.3 ns |     baseline |         | 0.0100 |     168 B |             |
| Serilog_Simple            | Simple          |   347.1 ns |  8.95 ns |  26.40 ns |   344.8 ns | 1.87x slower |   0.21x | 0.0315 |     528 B |  3.14x more |
| NLog_Simple               | Simple          |   366.1 ns |  7.32 ns |  17.81 ns |   366.7 ns | 1.97x slower |   0.19x | 0.0453 |     760 B |  4.52x more |
| ZLogger_Simple            | Simple          |   241.4 ns | 11.86 ns |  34.98 ns |   235.9 ns | 1.30x slower |   0.22x | 0.0129 |     216 B |  1.29x more |
|                           |                 |            |          |           |            |              |         |        |           |             |
| Pragmatic_SourceGenerated | SourceGenerated |   151.5 ns |  3.00 ns |   7.53 ns |   149.3 ns |     baseline |         | 0.0076 |     128 B |             |
| Serilog_SourceGenerated   | SourceGenerated |   436.3 ns | 15.32 ns |  45.19 ns |   432.4 ns | 2.89x slower |   0.33x | 0.0477 |     800 B |  6.25x more |
| NLog_SourceGenerated      | SourceGenerated |   487.1 ns | 15.36 ns |  45.28 ns |   479.0 ns | 3.22x slower |   0.33x | 0.0844 |    1416 B | 11.06x more |
| ZLogger_SourceGenerated   | SourceGenerated |   201.0 ns |  7.92 ns |  23.35 ns |   202.3 ns | 1.33x slower |   0.17x | 0.0114 |     192 B |  1.50x more |
|                           |                 |            |          |           |            |              |         |        |           |             |
| Pragmatic_Structured      | Structured      |   560.2 ns | 21.97 ns |  64.79 ns |   552.0 ns |     baseline |         | 0.0277 |     464 B |             |
| Serilog_Structured        | Structured      | 1,126.3 ns | 27.89 ns |  81.34 ns | 1,137.7 ns | 2.04x slower |   0.27x | 0.1125 |    1912 B |  4.12x more |
| NLog_Structured           | Structured      |   714.3 ns | 11.14 ns |  10.42 ns |   713.6 ns | 1.29x slower |   0.15x | 0.0792 |    1328 B |  2.86x more |
| ZLogger_Structured        | Structured      |   443.8 ns |  7.87 ns |   6.98 ns |   441.6 ns | 1.26x faster |   0.15x | 0.0310 |     520 B |  1.12x more |
