```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                                              | Mean      | Error    | StdDev   | Ratio        | RatioSD | Gen0   | Code Size | Allocated | Alloc Ratio |
|---------------------------------------------------- |----------:|---------:|---------:|-------------:|--------:|-------:|----------:|----------:|------------:|
| ZeroAllocMessageFormatterTryFormat                  |  60.14 ns | 0.678 ns | 0.635 ns | 1.11x faster |   0.04x |      - |   3,319 B |         - |          NA |
| StandardStringInterpolation                         |  66.46 ns | 1.343 ns | 2.387 ns |     baseline |         | 0.0143 |   2,993 B |     240 B |             |
| ZeroAllocMessageFormatterFormat                     |  81.11 ns | 1.616 ns | 1.512 ns | 1.22x slower |   0.05x | 0.0105 |   4,110 B |     176 B |  1.36x less |
| StandardStringFormat                                |  84.90 ns | 1.725 ns | 1.694 ns | 1.28x slower |   0.05x | 0.0176 |   2,741 B |     296 B |  1.23x more |
| LogMessageToString                                  |  85.32 ns | 1.702 ns | 2.547 ns | 1.29x slower |   0.06x | 0.0205 |   4,392 B |     344 B |  1.43x more |
| LogMessageTryFormat                                 |  88.14 ns | 1.340 ns | 1.187 ns | 1.33x slower |   0.05x | 0.0205 |   4,970 B |     344 B |  1.43x more |
| &#39;MessageFormatter.Format (legacy reflection-based)&#39; | 111.71 ns | 2.021 ns | 2.483 ns | 1.68x slower |   0.07x | 0.0315 |      30 B |     528 B |  2.20x more |
| StructuredLoggingWithILogger                        | 179.74 ns | 3.544 ns | 4.968 ns | 2.71x slower |   0.12x | 0.0539 |   5,063 B |     904 B |  3.77x more |
