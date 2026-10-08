```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                    | Mean     | Error    | StdDev   | Ratio        | RatioSD | Allocated | Alloc Ratio |
|-------------------------- |---------:|---------:|---------:|-------------:|--------:|----------:|------------:|
| Layout                    | 15.98 ns | 0.358 ns | 1.055 ns |     baseline |         |         - |          NA |
| Utf8Formatter_O           | 10.89 ns | 0.293 ns | 0.863 ns | 1.48x faster |   0.15x |         - |          NA |
| DateTime_TryFormat_Custom | 85.50 ns | 3.091 ns | 9.115 ns | 5.37x slower |   0.68x |         - |          NA |
