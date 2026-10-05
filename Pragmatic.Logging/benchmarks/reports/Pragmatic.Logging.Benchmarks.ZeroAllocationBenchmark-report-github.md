```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                                              | Mean      | Error    | StdDev   | Ratio        | RatioSD | Gen0   | Code Size | Allocated | Alloc Ratio |
|---------------------------------------------------- |----------:|---------:|---------:|-------------:|--------:|-------:|----------:|----------:|------------:|
| ZeroAllocMessageFormatterTryFormat                  |  62.86 ns | 0.464 ns | 0.434 ns | 1.01x faster |   0.04x |      - |   3,319 B |         - |          NA |
| StandardStringInterpolation                         |  63.76 ns | 1.235 ns | 2.226 ns |     baseline |         | 0.0143 |   2,993 B |     240 B |             |
| ZeroAllocMessageFormatterFormat                     |  75.97 ns | 1.320 ns | 1.235 ns | 1.19x slower |   0.04x | 0.0105 |   4,106 B |     176 B |  1.36x less |
| StandardStringFormat                                |  81.87 ns | 1.646 ns | 3.361 ns | 1.29x slower |   0.07x | 0.0176 |   2,741 B |     296 B |  1.23x more |
| LogMessageTryFormat                                 |  85.93 ns | 1.736 ns | 1.858 ns | 1.35x slower |   0.05x | 0.0205 |   4,976 B |     344 B |  1.43x more |
| LogMessageToString                                  |  86.33 ns | 1.759 ns | 3.749 ns | 1.36x slower |   0.07x | 0.0205 |   4,378 B |     344 B |  1.43x more |
| &#39;MessageFormatter.Format (legacy reflection-based)&#39; | 109.04 ns | 1.738 ns | 1.785 ns | 1.71x slower |   0.06x | 0.0315 |      30 B |     528 B |  2.20x more |
| StructuredLoggingWithILogger                        | 170.68 ns | 3.313 ns | 3.402 ns | 2.68x slower |   0.10x | 0.0539 |   5,111 B |     904 B |  3.77x more |
