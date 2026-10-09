```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                     | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------------------- |----------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| Stj_FastPath               | 11.898 μs | 0.2361 μs | 0.5747 μs |  1.15 |    0.08 | 1.2054 |  19.76 KB |        1.01 |
| Generated_Writer           |  9.198 μs | 0.1826 μs | 0.4480 μs |  0.89 |    0.06 | 1.1902 |  19.59 KB |        1.00 |
| Probe_Plain                | 10.387 μs | 0.2077 μs | 0.5133 μs |  1.00 |    0.07 | 1.1902 |  19.59 KB |        1.00 |
| Probe_Plain_DefaultEncoder | 11.048 μs | 0.1858 μs | 0.1552 μs |  1.07 |    0.05 | 1.2054 |  19.76 KB |        1.01 |
| Minus_StringValues         |  8.847 μs | 0.1618 μs | 0.3938 μs |  0.85 |    0.06 | 1.0986 |  18.12 KB |        0.92 |
| Minus_DateFormatting       |  7.831 μs | 0.1492 μs | 0.1776 μs |  0.76 |    0.04 | 0.9842 |  16.23 KB |        0.83 |
| Raw_Numbers                |  9.926 μs | 0.1979 μs | 0.4664 μs |  0.96 |    0.06 | 1.1902 |  19.59 KB |        1.00 |
| Raw_EncoderFree            |  8.698 μs | 0.1733 μs | 0.4284 μs |  0.84 |    0.06 | 1.1902 |  19.59 KB |        1.00 |
