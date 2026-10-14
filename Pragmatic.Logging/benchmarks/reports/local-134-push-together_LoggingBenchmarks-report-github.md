```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

Categories=Production  

```
| Method               | Mean       | Error    | StdDev    | Ratio        | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------------- |-----------:|---------:|----------:|-------------:|--------:|-------:|----------:|------------:|
| Pragmatic_Production |   655.6 ns | 13.09 ns |  32.36 ns |     baseline |         | 0.0505 |     848 B |             |
| Serilog_Production   | 1,289.9 ns | 41.53 ns | 121.81 ns | 1.97x slower |   0.21x | 0.1793 |    3000 B |  3.54x more |
| NLog_Production      | 1,151.7 ns | 22.97 ns |  58.87 ns | 1.76x slower |   0.12x | 0.1717 |    2872 B |  3.39x more |
| ZLogger_Production   |   600.1 ns | 12.01 ns |  30.99 ns | 1.10x faster |   0.08x | 0.0496 |     832 B |  1.02x less |
