```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                          | Mean     | Error    | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|-------------------------------- |---------:|---------:|---------:|------:|--------:|----------:|------------:|
| Host_Reflection                 | 718.2 μs | 14.32 μs | 18.62 μs |  1.00 |    0.04 |    1113 B |        1.00 |
| Host_GeneratedMetadata          | 722.1 μs | 14.11 μs | 25.09 μs |  1.01 |    0.04 |    1113 B |        1.00 |
| Stj_FastPath                    | 286.6 μs |  5.64 μs | 10.46 μs |  0.40 |    0.02 |     625 B |        0.56 |
| REDox                           | 410.2 μs |  8.05 μs | 14.71 μs |  0.57 |    0.02 |     449 B |        0.40 |
| Generated_Writer                | 164.1 μs |  3.26 μs |  4.00 μs |  0.23 |    0.01 |         - |        0.00 |
| Generated_Writer_DefaultEncoder | 165.6 μs |  3.20 μs |  3.68 μs |  0.23 |    0.01 |         - |        0.00 |
