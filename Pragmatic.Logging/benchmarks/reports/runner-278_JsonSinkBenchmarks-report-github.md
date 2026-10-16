```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.303
  [Host]     : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX2


```
| Method                                   | Categories       | Mean       | Error    | StdDev   | Ratio        | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|----------------------------------------- |----------------- |-----------:|---------:|---------:|-------------:|--------:|-------:|-------:|----------:|------------:|
| Pragmatic_CallSite                       | Json             |   302.3 ns |  0.41 ns |  0.37 ns |     baseline |         |      - |      - |         - |          NA |
| Pragmatic_LoggerMessage                  | Json             | 1,798.1 ns | 27.45 ns | 22.92 ns | 5.95x slower |   0.07x | 0.3910 | 0.0076 |    6560 B |          NA |
| Serilog_LoggerMessage                    | Json             | 1,598.1 ns |  5.77 ns |  5.11 ns | 5.29x slower |   0.02x | 0.0858 |      - |    1448 B |          NA |
| NLog_LoggerMessage                       | Json             | 1,654.1 ns |  8.73 ns |  7.29 ns | 5.47x slower |   0.02x | 0.1011 |      - |    1704 B |          NA |
| ZLogger_ZLoggerMessage                   | Json             |   445.5 ns |  0.21 ns |  0.17 ns | 1.47x slower |   0.00x |      - |      - |         - |          NA |
| ZLogger_ZLoggerMessage_Stream            | Json             |   476.7 ns |  0.87 ns |  0.77 ns | 1.58x slower |   0.00x |      - |      - |         - |          NA |
| ZLogger_ZLoggerMessage_SameFields_Stream | Json             |   536.5 ns |  0.51 ns |  0.42 ns | 1.77x slower |   0.00x |      - |      - |         - |          NA |
|                                          |                  |            |          |          |              |         |        |        |           |             |
| Pragmatic_CallSite_PersonalData          | JsonPersonalData |   209.6 ns |  0.95 ns |  0.85 ns |     baseline |         |      - |      - |         - |          NA |
