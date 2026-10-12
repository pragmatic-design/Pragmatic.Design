```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

Categories=Production  

```
| Method                                   | Mean      | Error     | StdDev    | Median    | Ratio         | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------------------------------------- |----------:|----------:|----------:|----------:|--------------:|--------:|-------:|----------:|------------:|
| Classic_Production                       | 530.39 ns | 10.081 ns | 18.179 ns | 526.02 ns |      baseline |         | 0.0477 |     800 B |             |
| Classic_Production_ContextOnly           |  53.55 ns |  1.068 ns |  2.579 ns |  53.26 ns |  9.93x faster |   0.57x | 0.0200 |     336 B |  2.38x less |
| Classic_Production_ContextFromProperties |  43.71 ns |  0.883 ns |  1.939 ns |  43.24 ns | 12.16x faster |   0.67x | 0.0157 |     264 B |  3.03x less |
| Classic_Production_NoContext             | 434.29 ns | 11.285 ns | 33.274 ns | 431.67 ns |  1.23x faster |   0.10x | 0.0257 |     432 B |  1.85x less |
| Classic_Production_ContextAndScope       | 109.69 ns |  3.963 ns | 11.496 ns | 106.35 ns |  4.88x faster |   0.50x | 0.0339 |     568 B |  1.41x less |
| Classic_Production_ScopeOnly             |  45.80 ns |  1.874 ns |  5.408 ns |  44.45 ns | 11.73x faster |   1.36x | 0.0119 |     200 B |  4.00x less |
| ZLogger_Production_ContextAndScope       | 101.96 ns |  5.321 ns | 15.688 ns | 105.66 ns |  5.34x faster |   0.94x | 0.0291 |     488 B |  1.64x less |
| ZLogger_Production                       | 579.23 ns | 18.145 ns | 52.929 ns | 571.65 ns |  1.09x slower |   0.11x | 0.0458 |     768 B |  1.04x less |
| ZLogger_Production_ContextOnly           |  57.65 ns |  4.124 ns | 12.160 ns |  59.48 ns |  9.67x faster |   2.30x | 0.0157 |     264 B |  3.03x less |
