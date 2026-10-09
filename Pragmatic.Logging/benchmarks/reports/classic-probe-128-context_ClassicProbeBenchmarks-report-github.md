```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                         | Categories | Mean      | Error     | StdDev    | Median    | Ratio         | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------------------- |----------- |----------:|----------:|----------:|----------:|--------------:|--------:|-------:|----------:|------------:|
| Classic_Production             | Production | 893.08 ns | 17.732 ns | 22.425 ns | 896.45 ns |      baseline |         | 0.0582 |     976 B |             |
| Classic_Production_ContextOnly | Production |  90.35 ns |  1.833 ns |  5.140 ns |  90.83 ns |  9.92x faster |   0.63x | 0.0224 |     376 B |  2.60x less |
| Classic_Production_NoContext   | Production | 656.99 ns | 13.110 ns | 23.972 ns | 654.61 ns |  1.36x faster |   0.06x | 0.0334 |     568 B |  1.72x less |
| ZLogger_Production             | Production | 716.25 ns | 14.308 ns | 33.162 ns | 721.02 ns |  1.25x faster |   0.07x | 0.0458 |     768 B |  1.27x less |
| ZLogger_Production_ContextOnly | Production |  56.40 ns |  1.149 ns |  2.708 ns |  56.42 ns | 15.87x faster |   0.85x | 0.0157 |     264 B |  3.70x less |
|                                |            |           |           |           |           |               |         |        |           |             |
| Classic_Structured             | Structured | 585.01 ns | 11.578 ns | 19.344 ns | 589.57 ns |      baseline |         | 0.0339 |     568 B |             |
| Deferred_Structured            | Structured | 490.05 ns |  9.785 ns | 19.988 ns | 490.67 ns |  1.20x faster |   0.06x | 0.0372 |     624 B |  1.10x more |
| ZLogger_Structured             | Structured | 565.97 ns | 11.289 ns | 25.017 ns | 570.86 ns |  1.04x faster |   0.06x | 0.0291 |     488 B |  1.16x less |
| Classic_Structured_ScopeOnly   | Structured |  93.39 ns |  3.431 ns | 10.115 ns |  96.81 ns |  6.35x faster |   0.79x | 0.0200 |     336 B |  1.69x less |
| Classic_Structured_CallOnly    | Structured | 245.56 ns |  8.230 ns | 24.267 ns | 246.17 ns |  2.41x faster |   0.25x | 0.0138 |     232 B |  2.45x less |
| ZLogger_Structured_ScopeOnly   | Structured |  41.71 ns |  1.921 ns |  5.664 ns |  40.63 ns | 14.28x faster |   1.93x | 0.0124 |     208 B |  2.73x less |
| ZLogger_Structured_CallOnly    | Structured | 297.53 ns |  7.306 ns | 21.542 ns | 297.63 ns |  1.98x faster |   0.17x | 0.0167 |     280 B |  2.03x less |
