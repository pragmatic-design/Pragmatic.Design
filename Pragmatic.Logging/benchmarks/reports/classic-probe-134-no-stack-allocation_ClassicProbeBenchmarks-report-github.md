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
| Classic_Production_ContextOnly               |  61.66 ns | 1.701 ns | 4.989 ns |  59.86 ns | 0.0210 |     352 B |
| Classic_Production_ContextPropertyByProperty |  69.24 ns | 1.835 ns | 5.353 ns |  67.97 ns | 0.0200 |     336 B |
| Classic_Production_ContextAndScope           | 126.15 ns | 2.824 ns | 8.282 ns | 124.72 ns | 0.0348 |     584 B |
