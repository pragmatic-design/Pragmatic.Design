```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9711)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                        | Mean         | Error       | StdDev      | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------------------------ |-------------:|------------:|------------:|------:|--------:|-------:|-------:|----------:|------------:|
| OptionsSnapshot               |   561.579 ns |  11.2660 ns |  31.2181 ns |  1.00 |    0.08 | 0.1116 |      - |    1872 B |        1.00 |
| OptionsMonitor                |     7.971 ns |   0.1924 ns |   0.3614 ns |  0.01 |    0.00 |      - |      - |         - |        0.00 |
| TenantOptions_GeneratedBinder | 1,073.586 ns |  21.4776 ns |  35.2883 ns |  1.92 |    0.12 | 0.1631 |      - |    2728 B |        1.46 |
| TenantOptions_Reflection      | 9,501.663 ns | 314.5479 ns | 907.5425 ns | 16.97 |    1.86 | 0.6409 | 0.2136 |   10889 B |        5.82 |
