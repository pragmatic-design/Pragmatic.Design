```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                          | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0     | Gen1     | Gen2     | Allocated | Alloc Ratio |
|-------------------------------- |----------:|----------:|----------:|------:|--------:|---------:|---------:|---------:|----------:|------------:|
| Host_Reflection                 | 10.211 ms | 0.2882 ms | 0.8498 ms |  1.01 |    0.12 | 250.0000 | 250.0000 | 250.0000 |      2 MB |        1.00 |
| Host_GeneratedMetadata          | 10.130 ms | 0.1988 ms | 0.5641 ms |  1.00 |    0.10 | 218.7500 | 218.7500 | 218.7500 |      2 MB |        1.00 |
| Stj_FastPath                    |  8.868 ms | 0.1770 ms | 0.4341 ms |  0.87 |    0.08 | 265.6250 | 265.6250 | 265.6250 |   1.99 MB |        1.00 |
| REDox                           |  8.923 ms | 0.1758 ms | 0.3551 ms |  0.88 |    0.08 | 265.6250 | 265.6250 | 265.6250 |   1.99 MB |        1.00 |
| Generated_Writer                |  8.355 ms | 0.1642 ms | 0.3535 ms |  0.82 |    0.07 | 328.1250 | 328.1250 | 328.1250 |   1.99 MB |        1.00 |
| Generated_Writer_DefaultEncoder |  8.316 ms | 0.1625 ms | 0.3498 ms |  0.82 |    0.07 | 328.1250 | 328.1250 | 328.1250 |   1.99 MB |        1.00 |
