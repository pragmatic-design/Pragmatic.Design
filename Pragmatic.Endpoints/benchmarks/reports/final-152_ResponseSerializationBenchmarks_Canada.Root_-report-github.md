```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9711)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  Job-DDIHFF : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

LaunchCount=3  

```
| Method                          | Mean     | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|-------------------------------- |---------:|----------:|----------:|------:|--------:|----------:|------------:|
| Host_Reflection                 | 8.615 ms | 0.0940 ms | 0.2142 ms |  1.00 |    0.03 |    1123 B |        1.00 |
| Host_GeneratedMetadata          | 8.679 ms | 0.1189 ms | 0.2347 ms |  1.01 |    0.04 |    1134 B |        1.01 |
| Stj_FastPath                    | 7.613 ms | 0.1294 ms | 0.2999 ms |  0.88 |    0.04 |         - |        0.00 |
| REDox                           | 8.098 ms | 0.0930 ms | 0.1941 ms |  0.94 |    0.03 |         - |        0.00 |
| Generated_Writer                | 3.741 ms | 0.0396 ms | 0.0893 ms |  0.43 |    0.01 |         - |        0.00 |
| Generated_Writer_DefaultEncoder | 3.584 ms | 0.0407 ms | 0.0868 ms |  0.42 |    0.01 |         - |        0.00 |
