```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                   | Mean       | Error    | StdDev   | Median     | P95        | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------------------- |-----------:|---------:|---------:|-----------:|-----------:|------:|--------:|-------:|-------:|----------:|------------:|
| CreateSuccess            |   489.4 ns |  1.74 ns |  1.63 ns |   489.2 ns |   491.8 ns |  1.00 |    0.00 |      - |      - |         - |          NA |
| CreateFailure            | 1,245.9 ns | 12.30 ns | 11.51 ns | 1,249.2 ns | 1,259.0 ns |  2.55 |    0.02 |      - |      - |         - |          NA |
| CreateSuccess_Implicit   |   486.5 ns |  4.70 ns |  4.40 ns |   483.8 ns |   492.8 ns |  0.99 |    0.01 |      - |      - |         - |          NA |
| CreateFailure_Implicit   | 1,238.2 ns | 13.14 ns | 12.29 ns | 1,231.7 ns | 1,256.9 ns |  2.53 |    0.03 |      - |      - |         - |          NA |
| IsSuccess_Check          |   335.4 ns |  6.16 ns |  9.41 ns |   334.1 ns |   351.9 ns |  0.69 |    0.02 |      - |      - |         - |          NA |
| Value_DirectAccess       |   326.9 ns |  1.09 ns |  0.96 ns |   327.0 ns |   328.3 ns |  0.67 |    0.00 |      - |      - |         - |          NA |
| TryGetValue_Pattern      |   350.0 ns |  5.05 ns |  4.72 ns |   348.8 ns |   358.4 ns |  0.72 |    0.01 |      - |      - |         - |          NA |
| Match_Pattern            |   663.8 ns |  1.82 ns |  1.61 ns |   663.9 ns |   666.2 ns |  1.36 |    0.01 |      - |      - |         - |          NA |
| Map_SingleTransform      | 3,941.8 ns | 38.87 ns | 36.36 ns | 3,954.9 ns | 3,985.0 ns |  8.05 |    0.08 | 0.9995 | 0.0610 |   16832 B |          NA |
| Map_ChainedTransforms    | 5,739.4 ns | 50.83 ns | 47.54 ns | 5,724.6 ns | 5,821.9 ns | 11.73 |    0.10 | 1.2283 | 0.0916 |   20608 B |          NA |
| Bind_SingleOperation     | 1,355.0 ns | 11.23 ns | 10.50 ns | 1,356.4 ns | 1,368.8 ns |  2.77 |    0.02 |      - |      - |         - |          NA |
| VoidResult_Success       |   323.7 ns |  2.37 ns |  2.22 ns |   323.9 ns |   326.6 ns |  0.66 |    0.00 |      - |      - |         - |          NA |
| VoidResult_Failure       | 1,235.9 ns |  5.97 ns |  5.58 ns | 1,234.9 ns | 1,244.7 ns |  2.53 |    0.01 |      - |      - |         - |          NA |
| Maybe_CreateSome         |   415.0 ns |  3.80 ns |  3.55 ns |   415.9 ns |   419.8 ns |  0.85 |    0.01 |      - |      - |         - |          NA |
| Maybe_CreateNone         |   381.4 ns |  3.12 ns |  2.92 ns |   382.2 ns |   384.8 ns |  0.78 |    0.01 |      - |      - |         - |          NA |
| Maybe_GetValueOrDefault  |   266.5 ns |  2.41 ns |  2.26 ns |   265.9 ns |   269.7 ns |  0.54 |    0.00 |      - |      - |         - |          NA |
| Maybe_Match              |   627.9 ns |  3.43 ns |  3.04 ns |   627.6 ns |   632.2 ns |  1.28 |    0.01 |      - |      - |         - |          NA |
| MultiError_CreateSuccess |   538.1 ns |  4.13 ns |  3.66 ns |   538.7 ns |   542.8 ns |  1.10 |    0.01 |      - |      - |         - |          NA |
| MultiError_Match         |   816.1 ns |  8.07 ns |  7.55 ns |   816.3 ns |   827.3 ns |  1.67 |    0.02 |      - |      - |         - |          NA |
| Twin_CreateSuccess       |   564.7 ns |  2.50 ns |  2.09 ns |   564.2 ns |   568.2 ns |  1.15 |    0.01 |      - |      - |         - |          NA |
| Twin_CreateFailure       | 1,050.7 ns |  9.42 ns |  8.81 ns | 1,047.5 ns | 1,064.6 ns |  2.15 |    0.02 |      - |      - |         - |          NA |
| Twin_IsSuccess           |   383.6 ns |  7.46 ns |  8.30 ns |   385.8 ns |   392.8 ns |  0.78 |    0.02 |      - |      - |         - |          NA |
| Twin_Value               |   230.3 ns |  2.61 ns |  2.44 ns |   230.0 ns |   234.2 ns |  0.47 |    0.01 |      - |      - |         - |          NA |
| Twin_ValueOrZero         |   386.0 ns |  2.81 ns |  2.63 ns |   386.8 ns |   388.6 ns |  0.79 |    0.01 |      - |      - |         - |          NA |
| Twin_Map                 | 3,886.6 ns | 59.32 ns | 55.49 ns | 3,899.5 ns | 3,948.3 ns |  7.94 |    0.11 | 0.9995 | 0.0610 |   16832 B |          NA |
| Twin_MapChained          | 4,131.5 ns | 37.36 ns | 33.11 ns | 4,128.2 ns | 4,181.1 ns |  8.44 |    0.07 | 1.2283 | 0.0916 |   20608 B |          NA |
| Twin_Bind                | 1,117.3 ns | 14.61 ns | 12.95 ns | 1,119.4 ns | 1,133.0 ns |  2.28 |    0.03 |      - |      - |         - |          NA |
| Twin_VoidSuccess         |   194.7 ns |  1.83 ns |  1.71 ns |   195.0 ns |   196.9 ns |  0.40 |    0.00 |      - |      - |         - |          NA |
| Twin_VoidFailure         | 1,048.5 ns | 11.70 ns | 10.95 ns | 1,050.8 ns | 1,061.6 ns |  2.14 |    0.02 |      - |      - |         - |          NA |
| Twin_MaybeSome           |   406.6 ns |  2.43 ns |  2.27 ns |   406.2 ns |   409.8 ns |  0.83 |    0.01 |      - |      - |         - |          NA |
| Twin_MaybeNone           |   276.3 ns |  2.11 ns |  1.65 ns |   276.7 ns |   277.8 ns |  0.56 |    0.00 |      - |      - |         - |          NA |
| Twin_MaybeValueOrZero    |   382.9 ns |  3.20 ns |  3.00 ns |   383.0 ns |   386.7 ns |  0.78 |    0.01 |      - |      - |         - |          NA |
| Twin_MultiSuccess        |   765.6 ns |  2.99 ns |  2.65 ns |   766.4 ns |   769.0 ns |  1.56 |    0.01 |      - |      - |         - |          NA |
| Twin_MultiMatch          |   423.3 ns |  8.35 ns |  8.20 ns |   421.7 ns |   435.0 ns |  0.86 |    0.02 |      - |      - |         - |          NA |
