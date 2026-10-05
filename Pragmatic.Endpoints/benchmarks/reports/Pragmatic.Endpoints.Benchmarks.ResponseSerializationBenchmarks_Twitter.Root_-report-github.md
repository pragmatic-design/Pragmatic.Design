```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                 | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0     | Gen1     | Gen2     | Allocated | Alloc Ratio |
|----------------------- |---------:|---------:|---------:|------:|--------:|---------:|---------:|---------:|----------:|------------:|
| Host_Reflection        | 868.6 μs | 17.24 μs | 31.52 μs |  1.00 |    0.05 | 102.5391 | 102.5391 | 102.5391 |  523.4 KB |        1.00 |
| Host_GeneratedMetadata | 887.5 μs | 17.68 μs | 44.68 μs |  1.02 |    0.06 |  97.6563 |  97.6563 |  97.6563 | 523.36 KB |        1.00 |
| Stj_FastPath           | 671.6 μs | 13.40 μs | 23.47 μs |  0.77 |    0.04 |  94.7266 |  92.7734 |  92.7734 | 551.54 KB |        1.05 |
| REDox                  | 354.3 μs |  6.97 μs | 13.77 μs |  0.41 |    0.02 |  73.7305 |  73.7305 |  73.7305 | 418.96 KB |        0.80 |
