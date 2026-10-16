```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9711)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                                   | Categories       | Mean     | Error    | StdDev   | Median   | Ratio        | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------------------------------------- |----------------- |---------:|---------:|---------:|---------:|-------------:|--------:|-------:|----------:|------------:|
| Pragmatic_CallSite                       | Json             | 183.4 ns |  6.19 ns | 17.97 ns | 186.3 ns |     baseline |         |      - |         - |          NA |
| Pragmatic_LoggerMessage                  | Json             | 596.0 ns | 12.76 ns | 37.23 ns | 596.8 ns | 3.28x slower |   0.40x | 0.0086 |     144 B |          NA |
| Serilog_LoggerMessage                    | Json             | 879.1 ns | 17.91 ns | 51.95 ns | 861.9 ns | 4.84x slower |   0.58x | 0.0858 |    1448 B |          NA |
| NLog_LoggerMessage                       | Json             | 911.4 ns | 12.94 ns | 10.81 ns | 910.4 ns | 5.02x slower |   0.52x | 0.1011 |    1704 B |          NA |
| ZLogger_ZLoggerMessage                   | Json             | 264.4 ns |  4.57 ns |  3.81 ns | 263.2 ns | 1.46x slower |   0.15x |      - |         - |          NA |
| ZLogger_ZLoggerMessage_Stream            | Json             | 287.1 ns |  5.67 ns |  5.57 ns | 286.5 ns | 1.58x slower |   0.17x |      - |         - |          NA |
| ZLogger_ZLoggerMessage_SameFields_Stream | Json             | 277.6 ns |  5.51 ns |  8.90 ns | 276.7 ns | 1.53x slower |   0.17x |      - |         - |          NA |
|                                          |                  |          |          |          |          |              |         |        |           |             |
| Pragmatic_CallSite_PersonalData          | JsonPersonalData | 113.1 ns |  2.24 ns |  2.49 ns | 112.1 ns |     baseline |         |      - |         - |          NA |
