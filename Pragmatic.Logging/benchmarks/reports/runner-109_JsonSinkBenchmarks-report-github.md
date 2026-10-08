```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 9V45, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                                   | Categories       | Mean       | Error    | StdDev   | Ratio        | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|----------------------------------------- |----------------- |-----------:|---------:|---------:|-------------:|--------:|-------:|-------:|----------:|------------:|
| Pragmatic_CallSite                       | Json             |   263.5 ns |  5.26 ns |  5.40 ns |     baseline |         |      - |      - |         - |          NA |
| Pragmatic_LoggerMessage                  | Json             | 1,101.4 ns | 20.88 ns | 32.50 ns | 4.18x slower |   0.15x | 0.4177 | 0.0076 |    6992 B |          NA |
| Serilog_LoggerMessage                    | Json             |   841.7 ns | 12.41 ns | 11.61 ns | 3.20x slower |   0.08x | 0.0858 |      - |    1448 B |          NA |
| NLog_LoggerMessage                       | Json             | 1,005.3 ns | 16.37 ns | 14.51 ns | 3.82x slower |   0.09x | 0.1011 |      - |    1704 B |          NA |
| ZLogger_ZLoggerMessage                   | Json             |   224.0 ns |  2.29 ns |  2.14 ns | 1.18x faster |   0.03x |      - |      - |         - |          NA |
| ZLogger_ZLoggerMessage_Stream            | Json             |   251.0 ns |  2.08 ns |  1.74 ns | 1.05x faster |   0.02x |      - |      - |         - |          NA |
| ZLogger_ZLoggerMessage_SameFields_Stream | Json             |   264.2 ns |  4.15 ns |  3.46 ns | 1.00x slower |   0.02x |      - |      - |         - |          NA |
|                                          |                  |            |          |          |              |         |        |        |           |             |
| Pragmatic_CallSite_PersonalData          | JsonPersonalData |   187.0 ns |  2.33 ns |  1.95 ns |     baseline |         |      - |      - |         - |          NA |
