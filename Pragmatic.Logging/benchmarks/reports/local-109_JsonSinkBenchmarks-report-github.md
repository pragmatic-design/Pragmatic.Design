```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

Categories=Json  

```
| Method                                   | Mean       | Error    | StdDev   | Ratio        | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|----------------------------------------- |-----------:|---------:|---------:|-------------:|--------:|-------:|-------:|----------:|------------:|
| Pragmatic_CallSite                       |   258.6 ns |  6.95 ns | 20.48 ns |     baseline |         |      - |      - |         - |          NA |
| Pragmatic_LoggerMessage                  | 1,150.0 ns | 28.35 ns | 83.58 ns | 4.47x slower |   0.48x | 0.4177 | 0.0086 |    6992 B |          NA |
| Serilog_LoggerMessage                    |   863.8 ns | 22.05 ns | 65.01 ns | 3.36x slower |   0.37x | 0.0858 |      - |    1448 B |          NA |
| NLog_LoggerMessage                       | 1,006.4 ns | 25.95 ns | 76.51 ns | 3.92x slower |   0.43x | 0.1011 |      - |    1704 B |          NA |
| ZLogger_ZLoggerMessage                   |   281.9 ns |  7.52 ns | 22.17 ns | 1.10x slower |   0.12x |      - |      - |         - |          NA |
| ZLogger_ZLoggerMessage_Stream            |   302.8 ns |  6.50 ns | 19.07 ns | 1.18x slower |   0.12x |      - |      - |         - |          NA |
| ZLogger_ZLoggerMessage_SameFields_Stream |   307.4 ns | 10.78 ns | 31.78 ns | 1.20x slower |   0.16x |      - |      - |         - |          NA |
