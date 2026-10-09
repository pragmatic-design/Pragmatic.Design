```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method              | Categories | Mean     | Error    | StdDev   | Ratio        | RatioSD | Gen0   | Allocated | Alloc Ratio |
|-------------------- |----------- |---------:|---------:|---------:|-------------:|--------:|-------:|----------:|------------:|
| Classic_Exception   | Exception  | 234.1 ns |  4.66 ns |  8.87 ns |     baseline |         | 0.0362 |     608 B |             |
| Deferred_Exception  | Exception  | 100.4 ns |  2.02 ns |  5.19 ns | 2.34x faster |   0.14x | 0.0110 |     184 B |  3.30x less |
| ZLogger_Exception   | Exception  | 202.7 ns |  7.21 ns | 21.26 ns | 1.17x faster |   0.12x | 0.0138 |     232 B |  2.62x less |
|                     |            |          |          |          |              |         |        |           |             |
| Classic_Simple      | Simple     | 253.0 ns |  7.91 ns | 22.82 ns |     baseline |         | 0.0353 |     592 B |             |
| Deferred_Simple     | Simple     | 107.9 ns |  2.18 ns |  5.81 ns | 2.35x faster |   0.24x | 0.0100 |     168 B |  3.52x less |
| ZLogger_Simple      | Simple     | 174.4 ns |  3.47 ns |  7.48 ns | 1.45x faster |   0.14x | 0.0129 |     216 B |  2.74x less |
|                     |            |          |          |          |              |         |        |           |             |
| Classic_Structured  | Structured | 544.8 ns | 14.85 ns | 41.88 ns |     baseline |         | 0.0677 |    1144 B |             |
| Deferred_Structured | Structured | 376.5 ns |  7.43 ns | 12.81 ns | 1.45x faster |   0.12x | 0.0362 |     608 B |  1.88x less |
| ZLogger_Structured  | Structured | 418.4 ns |  8.38 ns | 12.80 ns | 1.30x faster |   0.11x | 0.0291 |     488 B |  2.34x less |
