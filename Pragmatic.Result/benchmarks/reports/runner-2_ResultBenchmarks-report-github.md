```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX2


```
| Method                   | Mean        | Error     | StdDev    | Median      | P95         | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------------------- |------------:|----------:|----------:|------------:|------------:|------:|--------:|-------:|-------:|----------:|------------:|
| CreateSuccess            |  1,085.4 ns |   0.82 ns |   0.73 ns |  1,085.5 ns |  1,086.5 ns |  1.00 |    0.00 |      - |      - |         - |          NA |
| CreateFailure            |  3,215.6 ns |  12.21 ns |  10.82 ns |  3,210.8 ns |  3,236.5 ns |  2.96 |    0.01 |      - |      - |         - |          NA |
| CreateSuccess_Implicit   |  1,127.9 ns |   1.32 ns |   1.10 ns |  1,128.2 ns |  1,129.3 ns |  1.04 |    0.00 |      - |      - |         - |          NA |
| CreateFailure_Implicit   |  3,193.9 ns |   1.18 ns |   0.92 ns |  3,194.0 ns |  3,195.0 ns |  2.94 |    0.00 |      - |      - |         - |          NA |
| IsSuccess_Check          |    711.8 ns |   4.26 ns |   3.56 ns |    712.5 ns |    716.1 ns |  0.66 |    0.00 |      - |      - |         - |          NA |
| Value_DirectAccess       |    682.0 ns |   1.65 ns |   1.55 ns |    681.6 ns |    684.8 ns |  0.63 |    0.00 |      - |      - |         - |          NA |
| TryGetValue_Pattern      |    776.4 ns |   3.31 ns |   3.09 ns |    775.6 ns |    781.3 ns |  0.72 |    0.00 |      - |      - |         - |          NA |
| Match_Pattern            |  1,364.3 ns |   2.30 ns |   1.79 ns |  1,364.0 ns |  1,367.0 ns |  1.26 |    0.00 |      - |      - |         - |          NA |
| Map_SingleTransform      | 10,388.5 ns |  43.64 ns |  40.82 ns | 10,383.9 ns | 10,444.5 ns |  9.57 |    0.04 | 0.9918 | 0.0610 |   16832 B |          NA |
| Map_ChainedTransforms    | 13,002.7 ns | 202.26 ns | 189.19 ns | 12,934.2 ns | 13,338.9 ns | 11.98 |    0.17 | 1.2207 | 0.0916 |   20608 B |          NA |
| Bind_SingleOperation     |  3,059.3 ns |   5.95 ns |   4.97 ns |  3,057.5 ns |  3,067.6 ns |  2.82 |    0.00 |      - |      - |         - |          NA |
| VoidResult_Success       |    684.0 ns |   0.60 ns |   0.50 ns |    683.8 ns |    684.7 ns |  0.63 |    0.00 |      - |      - |         - |          NA |
| VoidResult_Failure       |  3,196.0 ns |   2.22 ns |   1.97 ns |  3,196.0 ns |  3,199.6 ns |  2.94 |    0.00 |      - |      - |         - |          NA |
| Maybe_CreateSome         |  1,075.2 ns |   4.30 ns |   3.82 ns |  1,073.9 ns |  1,081.9 ns |  0.99 |    0.00 |      - |      - |         - |          NA |
| Maybe_CreateNone         |    652.3 ns |   0.55 ns |   0.43 ns |    652.3 ns |    652.9 ns |  0.60 |    0.00 |      - |      - |         - |          NA |
| Maybe_GetValueOrDefault  |    717.4 ns |   6.86 ns |   6.42 ns |    717.9 ns |    725.9 ns |  0.66 |    0.01 |      - |      - |         - |          NA |
| Maybe_Match              |  1,282.9 ns |   1.72 ns |   1.43 ns |  1,282.6 ns |  1,285.3 ns |  1.18 |    0.00 |      - |      - |         - |          NA |
| MultiError_CreateSuccess |  1,352.8 ns |   1.30 ns |   1.15 ns |  1,352.4 ns |  1,354.7 ns |  1.25 |    0.00 |      - |      - |         - |          NA |
| MultiError_Match         |  1,484.7 ns |   1.00 ns |   0.84 ns |  1,484.9 ns |  1,485.6 ns |  1.37 |    0.00 |      - |      - |         - |          NA |
| Twin_CreateSuccess       |    967.8 ns |   1.02 ns |   0.90 ns |    967.7 ns |    969.0 ns |  0.89 |    0.00 |      - |      - |         - |          NA |
| Twin_CreateFailure       |  2,877.0 ns |   1.94 ns |   1.72 ns |  2,877.5 ns |  2,879.3 ns |  2.65 |    0.00 |      - |      - |         - |          NA |
| Twin_IsSuccess           |    706.1 ns |   5.19 ns |   4.05 ns |    704.9 ns |    713.0 ns |  0.65 |    0.00 |      - |      - |         - |          NA |
| Twin_Value               |    645.2 ns |   2.18 ns |   2.04 ns |    644.7 ns |    648.5 ns |  0.59 |    0.00 |      - |      - |         - |          NA |
| Twin_ValueOrZero         |    762.9 ns |   9.69 ns |   9.06 ns |    760.2 ns |    775.2 ns |  0.70 |    0.01 |      - |      - |         - |          NA |
| Twin_Map                 |  9,360.1 ns |  56.79 ns |  53.12 ns |  9,350.8 ns |  9,449.1 ns |  8.62 |    0.05 | 0.9918 | 0.0610 |   16832 B |          NA |
| Twin_MapChained          |  9,955.5 ns | 145.00 ns | 135.63 ns |  9,922.9 ns | 10,178.3 ns |  9.17 |    0.12 | 1.2207 | 0.0916 |   20608 B |          NA |
| Twin_Bind                |  2,479.7 ns |   1.77 ns |   1.48 ns |  2,479.0 ns |  2,482.3 ns |  2.28 |    0.00 |      - |      - |         - |          NA |
| Twin_VoidSuccess         |    644.1 ns |   0.88 ns |   0.78 ns |    644.1 ns |    645.4 ns |  0.59 |    0.00 |      - |      - |         - |          NA |
| Twin_VoidFailure         |  3,195.5 ns |   2.55 ns |   2.26 ns |  3,195.0 ns |  3,199.6 ns |  2.94 |    0.00 |      - |      - |         - |          NA |
| Twin_MaybeSome           |    982.9 ns |   1.51 ns |   1.34 ns |    982.8 ns |    984.7 ns |  0.91 |    0.00 |      - |      - |         - |          NA |
| Twin_MaybeNone           |    652.7 ns |   1.87 ns |   1.56 ns |    652.8 ns |    655.0 ns |  0.60 |    0.00 |      - |      - |         - |          NA |
| Twin_MaybeValueOrZero    |    719.8 ns |   5.97 ns |   5.30 ns |    717.7 ns |    728.4 ns |  0.66 |    0.00 |      - |      - |         - |          NA |
| Twin_MultiSuccess        |  1,061.6 ns |   1.08 ns |   1.01 ns |  1,061.9 ns |  1,063.0 ns |  0.98 |    0.00 |      - |      - |         - |          NA |
| Twin_MultiMatch          |    892.2 ns |  17.15 ns |  16.84 ns |    897.9 ns |    909.0 ns |  0.82 |    0.02 |      - |      - |         - |          NA |
