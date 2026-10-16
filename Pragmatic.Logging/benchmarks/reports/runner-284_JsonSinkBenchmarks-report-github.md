```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.303
  [Host]     : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX2


```
| Method                                   | Categories       | Mean       | Error    | StdDev  | Ratio        | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------------------------------------- |----------------- |-----------:|---------:|--------:|-------------:|--------:|-------:|----------:|------------:|
| Pragmatic_CallSite                       | Json             |   313.6 ns |  0.24 ns | 0.22 ns |     baseline |         |      - |         - |          NA |
| Pragmatic_LoggerMessage                  | Json             |   817.0 ns |  1.47 ns | 1.30 ns | 2.60x slower |   0.00x | 0.0086 |     144 B |          NA |
| Serilog_LoggerMessage                    | Json             | 1,535.8 ns |  6.23 ns | 5.52 ns | 4.90x slower |   0.02x | 0.0858 |    1448 B |          NA |
| NLog_LoggerMessage                       | Json             | 1,661.1 ns | 10.47 ns | 9.79 ns | 5.30x slower |   0.03x | 0.1011 |    1704 B |          NA |
| ZLogger_ZLoggerMessage                   | Json             |   454.8 ns |  0.31 ns | 0.26 ns | 1.45x slower |   0.00x |      - |         - |          NA |
| ZLogger_ZLoggerMessage_Stream            | Json             |   475.4 ns |  0.67 ns | 0.59 ns | 1.52x slower |   0.00x |      - |         - |          NA |
| ZLogger_ZLoggerMessage_SameFields_Stream | Json             |   523.2 ns |  0.74 ns | 0.70 ns | 1.67x slower |   0.00x |      - |         - |          NA |
|                                          |                  |            |          |         |              |         |        |           |             |
| Pragmatic_CallSite_PersonalData          | JsonPersonalData |   202.0 ns |  0.23 ns | 0.21 ns |     baseline |         |      - |         - |          NA |
