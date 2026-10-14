```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method             | Mean           | Error         | StdDev        | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------- |---------------:|--------------:|--------------:|------:|--------:|----------:|------------:|
| TryFormat_Canada   | 7,011,932.0 ns | 138,848.04 ns | 289,827.58 ns | 1.002 |    0.06 |         - |          NA |
| TryFormat_Branches |     1,410.0 ns |      25.20 ns |      21.04 ns | 0.000 |    0.00 |         - |          NA |
| Shortest_Canada    | 2,930,201.8 ns |  36,737.63 ns |  30,677.59 ns | 0.419 |    0.02 |         - |          NA |
| Shortest_Branches  |       671.7 ns |      13.35 ns |      25.73 ns | 0.000 |    0.00 |         - |          NA |
