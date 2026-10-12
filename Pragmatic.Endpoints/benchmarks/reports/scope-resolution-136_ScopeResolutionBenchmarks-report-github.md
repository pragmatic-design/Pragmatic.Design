```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                             | Categories         | Mean         | Error        | StdDev       | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|----------------------------------- |------------------- |-------------:|-------------:|-------------:|------:|--------:|-------:|-------:|----------:|------------:|
| Scopes_Blocking_ConcurrentRequests | ConcurrentRequests | 86,308.58 ns | 1,723.421 ns | 2,831.631 ns |  1.00 |    0.05 | 7.8125 | 0.4883 |  135204 B |        1.00 |
| Scopes_Direct_ConcurrentRequests   | ConcurrentRequests | 18,230.67 ns |   334.618 ns |   530.740 ns |  0.21 |    0.01 | 1.6479 | 0.0305 |   27808 B |        0.21 |
|                                    |                    |              |              |              |       |         |        |        |           |             |
| Scopes_Blocking_OneRequest         | OneRequest         |  2,167.25 ns |   120.895 ns |   354.564 ns |  1.03 |    0.23 | 0.1183 |      - |    2016 B |        1.00 |
| Scopes_Direct_OneRequest           | OneRequest         |     78.60 ns |     2.076 ns |     6.121 ns |  0.04 |    0.01 | 0.0205 |      - |     344 B |        0.17 |
