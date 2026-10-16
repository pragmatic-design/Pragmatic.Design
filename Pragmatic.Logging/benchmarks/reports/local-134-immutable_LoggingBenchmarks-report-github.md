```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9711)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

Categories=Production  

```
| Method               | Mean       | Error    | StdDev    | Median     | Ratio        | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------------- |-----------:|---------:|----------:|-----------:|-------------:|--------:|-------:|----------:|------------:|
| Pragmatic_Production |   630.7 ns | 15.39 ns |  45.39 ns |   613.2 ns |     baseline |         | 0.0467 |     784 B |             |
| Serilog_Production   | 1,241.1 ns | 36.03 ns | 105.11 ns | 1,214.2 ns | 1.98x slower |   0.21x | 0.1793 |    3000 B |  3.83x more |
| NLog_Production      | 1,066.1 ns | 20.37 ns |  56.46 ns | 1,060.7 ns | 1.70x slower |   0.15x | 0.1717 |    2872 B |  3.66x more |
| ZLogger_Production   |   587.1 ns | 11.73 ns |  31.52 ns |   584.3 ns | 1.08x faster |   0.10x | 0.0496 |     832 B |  1.06x more |
