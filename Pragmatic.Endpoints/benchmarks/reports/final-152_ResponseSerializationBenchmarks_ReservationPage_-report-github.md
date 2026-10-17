```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9711)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  Job-DDIHFF : .NET 10.0.13 (10.0.1326.50504), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

LaunchCount=3  

```
| Method                          | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|-------------------------------- |----------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| Host_Reflection                 | 14.240 μs | 0.2012 μs | 0.4459 μs |  1.00 |    0.04 | 0.0153 |     424 B |        1.00 |
| Host_GeneratedMetadata          | 13.798 μs | 0.1812 μs | 0.4016 μs |  0.97 |    0.04 | 0.0153 |     424 B |        1.00 |
| Stj_FastPath                    | 11.534 μs | 0.1875 μs | 0.4156 μs |  0.81 |    0.04 |      - |         - |        0.00 |
| REDox                           | 12.587 μs | 0.1231 μs | 0.2996 μs |  0.88 |    0.03 |      - |      40 B |        0.09 |
| Generated_Writer                |  7.490 μs | 0.0990 μs | 0.2088 μs |  0.53 |    0.02 |      - |         - |        0.00 |
| Generated_Writer_DefaultEncoder |  7.402 μs | 0.0725 μs | 0.1575 μs |  0.52 |    0.02 |      - |         - |        0.00 |
