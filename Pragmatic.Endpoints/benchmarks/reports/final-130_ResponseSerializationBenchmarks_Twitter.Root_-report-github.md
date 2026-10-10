```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                          | Mean     | Error   | StdDev  | Ratio | Gen0     | Gen1     | Gen2     | Allocated | Alloc Ratio |
|-------------------------------- |---------:|--------:|--------:|------:|---------:|---------:|---------:|----------:|------------:|
| Host_Reflection                 | 383.7 μs | 4.14 μs | 3.46 μs |  1.00 |  89.8438 |  89.8438 |  89.8438 | 422.05 KB |        1.00 |
| Host_GeneratedMetadata          | 381.0 μs | 2.94 μs | 2.46 μs |  0.99 |  90.3320 |  90.3320 |  90.3320 |  422.1 KB |        1.00 |
| Stj_FastPath                    | 433.3 μs | 3.66 μs | 3.42 μs |  1.13 | 112.3047 | 110.3516 | 110.3516 | 551.63 KB |        1.31 |
| REDox                           | 248.4 μs | 3.89 μs | 3.64 μs |  0.65 |  82.5195 |  82.5195 |  82.5195 | 418.97 KB |        0.99 |
| Generated_Writer                | 272.6 μs | 4.88 μs | 4.57 μs |  0.71 |  83.4961 |  83.4961 |  83.4961 | 419.23 KB |        0.99 |
| Generated_Writer_DefaultEncoder | 419.7 μs | 4.12 μs | 3.86 μs |  1.09 | 109.3750 | 109.3750 | 109.3750 | 520.73 KB |        1.23 |
