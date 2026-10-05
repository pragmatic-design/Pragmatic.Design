```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                 | Mean       | Error    | StdDev   | Ratio | RatioSD | Gen0     | Gen1     | Gen2     | Allocated | Alloc Ratio |
|----------------------- |-----------:|---------:|---------:|------:|--------:|---------:|---------:|---------:|----------:|------------:|
| Host_Reflection        | 1,093.9 μs | 21.72 μs | 38.03 μs |  1.00 |    0.05 | 125.0000 | 125.0000 | 125.0000 | 471.52 KB |        1.00 |
| Host_GeneratedMetadata | 1,056.9 μs | 21.09 μs | 26.68 μs |  0.97 |    0.04 | 125.0000 | 125.0000 | 125.0000 | 471.53 KB |        1.00 |
| Stj_FastPath           |   454.6 μs |  9.07 μs | 20.10 μs |  0.42 |    0.02 | 115.2344 | 115.2344 | 115.2344 | 471.01 KB |        1.00 |
| REDox                  |   538.8 μs | 10.75 μs | 25.34 μs |  0.49 |    0.03 | 121.5820 | 121.5820 | 121.5820 | 470.15 KB |        1.00 |
