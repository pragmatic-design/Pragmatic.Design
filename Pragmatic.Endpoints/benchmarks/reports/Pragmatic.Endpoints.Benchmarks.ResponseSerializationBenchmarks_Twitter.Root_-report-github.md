```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                          | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0     | Gen1     | Gen2     | Allocated | Alloc Ratio |
|-------------------------------- |---------:|---------:|---------:|------:|--------:|---------:|---------:|---------:|----------:|------------:|
| Host_Reflection                 | 561.3 μs | 11.10 μs | 28.86 μs |  1.00 |    0.07 |  86.9141 |  86.9141 |  86.9141 | 421.99 KB |        1.00 |
| Host_GeneratedMetadata          | 540.9 μs | 10.75 μs | 23.38 μs |  0.97 |    0.06 |  89.8438 |  89.8438 |  89.8438 | 422.01 KB |        1.00 |
| Stj_FastPath                    | 598.8 μs | 11.93 μs | 24.90 μs |  1.07 |    0.07 | 105.9570 | 104.0039 | 104.0039 | 551.58 KB |        1.31 |
| REDox                           | 330.5 μs |  7.33 μs | 21.60 μs |  0.59 |    0.05 |  82.5195 |  82.5195 |  82.5195 | 418.97 KB |        0.99 |
| Generated_Writer                |       NA |       NA |       NA |     ? |       ? |       NA |       NA |       NA |        NA |           ? |
| Generated_Writer_DefaultEncoder |       NA |       NA |       NA |     ? |       ? |       NA |       NA |       NA |        NA |           ? |

Benchmarks with issues:
  ResponseSerializationBenchmarks<Root>.Generated_Writer: DefaultJob
  ResponseSerializationBenchmarks<Root>.Generated_Writer_DefaultEncoder: DefaultJob
