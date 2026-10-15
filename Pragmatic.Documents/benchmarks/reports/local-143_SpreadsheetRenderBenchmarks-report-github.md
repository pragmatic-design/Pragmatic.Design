```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9711)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method         | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0     | Gen1     | Gen2     | Allocated | Alloc Ratio |
|--------------- |----------:|----------:|----------:|------:|--------:|---------:|---------:|---------:|----------:|------------:|
| Xlsx_Pragmatic |  2.754 ms | 0.0540 ms | 0.0887 ms |  1.00 |    0.04 | 156.2500 |  78.1250 |        - |   2.76 MB |        1.00 |
| Xlsx_ClosedXml | 16.515 ms | 0.3298 ms | 0.5036 ms |  6.00 |    0.26 | 687.5000 | 375.0000 | 125.0000 |  11.08 MB |        4.02 |
