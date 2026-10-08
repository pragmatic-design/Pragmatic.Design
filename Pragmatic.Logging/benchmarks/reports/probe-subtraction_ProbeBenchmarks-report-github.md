```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                    | Mean       | Error     | StdDev     | Median     | Ratio           | RatioSD | Allocated | Alloc Ratio |
|-------------------------- |-----------:|----------:|-----------:|-----------:|----------------:|--------:|----------:|------------:|
| RealProvider              | 276.335 ns | 5.5403 ns | 11.4416 ns | 273.497 ns |   1.043x faster |   0.05x |         - |          NA |
| Same                      | 287.768 ns | 5.5529 ns |  6.8194 ns | 287.754 ns |        baseline |         |         - |          NA |
| ParsedEachLine            | 366.069 ns | 7.1118 ns |  8.1900 ns | 365.143 ns |   1.273x slower |   0.04x |         - |          NA |
| BuiltInTimestamp          | 265.709 ns | 5.2622 ns | 11.0999 ns | 262.727 ns |   1.085x faster |   0.05x |         - |          NA |
| NoTemplate                | 256.724 ns | 5.1235 ns | 12.0768 ns | 252.278 ns |   1.123x faster |   0.06x |         - |          NA |
| NoExtraFields             | 241.631 ns | 4.8055 ns |  6.4152 ns | 240.800 ns |   1.192x faster |   0.04x |         - |          NA |
| EncodedEventFields        | 252.615 ns | 4.0941 ns |  5.7393 ns | 251.265 ns |   1.140x faster |   0.04x |         - |          NA |
| EncodedConstants          | 265.059 ns | 5.2029 ns |  9.6439 ns | 262.113 ns |   1.087x faster |   0.05x |         - |          NA |
| RawMessage                | 282.208 ns | 5.6178 ns | 11.4756 ns | 279.991 ns |   1.021x faster |   0.05x |         - |          NA |
| NoLock                    | 269.047 ns | 5.3831 ns | 11.5876 ns | 266.731 ns |   1.071x faster |   0.05x |         - |          NA |
| NoStream                  | 276.232 ns | 5.3334 ns |  8.1447 ns | 273.326 ns |   1.043x faster |   0.04x |         - |          NA |
| AutoFlushOnce             | 273.820 ns | 5.1411 ns |  9.4008 ns | 272.597 ns |   1.052x faster |   0.04x |         - |          NA |
| EmptyWrite                |  23.025 ns | 0.4703 ns |  0.4399 ns |  23.010 ns |  12.502x faster |   0.37x |         - |          NA |
| ZLogger_SameFields_Stream | 247.944 ns | 4.8778 ns |  6.5117 ns | 246.740 ns |   1.161x faster |   0.04x |         - |          NA |
| ZLogger_Empty             |  51.220 ns | 1.0000 ns |  0.9822 ns |  50.970 ns |   5.620x faster |   0.17x |         - |          NA |
| IsEnabled_Pragmatic       |   3.724 ns | 0.0867 ns |  0.1495 ns |   3.699 ns |  77.398x faster |   3.45x |         - |          NA |
| IsEnabled_ZLogger         |   2.732 ns | 0.0737 ns |  0.1170 ns |   2.690 ns | 105.526x faster |   4.94x |         - |          NA |
