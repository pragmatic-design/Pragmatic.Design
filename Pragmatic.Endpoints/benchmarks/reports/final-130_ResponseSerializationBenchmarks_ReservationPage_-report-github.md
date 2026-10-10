```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                          | Mean      | Error     | StdDev    | Ratio | Gen0   | Allocated | Alloc Ratio |
|-------------------------------- |----------:|----------:|----------:|------:|-------:|----------:|------------:|
| Host_Reflection                 | 12.482 μs | 0.1433 μs | 0.1340 μs |  1.00 | 1.2207 |  20.01 KB |        1.00 |
| Host_GeneratedMetadata          | 11.872 μs | 0.0771 μs | 0.0721 μs |  0.95 | 1.2207 |  20.01 KB |        1.00 |
| Stj_FastPath                    | 10.505 μs | 0.0528 μs | 0.0494 μs |  0.84 | 1.2054 |  19.76 KB |        0.99 |
| REDox                           | 11.202 μs | 0.0569 μs | 0.0532 μs |  0.90 | 1.1902 |  19.63 KB |        0.98 |
| Generated_Writer                |  7.703 μs | 0.1318 μs | 0.1233 μs |  0.62 | 1.1902 |  19.59 KB |        0.98 |
| Generated_Writer_DefaultEncoder |  8.503 μs | 0.0829 μs | 0.0775 μs |  0.68 | 1.2054 |  19.76 KB |        0.99 |
