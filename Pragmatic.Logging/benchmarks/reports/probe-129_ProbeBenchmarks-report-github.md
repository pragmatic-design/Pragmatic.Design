```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                    | Mean     | Error   | StdDev   | Allocated |
|-------------------------- |---------:|--------:|---------:|----------:|
| RealProvider              | 158.0 ns | 5.41 ns | 15.94 ns |         - |
| ConstantBlocks            | 259.0 ns | 7.13 ns | 21.02 ns |         - |
| AllRaw                    | 145.1 ns | 3.95 ns | 11.64 ns |         - |
| ZLogger_SameFields_Stream | 298.8 ns | 8.67 ns | 25.56 ns |         - |
| ZLogger_Buffer            | 275.7 ns | 8.34 ns | 24.59 ns |         - |
