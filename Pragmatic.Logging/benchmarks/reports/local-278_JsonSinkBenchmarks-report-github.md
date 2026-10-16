```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9711)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                                   | Categories       | Mean       | Error    | StdDev    | Median     | Ratio        | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|----------------------------------------- |----------------- |-----------:|---------:|----------:|-----------:|-------------:|--------:|-------:|-------:|----------:|------------:|
| Pragmatic_CallSite                       | Json             |   190.4 ns |  6.35 ns |  18.73 ns |   190.6 ns |     baseline |         |      - |      - |         - |          NA |
| Pragmatic_LoggerMessage                  | Json             | 1,044.4 ns | 61.22 ns | 179.54 ns |   993.6 ns | 5.54x slower |   1.11x | 0.3910 | 0.0076 |    6560 B |          NA |
| Serilog_LoggerMessage                    | Json             |   991.5 ns | 45.22 ns | 133.33 ns |   959.5 ns | 5.26x slower |   0.89x | 0.0858 |      - |    1448 B |          NA |
| NLog_LoggerMessage                       | Json             | 1,127.0 ns | 48.35 ns | 141.03 ns | 1,103.5 ns | 5.98x slower |   0.97x | 0.1011 |      - |    1704 B |          NA |
| ZLogger_ZLoggerMessage                   | Json             |   332.6 ns | 10.54 ns |  30.92 ns |   329.2 ns | 1.76x slower |   0.24x |      - |      - |         - |          NA |
| ZLogger_ZLoggerMessage_Stream            | Json             |   341.1 ns |  8.41 ns |  24.81 ns |   337.2 ns | 1.81x slower |   0.23x |      - |      - |         - |          NA |
| ZLogger_ZLoggerMessage_SameFields_Stream | Json             |   349.9 ns | 12.74 ns |  37.58 ns |   360.3 ns | 1.86x slower |   0.28x |      - |      - |         - |          NA |
|                                          |                  |            |          |           |            |              |         |        |        |           |             |
| Pragmatic_CallSite_PersonalData          | JsonPersonalData |   144.1 ns |  2.89 ns |   7.24 ns |   144.6 ns |     baseline |         |      - |      - |         - |          NA |
