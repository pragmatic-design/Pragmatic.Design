```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                 | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0     | Gen1     | Gen2     | Allocated | Alloc Ratio |
|----------------------- |----------:|----------:|----------:|------:|--------:|---------:|---------:|---------:|----------:|------------:|
| Host_Reflection        | 10.595 ms | 0.2108 ms | 0.6183 ms |  1.00 |    0.08 | 296.8750 | 296.8750 | 296.8750 |      2 MB |        1.00 |
| Host_GeneratedMetadata | 12.564 ms | 0.4895 ms | 1.4433 ms |  1.19 |    0.15 | 296.8750 | 296.8750 | 296.8750 |      2 MB |        1.00 |
| Stj_FastPath           | 11.256 ms | 0.2171 ms | 0.3114 ms |  1.07 |    0.07 | 140.6250 | 140.6250 | 140.6250 |   1.99 MB |        1.00 |
| REDox                  |  9.981 ms | 0.1982 ms | 0.3723 ms |  0.95 |    0.07 | 296.8750 | 296.8750 | 296.8750 |   1.99 MB |        1.00 |
