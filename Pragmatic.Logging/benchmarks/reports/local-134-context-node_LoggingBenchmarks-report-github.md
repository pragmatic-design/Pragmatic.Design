```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

Categories=Production  

```
| Method               | Mean       | Error    | StdDev   | Ratio        | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------------- |-----------:|---------:|---------:|-------------:|--------:|-------:|----------:|------------:|
| Pragmatic_Production |   642.7 ns | 12.43 ns | 13.81 ns |     baseline |         | 0.0496 |     840 B |             |
| Serilog_Production   | 1,232.7 ns | 24.43 ns | 49.35 ns | 1.92x slower |   0.09x | 0.1793 |    3000 B |  3.57x more |
| NLog_Production      | 1,149.9 ns | 22.52 ns | 33.70 ns | 1.79x slower |   0.06x | 0.1717 |    2872 B |  3.42x more |
| ZLogger_Production   |   652.1 ns | 13.03 ns | 28.05 ns | 1.01x slower |   0.05x | 0.0496 |     832 B |  1.01x less |
