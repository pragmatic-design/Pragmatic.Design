```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                | Mean     | Error    | StdDev   | Median   | Ratio        | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|---------------------- |---------:|---------:|---------:|---------:|-------------:|--------:|-------:|-------:|----------:|------------:|
| WithoutRedaction      | 297.5 ns |  5.73 ns |  6.37 ns | 297.2 ns |     baseline |         | 0.1221 | 0.0005 |      2 KB |             |
| WithDeclaredRedaction | 962.9 ns | 19.27 ns | 31.11 ns | 947.0 ns | 3.24x slower |   0.12x | 0.1373 |      - |   2.26 KB |  1.13x more |
