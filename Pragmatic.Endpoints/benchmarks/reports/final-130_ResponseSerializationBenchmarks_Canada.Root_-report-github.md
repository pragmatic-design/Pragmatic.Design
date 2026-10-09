```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                          | Mean     | Error     | StdDev    | Ratio | Gen0     | Gen1     | Gen2     | Allocated | Alloc Ratio |
|-------------------------------- |---------:|----------:|----------:|------:|---------:|---------:|---------:|----------:|------------:|
| Host_Reflection                 | 7.770 ms | 0.0195 ms | 0.0182 ms |  1.00 | 140.6250 | 140.6250 | 140.6250 |      2 MB |        1.00 |
| Host_GeneratedMetadata          | 7.779 ms | 0.0216 ms | 0.0191 ms |  1.00 | 140.6250 | 140.6250 | 140.6250 |      2 MB |        1.00 |
| Stj_FastPath                    | 6.901 ms | 0.0246 ms | 0.0230 ms |  0.89 | 140.6250 | 140.6250 | 140.6250 |   1.99 MB |        1.00 |
| REDox                           | 7.169 ms | 0.0503 ms | 0.0471 ms |  0.92 | 140.6250 | 140.6250 | 140.6250 |   1.99 MB |        1.00 |
| Generated_Writer                | 6.384 ms | 0.0267 ms | 0.0250 ms |  0.82 | 140.6250 | 140.6250 | 140.6250 |   1.99 MB |        1.00 |
| Generated_Writer_DefaultEncoder | 6.653 ms | 0.0288 ms | 0.0269 ms |  0.86 | 140.6250 | 140.6250 | 140.6250 |   1.99 MB |        1.00 |
