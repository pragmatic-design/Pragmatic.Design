```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                         | Categories | Mean        | Error     | StdDev    | Median      | Ratio         | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------------------- |----------- |------------:|----------:|----------:|------------:|--------------:|--------:|-------:|----------:|------------:|
| Classic_Production             | Production | 1,065.18 ns | 20.591 ns | 43.433 ns | 1,061.44 ns |      baseline |         | 0.0858 |    1464 B |             |
| Classic_Production_ContextOnly | Production |   159.91 ns |  3.185 ns |  5.904 ns |   160.22 ns |  6.67x faster |   0.36x | 0.0491 |     824 B |  1.78x less |
| Classic_Production_NoContext   | Production |   684.48 ns | 13.699 ns | 27.673 ns |   686.47 ns |  1.56x faster |   0.09x | 0.0324 |     552 B |  2.65x less |
| ZLogger_Production             | Production |   732.98 ns | 14.638 ns | 41.999 ns |   731.65 ns |  1.46x faster |   0.10x | 0.0458 |     768 B |  1.91x less |
| ZLogger_Production_ContextOnly | Production |    66.30 ns |  1.376 ns |  3.993 ns |    66.47 ns | 16.12x faster |   1.19x | 0.0157 |     264 B |  5.55x less |
|                                |            |             |           |           |             |               |         |        |           |             |
| Classic_Structured             | Structured |   605.75 ns | 11.658 ns | 13.878 ns |   606.41 ns |      baseline |         | 0.0329 |     552 B |             |
| Deferred_Structured            | Structured |   546.00 ns | 10.575 ns |  9.892 ns |   547.44 ns |  1.11x faster |   0.03x | 0.0362 |     608 B |  1.10x more |
| ZLogger_Structured             | Structured |   624.83 ns | 12.498 ns | 26.904 ns |   624.56 ns |  1.03x slower |   0.05x | 0.0291 |     488 B |  1.13x less |
| Classic_Structured_ScopeOnly   | Structured |   109.55 ns |  2.195 ns |  4.122 ns |   110.00 ns |  5.54x faster |   0.24x | 0.0191 |     320 B |  1.73x less |
| Classic_Structured_CallOnly    | Structured |   319.27 ns |  6.094 ns |  5.700 ns |   316.71 ns |  1.90x faster |   0.05x | 0.0138 |     232 B |  2.38x less |
| ZLogger_Structured_ScopeOnly   | Structured |    66.35 ns |  4.411 ns | 13.007 ns |    62.70 ns |  9.48x faster |   1.84x | 0.0124 |     208 B |  2.65x less |
| ZLogger_Structured_CallOnly    | Structured |   398.77 ns |  6.024 ns |  5.635 ns |   399.19 ns |  1.52x faster |   0.04x | 0.0167 |     280 B |  1.97x less |
