```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9711)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                 | Mean        | Error     | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|----------------------- |------------:|----------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| Search_ValueAsConstant | 1,223.13 μs | 24.393 μs | 33.389 μs |  1.00 |    0.04 | 5.8594 |      - |  112.4 KB |        1.00 |
| Search_PrimeNGAdapter  |    38.31 μs |  1.694 μs |  4.995 μs |  0.03 |    0.00 | 2.9297 | 0.2441 |  47.87 KB |        0.43 |
