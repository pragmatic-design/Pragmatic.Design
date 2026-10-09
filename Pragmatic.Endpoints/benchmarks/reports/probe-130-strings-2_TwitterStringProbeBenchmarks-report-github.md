```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                  | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0    | Gen1    | Gen2    | Allocated | Alloc Ratio |
|------------------------ |----------:|---------:|---------:|------:|--------:|--------:|--------:|--------:|----------:|------------:|
| Writer_WriteStringValue | 172.83 μs | 3.351 μs | 3.585 μs |  1.00 |    0.03 | 54.9316 | 54.9316 | 54.9316 | 211.47 KB |        1.00 |
| Raw_CheckedCopy         | 238.46 μs | 4.426 μs | 3.924 μs |  1.38 |    0.04 | 57.1289 | 57.1289 | 57.1289 | 211.48 KB |        1.00 |
| Raw_AsciiCopy           | 159.37 μs | 3.002 μs | 2.661 μs |  0.92 |    0.02 | 54.9316 | 54.9316 | 54.9316 | 211.47 KB |        1.00 |
| Minus_Text              |  31.93 μs | 0.615 μs | 0.841 μs |  0.18 |    0.01 |  0.8240 |       - |       - |  13.95 KB |        0.07 |
