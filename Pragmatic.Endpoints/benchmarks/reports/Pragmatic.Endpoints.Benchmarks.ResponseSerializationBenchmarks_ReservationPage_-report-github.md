```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                          | Mean     | Error    | StdDev   | Median   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|-------------------------------- |---------:|---------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| Host_Reflection                 | 14.86 μs | 0.294 μs | 0.675 μs | 14.72 μs |  1.00 |    0.06 | 1.2207 |  20.01 KB |        1.00 |
| Host_GeneratedMetadata          | 13.72 μs | 0.273 μs | 0.599 μs | 13.61 μs |  0.93 |    0.06 | 1.2207 |  20.01 KB |        1.00 |
| Stj_FastPath                    | 13.12 μs | 0.563 μs | 1.659 μs | 12.53 μs |  0.88 |    0.12 | 1.2054 |  19.76 KB |        0.99 |
| REDox                           | 16.26 μs | 0.464 μs | 1.369 μs | 16.31 μs |  1.10 |    0.10 | 1.1902 |  19.63 KB |        0.98 |
| Generated_Writer                | 14.05 μs | 0.845 μs | 2.492 μs | 13.95 μs |  0.95 |    0.17 | 1.1902 |  19.59 KB |        0.98 |
| Generated_Writer_DefaultEncoder | 19.04 μs | 0.331 μs | 0.310 μs | 19.00 μs |  1.28 |    0.06 | 1.1902 |  19.76 KB |        0.99 |
