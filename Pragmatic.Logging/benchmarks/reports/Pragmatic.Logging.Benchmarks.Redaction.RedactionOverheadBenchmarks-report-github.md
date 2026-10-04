```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                | Mean       | Error    | StdDev   | Ratio        | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------------- |-----------:|---------:|---------:|-------------:|--------:|-------:|----------:|------------:|
| WithoutRedaction      |   749.0 ns | 13.87 ns | 21.18 ns |     baseline |         | 0.1574 |   2.58 KB |             |
| WithDeclaredRedaction | 1,453.5 ns | 28.66 ns | 54.54 ns | 1.94x slower |   0.09x | 0.1984 |   3.34 KB |  1.30x more |
