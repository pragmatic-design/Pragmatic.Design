```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                    | Mean      | Error    | StdDev   | Ratio         | RatioSD | Allocated | Alloc Ratio |
|-------------------------- |----------:|---------:|---------:|--------------:|--------:|----------:|------------:|
| RealProvider              | 222.87 ns | 1.390 ns | 1.232 ns |  1.16x faster |   0.01x |         - |          NA |
| Same                      | 259.26 ns | 1.986 ns | 1.858 ns |      baseline |         |         - |          NA |
| ParsedEachLine            | 345.37 ns | 1.609 ns | 1.505 ns |  1.33x slower |   0.01x |         - |          NA |
| EmptyWrite                |  21.07 ns | 0.154 ns | 0.144 ns | 12.30x faster |   0.12x |         - |          NA |
| ConstantBlocks            | 209.16 ns | 1.711 ns | 1.600 ns |  1.24x faster |   0.01x |         - |          NA |
| ZLogger_SameFields_Stream | 228.34 ns | 1.137 ns | 1.064 ns |  1.14x faster |   0.01x |         - |          NA |
| ZLogger_Empty             |  46.11 ns | 0.265 ns | 0.235 ns |  5.62x faster |   0.05x |         - |          NA |
