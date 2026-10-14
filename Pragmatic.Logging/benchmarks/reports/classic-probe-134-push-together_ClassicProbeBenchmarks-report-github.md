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
| Classic_Production                           | 755.23 ns | 14.579 ns | 14.318 ns | 754.58 ns |      baseline |         | 0.0467 |     784 B |             |
| Classic_Production_ContextOnly               |  77.95 ns |  2.130 ns |  6.248 ns |  77.66 ns |  9.75x faster |   0.83x | 0.0191 |     320 B |  2.45x less |
| Classic_Production_ContextPropertyByProperty |  85.75 ns |  3.619 ns | 10.670 ns |  86.63 ns |  8.95x faster |   1.22x | 0.0200 |     336 B |  2.33x less |
| Classic_Production_NoContext                 | 542.82 ns | 10.846 ns | 28.573 ns | 545.03 ns |  1.40x faster |   0.08x | 0.0257 |     432 B |  1.81x less |
| Classic_Production_ContextAndScope           | 143.00 ns |  7.673 ns | 22.623 ns | 146.16 ns |  5.42x faster |   0.92x | 0.0329 |     552 B |  1.42x less |
| Classic_Production_ScopeOnly                 |  44.82 ns |  1.397 ns |  4.032 ns |  43.46 ns | 16.97x faster |   1.42x | 0.0119 |     200 B |  3.92x less |
| ZLogger_Production_ContextAndScope           |  77.51 ns |  1.594 ns |  4.470 ns |  75.92 ns |  9.77x faster |   0.56x | 0.0291 |     488 B |  1.61x less |
| ZLogger_Production                           | 562.00 ns | 11.191 ns | 30.635 ns | 553.30 ns |  1.35x faster |   0.07x | 0.0458 |     768 B |  1.02x less |
| ZLogger_Production_ContextOnly               |  38.61 ns |  0.796 ns |  2.246 ns |  37.97 ns | 19.62x faster |   1.14x | 0.0157 |     264 B |  2.97x less |
