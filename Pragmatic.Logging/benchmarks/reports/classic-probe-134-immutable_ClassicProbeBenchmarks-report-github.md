```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9711)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

Categories=Production  

```
| Method                                       | Mean      | Error     | StdDev    | Median    | Ratio         | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------------------------------------- |----------:|----------:|----------:|----------:|--------------:|--------:|-------:|----------:|------------:|
| Classic_Production                           | 551.05 ns | 12.504 ns | 35.267 ns | 548.43 ns |      baseline |         | 0.0429 |     720 B |             |
| Classic_Production_ContextOnly               |  39.17 ns |  1.058 ns |  3.019 ns |  38.65 ns | 14.15x faster |   1.37x | 0.0162 |     272 B |  2.65x less |
| Classic_Production_ContextPropertyByProperty |  57.57 ns |  1.175 ns |  3.033 ns |  56.78 ns |  9.60x faster |   0.78x | 0.0186 |     312 B |  2.31x less |
| Classic_Production_NoContext                 | 424.17 ns |  8.244 ns | 21.572 ns | 417.63 ns |  1.30x faster |   0.10x | 0.0257 |     432 B |  1.67x less |
| Classic_Production_ContextAndScope           |  91.06 ns |  2.690 ns |  7.761 ns |  89.36 ns |  6.09x faster |   0.63x | 0.0291 |     488 B |  1.48x less |
| Classic_Production_ScopeOnly                 |  43.66 ns |  1.258 ns |  3.588 ns |  42.63 ns | 12.70x faster |   1.26x | 0.0119 |     200 B |  3.60x less |
| ZLogger_Production_ContextAndScope           |  76.34 ns |  1.803 ns |  5.087 ns |  75.27 ns |  7.25x faster |   0.66x | 0.0291 |     488 B |  1.48x less |
| ZLogger_Production                           | 567.06 ns | 12.628 ns | 36.029 ns | 557.47 ns |  1.03x slower |   0.09x | 0.0458 |     768 B |  1.07x more |
| ZLogger_Production_ContextOnly               |  52.08 ns |  1.096 ns |  3.092 ns |  51.61 ns | 10.62x faster |   0.91x | 0.0157 |     264 B |  2.73x less |
