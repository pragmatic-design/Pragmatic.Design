```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  Job-VDDPDE : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

LaunchCount=3  

```
| Method                   | Mean       | Error     | StdDev    | Median     | P95        | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------------------- |-----------:|----------:|----------:|-----------:|-----------:|------:|--------:|-------:|-------:|----------:|------------:|
| CreateSuccess            |   761.8 ns |   2.37 ns |   4.34 ns |   761.2 ns |   768.8 ns |  1.00 |    0.01 |      - |      - |         - |          NA |
| CreateFailure            | 1,110.7 ns |  10.88 ns |  22.94 ns | 1,108.0 ns | 1,159.6 ns |  1.46 |    0.03 |      - |      - |         - |          NA |
| CreateSuccess_Implicit   |   761.0 ns |   4.68 ns |   8.67 ns |   761.2 ns |   778.3 ns |  1.00 |    0.01 |      - |      - |         - |          NA |
| CreateFailure_Implicit   | 1,260.1 ns |  10.60 ns |  19.37 ns | 1,259.2 ns | 1,302.4 ns |  1.65 |    0.03 |      - |      - |         - |          NA |
| IsSuccess_Check          |   338.6 ns |   3.69 ns |   9.33 ns |   338.2 ns |   352.0 ns |  0.44 |    0.01 |      - |      - |         - |          NA |
| Value_DirectAccess       |   334.1 ns |   2.92 ns |   5.49 ns |   332.1 ns |   344.8 ns |  0.44 |    0.01 |      - |      - |         - |          NA |
| TryGetValue_Pattern      |   414.6 ns |   7.68 ns |  21.53 ns |   410.6 ns |   456.6 ns |  0.54 |    0.03 |      - |      - |         - |          NA |
| Match_Pattern            |   696.4 ns |   9.02 ns |  25.44 ns |   689.1 ns |   747.5 ns |  0.91 |    0.03 |      - |      - |         - |          NA |
| Map_SingleTransform      | 4,232.0 ns |  45.06 ns | 117.92 ns | 4,220.5 ns | 4,462.1 ns |  5.56 |    0.16 | 0.9995 | 0.0610 |   16832 B |          NA |
| Map_ChainedTransforms    | 6,366.5 ns | 139.09 ns | 498.58 ns | 6,233.4 ns | 7,360.7 ns |  8.36 |    0.65 | 1.2283 | 0.0916 |   20608 B |          NA |
| Bind_SingleOperation     | 1,720.4 ns |  55.22 ns | 286.82 ns | 1,622.4 ns | 2,319.6 ns |  2.26 |    0.38 |      - |      - |         - |          NA |
| VoidResult_Success       |   504.3 ns |   8.44 ns |  41.69 ns |   503.4 ns |   571.1 ns |  0.66 |    0.05 |      - |      - |         - |          NA |
| VoidResult_Failure       | 1,782.8 ns |  28.56 ns | 132.25 ns | 1,806.6 ns | 1,961.5 ns |  2.34 |    0.17 |      - |      - |         - |          NA |
| Maybe_CreateSome         |   666.8 ns |   9.95 ns |  29.01 ns |   658.6 ns |   728.2 ns |  0.88 |    0.04 |      - |      - |         - |          NA |
| Maybe_CreateNone         |   377.8 ns |   8.46 ns |  39.23 ns |   364.1 ns |   455.2 ns |  0.50 |    0.05 |      - |      - |         - |          NA |
| Maybe_GetValueOrDefault  |   409.1 ns |   6.38 ns |  33.26 ns |   410.0 ns |   460.6 ns |  0.54 |    0.04 |      - |      - |         - |          NA |
| Maybe_Match              |   956.0 ns |  14.41 ns |  50.89 ns |   967.2 ns | 1,022.9 ns |  1.25 |    0.07 |      - |      - |         - |          NA |
| MultiError_CreateSuccess |   906.9 ns |  10.26 ns |  24.77 ns |   909.1 ns |   945.9 ns |  1.19 |    0.03 |      - |      - |         - |          NA |
| MultiError_Match         | 1,107.1 ns |  29.46 ns |  98.92 ns | 1,077.3 ns | 1,274.6 ns |  1.45 |    0.13 |      - |      - |         - |          NA |
| Twin_CreateSuccess       |   646.4 ns |   4.16 ns |   7.71 ns |   645.5 ns |   657.6 ns |  0.85 |    0.01 |      - |      - |         - |          NA |
| Twin_CreateFailure       | 1,393.7 ns |  14.00 ns |  35.88 ns | 1,385.7 ns | 1,471.7 ns |  1.83 |    0.05 |      - |      - |         - |          NA |
| Twin_IsSuccess           |   428.7 ns |   3.55 ns |   6.66 ns |   429.1 ns |   439.5 ns |  0.56 |    0.01 |      - |      - |         - |          NA |
| Twin_Value               |   311.5 ns |   3.00 ns |   6.27 ns |   311.6 ns |   322.3 ns |  0.41 |    0.01 |      - |      - |         - |          NA |
| Twin_ValueOrZero         |   434.5 ns |   4.89 ns |  11.03 ns |   433.3 ns |   450.9 ns |  0.57 |    0.01 |      - |      - |         - |          NA |
| Twin_Map                 | 5,680.6 ns |  86.17 ns | 290.58 ns | 5,620.5 ns | 6,224.5 ns |  7.46 |    0.38 | 0.9995 | 0.0610 |   16832 B |          NA |
| Twin_MapChained          | 5,780.6 ns |  86.39 ns | 230.59 ns | 5,737.1 ns | 6,247.3 ns |  7.59 |    0.30 | 1.2283 | 0.0916 |   20608 B |          NA |
| Twin_Bind                | 1,492.3 ns |  18.40 ns |  63.31 ns | 1,477.4 ns | 1,617.3 ns |  1.96 |    0.08 |      - |      - |         - |          NA |
| Twin_VoidSuccess         |   316.2 ns |   5.82 ns |  22.25 ns |   311.6 ns |   364.3 ns |  0.42 |    0.03 |      - |      - |         - |          NA |
| Twin_VoidFailure         | 1,357.2 ns |  13.30 ns |  25.31 ns | 1,351.7 ns | 1,401.9 ns |  1.78 |    0.03 |      - |      - |         - |          NA |
| Twin_MaybeSome           |   495.4 ns |   4.01 ns |   7.73 ns |   493.7 ns |   507.8 ns |  0.65 |    0.01 |      - |      - |         - |          NA |
| Twin_MaybeNone           |   333.7 ns |   3.30 ns |   6.89 ns |   333.1 ns |   345.8 ns |  0.44 |    0.01 |      - |      - |         - |          NA |
| Twin_MaybeValueOrZero    |   352.7 ns |   3.71 ns |   8.89 ns |   351.8 ns |   368.6 ns |  0.46 |    0.01 |      - |      - |         - |          NA |
| Twin_MultiSuccess        |   575.3 ns |   4.30 ns |   8.08 ns |   575.3 ns |   586.6 ns |  0.76 |    0.01 |      - |      - |         - |          NA |
| Twin_MultiMatch          |   523.3 ns |   5.77 ns |  13.14 ns |   522.4 ns |   547.2 ns |  0.69 |    0.02 |      - |      - |         - |          NA |
