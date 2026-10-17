```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9711)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  Job-DDIHFF : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

LaunchCount=3  

```
| Method                          | Mean     | Error    | StdDev   | Median   | Ratio | RatioSD | Allocated | Alloc Ratio |
|-------------------------------- |---------:|---------:|---------:|---------:|------:|--------:|----------:|------------:|
| Host_Reflection                 | 777.4 μs |  8.81 μs | 15.66 μs | 776.7 μs |  1.00 |    0.03 |    1113 B |        1.00 |
| Host_GeneratedMetadata          | 788.3 μs | 32.26 μs | 58.99 μs | 761.1 μs |  1.01 |    0.08 |    1113 B |        1.00 |
| Stj_FastPath                    | 304.4 μs |  3.42 μs | 11.15 μs | 302.1 μs |  0.39 |    0.02 |     625 B |        0.56 |
| REDox                           | 426.3 μs |  5.06 μs | 12.87 μs | 423.2 μs |  0.55 |    0.02 |     449 B |        0.40 |
| Generated_Writer                | 169.7 μs |  1.38 μs |  2.42 μs | 169.5 μs |  0.22 |    0.01 |         - |        0.00 |
| Generated_Writer_DefaultEncoder | 170.0 μs |  1.90 μs |  4.30 μs | 169.3 μs |  0.22 |    0.01 |         - |        0.00 |
