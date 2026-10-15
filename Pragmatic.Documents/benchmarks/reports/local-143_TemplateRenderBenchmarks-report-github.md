```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9711)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                     | Categories | Mean      | Error     | StdDev     | Median     | Ratio | RatioSD | Gen0      | Gen1     | Gen2     | Allocated | Alloc Ratio |
|--------------------------- |----------- |----------:|----------:|-----------:|-----------:|------:|--------:|----------:|---------:|---------:|----------:|------------:|
| Resolve_GeneratedAccessors | DataAccess |  5.075 ms | 0.3149 ms |  0.9236 ms |  4.8823 ms |  1.03 |    0.26 |  558.5938 | 468.7500 |        - |   8.97 MB |        1.00 |
| Resolve_Reflection         | DataAccess |  4.630 ms | 0.2101 ms |  0.6129 ms |  4.3414 ms |  0.94 |    0.20 |  531.2500 | 281.2500 |        - |   8.97 MB |        1.00 |
|                            |            |           |           |            |            |       |         |           |          |          |           |             |
| Read_GeneratedAccessors    | FieldReads |  1.087 ms | 0.0847 ms |  0.2496 ms |  0.9875 ms |  1.05 |    0.32 |  194.3359 |   2.9297 |        - |   3.11 MB |        1.00 |
| Read_Reflection            | FieldReads |  1.336 ms | 0.0447 ms |  0.1296 ms |  1.3277 ms |  1.29 |    0.28 |  187.5000 |        - |        - |   3.11 MB |        1.00 |
|                            |            |           |           |            |            |       |         |           |          |          |           |             |
| Pdf_TemplateToPdf          | Pdf        | 67.277 ms | 4.3758 ms | 12.9021 ms | 65.4748 ms |     ? |       ? | 1125.0000 | 875.0000 | 375.0000 |  14.35 MB |           ? |
