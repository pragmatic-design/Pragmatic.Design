```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX2


```
| Method                   | Mean        | Error     | StdDev    | Median      | P95         | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------------------- |------------:|----------:|----------:|------------:|------------:|------:|--------:|-------:|-------:|----------:|------------:|
| CreateSuccess            |  1,178.7 ns |   0.73 ns |   0.65 ns |  1,178.7 ns |  1,179.7 ns |  1.00 |    0.00 |      - |      - |         - |          NA |
| CreateFailure            |  3,209.9 ns |   3.11 ns |   2.76 ns |  3,209.9 ns |  3,213.3 ns |  2.72 |    0.00 |      - |      - |         - |          NA |
| CreateSuccess_Implicit   |  1,101.9 ns |   1.82 ns |   1.52 ns |  1,102.0 ns |  1,104.1 ns |  0.93 |    0.00 |      - |      - |         - |          NA |
| CreateFailure_Implicit   |  3,195.8 ns |   4.46 ns |   3.72 ns |  3,194.6 ns |  3,202.3 ns |  2.71 |    0.00 |      - |      - |         - |          NA |
| IsSuccess_Check          |    704.5 ns |  14.09 ns |  21.09 ns |    697.2 ns |    743.3 ns |  0.60 |    0.02 |      - |      - |         - |          NA |
| Value_DirectAccess       |    682.2 ns |   0.93 ns |   0.82 ns |    682.0 ns |    683.6 ns |  0.58 |    0.00 |      - |      - |         - |          NA |
| TryGetValue_Pattern      |    770.6 ns |   2.59 ns |   2.43 ns |    770.6 ns |    774.5 ns |  0.65 |    0.00 |      - |      - |         - |          NA |
| Match_Pattern            |  1,365.0 ns |   1.93 ns |   1.80 ns |  1,364.5 ns |  1,367.8 ns |  1.16 |    0.00 |      - |      - |         - |          NA |
| Map_SingleTransform      | 10,121.5 ns |  47.87 ns |  42.43 ns | 10,100.1 ns | 10,180.7 ns |  8.59 |    0.04 | 0.9918 | 0.0610 |   16832 B |          NA |
| Map_ChainedTransforms    | 13,334.6 ns | 138.64 ns | 129.68 ns | 13,332.5 ns | 13,524.3 ns | 11.31 |    0.11 | 1.2207 | 0.0916 |   20608 B |          NA |
| Bind_SingleOperation     |  3,110.9 ns |   8.02 ns |   6.70 ns |  3,109.7 ns |  3,121.7 ns |  2.64 |    0.01 |      - |      - |         - |          NA |
| VoidResult_Success       |    798.1 ns |   1.66 ns |   1.47 ns |    797.3 ns |    800.7 ns |  0.68 |    0.00 |      - |      - |         - |          NA |
| VoidResult_Failure       |  2,888.8 ns |   1.86 ns |   1.55 ns |  2,888.3 ns |  2,891.6 ns |  2.45 |    0.00 |      - |      - |         - |          NA |
| Maybe_CreateSome         |    993.1 ns |   4.01 ns |   3.35 ns |    992.5 ns |    998.3 ns |  0.84 |    0.00 |      - |      - |         - |          NA |
| Maybe_CreateNone         |    650.1 ns |   1.73 ns |   1.53 ns |    649.8 ns |    652.7 ns |  0.55 |    0.00 |      - |      - |         - |          NA |
| Maybe_GetValueOrDefault  |    714.0 ns |   4.40 ns |   4.11 ns |    714.7 ns |    720.2 ns |  0.61 |    0.00 |      - |      - |         - |          NA |
| Maybe_Match              |  1,286.5 ns |   6.63 ns |   5.88 ns |  1,283.7 ns |  1,297.9 ns |  1.09 |    0.00 |      - |      - |         - |          NA |
| MultiError_CreateSuccess |  1,311.7 ns |   1.07 ns |   0.90 ns |  1,311.7 ns |  1,313.1 ns |  1.11 |    0.00 |      - |      - |         - |          NA |
| MultiError_Match         |  3,458.7 ns | 328.78 ns | 969.41 ns |  4,118.2 ns |  4,260.3 ns |  2.93 |    0.82 |      - |      - |         - |          NA |
