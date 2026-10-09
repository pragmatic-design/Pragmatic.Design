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
| Pragmatic_CallSite                       |   166.9 ns |  5.83 ns | 17.20 ns |     baseline |         |      - |      - |         - |          NA |
| Pragmatic_LoggerMessage                  | 1,176.5 ns | 26.39 ns | 77.80 ns | 7.13x slower |   0.89x | 0.4177 | 0.0086 |    6992 B |          NA |
| Serilog_LoggerMessage                    |   885.4 ns | 21.99 ns | 64.84 ns | 5.36x slower |   0.69x | 0.0858 |      - |    1448 B |          NA |
| NLog_LoggerMessage                       |   998.3 ns | 26.09 ns | 76.93 ns | 6.05x slower |   0.79x | 0.1011 |      - |    1704 B |          NA |
| ZLogger_ZLoggerMessage                   |   281.6 ns |  7.64 ns | 22.54 ns | 1.71x slower |   0.23x |      - |      - |         - |          NA |
| ZLogger_ZLoggerMessage_Stream            |   304.0 ns | 10.39 ns | 30.62 ns | 1.84x slower |   0.27x |      - |      - |         - |          NA |
| ZLogger_ZLoggerMessage_SameFields_Stream |   297.2 ns |  7.74 ns | 22.83 ns | 1.80x slower |   0.23x |      - |      - |         - |          NA |
