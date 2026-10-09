```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                         | Categories | Mean      | Error     | StdDev    | Median    | Ratio         | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------------------- |----------- |----------:|----------:|----------:|----------:|--------------:|--------:|-------:|----------:|------------:|
| Classic_Production             | Production | 706.35 ns | 23.806 ns | 70.192 ns | 706.73 ns |      baseline |         | 0.0477 |     800 B |             |
| Classic_Production_ContextOnly | Production |  70.58 ns |  2.377 ns |  7.010 ns |  70.68 ns | 10.11x faster |   1.45x | 0.0200 |     336 B |  2.38x less |
| Classic_Production_NoContext   | Production | 491.40 ns | 14.843 ns | 43.764 ns | 485.52 ns |  1.45x faster |   0.19x | 0.0257 |     432 B |  1.85x less |
| ZLogger_Production             | Production | 648.28 ns | 22.762 ns | 67.115 ns | 645.68 ns |  1.10x faster |   0.16x | 0.0458 |     768 B |  1.04x less |
| ZLogger_Production_ContextOnly | Production |  48.52 ns |  2.106 ns |  6.211 ns |  48.07 ns | 14.80x faster |   2.43x | 0.0157 |     264 B |  3.03x less |
|                                |            |           |           |           |           |               |         |        |           |             |
| Classic_Structured             | Structured | 472.61 ns | 15.670 ns | 46.202 ns | 476.50 ns |      baseline |         | 0.0257 |     432 B |             |
| Deferred_Structured            | Structured | 413.27 ns | 13.496 ns | 39.794 ns | 414.69 ns |  1.15x faster |   0.16x | 0.0291 |     488 B |  1.13x more |
| ZLogger_Structured             | Structured | 517.90 ns | 14.449 ns | 42.604 ns | 522.57 ns |  1.11x slower |   0.14x | 0.0291 |     488 B |  1.13x more |
| Classic_Structured_ScopeOnly   | Structured |  59.29 ns |  2.034 ns |  5.997 ns |  60.99 ns |  8.06x faster |   1.19x | 0.0119 |     200 B |  2.16x less |
| Classic_Structured_CallOnly    | Structured | 270.86 ns |  5.320 ns |  8.124 ns | 272.35 ns |  1.75x faster |   0.18x | 0.0138 |     232 B |  1.86x less |
| ZLogger_Structured_ScopeOnly   | Structured |  50.73 ns |  1.164 ns |  3.433 ns |  51.15 ns |  9.36x faster |   1.12x | 0.0124 |     208 B |  2.08x less |
| ZLogger_Structured_CallOnly    | Structured | 319.50 ns |  6.310 ns |  9.445 ns | 319.38 ns |  1.48x faster |   0.15x | 0.0167 |     280 B |  1.54x less |
