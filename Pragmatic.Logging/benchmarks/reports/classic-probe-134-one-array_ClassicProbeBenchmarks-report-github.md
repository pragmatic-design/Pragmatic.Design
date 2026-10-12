```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

Categories=Production  

```
| Method                             | Mean      | Error     | StdDev    | Ratio         | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------------------------------- |----------:|----------:|----------:|--------------:|--------:|-------:|----------:|------------:|
| Classic_Production                 | 561.25 ns | 11.176 ns | 25.226 ns |      baseline |         | 0.0477 |     800 B |             |
| Classic_Production_ContextOnly     |  54.31 ns |  1.109 ns |  2.679 ns | 10.36x faster |   0.67x | 0.0200 |     336 B |  2.38x less |
| Classic_Production_NoContext       | 411.47 ns |  8.106 ns | 14.408 ns |  1.37x faster |   0.08x | 0.0257 |     432 B |  1.85x less |
| Classic_Production_ContextAndScope | 105.69 ns |  2.127 ns |  4.669 ns |  5.32x faster |   0.33x | 0.0339 |     568 B |  1.41x less |
| Classic_Production_ScopeOnly       |  40.90 ns |  0.805 ns |  1.767 ns | 13.75x faster |   0.85x | 0.0119 |     200 B |  4.00x less |
| ZLogger_Production_ContextAndScope |  72.47 ns |  1.441 ns |  3.796 ns |  7.77x faster |   0.52x | 0.0291 |     488 B |  1.64x less |
| ZLogger_Production                 | 549.36 ns | 10.768 ns | 30.372 ns |  1.02x faster |   0.07x | 0.0458 |     768 B |  1.04x less |
| ZLogger_Production_ContextOnly     |  47.96 ns |  0.983 ns |  2.317 ns | 11.73x faster |   0.77x | 0.0157 |     264 B |  3.03x less |
