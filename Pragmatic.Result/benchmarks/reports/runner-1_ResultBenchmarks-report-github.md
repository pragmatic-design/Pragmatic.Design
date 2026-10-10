```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX2


```
| Method                   | Mean        | Error     | StdDev    | Median      | P95         | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------------------- |------------:|----------:|----------:|------------:|------------:|------:|--------:|-------:|-------:|----------:|------------:|
| CreateSuccess            |  1,169.9 ns |   6.44 ns |   5.71 ns |  1,171.2 ns |  1,172.9 ns |  1.00 |    0.01 |      - |      - |         - |          NA |
| CreateFailure            |  3,195.0 ns |   2.89 ns |   2.41 ns |  3,194.0 ns |  3,198.7 ns |  2.73 |    0.01 |      - |      - |         - |          NA |
| CreateSuccess_Implicit   |  1,077.9 ns |   2.38 ns |   2.11 ns |  1,078.1 ns |  1,080.8 ns |  0.92 |    0.00 |      - |      - |         - |          NA |
| CreateFailure_Implicit   |  3,197.3 ns |   6.08 ns |   5.39 ns |  3,196.9 ns |  3,206.5 ns |  2.73 |    0.01 |      - |      - |         - |          NA |
| IsSuccess_Check          |    683.5 ns |  13.00 ns |  12.16 ns |    682.4 ns |    698.5 ns |  0.58 |    0.01 |      - |      - |         - |          NA |
| Value_DirectAccess       |    664.2 ns |   1.51 ns |   1.33 ns |    664.2 ns |    666.3 ns |  0.57 |    0.00 |      - |      - |         - |          NA |
| TryGetValue_Pattern      |    762.4 ns |   5.94 ns |   5.56 ns |    760.5 ns |    773.6 ns |  0.65 |    0.01 |      - |      - |         - |          NA |
| Match_Pattern            |  1,287.8 ns |   1.22 ns |   1.09 ns |  1,287.7 ns |  1,289.4 ns |  1.10 |    0.01 |      - |      - |         - |          NA |
| Map_SingleTransform      | 10,294.4 ns |  58.31 ns |  51.69 ns | 10,274.4 ns | 10,376.1 ns |  8.80 |    0.06 | 0.9918 | 0.0610 |   16832 B |          NA |
| Map_ChainedTransforms    | 13,586.6 ns | 118.74 ns | 111.07 ns | 13,569.5 ns | 13,771.3 ns | 11.61 |    0.11 | 1.2207 | 0.0916 |   20608 B |          NA |
| Bind_SingleOperation     |  3,122.7 ns |   3.09 ns |   2.58 ns |  3,123.3 ns |  3,125.0 ns |  2.67 |    0.01 |      - |      - |         - |          NA |
| VoidResult_Success       |    656.0 ns |   3.33 ns |   2.95 ns |    655.6 ns |    660.5 ns |  0.56 |    0.00 |      - |      - |         - |          NA |
| VoidResult_Failure       |  2,889.7 ns |   2.65 ns |   2.07 ns |  2,889.3 ns |  2,892.4 ns |  2.47 |    0.01 |      - |      - |         - |          NA |
| Maybe_CreateSome         |    985.1 ns |   0.99 ns |   0.88 ns |    985.4 ns |    986.2 ns |  0.84 |    0.00 |      - |      - |         - |          NA |
| Maybe_CreateNone         |    656.0 ns |   1.18 ns |   1.11 ns |    656.2 ns |    657.4 ns |  0.56 |    0.00 |      - |      - |         - |          NA |
| Maybe_GetValueOrDefault  |    715.8 ns |   3.76 ns |   3.52 ns |    715.7 ns |    721.4 ns |  0.61 |    0.00 |      - |      - |         - |          NA |
| Maybe_Match              |  1,283.6 ns |   1.18 ns |   0.93 ns |  1,283.6 ns |  1,284.9 ns |  1.10 |    0.01 |      - |      - |         - |          NA |
| MultiError_CreateSuccess |  1,306.9 ns |   0.36 ns |   0.32 ns |  1,306.8 ns |  1,307.4 ns |  1.12 |    0.01 |      - |      - |         - |          NA |
| MultiError_Match         |  3,908.1 ns |  40.52 ns |  35.92 ns |  3,899.4 ns |  3,973.3 ns |  3.34 |    0.03 |      - |      - |         - |          NA |
| Twin_CreateSuccess       |  1,048.5 ns |   1.02 ns |   0.91 ns |  1,048.4 ns |  1,049.8 ns |  0.90 |    0.00 |      - |      - |         - |          NA |
| Twin_CreateFailure       |  3,208.7 ns |   3.05 ns |   2.70 ns |  3,208.5 ns |  3,212.7 ns |  2.74 |    0.01 |      - |      - |         - |          NA |
| Twin_IsSuccess           |    713.4 ns |  13.47 ns |  12.60 ns |    711.9 ns |    731.3 ns |  0.61 |    0.01 |      - |      - |         - |          NA |
| Twin_Value               |    644.3 ns |   1.06 ns |   0.83 ns |    644.3 ns |    645.6 ns |  0.55 |    0.00 |      - |      - |         - |          NA |
| Twin_ValueOrZero         |    768.9 ns |   2.50 ns |   2.21 ns |    768.5 ns |    772.0 ns |  0.66 |    0.00 |      - |      - |         - |          NA |
| Twin_Map                 |  9,602.4 ns |  84.93 ns |  75.29 ns |  9,593.7 ns |  9,720.5 ns |  8.21 |    0.07 | 0.9918 | 0.0610 |   16832 B |          NA |
| Twin_MapChained          |  9,768.6 ns | 140.54 ns | 131.46 ns |  9,723.7 ns |  9,976.1 ns |  8.35 |    0.12 | 1.2207 | 0.0916 |   20608 B |          NA |
| Twin_Bind                |  2,799.8 ns |   4.24 ns |   3.96 ns |  2,798.2 ns |  2,806.6 ns |  2.39 |    0.01 |      - |      - |         - |          NA |
| Twin_VoidSuccess         |    642.8 ns |   0.77 ns |   0.64 ns |    642.7 ns |    643.9 ns |  0.55 |    0.00 |      - |      - |         - |          NA |
| Twin_VoidFailure         |  2,877.1 ns |   2.67 ns |   2.08 ns |  2,876.5 ns |  2,880.6 ns |  2.46 |    0.01 |      - |      - |         - |          NA |
| Twin_MaybeSome           |  1,073.9 ns |   2.34 ns |   2.19 ns |  1,072.9 ns |  1,077.9 ns |  0.92 |    0.00 |      - |      - |         - |          NA |
| Twin_MaybeNone           |    650.6 ns |   0.88 ns |   0.73 ns |    650.7 ns |    651.6 ns |  0.56 |    0.00 |      - |      - |         - |          NA |
| Twin_MaybeValueOrZero    |    716.0 ns |   7.74 ns |   7.24 ns |    713.4 ns |    728.5 ns |  0.61 |    0.01 |      - |      - |         - |          NA |
| Twin_MultiSuccess        |  1,012.9 ns |   1.06 ns |   0.89 ns |  1,013.1 ns |  1,013.8 ns |  0.87 |    0.00 |      - |      - |         - |          NA |
| Twin_MultiMatch          |    911.0 ns |  18.21 ns |  41.11 ns |    911.4 ns |    961.7 ns |  0.78 |    0.04 |      - |      - |         - |          NA |
