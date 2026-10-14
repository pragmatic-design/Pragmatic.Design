```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                          | Mean     | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|-------------------------------- |---------:|----------:|----------:|------:|--------:|----------:|------------:|
| Host_Reflection                 | 8.194 ms | 0.1100 ms | 0.1029 ms |  1.00 |    0.02 |    1123 B |        1.00 |
| Host_GeneratedMetadata          | 8.306 ms | 0.1450 ms | 0.2342 ms |  1.01 |    0.03 |    1123 B |        1.00 |
| Stj_FastPath                    | 7.415 ms | 0.1317 ms | 0.2011 ms |  0.91 |    0.03 |         - |        0.00 |
| REDox                           | 7.619 ms | 0.1500 ms | 0.1252 ms |  0.93 |    0.02 |         - |        0.00 |
| Generated_Writer                | 7.048 ms | 0.1325 ms | 0.1240 ms |  0.86 |    0.02 |         - |        0.00 |
| Generated_Writer_DefaultEncoder | 7.348 ms | 0.1445 ms | 0.2292 ms |  0.90 |    0.03 |         - |        0.00 |
