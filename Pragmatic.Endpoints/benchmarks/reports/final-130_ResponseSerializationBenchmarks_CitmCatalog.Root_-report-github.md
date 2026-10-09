```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                          | Mean     | Error   | StdDev  | Ratio | Gen0     | Gen1     | Gen2     | Allocated | Alloc Ratio |
|-------------------------------- |---------:|--------:|--------:|------:|---------:|---------:|---------:|----------:|------------:|
| Host_Reflection                 | 760.9 μs | 5.11 μs | 4.78 μs |  1.00 | 123.0469 | 123.0469 | 123.0469 | 470.72 KB |        1.00 |
| Host_GeneratedMetadata          | 738.8 μs | 2.21 μs | 1.96 μs |  0.97 | 123.0469 | 123.0469 | 123.0469 | 470.73 KB |        1.00 |
| Stj_FastPath                    | 343.6 μs | 4.56 μs | 4.27 μs |  0.45 | 123.0469 | 123.0469 | 123.0469 | 471.07 KB |        1.00 |
| REDox                           | 428.5 μs | 3.65 μs | 3.42 μs |  0.56 | 123.5352 | 123.5352 | 123.5352 | 470.18 KB |        1.00 |
| Generated_Writer                | 180.0 μs | 2.62 μs | 2.45 μs |  0.24 | 111.0840 | 111.0840 | 111.0840 | 468.81 KB |        1.00 |
| Generated_Writer_DefaultEncoder | 184.1 μs | 2.45 μs | 2.29 μs |  0.24 | 110.8398 | 110.8398 | 110.8398 | 469.59 KB |        1.00 |
