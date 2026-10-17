```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9711)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  Job-DDIHFF : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

LaunchCount=3  

```
| Method                          | Mean     | Error   | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|-------------------------------- |---------:|--------:|---------:|------:|--------:|-------:|----------:|------------:|
| Host_Reflection                 | 380.0 μs | 3.90 μs | 10.28 μs |  1.00 |    0.04 |      - |    2441 B |        1.00 |
| Host_GeneratedMetadata          | 373.2 μs | 3.74 μs |  7.38 μs |  0.98 |    0.03 |      - |    2440 B |        1.00 |
| Stj_FastPath                    | 418.9 μs | 4.18 μs |  8.14 μs |  1.10 |    0.04 | 1.4648 |   31201 B |       12.78 |
| REDox                           | 237.9 μs | 2.28 μs |  4.40 μs |  0.63 |    0.02 |      - |         - |        0.00 |
| Generated_Writer                | 217.8 μs | 2.37 μs |  5.04 μs |  0.57 |    0.02 |      - |         - |        0.00 |
| Generated_Writer_DefaultEncoder | 217.1 μs | 2.11 μs |  4.40 μs |  0.57 |    0.02 |      - |         - |        0.00 |
