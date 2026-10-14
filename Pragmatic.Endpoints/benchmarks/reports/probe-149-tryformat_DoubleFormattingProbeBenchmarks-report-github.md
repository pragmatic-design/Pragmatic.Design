```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method             | Mean         | Error       | StdDev      | Median       | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------- |-------------:|------------:|------------:|-------------:|------:|--------:|----------:|------------:|
| TryFormat_Canada   | 8,898.957 μs | 176.3053 μs | 429.1516 μs | 8,905.880 μs | 1.002 |    0.07 |         - |          NA |
| TryFormat_Branches |     1.746 μs |   0.0567 μs |   0.1673 μs |     1.808 μs | 0.000 |    0.00 |         - |          NA |
