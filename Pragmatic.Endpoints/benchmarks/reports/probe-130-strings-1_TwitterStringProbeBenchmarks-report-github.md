```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                  | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0    | Gen1    | Gen2    | Allocated | Alloc Ratio |
|------------------------ |----------:|---------:|---------:|------:|--------:|--------:|--------:|--------:|----------:|------------:|
| Writer_WriteStringValue | 171.40 μs | 3.377 μs | 4.021 μs |  1.00 |    0.03 | 54.6875 | 54.6875 | 54.6875 | 211.47 KB |        1.00 |
| Raw_CheckedCopy         | 233.43 μs | 3.494 μs | 3.097 μs |  1.36 |    0.04 | 57.1289 | 57.1289 | 57.1289 | 211.48 KB |        1.00 |
| Minus_Text              |  32.05 μs | 0.627 μs | 0.879 μs |  0.19 |    0.01 |  0.8240 |       - |       - |  13.95 KB |        0.07 |
