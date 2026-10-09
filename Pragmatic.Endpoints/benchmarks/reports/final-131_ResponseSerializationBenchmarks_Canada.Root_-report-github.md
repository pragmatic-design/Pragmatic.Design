```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                          | Mean     | Error     | StdDev    | Ratio | RatioSD | Gen0     | Gen1     | Gen2     | Allocated | Alloc Ratio |
|-------------------------------- |---------:|----------:|----------:|------:|--------:|---------:|---------:|---------:|----------:|------------:|
| Host_Reflection                 | 9.603 ms | 0.1917 ms | 0.2750 ms |  1.00 |    0.04 | 109.3750 | 109.3750 | 109.3750 |      2 MB |        1.00 |
| Host_GeneratedMetadata          | 9.382 ms | 0.1874 ms | 0.2566 ms |  0.98 |    0.04 | 109.3750 | 109.3750 | 109.3750 |      2 MB |        1.00 |
| Stj_FastPath                    | 8.835 ms | 0.1731 ms | 0.2939 ms |  0.92 |    0.04 | 226.5625 | 226.5625 | 226.5625 |   1.99 MB |        1.00 |
| REDox                           | 9.188 ms | 0.1828 ms | 0.2562 ms |  0.96 |    0.04 | 265.6250 | 265.6250 | 265.6250 |   1.99 MB |        1.00 |
| Generated_Writer                | 7.919 ms | 0.1551 ms | 0.2369 ms |  0.83 |    0.03 | 226.5625 | 226.5625 | 226.5625 |   1.99 MB |        1.00 |
| Generated_Writer_DefaultEncoder | 7.957 ms | 0.1560 ms | 0.1916 ms |  0.83 |    0.03 | 226.5625 | 226.5625 | 226.5625 |   1.99 MB |        1.00 |
