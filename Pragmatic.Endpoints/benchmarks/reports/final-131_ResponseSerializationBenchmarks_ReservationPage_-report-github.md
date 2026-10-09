```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                          | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|-------------------------------- |----------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| Host_Reflection                 | 15.822 μs | 0.2757 μs | 0.2302 μs |  1.00 |    0.02 | 1.2207 |  20.01 KB |        1.00 |
| Host_GeneratedMetadata          | 15.126 μs | 0.2714 μs | 0.2406 μs |  0.96 |    0.02 | 1.2207 |  20.01 KB |        1.00 |
| Stj_FastPath                    | 12.965 μs | 0.1960 μs | 0.1637 μs |  0.82 |    0.02 | 1.2054 |  19.76 KB |        0.99 |
| REDox                           | 13.776 μs | 0.2032 μs | 0.1697 μs |  0.87 |    0.02 | 1.1902 |  19.63 KB |        0.98 |
| Generated_Writer                |  9.436 μs | 0.1718 μs | 0.1523 μs |  0.60 |    0.01 | 1.1902 |  19.59 KB |        0.98 |
| Generated_Writer_DefaultEncoder | 10.568 μs | 0.1858 μs | 0.1552 μs |  0.67 |    0.01 | 1.2054 |  19.76 KB |        0.99 |
