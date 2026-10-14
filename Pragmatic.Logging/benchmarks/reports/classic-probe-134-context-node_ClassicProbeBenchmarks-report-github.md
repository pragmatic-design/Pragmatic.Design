```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

Categories=Production  

```
| Method                                       | Mean      | Error     | StdDev     | Median    | Ratio         | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------------------------------------- |----------:|----------:|-----------:|----------:|--------------:|--------:|-------:|----------:|------------:|
| Classic_Production                           | 604.99 ns | 12.780 ns |  36.463 ns | 599.19 ns |      baseline |         | 0.0458 |     776 B |             |
| Classic_Production_ContextOnly               |  63.47 ns |  2.491 ns |   7.344 ns |  61.46 ns |  9.65x faster |   1.18x | 0.0196 |     328 B |  2.37x less |
| Classic_Production_ContextPropertyByProperty |  70.11 ns |  2.552 ns |   7.524 ns |  69.04 ns |  8.72x faster |   1.04x | 0.0186 |     312 B |  2.49x less |
| Classic_Production_NoContext                 | 582.97 ns | 34.631 ns | 102.111 ns | 568.25 ns |  1.07x faster |   0.20x | 0.0257 |     432 B |  1.80x less |
| Classic_Production_ContextAndScope           | 120.11 ns |  2.428 ns |   4.441 ns | 119.20 ns |  5.04x faster |   0.35x | 0.0324 |     544 B |  1.43x less |
| Classic_Production_ScopeOnly                 |  46.93 ns |  0.961 ns |   2.089 ns |  46.46 ns | 12.91x faster |   0.95x | 0.0119 |     200 B |  3.88x less |
| ZLogger_Production_ContextAndScope           |  87.30 ns |  1.942 ns |   5.541 ns |  86.04 ns |  6.96x faster |   0.59x | 0.0291 |     488 B |  1.59x less |
| ZLogger_Production                           | 588.24 ns | 11.685 ns |  24.902 ns | 582.10 ns |  1.03x faster |   0.07x | 0.0458 |     768 B |  1.01x less |
| ZLogger_Production_ContextOnly               |  42.46 ns |  0.860 ns |   1.573 ns |  42.10 ns | 14.27x faster |   1.00x | 0.0157 |     264 B |  2.94x less |
