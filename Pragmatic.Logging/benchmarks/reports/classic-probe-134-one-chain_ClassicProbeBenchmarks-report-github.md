```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

Categories=Production  

```
| Method                                       | Mean      | Error     | StdDev    | Median    | Ratio         | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------------------------------------- |----------:|----------:|----------:|----------:|--------------:|--------:|-------:|----------:|------------:|
| Classic_Production                           | 704.25 ns | 18.936 ns | 55.834 ns | 721.06 ns |      baseline |         | 0.0477 |     808 B |             |
| Classic_Production_ContextOnly               |  68.38 ns |  2.678 ns |  7.896 ns |  71.71 ns | 10.45x faster |   1.55x | 0.0215 |     360 B |  2.24x less |
| Classic_Production_ContextPropertyByProperty |  78.08 ns |  2.828 ns |  8.338 ns |  80.25 ns |  9.13x faster |   1.31x | 0.0205 |     344 B |  2.35x less |
| Classic_Production_NoContext                 | 512.26 ns | 14.997 ns | 44.220 ns | 531.67 ns |  1.39x faster |   0.17x | 0.0257 |     432 B |  1.87x less |
| Classic_Production_ContextAndScope           | 160.60 ns |  9.125 ns | 26.906 ns | 159.05 ns |  4.51x faster |   0.87x | 0.0343 |     576 B |  1.40x less |
| Classic_Production_ScopeOnly                 |  60.97 ns |  2.888 ns |  8.514 ns |  59.96 ns | 11.77x faster |   1.83x | 0.0119 |     200 B |  4.04x less |
| ZLogger_Production_ContextAndScope           | 108.38 ns |  3.860 ns | 11.319 ns | 109.90 ns |  6.58x faster |   0.93x | 0.0291 |     488 B |  1.66x less |
| ZLogger_Production                           | 720.47 ns | 17.234 ns | 50.816 ns | 723.94 ns |  1.03x slower |   0.12x | 0.0458 |     768 B |  1.05x less |
| ZLogger_Production_ContextOnly               |  56.50 ns |  2.758 ns |  8.132 ns |  57.36 ns | 12.75x faster |   2.28x | 0.0157 |     264 B |  3.06x less |
