```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                 | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------------------- |---------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| Host_Reflection        | 21.26 μs | 0.408 μs | 0.419 μs |  1.00 |    0.03 | 1.2207 |  20.17 KB |        1.00 |
| Host_GeneratedMetadata | 20.80 μs | 0.414 μs | 1.220 μs |  0.98 |    0.06 | 1.2207 |  20.17 KB |        1.00 |
| Stj_FastPath           | 17.06 μs | 0.340 μs | 0.753 μs |  0.80 |    0.04 | 1.2054 |  19.76 KB |        0.98 |
| REDox                  | 17.44 μs | 0.338 μs | 0.362 μs |  0.82 |    0.02 | 1.1902 |  19.63 KB |        0.97 |
