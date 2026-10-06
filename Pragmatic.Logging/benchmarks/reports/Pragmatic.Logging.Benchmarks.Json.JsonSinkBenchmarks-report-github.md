```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX2


```
| Method                          | Categories       | Mean       | Error    | StdDev   | Ratio        | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|-------------------------------- |----------------- |-----------:|---------:|---------:|-------------:|--------:|-------:|-------:|----------:|------------:|
| Pragmatic_CallSite              | Json             |   730.2 ns |  1.84 ns |  1.72 ns |     baseline |         |      - |      - |         - |          NA |
| Pragmatic_LoggerMessage         | Json             | 1,958.3 ns | 25.08 ns | 23.46 ns | 2.68x slower |   0.03x | 0.4158 | 0.0076 |    6992 B |          NA |
| Serilog_LoggerMessage           | Json             | 1,562.4 ns |  8.82 ns |  8.25 ns | 2.14x slower |   0.01x | 0.0858 |      - |    1448 B |          NA |
| NLog_LoggerMessage              | Json             | 1,674.0 ns |  6.88 ns |  6.44 ns | 2.29x slower |   0.01x | 0.1011 |      - |    1704 B |          NA |
| ZLogger_ZLoggerMessage          | Json             |   447.7 ns |  0.45 ns |  0.40 ns | 1.63x faster |   0.00x |      - |      - |         - |          NA |
|                                 |                  |            |          |          |              |         |        |        |           |             |
| Pragmatic_CallSite_PersonalData | JsonPersonalData |   604.0 ns |  0.28 ns |  0.26 ns |     baseline |         |      - |      - |         - |          NA |
