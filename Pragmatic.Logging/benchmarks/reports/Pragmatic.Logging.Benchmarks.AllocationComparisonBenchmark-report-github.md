```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                             | Mean      | Error    | StdDev   | Ratio        | RatioSD | Gen0    | Allocated | Alloc Ratio |
|----------------------------------- |----------:|---------:|---------:|-------------:|--------:|--------:|----------:|------------:|
| ZeroAllocFormatting                |  69.92 μs | 1.377 μs | 1.221 μs | 1.30x faster |   0.04x | 15.7471 | 257.81 KB |  1.52x less |
| StandardFormatting                 |  91.04 μs | 1.678 μs | 2.459 μs |     baseline |         | 23.8037 | 390.63 KB |             |
| ZeroAllocFormattingWithStackBuffer | 102.02 μs | 2.028 μs | 3.708 μs | 1.12x slower |   0.05x |  7.0801 | 117.19 KB |  3.33x less |
