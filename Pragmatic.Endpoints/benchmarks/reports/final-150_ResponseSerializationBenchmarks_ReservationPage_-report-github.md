```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                          | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|-------------------------------- |----------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| Host_Reflection                 | 13.591 μs | 0.2681 μs | 0.3759 μs |  1.00 |    0.04 | 0.0153 |     424 B |        1.00 |
| Host_GeneratedMetadata          | 12.534 μs | 0.2057 μs | 0.2020 μs |  0.92 |    0.03 | 0.0153 |     424 B |        1.00 |
| Stj_FastPath                    | 10.737 μs | 0.2118 μs | 0.3105 μs |  0.79 |    0.03 |      - |         - |        0.00 |
| REDox                           | 11.967 μs | 0.2177 μs | 0.1930 μs |  0.88 |    0.03 |      - |      40 B |        0.09 |
| Generated_Writer                |  7.831 μs | 0.1460 μs | 0.2094 μs |  0.58 |    0.02 |      - |         - |        0.00 |
| Generated_Writer_DefaultEncoder |  8.694 μs | 0.1704 μs | 0.2155 μs |  0.64 |    0.02 |      - |         - |        0.00 |
