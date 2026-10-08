```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                    | Mean     | Error   | StdDev   | Ratio        | RatioSD | Allocated | Alloc Ratio |
|-------------------------- |---------:|--------:|---------:|-------------:|--------:|----------:|------------:|
| RealProvider              | 283.0 ns | 5.61 ns | 12.32 ns | 1.02x slower |   0.06x |         - |          NA |
| Same                      | 277.0 ns | 5.53 ns | 12.93 ns |     baseline |         |         - |          NA |
| NoTemplate                | 261.3 ns | 4.97 ns |  8.97 ns | 1.06x faster |   0.06x |         - |          NA |
| EncodedEventFields        | 271.2 ns | 5.39 ns |  9.16 ns | 1.02x faster |   0.06x |         - |          NA |
| ZLogger_SameFields_Stream | 254.8 ns | 4.56 ns |  7.87 ns | 1.09x faster |   0.06x |         - |          NA |
