```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                             | Mean     | Error    | StdDev   | Ratio        | RatioSD | Gen0    | Allocated | Alloc Ratio |
|----------------------------------- |---------:|---------:|---------:|-------------:|--------:|--------:|----------:|------------:|
| ZeroAllocFormatting                | 62.69 μs | 1.208 μs | 1.130 μs | 1.36x faster |   0.04x | 15.7471 | 257.81 KB |  1.52x less |
| StandardFormatting                 | 84.96 μs | 1.654 μs | 1.698 μs |     baseline |         | 23.8037 | 390.63 KB |             |
| ZeroAllocFormattingWithStackBuffer | 99.05 μs | 1.523 μs | 1.350 μs | 1.17x slower |   0.03x |  7.0801 | 117.19 KB |  3.33x less |
