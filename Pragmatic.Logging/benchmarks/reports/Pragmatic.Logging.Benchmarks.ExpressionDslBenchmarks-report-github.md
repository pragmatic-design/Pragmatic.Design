```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                               | Categories        | Mean            | Error         | StdDev        | Ratio        | RatioSD | Gen0     | Allocated | Alloc Ratio |
|------------------------------------- |------------------ |----------------:|--------------:|--------------:|-------------:|--------:|---------:|----------:|------------:|
| SimpleLevel_BatchEvaluation          | BatchEvaluation   |       381.81 ns |      6.949 ns |      9.741 ns |     baseline |         |   0.1192 |    2000 B |             |
| ComplexBusinessLogic_BatchEvaluation | BatchEvaluation   |       520.94 ns |     10.209 ns |     12.911 ns | 1.37x slower |   0.05x |   0.1192 |    2000 B |  1.00x more |
| EnterpriseCompliance_BatchEvaluation | BatchEvaluation   |       613.51 ns |     12.291 ns |     20.195 ns | 1.61x slower |   0.07x |   0.1192 |    2000 B |  1.00x more |
| AllFilters_BatchEvaluation           | BatchEvaluation   |     2,938.82 ns |     48.912 ns |     60.069 ns | 7.70x slower |   0.24x |   0.5951 |   10000 B |  5.00x more |
|                                      |                   |                 |               |               |              |         |          |           |             |
| CacheWarmup_SingleExpression         | CachePerformance  |    71,481.47 ns |  1,162.611 ns |  1,338.865 ns |     baseline |         |  23.9258 |  400400 B |             |
| CacheStress_MultipleExpressions      | CachePerformance  |   588,645.67 ns | 10,827.951 ns | 12,035.239 ns | 8.24x slower |   0.22x | 119.1406 | 2002000 B |  5.00x more |
|                                      |                   |                 |               |               |              |         |          |           |             |
| HighVolume_SimpleFilter              | HighVolume        |   751,281.98 ns | 14,568.365 ns | 20,422.814 ns |     baseline |         | 238.2813 | 4000000 B |             |
| HighVolume_ComplexFilter             | HighVolume        |   976,733.37 ns | 18,800.371 ns | 21,650.536 ns | 1.30x slower |   0.04x | 238.2813 | 4000000 B |  1.00x more |
| HighVolume_MixedFilters              | HighVolume        | 1,040,104.35 ns | 20,415.066 ns | 25,071.536 ns | 1.39x slower |   0.05x | 238.2813 | 4000000 B |  1.00x more |
|                                      |                   |                 |               |               |              |         |          |           |             |
| PropertyExists_Evaluation            | PropertyFiltering |        89.10 ns |      1.682 ns |      1.652 ns |     baseline |         |   0.0286 |     480 B |             |
| PropertyValue_Evaluation             | PropertyFiltering |        87.84 ns |      1.723 ns |      1.844 ns | 1.01x faster |   0.03x |   0.0286 |     480 B |  1.00x more |
| StructuredProperties_Evaluation      | PropertyFiltering |        87.57 ns |      1.734 ns |      2.129 ns | 1.02x faster |   0.03x |   0.0300 |     504 B |  1.05x more |
| NumericProperties_Evaluation         | PropertyFiltering |       102.99 ns |      1.910 ns |      1.693 ns | 1.16x slower |   0.03x |   0.0296 |     496 B |  1.03x more |
|                                      |                   |                 |               |               |              |         |          |           |             |
| SimpleLevel_Evaluation               | SingleEvaluation  |        71.57 ns |      1.450 ns |      1.356 ns |     baseline |         |   0.0238 |     400 B |             |
| ComplexBusinessLogic_Evaluation      | SingleEvaluation  |        98.32 ns |      1.923 ns |      1.975 ns | 1.37x slower |   0.04x |   0.0238 |     400 B |  1.00x more |
| EnterpriseCompliance_Evaluation      | SingleEvaluation  |       119.83 ns |      2.343 ns |      2.507 ns | 1.67x slower |   0.05x |   0.0238 |     400 B |  1.00x more |
| PerformanceMonitoring_Evaluation     | SingleEvaluation  |       154.34 ns |      3.086 ns |      3.790 ns | 2.16x slower |   0.07x |   0.0238 |     400 B |  1.00x more |
| SecurityAuditing_Evaluation          | SingleEvaluation  |        97.87 ns |      1.832 ns |      1.799 ns | 1.37x slower |   0.04x |   0.0238 |     400 B |  1.00x more |
