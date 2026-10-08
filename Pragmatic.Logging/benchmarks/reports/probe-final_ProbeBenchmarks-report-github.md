```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9457)
Unknown processor
.NET SDK 10.0.303
  [Host]     : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                    | Mean      | Error     | StdDev    | Allocated |
|-------------------------- |----------:|----------:|----------:|----------:|
| RealProvider              | 257.20 ns |  7.493 ns | 22.093 ns |         - |
| ParsedEachLine            | 445.95 ns | 11.244 ns | 33.153 ns |         - |
| EmptyWrite                |  26.53 ns |  0.703 ns |  2.073 ns |         - |
| ConstantBlocks            | 257.32 ns |  7.303 ns | 21.304 ns |         - |
| ZLogger_SameFields_Stream | 302.60 ns |  7.783 ns | 22.948 ns |         - |
| ZLogger_Empty             |  56.73 ns |  1.358 ns |  4.003 ns |         - |
