```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9711)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                     | Categories | Mean         | Error       | StdDev      | Ratio | RatioSD | Gen0       | Gen1      | Gen2      | Allocated | Alloc Ratio |
|--------------------------- |----------- |-------------:|------------:|------------:|------:|--------:|-----------:|----------:|----------:|----------:|------------:|
| Resolve_GeneratedAccessors | DataAccess |   3,926.1 μs |    68.01 μs |    99.69 μs |  1.00 |    0.03 |   531.2500 |  281.2500 |         - |   8.97 MB |        1.00 |
| Resolve_Reflection         | DataAccess |   3,941.3 μs |    51.25 μs |    45.43 μs |  1.00 |    0.03 |   531.2500 |  281.2500 |         - |   8.97 MB |        1.00 |
|                            |            |              |             |             |       |         |            |           |           |           |             |
| Read_GeneratedAccessors    | FieldReads |     808.9 μs |     8.62 μs |     7.64 μs |  1.00 |    0.01 |   194.3359 |    2.9297 |         - |   3.11 MB |        1.00 |
| Read_Reflection            | FieldReads |     924.9 μs |    18.29 μs |    34.35 μs |  1.14 |    0.04 |   194.3359 |    2.9297 |         - |   3.11 MB |        1.00 |
|                            |            |              |             |             |       |         |            |           |           |           |             |
| Pdf_TemplateToPdf          | Pdf        |  41,344.6 μs |   732.47 μs |   871.96 μs |  1.00 |    0.03 |  1000.0000 |  750.0000 |  250.0000 |  14.35 MB |        1.00 |
| Pdf_MigraDoc               | Pdf        | 216,592.1 μs | 4,206.40 μs | 7,367.17 μs |  5.24 |    0.21 | 10000.0000 | 5000.0000 | 1000.0000 | 166.14 MB |       11.58 |
