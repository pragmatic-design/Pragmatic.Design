```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9711)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                  | Mean      | Error    | StdDev    | Median    | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------------ |----------:|---------:|----------:|----------:|------:|--------:|----------:|------------:|
| Writer_WriteStringValue | 148.24 μs | 5.257 μs | 15.501 μs | 144.62 μs |  1.01 |    0.15 |         - |          NA |
| Raw_CheckedCopy         | 214.21 μs | 8.265 μs | 24.368 μs | 211.74 μs |  1.46 |    0.22 |         - |          NA |
| Raw_AsciiCopy           | 144.26 μs | 2.870 μs |  7.041 μs | 144.75 μs |  0.98 |    0.11 |         - |          NA |
| Raw_AsciiOrEncoded      | 205.32 μs | 5.630 μs | 16.601 μs | 209.13 μs |  1.40 |    0.18 |         - |          NA |
| Raw_AsciiOrUtf8Check    | 206.76 μs | 5.288 μs | 15.591 μs | 208.90 μs |  1.41 |    0.18 |         - |          NA |
| Raw_AsciiOrUtf16Check   | 178.64 μs | 3.567 μs |  7.977 μs | 179.02 μs |  1.22 |    0.13 |         - |          NA |
| Minus_Text              |  29.27 μs | 1.277 μs |  3.726 μs |  27.50 μs |  0.20 |    0.03 |         - |          NA |
