```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                   | Mean       | Error    | StdDev   | Median     | P95        | Ratio | RatioSD | Code Size | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------------------- |-----------:|---------:|---------:|-----------:|-----------:|------:|--------:|----------:|-------:|-------:|----------:|------------:|
| CreateSuccess            |   485.9 ns |  1.15 ns |  1.02 ns |   485.8 ns |   487.4 ns |  1.00 |    0.00 |      76 B |      - |      - |         - |          NA |
| CreateFailure            | 1,239.9 ns |  6.81 ns |  6.37 ns | 1,238.3 ns | 1,250.8 ns |  2.55 |    0.01 |     101 B |      - |      - |         - |          NA |
| CreateSuccess_Implicit   |   488.2 ns |  4.02 ns |  3.76 ns |   488.0 ns |   493.5 ns |  1.00 |    0.01 |      76 B |      - |      - |         - |          NA |
| CreateFailure_Implicit   | 1,237.3 ns | 12.49 ns | 11.68 ns | 1,231.2 ns | 1,256.0 ns |  2.55 |    0.02 |     101 B |      - |      - |         - |          NA |
| IsSuccess_Check          |   339.4 ns |  6.77 ns | 15.29 ns |   337.3 ns |   364.8 ns |  0.70 |    0.03 |      63 B |      - |      - |         - |          NA |
| Value_DirectAccess       |   327.2 ns |  2.94 ns |  2.75 ns |   327.3 ns |   331.2 ns |  0.67 |    0.01 |     106 B |      - |      - |         - |          NA |
| TryGetValue_Pattern      |   349.6 ns |  4.95 ns |  4.63 ns |   351.4 ns |   355.9 ns |  0.72 |    0.01 |      75 B |      - |      - |         - |          NA |
| Match_Pattern            |   663.3 ns |  2.84 ns |  2.66 ns |   663.1 ns |   666.6 ns |  1.36 |    0.01 |     362 B |      - |      - |         - |          NA |
| Map_SingleTransform      | 3,923.6 ns | 34.54 ns | 28.84 ns | 3,927.9 ns | 3,960.9 ns |  8.07 |    0.06 |   1,545 B | 0.9995 | 0.0610 |   16832 B |          NA |
| Map_ChainedTransforms    | 5,839.1 ns | 73.64 ns | 68.88 ns | 5,841.7 ns | 5,944.4 ns | 12.02 |    0.14 |   1,951 B | 1.2283 | 0.0916 |   20608 B |          NA |
| Bind_SingleOperation     | 1,355.1 ns | 10.19 ns |  9.53 ns | 1,352.6 ns | 1,368.8 ns |  2.79 |    0.02 |     365 B |      - |      - |         - |          NA |
| VoidResult_Success       |   323.4 ns |  4.08 ns |  3.81 ns |   323.2 ns |   329.6 ns |  0.67 |    0.01 |      57 B |      - |      - |         - |          NA |
| VoidResult_Failure       | 1,240.3 ns | 11.13 ns | 10.41 ns | 1,243.8 ns | 1,251.9 ns |  2.55 |    0.02 |      96 B |      - |      - |         - |          NA |
| Maybe_CreateSome         |   413.0 ns |  4.65 ns |  4.35 ns |   415.1 ns |   418.3 ns |  0.85 |    0.01 |      65 B |      - |      - |         - |          NA |
| Maybe_CreateNone         |   380.6 ns |  3.32 ns |  3.10 ns |   381.4 ns |   384.1 ns |  0.78 |    0.01 |      50 B |      - |      - |         - |          NA |
| Maybe_GetValueOrDefault  |   266.8 ns |  2.48 ns |  2.32 ns |   266.3 ns |   270.2 ns |  0.55 |    0.00 |      67 B |      - |      - |         - |          NA |
| Maybe_Match              |   624.5 ns |  4.61 ns |  4.32 ns |   624.1 ns |   631.9 ns |  1.29 |    0.01 |     347 B |      - |      - |         - |          NA |
| MultiError_CreateSuccess |   539.7 ns |  4.09 ns |  3.82 ns |   540.7 ns |   545.3 ns |  1.11 |    0.01 |      80 B |      - |      - |         - |          NA |
| MultiError_Match         |   812.8 ns |  7.18 ns |  6.71 ns |   807.7 ns |   822.9 ns |  1.67 |    0.01 |     626 B |      - |      - |         - |          NA |
| Twin_CreateSuccess       |   566.3 ns |  2.71 ns |  2.53 ns |   566.2 ns |   570.3 ns |  1.17 |    0.01 |      72 B |      - |      - |         - |          NA |
| Twin_CreateFailure       | 1,053.1 ns |  8.37 ns |  7.83 ns | 1,052.8 ns | 1,063.6 ns |  2.17 |    0.02 |      94 B |      - |      - |         - |          NA |
| Twin_IsSuccess           |   377.6 ns |  7.37 ns |  9.59 ns |   378.1 ns |   390.8 ns |  0.78 |    0.02 |      63 B |      - |      - |         - |          NA |
| Twin_Value               |   229.4 ns |  2.11 ns |  1.97 ns |   228.9 ns |   232.4 ns |  0.47 |    0.00 |      58 B |      - |      - |         - |          NA |
| Twin_ValueOrZero         |   384.9 ns |  2.11 ns |  1.97 ns |   385.6 ns |   386.6 ns |  0.79 |    0.00 |      76 B |      - |      - |         - |          NA |
| Twin_Map                 | 4,003.1 ns | 75.03 ns | 70.18 ns | 4,006.8 ns | 4,121.6 ns |  8.24 |    0.14 |   1,276 B | 0.9995 | 0.0610 |   16832 B |          NA |
| Twin_MapChained          | 3,856.2 ns | 31.19 ns | 26.05 ns | 3,853.0 ns | 3,893.5 ns |  7.94 |    0.05 |   1,294 B | 1.2283 | 0.0916 |   20608 B |          NA |
| Twin_Bind                | 1,114.9 ns | 19.76 ns | 18.49 ns | 1,111.9 ns | 1,145.1 ns |  2.29 |    0.04 |     112 B |      - |      - |         - |          NA |
| Twin_VoidSuccess         |   193.4 ns |  0.97 ns |  0.90 ns |   193.3 ns |   194.7 ns |  0.40 |    0.00 |      53 B |      - |      - |         - |          NA |
| Twin_VoidFailure         | 1,048.0 ns | 10.85 ns | 10.15 ns | 1,052.0 ns | 1,060.1 ns |  2.16 |    0.02 |      77 B |      - |      - |         - |          NA |
| Twin_MaybeSome           |   406.4 ns |  3.25 ns |  3.04 ns |   404.9 ns |   410.8 ns |  0.84 |    0.01 |      68 B |      - |      - |         - |          NA |
| Twin_MaybeNone           |   281.3 ns |  3.64 ns |  3.40 ns |   281.2 ns |   286.0 ns |  0.58 |    0.01 |      53 B |      - |      - |         - |          NA |
| Twin_MaybeValueOrZero    |   394.1 ns |  5.70 ns |  5.33 ns |   394.8 ns |   400.7 ns |  0.81 |    0.01 |      70 B |      - |      - |         - |          NA |
| Twin_MultiSuccess        |   763.7 ns |  7.64 ns |  7.15 ns |   764.1 ns |   773.3 ns |  1.57 |    0.01 |      79 B |      - |      - |         - |          NA |
| Twin_MultiMatch          |   424.8 ns |  7.56 ns |  7.07 ns |   426.5 ns |   432.7 ns |  0.87 |    0.01 |      95 B |      - |      - |         - |          NA |
