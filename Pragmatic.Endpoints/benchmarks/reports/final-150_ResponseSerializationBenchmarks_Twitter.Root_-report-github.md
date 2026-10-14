```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                          | Mean     | Error   | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|-------------------------------- |---------:|--------:|---------:|------:|--------:|-------:|----------:|------------:|
| Host_Reflection                 | 362.6 μs | 7.21 μs | 11.02 μs |  1.00 |    0.04 |      - |    2441 B |        1.00 |
| Host_GeneratedMetadata          | 345.8 μs | 6.11 μs |  4.77 μs |  0.95 |    0.03 |      - |    2440 B |        1.00 |
| Stj_FastPath                    | 398.3 μs | 7.22 μs |  7.09 μs |  1.10 |    0.04 | 1.4648 |   31201 B |       12.78 |
| REDox                           | 225.4 μs | 4.45 μs |  7.68 μs |  0.62 |    0.03 |      - |         - |        0.00 |
| Generated_Writer                | 250.2 μs | 3.87 μs |  3.23 μs |  0.69 |    0.02 |      - |         - |        0.00 |
| Generated_Writer_DefaultEncoder | 410.0 μs | 8.16 μs | 10.32 μs |  1.13 |    0.04 |      - |         - |        0.00 |
