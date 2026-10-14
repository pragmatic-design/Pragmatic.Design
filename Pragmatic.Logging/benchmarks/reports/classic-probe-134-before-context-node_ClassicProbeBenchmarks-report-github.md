```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

Categories=Production  

```
| Method                                       | Mean      | Error    | StdDev   | Median    | Gen0   | Allocated |
|--------------------------------------------- |----------:|---------:|---------:|----------:|-------:|----------:|
| Classic_Production_ContextOnly               |  60.90 ns | 1.940 ns | 5.691 ns |  59.15 ns | 0.0191 |     320 B |
| Classic_Production_ContextPropertyByProperty |  66.68 ns | 1.672 ns | 4.851 ns |  64.97 ns | 0.0200 |     336 B |
| Classic_Production_ContextAndScope           | 117.41 ns | 2.323 ns | 6.476 ns | 115.78 ns | 0.0329 |     552 B |
