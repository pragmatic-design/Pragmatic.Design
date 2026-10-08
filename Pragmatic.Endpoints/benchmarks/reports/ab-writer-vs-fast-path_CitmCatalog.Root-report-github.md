```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method           | Mean     | Error    | StdDev   | Gen0     | Gen1     | Gen2     | Allocated |
|----------------- |---------:|---------:|---------:|---------:|---------:|---------:|----------:|
| Stj_FastPath     | 497.8 μs | 14.12 μs | 41.63 μs | 119.1406 | 119.1406 | 119.1406 | 471.03 KB |
| Generated_Writer | 441.6 μs | 15.40 μs | 44.92 μs | 142.5781 | 142.5781 | 142.5781 | 468.85 KB |
