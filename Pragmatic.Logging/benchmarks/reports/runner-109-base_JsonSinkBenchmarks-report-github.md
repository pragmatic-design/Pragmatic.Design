```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 9V45, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                          | Categories       | Mean       | Error    | StdDev   | Ratio        | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|-------------------------------- |----------------- |-----------:|---------:|---------:|-------------:|--------:|-------:|-------:|----------:|------------:|
| Pragmatic_CallSite              | Json             |   405.8 ns |  4.22 ns |  3.52 ns |     baseline |         |      - |      - |         - |          NA |
| Pragmatic_LoggerMessage         | Json             | 1,089.2 ns | 19.30 ns | 19.82 ns | 2.68x slower |   0.05x | 0.4177 | 0.0076 |    6992 B |          NA |
| Serilog_LoggerMessage           | Json             |   881.9 ns | 11.11 ns |  9.28 ns | 2.17x slower |   0.03x | 0.0858 |      - |    1448 B |          NA |
| NLog_LoggerMessage              | Json             |   963.0 ns | 11.04 ns |  9.22 ns | 2.37x slower |   0.03x | 0.1011 |      - |    1704 B |          NA |
| ZLogger_ZLoggerMessage          | Json             |   220.2 ns |  4.13 ns |  3.86 ns | 1.84x faster |   0.04x |      - |      - |         - |          NA |
|                                 |                  |            |          |          |              |         |        |        |           |             |
| Pragmatic_CallSite_PersonalData | JsonPersonalData |   330.7 ns |  6.27 ns |  5.86 ns |     baseline |         |      - |      - |         - |          NA |
