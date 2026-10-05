```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                               | Categories        | Mean            | Error         | StdDev        | Ratio        | RatioSD | Gen0     | Allocated | Alloc Ratio |
|------------------------------------- |------------------ |----------------:|--------------:|--------------:|-------------:|--------:|---------:|----------:|------------:|
| SimpleLevel_BatchEvaluation          | BatchEvaluation   |       368.35 ns |      3.241 ns |      2.706 ns |     baseline |         |   0.1192 |    2000 B |             |
| ComplexBusinessLogic_BatchEvaluation | BatchEvaluation   |       495.88 ns |      9.436 ns |     10.096 ns | 1.35x slower |   0.03x |   0.1192 |    2000 B |  1.00x more |
| EnterpriseCompliance_BatchEvaluation | BatchEvaluation   |       593.86 ns |     11.875 ns |     16.647 ns | 1.61x slower |   0.05x |   0.1192 |    2000 B |  1.00x more |
| AllFilters_BatchEvaluation           | BatchEvaluation   |     2,820.52 ns |     56.180 ns |     85.793 ns | 7.66x slower |   0.24x |   0.5951 |   10000 B |  5.00x more |
|                                      |                   |                 |               |               |              |         |          |           |             |
| CacheWarmup_SingleExpression         | CachePerformance  |    70,827.05 ns |  1,409.244 ns |  2,315.429 ns |     baseline |         |  23.9258 |  400400 B |             |
| CacheStress_MultipleExpressions      | CachePerformance  |   568,094.43 ns | 11,144.143 ns | 17,350.089 ns | 8.03x slower |   0.35x | 119.1406 | 2002000 B |  5.00x more |
|                                      |                   |                 |               |               |              |         |          |           |             |
| HighVolume_SimpleFilter              | HighVolume        |   704,773.03 ns | 13,441.957 ns | 14,382.744 ns |     baseline |         | 238.2813 | 4000000 B |             |
| HighVolume_ComplexFilter             | HighVolume        |   930,273.10 ns | 17,204.859 ns | 15,251.659 ns | 1.32x slower |   0.03x | 238.2813 | 4000000 B |  1.00x more |
| HighVolume_MixedFilters              | HighVolume        | 1,003,771.85 ns | 19,982.571 ns | 33,931.925 ns | 1.42x slower |   0.06x | 238.2813 | 4000000 B |  1.00x more |
|                                      |                   |                 |               |               |              |         |          |           |             |
| PropertyExists_Evaluation            | PropertyFiltering |        85.44 ns |      1.659 ns |      1.703 ns |     baseline |         |   0.0286 |     480 B |             |
| PropertyValue_Evaluation             | PropertyFiltering |        84.66 ns |      1.420 ns |      1.847 ns | 1.01x faster |   0.03x |   0.0286 |     480 B |  1.00x more |
| StructuredProperties_Evaluation      | PropertyFiltering |        84.56 ns |      1.630 ns |      1.445 ns | 1.01x faster |   0.03x |   0.0300 |     504 B |  1.05x more |
| NumericProperties_Evaluation         | PropertyFiltering |       100.91 ns |      1.985 ns |      2.847 ns | 1.18x slower |   0.04x |   0.0296 |     496 B |  1.03x more |
|                                      |                   |                 |               |               |              |         |          |           |             |
| SimpleLevel_Evaluation               | SingleEvaluation  |        69.91 ns |      1.400 ns |      2.561 ns |     baseline |         |   0.0238 |     400 B |             |
| ComplexBusinessLogic_Evaluation      | SingleEvaluation  |        93.57 ns |      1.877 ns |      1.927 ns | 1.34x slower |   0.05x |   0.0238 |     400 B |  1.00x more |
| EnterpriseCompliance_Evaluation      | SingleEvaluation  |       113.48 ns |      2.106 ns |      1.867 ns | 1.63x slower |   0.06x |   0.0238 |     400 B |  1.00x more |
| PerformanceMonitoring_Evaluation     | SingleEvaluation  |       147.10 ns |      2.783 ns |      5.428 ns | 2.11x slower |   0.11x |   0.0238 |     400 B |  1.00x more |
| SecurityAuditing_Evaluation          | SingleEvaluation  |       106.02 ns |      2.107 ns |      2.427 ns | 1.52x slower |   0.06x |   0.0238 |     400 B |  1.00x more |
