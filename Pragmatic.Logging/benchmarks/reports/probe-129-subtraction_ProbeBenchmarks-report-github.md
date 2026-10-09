```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                    | Mean     | Error   | StdDev   | Allocated |
|-------------------------- |---------:|--------:|---------:|----------:|
| RealProvider              | 160.7 ns | 5.36 ns | 15.81 ns |         - |
| ConstantBlocks            | 261.8 ns | 7.75 ns | 22.85 ns |         - |
| Blocks_HandTwice          | 260.5 ns | 6.65 ns | 19.62 ns |         - |
| Blocks_HandOnce           | 251.9 ns | 7.16 ns | 21.12 ns |         - |
| Blocks_NoMessage          | 260.8 ns | 8.05 ns | 23.73 ns |         - |
| Blocks_NoProperties       | 191.0 ns | 6.14 ns | 18.12 ns |         - |
| Blocks_HandRaw            | 206.2 ns | 6.07 ns | 17.69 ns |         - |
| AllRaw                    | 150.0 ns | 3.41 ns | 10.05 ns |         - |
| ZLogger_SameFields_Stream | 300.1 ns | 8.39 ns | 24.74 ns |         - |
| ZLogger_Buffer            | 280.9 ns | 6.68 ns | 19.69 ns |         - |
