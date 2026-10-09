```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                  | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0    | Gen1    | Gen2    | Allocated | Alloc Ratio |
|------------------------ |----------:|---------:|---------:|------:|--------:|--------:|--------:|--------:|----------:|------------:|
| Writer_WriteStringValue | 172.97 μs | 3.440 μs | 4.351 μs |  1.00 |    0.03 | 54.6875 | 54.6875 | 54.6875 | 211.47 KB |        1.00 |
| Raw_CheckedCopy         | 232.01 μs | 3.576 μs | 3.170 μs |  1.34 |    0.04 | 56.3965 | 56.3965 | 56.3965 | 211.47 KB |        1.00 |
| Raw_AsciiCopy           | 159.58 μs | 2.848 μs | 2.664 μs |  0.92 |    0.03 | 54.4434 | 54.4434 | 54.4434 | 211.47 KB |        1.00 |
| Raw_AsciiOrEncoded      | 225.60 μs | 4.510 μs | 6.020 μs |  1.31 |    0.05 | 56.3965 | 56.3965 | 56.3965 | 211.47 KB |        1.00 |
| Minus_Text              |  31.46 μs | 0.412 μs | 0.365 μs |  0.18 |    0.00 |  0.8240 |       - |       - |  13.95 KB |        0.07 |
